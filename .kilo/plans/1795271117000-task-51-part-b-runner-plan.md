# Task 51 Part B — The runner and its report

## Goal
Replace every `[ ] Verify (user, editor)` play-check line with cases in one `SelfTest.events` sheet that runs headless and prints one `PASS`/`FAIL`/`SKIP` line per case, failing the build on any FAIL.

Part A is done and committed (`22df636`): `expect`/`pass` parse and evaluate, `SelfTest.Cases`, `TestPassed`/`TestFailed`, `TimeoutSeconds` (5 s), case discovery in `ReadyTree`, `CurrentCase` tracking across `wait`.

## Decisions (all confirmed)
1. **`test "<label>"`** — a statement inside case bodies (parser has no bare top-level statements); `ReadyTree` captures the label at load; runtime `test` is idempotent/no-op. Label defaults to block order: `case 1`, `case 2`, …
2. **`self test timeout <n> seconds`** — a statement inside case bodies (confirmed with user); sets static `SelfTest.TimeoutSeconds`. Last-wins across sheets; one SelfTest sheet sets it once by convention.
3. **`when self test ends`** — new `TriggerKind.SelfTestEnds`, raised **once globally** when every case across all self-test sheets has settled (`Passed`/`Failed`/`Skipped`). All sheets listening fire. `ResultLabel` (Part C) reads static `TestPassed`/`TestFailed`.
4. **`TestPassed`/`TestFailed` globals** — synced to `Variables` globals `"TestPassed"`/`"TestFailed"` each tick so bare `TestPassed is 2` works in conditions; bridge reads statics directly (Part C).
5. **Warning capture** — `CaseRecord.Warnings` (`List<string>`); attribute warnings to the running case (`sheet.CurrentCase`) via a chained `OnRuntimeWarning` wrapper on self-test sheets; warnings attach to the FAIL reason and are not swallowed.
6. **SKIP** — cases still `Idle` at settlement → `Skipped` with gate text; never `PASS`.
7. **Timeout failure** — quote the last statement line reached (`case.StatementIndex` tracked in `RunBody`): `timed out after Ns; stuck on "<text>"`.

## Files to change
- `Source/Game/Events/Ast.cs` — add `TestNode`, `TimeoutNode`; extend `CaseRecord` with `Entered`, `GateText`, `StatementIndex`, `Warnings`.
- `Source/Game/Events/EventParser.cs` — `test "<label>"` and `self test timeout <n> seconds` statement parsing (friendly errors for malformed shapes); `ReadyTree` captures label per case.
- `Source/Game/Events/WordRegistry.cs` — registry rows for `test "<label>"`, `self test timeout <n> seconds`, `when self test ends`.
- `Source/Game/Runtime/Triggers.cs` — add `TriggerKind.SelfTestEnds` (after `AllEnemiesDead`).
- `Source/Game/Runtime/EventRuntime.cs`
  - `TriggerClassifier`: classify `when self test ends` → `SelfTestEnds`.
  - `ReadyTree`: capture `test "<label>"` label onto each `CaseRecord`.
  - Tick/drain: after `Time` advances and queues drain, settle cases for self-test sheets; raise `SelfTestEnds` once when all cases across sheets are settled (not per tick — once total).
  - `RunBody`: track `CurrentCase.StatementIndex` per statement; wrap `OnRuntimeWarning` so warnings while a case is `Running` append to `CurrentCase.Warnings`.
  - Sync `TestPassed`/`TestFailed` to `Variables` globals `"TestPassed"`/`"TestFailed"` each tick.
- `Source/Game/Runtime/SelfTest.cs` — `Settle()` / report: per-case ordered lines (`PASS`, `FAIL <reason>`, `SKIP <gate text>`, `TIMEOUT ...`), statics `TestPassed`/`TestFailed`/`TimeoutSeconds`.
- `Tests/Program.cs` — new Part B checks: SKIP for gate-never-opens, timeout quotes last line, warning capture on FAIL, `when self test ends` fires once, label defaulting, `self test timeout` changes deadline, counter globals.
- `keywords.html` — documentation rows for `test`, `self test timeout`, `when self test ends`.

## Failure modes & edge cases
- Sheet with no `expect`/`pass`: behaves exactly as before (no cases, no runner overhead).
- `pass` without prior `expect` in same block: not a case (per Part A discovery); `pass` statement warns outside a case.
- Case times out without ever reaching `expect`: FAIL, reason quotes last line.
- Case enters but gate/condition never re-fires after a `wait`: still `Running` at settlement → then SKIP if never set to a terminal state; report the stuck line.
- Multiple self-test sheets: settlement waits for every case on every self-test sheet; `SelfTestEnds` raised exactly once.
- Determinism: no `random` unless the case fixes `Rng.Seed`.

## Validation
- Rebuild engine-free sources + `Tests/Program.cs` (temp csproj at workspace root, delete after).
- Run suite: all Part A checks still pass; new Part B checks pass; only pre-existing editor-content test (`RTS_Test_0.scene`) crashes — unrelated, needs the Flax editor.
- `keywords.html` rows keep the registry↔reference test green (`keyword-gaps.txt` stays empty).

## Out of scope for Part B
- `SelfTest.scene` / `SelfTest.events` case set (Part C); `ResultLabel` bridge binding (Part C).
- Headless tier in `Tests/Program.cs` running the shipped file (Part D).
- `DESIGN.md` §9, execution-order line, Part E reconciliation (Part E).

Plan file: `.kilo/plans/1795271117000-task-51-part-b-runner-plan.md`
