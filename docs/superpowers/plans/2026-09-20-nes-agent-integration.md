# NES Agent Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add safe, structured AI-agent integration for NES V1 without allowing any model call to bypass the deterministic strategy, Risk Engine, execution safeguards, or audit trail.

**Architecture:** The agent layer is an asynchronous advisory layer. A provider abstraction returns strict structured outputs, a validator converts only valid outputs into immutable domain state, and an atomic state cache exposes the most recent fresh state to the deterministic runtime. The Market Agent remains deterministic in V1; the News Agent and Trade Agent may use an AI provider, but stale, invalid, refused, incomplete, over-budget, or unavailable model output always becomes fail-closed/no-trade.

**Tech Stack:** C#; .NET Framework 4.8; NUnit; Newtonsoft.Json; System.Net.Http; existing NES Core Safe Runtime.

**Spec:** `docs/superpowers/specs/2026-09-20-nes-ai-trading-v1-design.md`

## Global Constraints

- Instrument: NES only.
- Strategy: Trend Pullback + VWAP only.
- AI is not in the order-submission hot path.
- Risk Engine remains the final authority.
- Model confidence never changes V1 position size.
- Missing or stale mandatory state blocks new trades.
- News Agent may emit only ALLOW / CAUTION / BLOCK.
- Trade Agent may emit only LONG / SHORT / WAIT / EXIT_RECOMMENDATION.
- Market Agent is deterministic in V1 and may not call an LLM.
- No agent may submit broker orders.
- No agent may widen stops, increase size, override locks, or edit production configuration.
- Structured outputs are mandatory for model-backed agents.
- Refusals, incomplete responses, malformed payloads, schema violations, timeouts, provider failures, or usage-budget exhaustion are non-tradable states.
- API keys and provider secrets are read from secure environment/configuration surfaces and are never written to audit payloads.
- All agent states carry generation timestamp, expiry timestamp, provider/model metadata, schema version, prompt version, and reason codes.
- All agent decisions are auditable before the Risk Engine can consume them.

## Review Focus

1. **Provider returns syntactically valid JSON with semantically unsafe values** — validator must reject unknown enums, confidence outside 0..1, wrong symbol, impossible timestamps, unsupported schema versions, or missing required fields.
2. **Provider responds after its deadline** — late response must never overwrite a newer cached state and must never become tradable.
3. **Process restarts while cached AI state existed in memory** — cache restarts empty/fail-closed; old in-memory state is never assumed valid.
4. **Usage budget is exhausted during a live session** — agent refresh returns unavailable/budget-exhausted and new trades remain blocked rather than bypassing AI requirements.
5. **News and Trade Agent states disagree with deterministic Market state** — coordinator records the disagreement; Risk Engine still rejects direction mismatch and News BLOCK/CAUTION rules remain authoritative.

---

### Task 1: Agent envelope, strict schemas, and semantic validators

**Files:**
- Create: `src/NesTrading.Core/Agents/AgentEnvelope.cs`
- Create: `src/NesTrading.Core/Agents/AgentSchemaValidator.cs`
- Create: `src/NesTrading.Core/Agents/AgentSchemaVersion.cs`
- Test: `tests/NesTrading.Core.Tests/Agents/AgentSchemaValidatorTests.cs`

**Interfaces:**
- Produces:
  - `AgentEnvelope<T>`
  - `AgentValidationResult<T>`
  - `AgentSchemaValidator.ValidateNews(...)`
  - `AgentSchemaValidator.ValidateTrade(...)`

- [ ] **Step 1: Write failing tests for strict News Agent payload validation**

Pin the following payloads:
- valid NES ALLOW response => accepted;
- symbol `ES` => rejected;
- state `BUY` => rejected;
- confidence `1.2` => rejected;
- `valid_until_utc < generated_at_utc` => rejected;
- unsupported `schema_version` => rejected;
- missing `reason_codes` => rejected;
- extra decision-changing property such as `contracts: 5` => rejected;
- provider response marked refusal/incomplete => rejected before JSON conversion.

Representative test:

```csharp
[Test]
public void NewsPayload_UnknownState_IsRejected()
{
    var result = AgentSchemaValidator.ValidateNews(
        """
        {
          "schema_version":"news.v1",
          "symbol":"NES",
          "state":"BUY",
          "directional_bias":"NEUTRAL",
          "importance":"LOW",
          "event_code":"NONE",
          "generated_at_utc":"2026-09-21T14:00:00Z",
          "valid_until_utc":"2026-09-21T14:01:00Z",
          "confidence":0.8,
          "reason_codes":["NO_MAJOR_EVENT"]
        }
        """,
        ProviderCompletion.Completed("test-model"));

    Assert.That(result.IsValid, Is.False);
    Assert.That(result.ReasonCode, Is.EqualTo("SCHEMA_ENUM_INVALID"));
}
```

- [ ] **Step 2: Run focused tests and confirm RED**

Run:

```powershell
dotnet test tests/NesTrading.Core.Tests/NesTrading.Core.Tests.csproj -c Release --filter AgentSchemaValidatorTests
```

Expected: FAIL because agent schema types do not exist.

- [ ] **Step 3: Implement immutable envelopes**

`AgentEnvelope<T>` must carry:
- `AgentName`
- `SchemaVersion`
- `PromptVersion`
- `ProviderName`
- `ModelName`
- `GeneratedAtUtc`
- `ValidUntilUtc`
- `ReceivedAtUtc`
- `Payload`
- `RawResponseHash`

Raw provider text is not persisted by default; only a SHA-256 hash is required for correlation.

- [ ] **Step 4: Implement strict semantic validation**

Use `JObject` parsing with:
- explicit required-property allow-list;
- explicit enum allow-list;
- no unknown top-level properties;
- NES-only symbol validation;
- confidence range validation;
- UTC timestamp validation;
- TTL upper bound;
- schema-version equality check.

Do not silently coerce unknown values.

- [ ] **Step 5: Add Trade Agent payload tests**

Valid shape:

```json
{
  "schema_version": "trade.v1",
  "symbol": "NES",
  "decision": "LONG",
  "confidence": 0.81,
  "generated_at_utc": "2026-09-21T14:00:00Z",
  "valid_until_utc": "2026-09-21T14:00:15Z",
  "reason_codes": ["MARKET_ALIGNED","NEWS_ALLOW"]
}
```

Require LONG / SHORT / WAIT / EXIT_RECOMMENDATION only.

- [ ] **Step 6: Run tests and commit**

```bash
git add src/NesTrading.Core/Agents tests/NesTrading.Core.Tests/Agents
git commit -m "feat: add strict agent schemas and semantic validation"
```

---

### Task 2: Atomic agent-state cache with freshness and anti-stale-write protection

**Files:**
- Create: `src/NesTrading.Core/Agents/AgentStateCache.cs`
- Create: `src/NesTrading.Core/Agents/AgentStateSnapshot.cs`
- Test: `tests/NesTrading.Core.Tests/Agents/AgentStateCacheTests.cs`

**Interfaces:**
- Produces:
  - `bool TryPublishNews(AgentEnvelope<NewsState> state)`
  - `bool TryPublishMarket(AgentEnvelope<MarketState> state)`
  - `bool TryPublishTrade(AgentEnvelope<TradeDecision> state)`
  - `AgentStateSnapshot Read(DateTime utcNow)`

- [ ] **Step 1: Write failing freshness tests**

Require:
- fresh state is returned;
- expired state is returned as unavailable, never as stale-but-usable;
- cache starts empty after construction;
- older `GeneratedAtUtc` cannot overwrite newer state;
- response received after its own `ValidUntilUtc` cannot be published;
- response for wrong symbol cannot be published.

- [ ] **Step 2: Write concurrent publish/read test**

Use multiple Tasks/threads to publish increasing timestamps while reading snapshots. Assert the cache never exposes a timestamp older than the latest successfully published state for that role.

- [ ] **Step 3: Implement cache with role-specific locks**

Keep News, Market, and Trade slots independent. Publication is compare-and-swap by `GeneratedAtUtc` semantics under lock.

- [ ] **Step 4: Add restart/fail-closed test**

Create one cache, publish all states, discard it, create a second cache, and assert all roles are unavailable.

- [ ] **Step 5: Run tests and commit**

```bash
git add src/NesTrading.Core/Agents/AgentStateCache.cs src/NesTrading.Core/Agents/AgentStateSnapshot.cs tests/NesTrading.Core.Tests/Agents/AgentStateCacheTests.cs
git commit -m "feat: add fail-closed agent state cache"
```

---

### Task 3: AI provider abstraction, deadlines, refusals, and response metadata

**Files:**
- Create: `src/NesTrading.Core/Agents/Providers/IAgentTextProvider.cs`
- Create: `src/NesTrading.Core/Agents/Providers/ProviderRequest.cs`
- Create: `src/NesTrading.Core/Agents/Providers/ProviderCompletion.cs`
- Create: `src/NesTrading.Core/Agents/Providers/FakeAgentTextProvider.cs`
- Test: `tests/NesTrading.Core.Tests/Agents/ProviderSafetyTests.cs`

**Interfaces:**
- Produces:
  - `Task<ProviderCompletion> CompleteAsync(ProviderRequest request, CancellationToken cancellationToken)`
  - completion status: `Completed | Refused | Incomplete | TimedOut | Failed | BudgetExhausted`

- [ ] **Step 1: Write failing provider-status tests**

Require downstream logic to distinguish:
- valid completion;
- explicit refusal;
- truncated/incomplete response;
- timeout;
- network/provider failure;
- cancellation;
- budget exhaustion.

None of those non-completed states may reach the semantic payload validator as tradable data.

- [ ] **Step 2: Implement provider contracts**

`ProviderRequest` includes:
- role/agent name;
- model;
- prompt version;
- schema name;
- JSON schema string;
- input string;
- deadline UTC;
- max output tokens.

`ProviderCompletion` includes:
- status;
- model actually used;
- output text;
- provider request ID when available;
- input token count;
- output token count;
- provider-reported cost when available;
- started/finished UTC.

- [ ] **Step 3: Implement fake provider for deterministic tests**

Fake provider supports:
- fixed completed output;
- delayed output;
- refusal;
- failure;
- incomplete output;
- token usage.

- [ ] **Step 4: Add late-completion test**

Request deadline expires before provider completion. Wrapper must return `TimedOut` even if the fake provider later produces text.

- [ ] **Step 5: Run tests and commit**

```bash
git add src/NesTrading.Core/Agents/Providers tests/NesTrading.Core.Tests/Agents/ProviderSafetyTests.cs
git commit -m "feat: add safe agent provider abstraction"
```

---

### Task 4: Deterministic Market Agent

**Files:**
- Create: `src/NesTrading.Core/Agents/Market/MarketAgent.cs`
- Test: `tests/NesTrading.Core.Tests/Agents/MarketAgentTests.cs`

**Interfaces:**
- Consumes: `MarketSnapshot`, `TrendPullbackStrategy`.
- Produces: `AgentEnvelope<MarketState> Evaluate(MarketSnapshot snapshot, DateTime utcNow)`.

- [ ] **Step 1: Write failing tests**

Require:
- Market Agent produces the same regime, direction, setup, and score as `TrendPullbackStrategy.EvaluateMarket`;
- provider-call count remains zero because this agent has no AI provider;
- RANGE and HIGH_VOLATILITY remain non-tradable;
- output TTL is exactly the strategy state TTL;
- prompt/provider metadata identifies deterministic implementation rather than pretending an LLM was used.

- [ ] **Step 2: Implement thin deterministic wrapper**

No duplicated market logic. The agent delegates to the existing strategy and wraps the resulting `MarketState` in an auditable envelope.

- [ ] **Step 3: Run tests and commit**

```bash
git add src/NesTrading.Core/Agents/Market tests/NesTrading.Core.Tests/Agents/MarketAgentTests.cs
git commit -m "feat: add deterministic market agent"
```

---

### Task 5: News Agent with structured output and fail-closed behavior

**Files:**
- Create: `src/NesTrading.Core/Agents/News/NewsAgent.cs`
- Create: `src/NesTrading.Core/Agents/News/NewsAgentPrompt.cs`
- Create: `src/NesTrading.Core/Agents/News/NewsAgentSchema.cs`
- Test: `tests/NesTrading.Core.Tests/Agents/NewsAgentTests.cs`

**Interfaces:**
- Consumes:
  - `IAgentTextProvider`
  - pre-collected news/calendar text supplied by caller
  - `AiUsageBudget`
- Produces:
  - `Task<AgentRefreshResult<NewsState>> RefreshAsync(...)`

- [ ] **Step 1: Write failing successful-refresh test**

Given a completed provider response with valid `news.v1` JSON, assert:
- semantic validator accepts;
- envelope is published to cache;
- model/provider metadata is preserved;
- no order/execution class is referenced.

- [ ] **Step 2: Write fail-closed matrix**

For each:
- provider refusal;
- timeout;
- invalid JSON;
- valid JSON but unknown enum;
- stale `valid_until_utc`;
- generated timestamp too far in future;
- wrong symbol;
- budget exhausted;

Assert:
- refresh result is non-tradable;
- cache is not overwritten with invalid state;
- reason code is explicit.

- [ ] **Step 3: Implement News Agent prompt contract**

Prompt must explicitly state:
- instrument NES / U.S. equity-index context only;
- output is risk context, not trading instruction;
- allowed states ALLOW/CAUTION/BLOCK only;
- reason codes concise;
- no position sizing;
- no stop/target;
- no BUY/SELL recommendation.

- [ ] **Step 4: Implement refresh workflow**

Order:
1. budget reservation;
2. provider request with deadline;
3. status check;
4. strict semantic validation;
5. budget settlement;
6. cache publish;
7. audit-ready result.

No automatic retry in V1. A second provider request requires the next scheduled refresh cycle.

- [ ] **Step 5: Run tests and commit**

```bash
git add src/NesTrading.Core/Agents/News tests/NesTrading.Core.Tests/Agents/NewsAgentTests.cs
git commit -m "feat: add structured fail-closed news agent"
```

---

### Task 6: Trade Agent with Market/News/Account context

**Files:**
- Create: `src/NesTrading.Core/Agents/Trade/TradeAgent.cs`
- Create: `src/NesTrading.Core/Agents/Trade/TradeAgentPrompt.cs`
- Create: `src/NesTrading.Core/Agents/Trade/TradeAgentSchema.cs`
- Create: `src/NesTrading.Core/Agents/Trade/TradeAgentContext.cs`
- Test: `tests/NesTrading.Core.Tests/Agents/TradeAgentTests.cs`

**Interfaces:**
- Consumes:
  - fresh Market envelope;
  - fresh News envelope;
  - account/session state;
  - technical candidate;
  - AI provider;
  - usage budget.
- Produces:
  - `Task<AgentRefreshResult<TradeDecision>> RefreshAsync(...)`

- [ ] **Step 1: Write pre-provider guard tests**

Provider must not be called when:
- News = BLOCK;
- Market state expired;
- News state expired;
- no technical candidate exists;
- Market direction differs from candidate;
- account/session state is already locked.

Expected result: deterministic WAIT/unavailable state with reason code; zero provider calls.

- [ ] **Step 2: Write valid LONG/SHORT response tests**

Valid provider LONG is publishable only when deterministic market/candidate direction is LONG.
Valid SHORT is publishable only when deterministic market/candidate direction is SHORT.

- [ ] **Step 3: Write contradiction tests**

If provider says:
- SHORT while candidate is LONG;
- LONG while News BLOCK;
- confidence outside 0..1;
- unsupported action;
- different symbol;

result must not publish a tradable Trade state.

- [ ] **Step 4: Implement prompt contract**

The model is asked to choose only:
- LONG;
- SHORT;
- WAIT;
- EXIT_RECOMMENDATION.

Prompt explicitly states:
- size is fixed by deterministic V1;
- Risk Engine can reject;
- output cannot alter stop/target;
- uncertainty should become WAIT;
- no market state fabrication.

- [ ] **Step 5: Run tests and commit**

```bash
git add src/NesTrading.Core/Agents/Trade tests/NesTrading.Core.Tests/Agents/TradeAgentTests.cs
git commit -m "feat: add constrained trade agent"
```

---

### Task 7: Usage/token/cost budget controller

**Files:**
- Create: `src/NesTrading.Core/Agents/Budget/AiUsageBudget.cs`
- Create: `src/NesTrading.Core/Agents/Budget/AiUsagePolicy.cs`
- Create: `src/NesTrading.Core/Agents/Budget/AiUsageRecord.cs`
- Test: `tests/NesTrading.Core.Tests/Agents/AiUsageBudgetTests.cs`

**Interfaces:**
- Produces:
  - `BudgetReservation TryReserve(AgentRole role, int maxOutputTokens, DateTime utcNow)`
  - `void Settle(BudgetReservation reservation, ProviderCompletion completion)`

- [ ] **Step 1: Write failing quota tests**

V1 budget dimensions:
- max calls/session;
- max calls/agent/session;
- max input tokens/session;
- max output tokens/session;
- max estimated/provider-reported USD cost/session.

Require atomic rejection when any cap would be exceeded.

- [ ] **Step 2: Write concurrency test**

Two simultaneous reservations that together exceed the remaining budget must result in only one successful reservation.

- [ ] **Step 3: Write failed-call accounting test**

Provider failures still count as calls. Token/cost settlement uses actual provider usage if available; reservations prevent overcommit before the call.

- [ ] **Step 4: Implement immutable policy + synchronized budget**

Budget exhaustion returns `BUDGET_EXHAUSTED`; caller does not fall back to an unbudgeted provider request.

- [ ] **Step 5: Run tests and commit**

```bash
git add src/NesTrading.Core/Agents/Budget tests/NesTrading.Core.Tests/Agents/AiUsageBudgetTests.cs
git commit -m "feat: add AI usage and cost budget controller"
```

---

### Task 8: Agent coordinator and audit integration

**Files:**
- Create: `src/NesTrading.Core/Agents/AgentCoordinator.cs`
- Create: `src/NesTrading.Core/Agents/AgentRefreshResult.cs`
- Modify: `src/NesTrading.Core/Storage/Schema.sql`
- Test: `tests/NesTrading.Core.Tests/Agents/AgentCoordinatorTests.cs`

**Interfaces:**
- Produces:
  - `Task<AgentCycleResult> RefreshAsync(AgentCycleInput input, CancellationToken cancellationToken)`

- [ ] **Step 1: Write successful cycle test**

Input:
- valid Market snapshot;
- News source context;
- fresh session/account state;
- fake News/Trade provider outputs.

Expected event order:
1. `MARKET_AGENT_STATE`
2. `NEWS_AGENT_REQUESTED`
3. `NEWS_AGENT_STATE`
4. `TRADE_AGENT_REQUESTED`
5. `TRADE_AGENT_STATE`
6. `AGENT_CYCLE_COMPLETE`

Assert all states use one correlation/cycle ID.

- [ ] **Step 2: Write News failure test**

News timeout/failure:
- Market state may still publish;
- Trade Agent must not be invoked;
- cycle returns non-tradable;
- audit contains failure reason.

- [ ] **Step 3: Write Trade failure test**

Trade provider failure:
- Market + News remain cacheable;
- no Trade state is published;
- cycle non-tradable;
- Risk Engine later sees missing mandatory Trade state and rejects.

- [ ] **Step 4: Write audit-persistence failure test**

If audit write fails before a model-backed state is published, the coordinator must not publish that state to cache. This preserves “database remembers; cache serves” ordering.

- [ ] **Step 5: Implement coordinator**

For V1 refresh order:
1. deterministic Market;
2. persist Market;
3. refresh News;
4. persist News;
5. determine technical candidate;
6. refresh Trade only if guards allow;
7. persist Trade;
8. publish cache;
9. persist cycle summary.

- [ ] **Step 6: Extend audit schema**

Ensure `agent_decisions` stores:
- `cycle_id`
- `agent_name`
- `schema_version`
- `prompt_version`
- `provider_name`
- `model_name`
- `generated_at_utc`
- `valid_until_utc`
- `status`
- `reason_code`
- `raw_response_hash`
- `input_tokens`
- `output_tokens`
- `reported_cost_usd`

Do not store API keys or bearer tokens.

- [ ] **Step 7: Run tests and commit**

```bash
git add src/NesTrading.Core/Agents src/NesTrading.Core/Storage/Schema.sql tests/NesTrading.Core.Tests/Agents
git commit -m "feat: coordinate and audit agent refresh cycles"
```

---

### Task 9: Optional OpenAI Responses provider adapter

**Files:**
- Create: `src/NesTrading.Core/Agents/Providers/OpenAiResponsesProvider.cs`
- Create: `src/NesTrading.Core/Agents/Providers/OpenAiProviderOptions.cs`
- Test: `tests/NesTrading.Core.Tests/Agents/OpenAiResponsesProviderTests.cs`

**Interfaces:**
- Implements: `IAgentTextProvider`.

- [ ] **Step 1: Write request-shape tests using a fake HttpMessageHandler**

Require request to:
- use HTTPS endpoint;
- send bearer token only in Authorization header;
- never include key in JSON/loggable request object;
- use Structured Outputs JSON Schema with strict mode;
- include configured model;
- apply request timeout/deadline;
- cap output tokens.

The current OpenAI Responses API supports schema-constrained structured output through `text.format` using `type: "json_schema"`, `strict: true`, and a supplied schema. The adapter must keep this API-specific shape isolated from the rest of the trading code.

- [ ] **Step 2: Write response parsing tests**

Handle:
- completed structured output;
- refusal;
- incomplete response;
- HTTP 429;
- HTTP 5xx;
- malformed body;
- cancellation;
- missing output text.

Map each to a `ProviderCompletion` status; do not throw raw provider exceptions into the trading runtime.

- [ ] **Step 3: Implement API key loading boundary**

`OpenAiProviderOptions` accepts the key at process composition time. Production composition reads it from an environment/secret mechanism. The provider never persists or returns it.

- [ ] **Step 4: Implement HttpClient adapter**

Do not reference Risk Engine or execution classes from the provider project area.

- [ ] **Step 5: Run tests and commit**

```bash
git add src/NesTrading.Core/Agents/Providers/OpenAi* tests/NesTrading.Core.Tests/Agents/OpenAiResponsesProviderTests.cs
git commit -m "feat: add isolated OpenAI structured-output provider"
```

---

### Task 10: End-to-end agent-to-risk safety tests and CI gate

**Files:**
- Create: `tests/NesTrading.Core.Tests/Agents/AgentToRiskIntegrationTests.cs`
- Modify: `README.md`
- Modify: `.github/workflows/core-tests.yml`

**Interfaces:**
- Verifies complete advisory path without broker/live execution.

- [ ] **Step 1: Write complete ALLOW/LONG path test**

Flow:

`MarketSnapshot -> MarketAgent -> NewsAgent(ALLOW) -> TradeAgent(LONG) -> cache -> RiskEngine -> APPROVED`

Assert:
- one NES;
- unchanged structural stop;
- model confidence does not change size;
- Risk Engine performs final approval.

- [ ] **Step 2: Write News BLOCK path**

Trade provider call count must be zero and Risk Engine receives/derives a non-tradable state.

- [ ] **Step 3: Write stale-state path**

Expire News after a successful agent cycle and before Risk evaluation. Risk Engine must reject `STALE_NEWS_STATE`.

- [ ] **Step 4: Write provider outage path**

Both AI providers fail. Market Agent still works deterministically, but no executable Trade state exists and execution remains impossible.

- [ ] **Step 5: Write budget-exhaustion path**

After exhausting budget, next refresh cannot call provider and cannot create fresh model state.

- [ ] **Step 6: Update README**

Document:
- agent architecture;
- strict structured-output boundary;
- no AI in execution hot path;
- cache TTL behavior;
- budget controller;
- current provider support;
- still no live broker execution.

- [ ] **Step 7: Run full suite**

```powershell
dotnet test NesTrading.sln -c Release
```

Expected: zero failures.

- [ ] **Step 8: Commit**

```bash
git add tests/NesTrading.Core.Tests/Agents README.md .github/workflows/core-tests.yml
git commit -m "test: gate AI agent integration with end-to-end safety cases"
```

---

## Definition of Done

This phase is complete only when:
- all agent outputs are schema-validated and semantically validated;
- stale/invalid/provider-failed outputs cannot become tradable state;
- Market Agent performs zero model calls;
- News and Trade Agent cannot access execution APIs;
- usage/cost budget is enforced atomically;
- audit-before-publish ordering is tested;
- restart begins with an empty AI cache;
- Risk Engine remains final authority;
- full Windows/.NET Framework 4.8 CI is green;
- no secrets are stored in repo, SQLite payloads, JSONL payloads, or exception text.

## Explicitly Deferred

This plan does not implement:
- NinjaTrader AddOn/NinjaScript execution adapter;
- real broker connectivity;
- live market-data subscriptions;
- real-time economic-calendar/news vendor ingestion;
- autonomous strategy changes;
- Supervisor AI;
- agent score weighting;
- dashboard;
- backtesting/walk-forward/Monte Carlo;
- funded-account adapters.

Those remain separate reviewed plans.

## Plan Self-Review

- **Spec coverage:** News, Market, Trade roles; TTL; fail-closed behavior; auditability; token/cost control; OpenAI structured provider boundary; Risk Engine authority are covered.
- **Placeholders:** no TBD/TODO or unspecified error-handling steps remain.
- **Type consistency:** `AgentEnvelope<T>`, `ProviderCompletion`, `AgentRefreshResult<T>`, `AgentStateCache`, and `AiUsageBudget` are defined before downstream tasks consume them.
- **Review Focus coverage:** invalid semantics -> Task 1; late writes/restart -> Task 2; budget exhaustion -> Task 7; cross-agent disagreement -> Tasks 6 and 10.
- **Scope control:** NinjaTrader/broker/news-vendor ingestion remains outside this phase.
