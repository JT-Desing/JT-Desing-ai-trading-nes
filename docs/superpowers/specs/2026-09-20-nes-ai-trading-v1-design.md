# AI Trading NES V1 — Design Specification

Date: 2026-09-20
Repository: JT-Desing/JT-Desing-ai-trading-nes
Status: Design specification for review

## 1. Objective

Build a conservative, auditable AI-assisted intraday trading system focused on a single instrument and a single strategy. V1 prioritizes capital preservation, reproducibility, explainability, deterministic risk controls, and measurable learning over maximum return.

The system must not assume guaranteed profitability. Its first objective is to determine whether a statistically positive edge exists after realistic fees, spread, slippage, latency, and execution constraints.

## 2. Scope

### Included
- Instrument: NES — E-nano S&P 500 futures.
- Trading style: intraday only.
- Strategy: Trend Pullback + VWAP.
- Maximum open positions: 1.
- Maximum contracts in V1: 1 NES.
- Operating agents:
  - News Agent.
  - Market Agent.
  - Trade Agent.
- Deterministic Risk Engine.
- Deterministic Execution Engine.
- NinjaTrader integration target.
- Local SQLite operational storage.
- Append-only JSONL event ledger.
- Private GitHub repository for code, specifications, strategy versions, reports, and historical exports.
- Post-session AI Supervisor.
- Dashboard/reporting layer.
- Backtest, replay, shadow, paper, and live modes using the same strategy logic.

### Explicitly excluded from V1
- Multiple markets.
- Multiple strategies.
- HFT.
- Martingale.
- Averaging down.
- Pyramiding.
- Overnight positions.
- Multiple concurrent positions.
- Dynamic contract scaling by model confidence.
- Direct trading from news.
- Autonomous production strategy modification.
- Multi-account execution.
- Funded-account automation until V1 is validated.

## 3. Core architecture

News Agent ----\
                \
Market Agent ----> Shared structured state --> Trade Agent --> Risk Engine --> Execution Engine --> NinjaTrader
                                                               |
                                                               +--> SQLite / JSONL event ledger
                                                                        |
                                                                        +--> End-of-day metrics
                                                                                 |
                                                                                 +--> AI Supervisor
                                                                                          |
                                                                                          +--> Reports
                                                                                          +--> Agent scoring
                                                                                          +--> Improvement proposals
                                                                                          +--> Dashboard

### Design rule
LLMs are not in the execution hot path. Real-time order approval and submission are deterministic. AI agents may classify, contextualize, and propose, but the Risk Engine has final authority.

## 4. System authority model

### News Agent
May output:
- ALLOW
- CAUTION
- BLOCK

May not:
- Submit BUY or SELL orders.
- Change risk limits.
- Change contract size.

### Market Agent
May:
- Classify market regime.
- Detect trend direction.
- Detect pullback setup.
- Produce setup score.
- Produce structured evidence.

May not:
- Submit orders.
- Override News or Risk states.

### Trade Agent
May output:
- LONG
- SHORT
- WAIT
- EXIT_RECOMMENDATION

May not:
- Send broker orders.
- Increase size.
- Widen stops.
- Override the Risk Engine.

### Risk Engine
May:
- APPROVE.
- REJECT.
- Lock trading for the session.
- Trigger emergency stop conditions.

Risk Engine decisions are deterministic.

### Execution Engine
May:
- Submit only approved orders.
- Attach mandatory protective bracket orders.
- Reconcile broker state.
- Cancel/replace according to deterministic rules.
- Refuse duplicate signals/orders.

### Supervisor
May:
- Audit.
- Score.
- Explain.
- Detect degradation.
- Create improvement proposals.
- Recommend shadowing or disabling an agent.

May not:
- Trade.
- Modify production strategy automatically.
- Change live risk limits automatically.

## 5. Trading session

Initial V1 trading window:
- 09:40–11:30 ET.
- No new entries after 11:30 ET.
- No overnight positions.
- Session must end flat.

The exact window is configurable and must be validated statistically before live deployment.

## 6. Timeframes

- 15-minute timeframe: market regime and directional context.
- 5-minute timeframe: pullback and entry confirmation.

V1 does not use 1-minute execution logic.

## 7. Strategy indicators

V1 uses:
- Session VWAP.
- EMA 20.
- EMA 50.
- ATR.
- Volume.
- Basic price structure: higher highs/lows and lower highs/lows.

No additional indicator should be added without an experiment demonstrating incremental value.

## 8. Market regime

Possible regime states:
- TREND_UP.
- TREND_DOWN.
- RANGE.
- HIGH_VOLATILITY.
- INVALID_DATA.

### Candidate LONG regime
Initial conditions:
- Price above VWAP.
- EMA20 > EMA50.
- EMA20 slope positive.
- Higher-high / higher-low structure present.
- ATR within acceptable range.
- Regime not RANGE.
- Regime not HIGH_VOLATILITY unless explicitly validated later.

### Candidate SHORT regime
Mirror conditions:
- Price below VWAP.
- EMA20 < EMA50.
- EMA20 slope negative.
- Lower-high / lower-low structure present.
- ATR within acceptable range.
- Regime not RANGE.
- Regime not HIGH_VOLATILITY unless explicitly validated later.

## 9. Pullback setup

The system does not chase momentum.

A valid candidate requires:
1. Established trend.
2. Impulse in trend direction.
3. Pullback toward one or more of:
   - VWAP.
   - EMA20.
   - Prior structural support/resistance.
4. Trend structure remains valid.
5. Entry confirmation appears after the pullback.

A pullback that invalidates structure is rejected.

## 10. Entry confirmation

Initial confirmation dimensions:
- Rejection/recovery around the pullback zone.
- Close back in trend direction.
- Volume not abnormally weak.
- Structure remains valid.
- News state permits trading.
- Setup score meets threshold.
- Risk Engine validates stop distance, costs, and limits.

Exact numerical thresholds remain configuration values to be established via backtesting and validation, not guessed into production.

## 11. Setup score

Initial conceptual 100-point score:
- Trend alignment: 20.
- Price vs VWAP: 15.
- EMA structure: 15.
- Market structure: 20.
- Pullback quality: 15.
- Volume confirmation: 10.
- Volatility quality: 5.

Initial candidate threshold:
- ALLOW: score >= 75.
- CAUTION: score >= 85.
- BLOCK: no trade.

These thresholds are hypotheses and must be tested.

## 12. News Agent

The News Agent specializes only in information materially relevant to S&P 500 intraday conditions.

Initial event classes include:
- FOMC.
- Federal Reserve rate decisions and material statements.
- CPI.
- PPI.
- NFP / Employment Situation.
- GDP.
- Jobless claims.
- Major Treasury/rate shocks.
- Material systemic/geopolitical shocks.
- Other scheduled high-impact U.S. macro events.

### Output schema concept

{
  "symbol": "NES",
  "state": "ALLOW | CAUTION | BLOCK",
  "directional_bias": "BULLISH | BEARISH | NEUTRAL | UNKNOWN",
  "importance": "LOW | MEDIUM | HIGH | CRITICAL",
  "event_code": "string",
  "source_timestamp": "ISO-8601",
  "valid_until": "ISO-8601",
  "confidence": 0.0,
  "reason_codes": []
}

### Scheduled high-impact event lock
Initial conservative hypothesis:
- No new entries 5 minutes before and 5 minutes after high-impact scheduled events.
- Some event classes may later use 15/15-minute windows if evidence supports it.

The News Agent is a filter in V1, not a news-trading engine.

## 13. Market Agent output

Conceptual schema:

{
  "symbol": "NES",
  "regime": "TREND_UP | TREND_DOWN | RANGE | HIGH_VOLATILITY | INVALID_DATA",
  "direction": "LONG | SHORT | NONE",
  "setup": "VWAP_PULLBACK | NONE",
  "setup_score": 0,
  "volatility": "LOW | NORMAL | HIGH",
  "valid": true,
  "valid_until": "ISO-8601",
  "reason_codes": []
}

## 14. Trade Agent output

Conceptual schema:

{
  "symbol": "NES",
  "decision": "LONG | SHORT | WAIT | EXIT_RECOMMENDATION",
  "confidence": 0.0,
  "signal_id": "unique-id",
  "valid_until": "ISO-8601",
  "reason_codes": []
}

Confidence does not control position size in V1.

## 15. Risk policy

Starting capital assumption for initial personal-account research: USD 150.

Initial V1 constraints:
- Maximum contracts: 1 NES.
- Maximum open positions: 1.
- Maximum trades/day: 2.
- Target risk/trade: approximately 1% of account equity.
- Hard maximum risk/trade: 2% of account equity.
- Daily soft-loss threshold: approximately 2%.
- Daily hard-loss threshold: approximately 3%.
- Two losing trades => end session.
- Mandatory cooldown after trade close: initial hypothesis 10 minutes.
- Martingale prohibited.
- Averaging down prohibited.
- Stop widening prohibited.
- Overnight positions prohibited.

### Risk calculation

Total estimated risk must include:
- Price stop distance.
- Commissions.
- Exchange/clearing/NFA fees where applicable.
- Expected slippage.

If the technically valid stop causes total risk to exceed the maximum risk budget, the trade is rejected. The stop is never artificially tightened solely to fit the budget.

## 16. Stops and targets

### Stop
Placed beyond the structural invalidation point, with an optional validated buffer.

Possible inputs:
- Last swing.
- ATR-aware buffer.
- Strategy invalidation level.

### Target
Initial hypothesis:
- Fixed target around 2R.

Validation must compare at least:
- 1.5R.
- 2.0R.
- 2.5R.

V1 excludes:
- Trailing stop.
- Automatic break-even.
- Partial exits.

These may be introduced only as controlled experiments.

## 17. Mandatory bracket protection

Every live entry must include or immediately establish:
- Entry.
- Stop-loss.
- Take-profit.

A naked live position is considered a critical fault.

## 18. No-trade conditions

The Risk Engine must reject a new trade if any of the following is true:
- Regime = RANGE.
- Regime = INVALID_DATA.
- News = BLOCK.
- Market data stale.
- Required agent unavailable.
- Broker disconnected.
- Unknown/unreconciled broker position.
- Required stop exceeds risk budget.
- Net risk/reward below configured minimum.
- Daily loss threshold reached.
- Maximum trades reached.
- Cooldown active.
- Existing position open.
- Spread abnormal.
- Expected slippage abnormal.
- Strategy/system state inconsistent.
- Session outside permitted time window.

## 19. State machine

Core states:
- SYSTEM_OFF.
- INITIALIZING.
- DATA_VALIDATION.
- READY.
- TRADING_ENABLED.
- POSITION_OPEN.
- MANAGING_POSITION.
- FLAT.
- SESSION_COMPLETE.

Protective lock states:
- NEWS_LOCK.
- RISK_LOCK.
- DATA_STALE.
- BROKER_DISCONNECTED.
- DAILY_LOSS_LOCK.
- EMERGENCY_STOP.

Orders are impossible unless the current state explicitly permits them.

## 20. Execution requirements

- Event-driven market processing.
- Persistent broker/data connection where supported.
- Unique signal_id, trade_id, and order_id.
- Idempotent order submission.
- No blind retry after unknown broker acknowledgement.
- Broker must be queried/reconciled before resubmission.
- Broker state is authoritative for live positions.
- Reconnect workflow must reconcile open positions and orders.
- Local clock synchronized.
- Latency timestamps captured for:
  - Market data arrival.
  - Signal generation.
  - Risk approval.
  - Order submission.
  - Broker acknowledgement.
  - Fill.

## 21. Latency design

The LLM is not called at the instant an order must be executed.

AI agents maintain fresh structured state. The deterministic engine consumes cached state.

Each AI state has:
- generated_at.
- valid_until.
- freshness threshold.

Expired AI state causes NO_TRADE.

Strategy validation must include simulated latency and slippage stress.

## 22. Storage

### Local operational storage
SQLite:
- trades.
- candidate_trades.
- agent_decisions.
- news_events.
- market_states.
- risk_events.
- execution_events.
- session_metrics.
- strategy_versions.
- supervisor_reports.

### Append-only ledger
Daily JSONL files:
- Complete event chronology.
- Never rewritten in normal operation.

### GitHub
Private repository stores:
- Source code.
- Specs.
- Configuration templates without secrets.
- Strategy versions.
- Exported historical summaries.
- Supervisor reports.
- Experiments.
- Documentation.

SQLite live database is not continuously committed to Git.

### Secrets
Never store broker passwords, API keys, withdrawal-capable credentials, or secrets in repository files/logs.

## 23. Candidate-trade logging

The system records both:
- Executed trades.
- Rejected/blocked candidate trades.

For blocked candidates, store:
- Setup.
- Agent states.
- Risk rejection reason.
- Hypothetical entry/stop/target where safe to calculate.
- Counterfactual outcome for research.

This allows evaluation of filters such as the News Agent.

## 24. Supervisor

The AI Supervisor does not run continuously.

Initial cadence:
- Daily: after session close.
- Weekly: deeper review.
- Monthly: strategic review.

During live trading, deterministic health monitors remain active but no supervisor LLM is required.

### Supervisor responsibilities
- Summarize session.
- Attribute gains/losses.
- Score agents by role.
- Detect performance degradation.
- Detect confidence miscalibration.
- Identify recurring loss patterns.
- Identify useful vetoes/blocked losses.
- Generate improvement proposals.
- Update reports/dashboard data.

### Supervisor restrictions
- Cannot trade.
- Cannot change live parameters.
- Cannot promote experiments to production.

## 25. Agent evaluation

Agent scores are role-specific.

### News Agent
Evaluate:
- High-impact event detection.
- Missed important events.
- False blocks.
- Timeliness.
- Correct veto contribution.
- Avoided-loss attribution.

### Market Agent
Evaluate:
- Regime classification quality.
- Setup quality.
- Directional contribution.
- False-trend classifications.
- Performance by regime.

### Trade Agent
Evaluate:
- LONG/SHORT/WAIT decision quality.
- Entry timing.
- Net expectancy contribution.
- Confidence calibration.
- Avoidable late-entry losses.

### Execution Engine
Evaluate:
- Slippage.
- Reject rate.
- Fill quality.
- Latency.
- Duplicate order prevention.
- Reconciliation correctness.

### Risk Engine
Evaluate:
- Rule violations: must remain zero.
- Avoided excessive-risk trades.
- Drawdown containment.
- Stop-protection integrity.

## 26. Attribution

The system must distinguish:
- Strategy loss that was statistically normal.
- Market-regime classification error.
- Setup-quality error.
- News/context error.
- Trade-decision error.
- Risk error.
- Execution error.
- Data/system error.

Explanations must be grounded in pre-trade recorded facts, not invented after the outcome is known.

## 27. Improvement workflow

Supervisor proposal
-> experiment branch/config
-> backtest
-> out-of-sample validation
-> walk-forward validation
-> Monte Carlo/stress
-> replay
-> shadow
-> paper
-> human approval
-> production

No direct self-modification in live trading.

## 28. Validation pipeline

Modes:
1. BACKTEST.
2. REPLAY.
3. SHADOW.
4. PAPER.
5. LIVE.

Same strategy logic should be reused across modes as much as technically possible.

### Minimum provisional live-promotion gates
- At least 150 evaluable trades.
- Positive expectancy after realistic costs.
- Profit factor > 1.25.
- Positive out-of-sample result.
- Positive walk-forward evidence.
- Robustness to realistic slippage/latency.
- No catastrophic drawdown.
- Zero risk-rule violations.
- No dependency on one or two extreme outlier days.

These are provisional gates and may be tightened before live deployment.

## 29. Backtest realism

Backtesting must model:
- Commissions.
- Exchange/clearing/NFA fees.
- Spread.
- Slippage.
- Latency.
- Missed fills where relevant.
- Partial fills where technically relevant.

Stress scenarios should include progressively worse slippage and latency.

A strategy that loses its edge under modest realistic friction is rejected.

## 30. Replay and shadow

### Replay
Historical event stream can be replayed into strategy logic to compare versions under identical conditions.

### Shadow
On live market data, the system emits and records what it would do but sends no broker orders.

Shadow mode is mandatory before live trading.

## 31. Git/versioning model

Suggested repository structure:

ai-trading-nes/
  docs/
    superpowers/specs/
  agents/
    news/
    market/
    trade/
    supervisor/
  risk/
  execution/
  strategy/
  storage/
  analytics/
  dashboard/
  tests/
  configs/
  reports/
  experiments/
  .github/workflows/

Every production strategy release must be versioned.

Session logs should record the exact versions of:
- Strategy.
- News Agent.
- Market Agent.
- Trade Agent.
- Risk Engine.
- Execution Engine.

## 32. Security

- Repository private.
- Least-privilege credentials.
- Broker credentials restricted to execution environment.
- GitHub should not require broker withdrawal permissions.
- OpenAI/supervisor secrets may use secure secret storage.
- No secrets in logs.
- No secrets in commits.
- Separate execution credentials from analytics/supervision credentials.

## 33. Dashboard objectives

First screen must answer:
1. Are we profitable?
2. Are we within risk limits?
3. Are agents healthy?
4. What is deteriorating?
5. What requires attention?

Core metrics:
- Equity.
- Daily/weekly/monthly net P&L.
- P&L in R.
- Profit factor.
- Expectancy.
- Maximum drawdown.
- Win rate as a supporting metric, not primary objective.
- Trades/day.
- Costs and slippage.
- Agent scores.
- Agent score trends.
- Rejected trades by reason.
- Avoided-loss estimates.
- Execution latency.
- System health.

## 34. Primary optimization objective

Do not optimize simply for maximum P&L.

Primary objective:
maximize risk-adjusted positive expectancy subject to strict drawdown and risk constraints.

## 35. Failure policy

Critical failure conditions must fail closed.

Examples:
- Unknown broker position -> disable new trades.
- Stale data -> disable new trades.
- Missing mandatory agent state -> disable new trades.
- Order acknowledgement uncertainty -> reconcile before new action.
- Stop not confirmed -> emergency handling.
- Risk database/storage failure -> disable new trades if auditability cannot be guaranteed.

## 36. Future phases, explicitly deferred

After V1 proves an edge:
- Additional strategies.
- Additional instruments.
- Dynamic capital allocation.
- Correlation engine.
- Multiple agents per role.
- Funded-account adapters.
- Multi-account orchestration.
- Portfolio-level risk.
- Additional execution venues.

None should be implemented before V1 is validated.

## 37. Definition of V1 success

V1 succeeds if it can demonstrate, under controlled validation:
- Reliable operation.
- Zero material risk-control violations.
- Complete audit trail.
- Reproducible results.
- Positive net expectancy after realistic friction.
- Stable behavior out of sample.
- Explainable causes of gains/losses.
- Measurable contribution of each AI agent.
- Safe promotion path from simulation to live.

V1 does not succeed merely because it produces a short period of high returns.
