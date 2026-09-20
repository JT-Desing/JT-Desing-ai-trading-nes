# NES Core Safe Runtime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the deterministic, testable core of V1 for NES Trend Pullback + VWAP without broker connectivity or live AI calls.

**Architecture:** A pure C# domain library owns state, contracts, strategy evaluation, risk decisions, and event records. A separate test project validates every safety rule. SQLite/JSONL adapters persist events but cannot influence trading decisions. This plan intentionally stops before NinjaTrader, OpenAI, news providers, or dashboard integration.

**Tech Stack:** C#; .NET Framework 4.8; SDK-style projects; NUnit; Microsoft.Data.Sqlite; Newtonsoft.Json.

**Spec:** `docs/superpowers/specs/2026-09-20-nes-ai-trading-v1-design.md`

## Global Constraints

- Instrument: NES only.
- Strategy: Trend Pullback + VWAP only.
- Intraday only; session ends flat.
- Maximum open positions: 1.
- Maximum contracts: 1 NES.
- Maximum trades/day: 2.
- Martingale, averaging down, pyramiding, overnight positions, trailing stops, automatic break-even, and autonomous production modification are prohibited in V1.
- LLMs are not in the execution hot path.
- Risk Engine decisions are deterministic.
- Expired or missing mandatory state fails closed to NO_TRADE.
- Every candidate, approval, rejection, and outcome must be auditable.
- No broker/API secrets may be persisted in logs or repository files.

## Review Focus

- Duplicate signal IDs must never create two approvals or two trade records.
- Stale News/Market/Trade state must fail closed rather than silently using old data.
- A technically valid setup whose structural stop exceeds the risk budget must be rejected without tightening the stop.
- Session/day limits must survive process restart because state is reconstructed from persisted events.
- Storage failure during an auditable trading decision must prevent a new trade rather than allow an unlogged action.

---

### Task 1: Solution skeleton and immutable domain contracts

**Files:**
- Create: `src/NesTrading.Core/NesTrading.Core.csproj`
- Create: `src/NesTrading.Core/Domain/Enums.cs`
- Create: `src/NesTrading.Core/Domain/AgentStates.cs`
- Create: `src/NesTrading.Core/Domain/TradeModels.cs`
- Create: `tests/NesTrading.Core.Tests/NesTrading.Core.Tests.csproj`
- Create: `tests/NesTrading.Core.Tests/Domain/ContractTests.cs`
- Create: `NesTrading.sln`

**Interfaces:**
- Produces: immutable domain records/classes used by all later tasks:
  - `NewsState`
  - `MarketState`
  - `TradeDecision`
  - `TradeCandidate`
  - `RiskDecision`
  - `TradeResult`

- [ ] **Step 1: Create the solution and projects**

Create `NesTrading.sln` with one class library targeting `net48` and one NUnit test project targeting `net48`.

Core package references:
- `Newtonsoft.Json`
- `Microsoft.Data.Sqlite`

Test references:
- `NUnit`
- `NUnit3TestAdapter`
- `Microsoft.NET.Test.Sdk`

- [ ] **Step 2: Write failing contract tests**

Create tests that require:
- `NewsState.State` in `Allow/Caution/Block`.
- `MarketState.Regime` in `TrendUp/TrendDown/Range/HighVolatility/InvalidData`.
- `TradeDecision.Action` in `Long/Short/Wait/ExitRecommendation`.
- Every state has `GeneratedAtUtc` and `ValidUntilUtc`.
- `TradeCandidate` carries `SignalId`, entry, structural stop, target, setup score, and exactly one NES contract in V1.

Example assertion:

```csharp
[Test]
public void TradeCandidate_V1_UsesOneNesContract()
{
    var candidate = TestFixtures.ValidLongCandidate();
    Assert.That(candidate.Symbol, Is.EqualTo("NES"));
    Assert.That(candidate.Contracts, Is.EqualTo(1));
}
```

- [ ] **Step 3: Run tests and verify failure**

Run on Windows:

```powershell
dotnet test tests/NesTrading.Core.Tests/NesTrading.Core.Tests.csproj -c Debug
```

Expected: FAIL because domain types do not exist.

- [ ] **Step 4: Implement minimal immutable contracts**

Use constructor validation for:
- non-empty IDs;
- NES symbol only;
- confidence within 0..1;
- setup score within 0..100;
- positive contract count, additionally constrained to exactly 1 for V1;
- `ValidUntilUtc >= GeneratedAtUtc`.

- [ ] **Step 5: Run tests**

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add NesTrading.sln src tests
git commit -m "feat: add NES V1 domain contracts"
```

---

### Task 2: Trading state machine and fail-closed guards

**Files:**
- Create: `src/NesTrading.Core/Runtime/TradingStateMachine.cs`
- Create: `src/NesTrading.Core/Runtime/TradingSessionState.cs`
- Test: `tests/NesTrading.Core.Tests/Runtime/TradingStateMachineTests.cs`

**Interfaces:**
- Consumes: domain enums from Task 1.
- Produces:
  - `TradingStateMachine.CanSubmitNewOrder(DateTime utcNow): bool`
  - `TradingStateMachine.Transition(SystemEvent evt): TransitionResult`

- [ ] **Step 1: Write failing tests for allowed transitions**

Pin these behaviors:
- `SystemOff -> Initializing -> DataValidation -> Ready -> TradingEnabled`.
- `TradingEnabled -> PositionOpen -> ManagingPosition -> Flat`.
- Any lock state prevents new orders.
- `EmergencyStop` cannot transition directly back to `TradingEnabled`.
- Unknown position forces `RiskLock`.
- Missing/stale mandatory state forces `DataStale`.

- [ ] **Step 2: Run the focused test file**

```powershell
dotnet test tests/NesTrading.Core.Tests/NesTrading.Core.Tests.csproj -c Debug --filter TradingStateMachineTests
```

Expected: FAIL.

- [ ] **Step 3: Implement the explicit transition table**

Do not infer transitions dynamically. Store a whitelist of valid source/target transitions and reject all others.

```csharp
public TransitionResult Transition(SystemEvent evt)
{
    var next = ResolveNextState(CurrentState, evt);
    if (!AllowedTransitions.Contains((CurrentState, next)))
        return TransitionResult.Rejected(CurrentState, "INVALID_TRANSITION");

    CurrentState = next;
    return TransitionResult.Accepted(next);
}
```

- [ ] **Step 4: Add restart reconstruction test**

Feed a persisted sequence of events into a fresh state machine and assert the reconstructed state equals the original state.

- [ ] **Step 5: Run tests and commit**

```bash
git add src/NesTrading.Core/Runtime tests/NesTrading.Core.Tests/Runtime
git commit -m "feat: add fail-closed trading state machine"
```

---

### Task 3: Market regime and Trend Pullback + VWAP evaluator

**Files:**
- Create: `src/NesTrading.Core/Strategy/MarketSnapshot.cs`
- Create: `src/NesTrading.Core/Strategy/SetupScorer.cs`
- Create: `src/NesTrading.Core/Strategy/TrendPullbackStrategy.cs`
- Test: `tests/NesTrading.Core.Tests/Strategy/TrendPullbackStrategyTests.cs`

**Interfaces:**
- Consumes: deterministic numeric `MarketSnapshot`; no broker APIs and no LLM.
- Produces:
  - `MarketState EvaluateMarket(MarketSnapshot snapshot, DateTime utcNow)`
  - `TradeCandidate? BuildCandidate(MarketState market, NewsState news, MarketSnapshot snapshot)`

- [ ] **Step 1: Define snapshot inputs**

`MarketSnapshot` contains only values already computed by the market-data adapter:
- current price;
- session VWAP;
- EMA20;
- EMA50;
- EMA20 slope;
- ATR;
- current volume;
- rolling reference volume;
- most recent swing high/low;
- boolean higher-high/higher-low or lower-high/lower-low flags;
- current spread in ticks;
- timestamp.

- [ ] **Step 2: Write failing LONG tests**

Require LONG eligibility only when:
- price > VWAP;
- EMA20 > EMA50;
- EMA20 slope > 0;
- bullish structure true;
- volatility allowed;
- pullback touched configured VWAP/EMA20 zone;
- setup score >= threshold.

Include a test where 6 of 7 conditions pass but regime is RANGE; result must be no candidate.

- [ ] **Step 3: Write failing SHORT mirror tests**

Require the exact mirrored logic.

- [ ] **Step 4: Implement the 100-point scorer**

Initial weights:
- Trend alignment 20.
- Price vs VWAP 15.
- EMA structure 15.
- Market structure 20.
- Pullback quality 15.
- Volume confirmation 10.
- Volatility quality 5.

Threshold:
- News ALLOW => >= 75.
- News CAUTION => >= 85.
- News BLOCK => never create candidate.

- [ ] **Step 5: Add anti-chasing test**

A snapshot whose price has moved beyond the configured maximum pullback-entry distance must return no candidate, even if trend score is otherwise high.

- [ ] **Step 6: Run all strategy tests and commit**

```bash
git add src/NesTrading.Core/Strategy tests/NesTrading.Core.Tests/Strategy
git commit -m "feat: implement deterministic NES pullback strategy"
```

---

### Task 4: Deterministic V1 Risk Engine

**Files:**
- Create: `src/NesTrading.Core/Risk/RiskPolicy.cs`
- Create: `src/NesTrading.Core/Risk/RiskEngine.cs`
- Create: `src/NesTrading.Core/Risk/CostEstimate.cs`
- Test: `tests/NesTrading.Core.Tests/Risk/RiskEngineTests.cs`

**Interfaces:**
- Consumes:
  - `TradeCandidate`
  - `AccountRiskState`
  - `CostEstimate`
  - `TradingSessionState`
- Produces:
  - `RiskDecision Evaluate(...)`

- [ ] **Step 1: Write failing test for risk math**

For NES, encode `UsdPerPoint = 0.50m`.

```csharp
grossPriceRisk = Math.Abs(entry - stop) * 0.50m * contracts;
totalRisk = grossPriceRisk + estimatedRoundTripFees + estimatedSlippageUsd;
riskPct = totalRisk / accountEquity;
```

Assert that a structural stop requiring >2% total account risk is rejected.

- [ ] **Step 2: Write failing safety-rule tests**

Reject when any is true:
- open position exists;
- trades today >= 2;
- two losing trades already occurred;
- daily hard loss reached;
- cooldown active;
- outside 09:40–11:30 ET after timezone-normalized session calculation;
- News = BLOCK;
- stale News/Market/Trade state;
- abnormal spread;
- projected R:R below configured minimum;
- account equity <= 0;
- candidate contracts != 1;
- state machine disallows new order.

- [ ] **Step 3: Add structural-stop integrity test**

Given a candidate whose natural stop is too far, verify the engine rejects it and returns the original stop unchanged. There must be no code path that silently tightens the stop to fit the budget.

- [ ] **Step 4: Add duplicate signal test**

Call `Evaluate` twice with the same `SignalId`; the second attempt must be rejected as `DUPLICATE_SIGNAL`.

- [ ] **Step 5: Implement minimal policy**

Default research policy:
- target risk: 1%.
- hard max risk: 2%.
- daily soft loss: 2%.
- daily hard loss: 3%.
- max trades/day: 2.
- cooldown: 10 minutes.
- max contracts: 1.

Keep all values configurable and immutable per session.

- [ ] **Step 6: Run risk tests and commit**

```bash
git add src/NesTrading.Core/Risk tests/NesTrading.Core.Tests/Risk
git commit -m "feat: add deterministic NES risk engine"
```

---

### Task 5: Append-only event ledger and SQLite audit store

**Files:**
- Create: `src/NesTrading.Core/Storage/IEventStore.cs`
- Create: `src/NesTrading.Core/Storage/JsonlEventStore.cs`
- Create: `src/NesTrading.Core/Storage/SqliteAuditStore.cs`
- Create: `src/NesTrading.Core/Storage/Schema.sql`
- Test: `tests/NesTrading.Core.Tests/Storage/EventStoreTests.cs`

**Interfaces:**
- Produces:
  - `void Append(TradingEvent evt)`
  - `IReadOnlyList<TradingEvent> LoadSession(string sessionId)`
  - `SessionAuditSnapshot Reconstruct(string sessionId)`

- [ ] **Step 1: Write failing append-only JSONL tests**

Assert:
- each call appends one line;
- existing lines are never rewritten;
- event includes event ID, session ID, timestamp, type, version, and payload hash;
- malformed pre-existing file causes fail-closed exception rather than silent truncation.

- [ ] **Step 2: Write failing SQLite schema tests**

Required tables:
- `trades`
- `candidate_trades`
- `agent_decisions`
- `news_events`
- `market_states`
- `risk_events`
- `execution_events`
- `session_metrics`
- `strategy_versions`
- `supervisor_reports`

Assert foreign identifiers use `session_id`, `signal_id`, and `trade_id` consistently.

- [ ] **Step 3: Implement transactional writes**

A risk approval must be persisted before it can be returned to the caller as executable.

If persistence fails:

```csharp
return RiskDecision.Reject(candidate.SignalId, "AUDIT_PERSISTENCE_FAILURE");
```

- [ ] **Step 4: Add restart reconstruction test**

Persist:
- session started;
- candidate evaluated;
- risk approved;
- trade opened;
- trade closed.

Reopen storage and verify reconstructed daily trade count, loss count, last close timestamp, and flat/open state.

- [ ] **Step 5: Commit**

```bash
git add src/NesTrading.Core/Storage tests/NesTrading.Core.Tests/Storage
git commit -m "feat: add append-only audit storage"
```

---

### Task 6: Simulation execution adapter with mandatory brackets

**Files:**
- Create: `src/NesTrading.Core/Execution/IExecutionAdapter.cs`
- Create: `src/NesTrading.Core/Execution/SimulatedExecutionAdapter.cs`
- Create: `src/NesTrading.Core/Execution/OrderModels.cs`
- Test: `tests/NesTrading.Core.Tests/Execution/SimulatedExecutionAdapterTests.cs`

**Interfaces:**
- Consumes: approved `RiskDecision`.
- Produces:
  - `ExecutionResult SubmitBracket(RiskDecision approvedDecision)`
  - `BrokerSnapshot GetSnapshot()`

- [ ] **Step 1: Write failing bracket test**

An approved trade must create:
- one entry;
- one stop;
- one target;
- linked by one `TradeId`.

Any attempt to create an entry without stop or target must fail.

- [ ] **Step 2: Write duplicate-order idempotency test**

Submitting the same approved `SignalId` twice must produce one simulated trade and one duplicate rejection.

- [ ] **Step 3: Write uncertain-ack test**

Simulate `AckState.Unknown`. Verify adapter refuses blind resubmission until `Reconcile` determines whether the order exists.

- [ ] **Step 4: Implement simulated fills**

Support configured:
- entry slippage ticks;
- exit slippage ticks;
- round-trip fees;
- missed fill flag for stress tests.

- [ ] **Step 5: Run tests and commit**

```bash
git add src/NesTrading.Core/Execution tests/NesTrading.Core.Tests/Execution
git commit -m "feat: add safe simulated bracket execution"
```

---

### Task 7: End-to-end paper-session runner

**Files:**
- Create: `src/NesTrading.Core/Runtime/PaperSessionRunner.cs`
- Create: `tests/NesTrading.Core.Tests/Runtime/PaperSessionRunnerTests.cs`
- Create: `tests/NesTrading.Core.Tests/Fixtures/sample-session.jsonl`

**Interfaces:**
- Consumes deterministic sequence of `MarketSnapshot` and `NewsState`.
- Produces a persisted session with candidates, rejects, approvals, fills, outcomes, and final metrics.

- [ ] **Step 1: Write one deterministic profitable-session test**

Feed a fixture where:
- valid bullish trend forms;
- valid pullback appears;
- News = ALLOW;
- risk fits budget;
- target is hit.

Assert:
- exactly one candidate;
- exactly one approved trade;
- bracket exists;
- P&L and R are correct;
- final state is FLAT;
- audit trail is complete.

- [ ] **Step 2: Write one deterministic blocked-news test**

Same market setup, News = BLOCK.

Assert:
- candidate is recorded;
- no execution is created;
- rejection reason is `NEWS_BLOCK`.

- [ ] **Step 3: Write fail-closed storage failure test**

Inject a throwing event store at approval time.

Assert:
- no execution call occurs;
- final decision is rejected;
- state remains safe.

- [ ] **Step 4: Implement the runner**

Runner order:
1. validate state freshness;
2. evaluate market;
3. build candidate;
4. persist candidate;
5. evaluate risk;
6. persist risk decision;
7. only then call execution adapter;
8. persist execution events;
9. update session state.

- [ ] **Step 5: Run full test suite**

```powershell
dotnet test NesTrading.sln -c Debug
```

Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add src tests
git commit -m "feat: add deterministic paper session runner"
```

---

### Task 8: CI safety gate

**Files:**
- Create: `.github/workflows/core-tests.yml`
- Create: `README.md`

**Interfaces:**
- CI is the gate for later plans.

- [ ] **Step 1: Create Windows GitHub Actions workflow**

Use `windows-latest` because the core targets .NET Framework 4.8.

Workflow steps:
1. checkout;
2. setup NuGet/dotnet tooling;
3. restore;
4. build;
5. run `dotnet test NesTrading.sln -c Release`.

- [ ] **Step 2: Add README safety status**

README must state:
- research software;
- V1 instrument NES only;
- no live trading implemented yet;
- current implemented milestone after this plan: deterministic simulation core;
- live deployment is blocked until later validation plans pass.

- [ ] **Step 3: Add CI failure cases**

Temporarily break one risk test and verify CI fails, then revert and verify CI passes.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/core-tests.yml README.md
git commit -m "ci: gate NES core with deterministic safety tests"
```

---

## Follow-on plans

This core plan intentionally does not implement independent subsystems that deserve their own review gates. After this plan passes:

1. **Agent Integration Plan**
   - News Agent provider abstraction.
   - Market Agent structured state producer.
   - Trade Agent.
   - state cache / TTL.
   - OpenAI usage limits and schema validation.

2. **NinjaTrader Execution Plan**
   - NinjaScript/AddOn adapter.
   - real-time market-data mapping.
   - bracket/OCO submission.
   - broker reconciliation.
   - disconnect/restart recovery.
   - latency instrumentation.
   - simulation-only first.

3. **Supervisor & Analytics Plan**
   - post-session metrics.
   - role-specific agent scoring.
   - attribution.
   - counterfactual blocked-trade analysis.
   - daily/weekly reports.
   - improvement queue.

4. **Dashboard Plan**
   - equity/P&L/R.
   - drawdown, expectancy, PF.
   - agent health/score trends.
   - rejection reasons.
   - costs/slippage/latency.
   - system health and alerts.

5. **Validation & Promotion Plan**
   - historical data ingestion.
   - backtest.
   - out-of-sample.
   - walk-forward.
   - Monte Carlo.
   - replay.
   - shadow.
   - paper.
   - live promotion gate.

## Plan self-review

- Spec coverage for the deterministic core: covered by Tasks 1–8.
- Live broker execution: deliberately deferred to a dedicated plan.
- AI providers and news sources: deliberately deferred to a dedicated plan.
- Supervisor/dashboard: deliberately deferred.
- No placeholders are present in implementation steps.
- Key cross-task identifiers are consistent: `session_id`, `signal_id`, `trade_id`.
- All critical fail-closed requirements in this core plan have explicit tests.
