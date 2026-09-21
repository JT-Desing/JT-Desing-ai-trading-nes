# SDD ledger — plan: docs/superpowers/plans/2026-09-20-nes-agent-integration.md

Execution mode: inline via GitHub connector + Windows CI; no subagent dispatcher is available.

Pre-flight:
- Task 1 -> Task 3 conflict: Task 1 validator tests need ProviderCompletion before Task 3 formally defines it.
- Ruling: define ProviderCompletion/ProviderCompletionStatus in Task 1 as shared agent-provider response contracts; Task 3 will add IAgentTextProvider, ProviderRequest, fake provider, and deadline wrapper around those contracts. This preserves the plan interfaces and avoids temporary duplicate types. Cost if wrong: one type relocation/refactor.
- Task 1 -> Tasks 5/6: strict validators produce domain-safe NewsState/TradeDecision envelopes; interfaces align.
- Task 2 -> Tasks 5/6/8: cache consumes AgentEnvelope<T>; interfaces align.
- Task 7 -> Tasks 5/6/8: budget controller consumed by model-backed agents; interfaces align.

Task 1: complete (tests: GitHub Actions run #49 -> build success, 133/133 passed)

Task 2: complete (tests: GitHub Actions run #52 -> 141/141 passed)

Task 3: complete (tests: GitHub Actions run #59 -> build success, 152/152 passed)

Task 4: complete (tests: GitHub Actions run #62 -> build success, 157/157 passed)
Ruling: execute Task 7 before Tasks 5/6 because NewsAgent and TradeAgent consume AiUsageBudget — preserves declared interfaces and avoids temporary budget stubs — cost if wrong: reorder only, no behavior change.
Ruling: AiUsageBudget.TryReserve accepts estimatedInputTokens in addition to maxOutputTokens — input-token and cost caps cannot be made atomic before a provider call otherwise — cost if wrong: one public method signature adjustment.

Task 7: complete (executed before Tasks 5/6 by ruling; tests: GitHub Actions run #66 -> build success, 168/168 passed)
Ruling: create AgentRefreshResult<T> in Task 5 rather than Task 8 — Tasks 5/6 need the shared result contract before the coordinator exists — cost if wrong: file/type relocation only.

Task 5: complete (tests: GitHub Actions run #69 -> build success, 180/180 passed)

Task 6: complete (tests: GitHub Actions run #75 -> build success, 195/195 passed)

Ruling: Task 8 requires audit-before-publish, while Tasks 5/6 currently publish inside RefreshAsync. Add validated-without-publish paths to NewsAgent/TradeAgent and let AgentCoordinator persist first, then publish to the shared cache. Existing RefreshAsync behavior remains unchanged for standalone use. Cost if wrong: one additional agent API path; safety benefit is preventing unaudited model state from reaching runtime.

Ruling: execute Task 7 before Tasks 5 and 6 — News/Trade Agents consume AiUsageBudget, so implementing the budget first avoids temporary duplicate interfaces; scope unchanged. Cost if wrong: task-order refactor only.
