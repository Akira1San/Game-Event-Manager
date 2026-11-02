# Plan: Fix mouse click hit test on UI buttons

**File:** `plans/1793221871396-fix-ui-button-click-hit-test.md`
**Scope:** `EventManager/EventManager/Source/Game/Bridge/EventsManager.cs`

## Problem

Clicking LMB on PlayButton or QuitButton in `scenes/menu_test` does nothing. `InputEdges()` detects `GetMouseButtonDown(MouseButton.Left)`, iterates all UIControls, but `MouseOverUIControl()` returns `false` for every button, so no `Clicked` trigger is raised and `Menu.events` never runs `load scene` / `quit game`.

## Root Cause

`MouseOverUIControl(EventsManager.cs:470)` computes the hit-test center as `new Float2(ui.Position.X, ui.Position.Y)` and treats it as screen-space top-left-origin coordinates. This is incorrect because:

1. `Actor.Position` for a UIControl under a `UICanvas` is world-space position, affected by the canvas's 180° X-axis rotation (flips Y).
2. No screen center (`Screen.Size * 0.5`) is added to convert from center-origin / world space to screen-space top-left origin.
3. No parent-chain position accumulation (UIControl -> UICanvas -> scene root).
4. The `yHitInverted` fallback only covers a narrow band around `-center.Y`, missing most of the button's hit box.

## Fix

Rewrite `MouseOverUIControl` in `EventsManager.cs` to:

1. Read `Screen.Size` and compute `screenCenter = Size * 0.5`.
2. Walk up the parent chain from the `UIControl` to the scene root, accumulating `node.Position.X/Y` into a running `center`.
3. Add `screenCenter` to convert to screen-space coordinates.
4. Flip Y so screen space (top-left origin, Y down) matches the coordinate space used by `Input.MousePosition`.
5. Keep the existing generous minimum hit box (120x40) and hit-test against `mousePos`.
6. Retain the debug `No hit` logging so the user can verify `center` vs `mouse` values in the editor console.

Also update the debug `No hit` message in `InputEdges()` to print the computed screen-space `center` (not just raw `ui.Position`) so the coordinate conversion can be verified.

## What is NOT in scope

- Scene import / `load scene "load_level_test"` not finding the scene (separate issue, covered in `task_debug.md` Issue 1).
- `menu_test.scene` vs `Menu.scene` setup — the EventsManager and button actors already exist in `menu_test.scene`.
- Right Mouse / Middle Mouse click detection — only LMB `is clicked` is reported broken.

## Validation

1. Open `menu_test.scene` in Flax editor, press Play.
2. Click PlayButton — editor console should show `Clicked UI actor: PlayButton` and scene should load `load_level_test`.
3. Click QuitButton — editor console should show `Clicked UI actor: QuitButton` and game should quit.
4. Click off-button — no `Clicked UI actor` message should appear.
5. Confirm existing tests still pass (`dotnet test` in `EventManager/EventManager/Source/Game` if available, or the test project).

## Rollback

Revert `EventsManager.cs` changes; the previous behavior (no hit) is the current broken state, so no data loss.
