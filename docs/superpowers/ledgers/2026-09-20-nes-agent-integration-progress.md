# SDD ledger — plan: docs/superpowers/plans/2026-09-20-nes-agent-integration.md

Execution mode: inline via GitHub connector + Windows CI; no subagent dispatcher is available.

Pre-flight:
- Task 1 -> Task 3 conflict: Task 1 validator tests need ProviderCompletion before Task 3 formally defines it.
- Ruling: define ProviderCompletion/ProviderCompletionStatus in Task 1 as shared agent-provider response contracts; Task 3 will add IAgentTextProvider, ProviderRequest, fake provider, and deadline wrapper around those contracts. This preserves the plan interfaces and avoids temporary duplicate types. Cost if wrong: one type relocation/refactor.
- Task 1 -> Tasks 5/6: strict validators produce domain-safe NewsState/TradeDecision envelopes; interfaces align.
- Task 2 -> Tasks 5/6/8: cache consumes AgentEnvelope<T>; interfaces align.
- Task 7 -> Tasks 5/6/8: budget controller consumed by model-backed agents; interfaces align.

Task 1: complete (tests: GitHub Actions run #49 -> build success, 133/133 passed)
