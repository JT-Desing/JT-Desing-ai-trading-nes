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

## Agent integration

The repository now also contains a **structured advisory-agent layer**. It does not replace deterministic strategy, risk, or execution code.

### Market Agent

The Market Agent is deterministic in V1. It wraps the existing Trend Pullback + VWAP evaluator and performs **zero LLM calls**.

It publishes a versioned `market.v1` envelope containing the same regime, direction, setup score, TTL, and reason codes produced by the deterministic strategy.

### News Agent

The News Agent accepts pre-collected calendar/news context and may use a model provider to classify only:

- `ALLOW`
- `CAUTION`
- `BLOCK`

Its schema does not permit BUY/SELL instructions, contract sizing, entries, stops, or targets.

Provider refusals, malformed JSON, unsupported enum values, wrong symbols, stale timestamps, future timestamps, timeout, API failure, or exhausted AI budget do **not** create a tradable News state.

### Trade Agent

The Trade Agent may emit only:

- `LONG`
- `SHORT`
- `WAIT`
- `EXIT_RECOMMENDATION`

Before a provider call is allowed, deterministic guards require a fresh Market state, fresh News state, a technical candidate, known broker position, no open position, and an unlocked system state.

A model decision that contradicts the deterministic candidate direction is rejected. Model confidence does not modify position size; V1 remains fixed at one NES contract maximum.

### Strict schema boundary

Model-backed output is parsed through strict semantic validation before it can enter the state cache.

Agent envelopes include:

- schema version;
- prompt version;
- provider/model metadata;
- generation and expiration timestamps;
- received timestamp;
- SHA-256 hash of the raw response;
- immutable validated domain payload.

Unknown top-level properties are rejected rather than ignored. This prevents a provider from silently introducing fields such as `contracts`, `stop`, or other risk-changing instructions.

### TTL and atomic state cache

News, Market, and Trade states are stored independently in an in-memory cache.

The cache:

- never treats expired state as usable;
- refuses an older state that attempts to overwrite a newer one;
- rejects state received after its own expiry;
- starts empty after process restart.

A restart therefore cannot assume previously cached AI context remains valid.

### AI usage and cost controls

A synchronized budget controller enforces, before provider calls:

- maximum calls per session;
- maximum calls per agent;
- input-token budget;
- output-token budget;
- estimated/provider-reported cost budget.

Reservations are atomic so concurrent agents cannot independently overcommit the same remaining budget. Failed provider calls still count as calls. Budget exhaustion fails closed and does not trigger an unbudgeted fallback request.

### Agent Coordinator and audit-before-publish

The coordinator runs the advisory cycle in this order:

`Market -> persist -> News -> persist -> technical candidate -> Trade -> persist -> cache publish -> cycle summary`

Model-backed state is validated without publishing first. If its audit write fails, that state is **not published to the shared cache**.

The agent audit records correlation IDs, schemas, prompts, provider/model metadata, TTL, response hashes, usage, costs, and explicit reason codes. Secrets are excluded.

### OpenAI provider boundary

An isolated OpenAI Responses API adapter is implemented for Structured Outputs.

It uses schema-constrained `text.format` requests with `type: json_schema` and strict validation. The API key is supplied only at process composition time (for example through `OPENAI_API_KEY`) and is sent only as the Bearer authorization header; it is not included in request JSON, agent envelopes, audit events, or provider error payloads.

The adapter maps completed responses, refusals, incomplete responses, HTTP failures, malformed responses, deadlines, and cancellation into the common provider contract consumed by the agents.

**This does not mean live OpenAI trading is enabled.** A production runtime still needs deliberate configuration, approved credentials, and a real news/calendar data source.

### Final authority remains deterministic

The complete advisory path is:

`Market data -> Market Agent -> News Agent -> Trade Agent -> validated cache -> Risk Engine -> execution`

The Risk Engine independently re-checks freshness, News policy, direction consistency, daily/session limits, structural stop risk, fees, slippage, spread, and reward/risk. A previously valid AI cycle can therefore still be rejected moments later if state has expired or risk conditions have changed.

## What is NOT implemented yet

This repository must not be treated as a live trading robot.

Still deferred to separate reviewed plans:

- Real-time economic-calendar/news-vendor ingestion.
- Production runtime composition with approved OpenAI credentials.
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
