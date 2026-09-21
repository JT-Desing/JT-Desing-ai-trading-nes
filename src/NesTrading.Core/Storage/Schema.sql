CREATE TABLE IF NOT EXISTS event_ledger (
    event_id TEXT PRIMARY KEY NOT NULL,
    session_id TEXT NOT NULL,
    timestamp_utc TEXT NOT NULL,
    event_type TEXT NOT NULL,
    version INTEGER NOT NULL,
    payload_json TEXT NOT NULL,
    payload_hash TEXT NOT NULL,
    signal_id TEXT NULL,
    trade_id TEXT NULL
);
CREATE INDEX IF NOT EXISTS idx_event_ledger_session_time ON event_ledger(session_id, timestamp_utc);

CREATE TABLE IF NOT EXISTS trades (
    trade_id TEXT PRIMARY KEY NOT NULL,
    session_id TEXT NOT NULL,
    signal_id TEXT NOT NULL,
    opened_at_utc TEXT NULL,
    closed_at_utc TEXT NULL,
    net_pnl_usd REAL NULL
);

CREATE TABLE IF NOT EXISTS candidate_trades (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    event_id TEXT UNIQUE NOT NULL,
    session_id TEXT NOT NULL,
    signal_id TEXT NOT NULL,
    payload_json TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS agent_decisions (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    event_id TEXT UNIQUE NOT NULL,
    session_id TEXT NOT NULL,
    signal_id TEXT NULL,
    cycle_id TEXT NOT NULL,
    agent_name TEXT NOT NULL,
    schema_version TEXT NOT NULL,
    prompt_version TEXT NOT NULL,
    provider_name TEXT NOT NULL,
    model_name TEXT NOT NULL,
    generated_at_utc TEXT NULL,
    valid_until_utc TEXT NULL,
    status TEXT NOT NULL,
    reason_code TEXT NULL,
    raw_response_hash TEXT NULL,
    input_tokens INTEGER NOT NULL DEFAULT 0,
    output_tokens INTEGER NOT NULL DEFAULT 0,
    reported_cost_usd REAL NULL,
    payload_json TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS news_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    event_id TEXT UNIQUE NOT NULL,
    session_id TEXT NOT NULL,
    payload_json TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS market_states (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    event_id TEXT UNIQUE NOT NULL,
    session_id TEXT NOT NULL,
    signal_id TEXT NULL,
    payload_json TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS risk_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    event_id TEXT UNIQUE NOT NULL,
    session_id TEXT NOT NULL,
    signal_id TEXT NOT NULL,
    payload_json TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS execution_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    event_id TEXT UNIQUE NOT NULL,
    session_id TEXT NOT NULL,
    signal_id TEXT NOT NULL,
    trade_id TEXT NULL,
    payload_json TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS session_metrics (
    session_id TEXT PRIMARY KEY NOT NULL,
    payload_json TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS strategy_versions (
    version_id TEXT PRIMARY KEY NOT NULL,
    session_id TEXT NOT NULL,
    payload_json TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS supervisor_reports (
    report_id TEXT PRIMARY KEY NOT NULL,
    session_id TEXT NOT NULL,
    payload_json TEXT NOT NULL
);
