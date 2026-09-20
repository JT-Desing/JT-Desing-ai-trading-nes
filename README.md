# JT-Desing AI Trading NES

Deterministic, safety-first research runtime for an AI-assisted intraday trading system focused on **NES (E-nano S&P 500 futures)**.

> **Current status: research / simulation only. No live broker execution is implemented.**

## What this repository is building

The long-term system is designed around one principle:

**AI may interpret context, but deterministic code controls risk and execution.**

The V1 scope intentionally stays narrow:

- One market: NES.
- One strategy: Trend Pullback + VWAP.
- Intraday only.
- One open position maximum.
- One NES contract maximum.
- Maximum two trades per session.
- No martingale.
- No averaging down.
- No pyramiding.
- No overnight positions.
- No AI-controlled position sizing.
- No autonomous production strategy changes.

The design specification lives at:

`docs/superpowers/specs/2026-09-20-nes-ai-trading-v1-design.md`

The first implementation plan lives at:

`docs/superpowers/plans/2026-09-20-nes-core-safe-runtime.md`

## Current implemented milestone

The current branch implements the **deterministic simulation core**.

### Domain contracts

Immutable contracts exist for:

- News state.
- Market state.
- Trade decisions.
- Trade candidates.
- Risk decisions.
- Trade results.

V1 domain guards reject unsupported symbols, invalid confidence ranges, invalid setup scores, non-UTC timestamps, and contract counts other than one NES.

### Fail-closed state machine

The runtime explicitly models:

- SYSTEM_OFF.
- INITIALIZING.
- DATA_VALIDATION.
- READY.
- TRADING_ENABLED.
- POSITION_OPEN.
- MANAGING_POSITION.
- FLAT.
- SESSION_COMPLETE.

Protective states include:

- NEWS_LOCK.
- RISK_LOCK.
- DATA_STALE.
- BROKER_DISCONNECTED.
- DAILY_LOSS_LOCK.
- EMERGENCY_STOP.

Invalid transitions are rejected rather than inferred.

### Trend Pullback + VWAP strategy

The deterministic strategy evaluates:

- Price relative to VWAP.
- EMA20 / EMA50 alignment.
- EMA20 slope.
- Higher-high / higher-low structure.
- Lower-high / lower-low structure.
- ATR.
- Volume.
- Pullback interaction.
- Entry confirmation.
- Anti-chasing distance.

The current scoring weights and thresholds are **research hypotheses**, not claims of profitability. They must later be validated with historical and out-of-sample data.

### Deterministic Risk Engine

The Risk Engine is the final authority before execution.

Current V1 defaults include:

- Starting research equity assumption: USD 150.
- One NES contract.
- Target risk: approximately 1%.
- Hard per-trade risk cap: 2%.
- Daily soft loss lock: 2%.
- Daily hard loss lock: 3%.
- Maximum two trades/day.
- Maximum two losing trades/day.
- Ten-minute cooldown.
- Initial session window: 09:40–11:30 ET.
- No weekends.
- Fresh News, Market, and Trade state required.
- Spread and expected slippage limits.
- Minimum net reward/risk.
- Signal ID and direction consistency.
- Existing-position and unknown-broker-position protection.

Risk includes:

`price stop risk + estimated round-trip fees + expected slippage`

If a technically valid stop exceeds the hard risk budget, the trade is rejected. The engine does **not** tighten the structural stop simply to force a trade through.

### Audit storage

Two complementary storage mechanisms are implemented:

**JSONL append-only ledger**

- One event per line.
- Existing events are never rewritten by normal append operations.
- Payload SHA-256 integrity checks.
- Duplicate event ID detection.
- Corruption fails closed.

**SQLite operational audit store**

- Transactional append.
- Session/event correlation.
- Session reconstruction after restart.
- Trade count reconstruction.
- Losing-trade reconstruction.
- Last-close reconstruction.
- Open-position reconstruction.

SQLite pooling is disabled in this local audit adapter so the database file is deterministically released on Windows when the store is disposed.

### Simulated Execution Engine

The simulation adapter supports:

- Mandatory ENTRY + STOP + TARGET bracket.
- One trade ID linking all three orders.
- Duplicate signal prevention.
- One open simulated trade maximum.
- Adverse entry slippage.
- Adverse exit slippage.
- Round-trip fees.
- Forced missed-fill scenarios.
- Broker rejection simulation.
- Unknown ACK handling.
- Mandatory reconciliation before an unknown order can be retried.

An ACK with unknown broker state is **never blindly retried**.

### Paper Session Runner

The paper runner connects:

`Market snapshot -> technical setup -> News state -> Trade decision -> Risk Engine -> audit persistence -> simulated execution -> outcome -> audit reconstruction`

Important ordering rule:

1. Detect candidate.
2. Persist candidate.
3. Evaluate risk.
4. Persist risk decision.
5. Only then call execution.

If persistence fails before execution, the trade is not submitted.

If persistence fails after execution may have occurred, the runtime enters an emergency-safe state rather than assuming it is flat.

Technical candidates are recorded even when News later blocks them. This enables future counterfactual analysis of whether News vetoes avoided losses or incorrectly blocked profitable trades.

## What is NOT implemented yet

This repository must not be treated as a live trading robot.

Still deferred to separate reviewed plans:

- Real-time News Agent integrations.
- OpenAI agent calls.
- Production schema validation for AI responses.
- NinjaTrader AddOn / NinjaScript adapter.
- Live market-data adapter.
- Live broker order submission.
- Real OCO/bracket reconciliation.
- Broker disconnect/restart recovery.
- Partial-fill handling against a real broker.
- Supervisor AI.
- Agent performance scoring.
- Attribution/counterfactual analytics.
- Dashboard.
- Historical market-data ingestion.
- Backtesting.
- Out-of-sample testing.
- Walk-forward validation.
- Monte Carlo robustness testing.
- Live replay.
- Shadow trading.
- Broker-connected paper trading.
- Live deployment.

## Promotion path

The intended promotion path remains:

`BACKTEST -> OUT-OF-SAMPLE -> WALK-FORWARD -> MONTE CARLO -> REPLAY -> SHADOW -> PAPER -> MICRO LIVE`

No stage should be skipped solely because recent simulated results look attractive.

Provisional live-promotion requirements from the design include:

- At least 150 evaluable trades.
- Positive expectancy after realistic costs.
- Profit factor above 1.25.
- Positive out-of-sample evidence.
- Positive walk-forward evidence.
- Robustness to realistic latency/slippage.
- No material risk-control violations.
- No dependence on one or two outlier sessions.

These gates can be tightened later.

## Running the core tests

The project targets .NET Framework 4.8 and is continuously verified on Windows through GitHub Actions.

Typical command on a compatible Windows development environment:

```powershell
dotnet restore NesTrading.sln
dotnet build NesTrading.sln -c Release --no-restore
dotnet test NesTrading.sln -c Release --no-build
```

CI workflow:

`.github/workflows/core-tests.yml`

The CI gate has been observed failing on intentional/test-first regressions and returning green after their fixes, including the hard-risk-cap diagnostic case.

## Safety philosophy

The system should prefer a missed opportunity over an unbounded or unexplained trade.

Examples of fail-closed behavior include:

- stale agent state -> no trade;
- unknown broker position -> no trade;
- duplicate signal -> no trade;
- storage integrity failure -> no trade;
- daily risk limit reached -> no trade;
- excessive stop risk -> no trade;
- unexpected system state -> no trade;
- unknown order ACK -> reconcile before retry;
- possible exposure without reliable audit -> emergency state.

## Financial-risk note

This code is experimental research software. Futures trading can lose capital rapidly. Passing software tests does not demonstrate that a trading strategy has positive expectancy, sufficient liquidity, acceptable transaction costs, or suitability for a particular account.

Before any real-money deployment, NES liquidity, spread, commissions, slippage, execution quality, and broker/NinjaTrader compatibility must be measured using current real-world data.
