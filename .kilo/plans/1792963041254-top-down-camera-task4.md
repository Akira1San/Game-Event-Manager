# Plan: Add/verify top-down camera for Player in Task 4 Part A

## Goal

Ensure the Top-Down Shooter (Task 4) has a working top-down camera so the player can see the action when moving with WASD and shooting with the mouse. `camera follows` / `camera looks at` are NOT implemented yet (parked for Task 9), so this is a static camera positioned and angled manually in the scene.

## Context

- `Scene.scene` already contains a `FlaxEngine.Camera` actor named "Camera" at `EventManager/EventManager/Content/Scene.scene:88-104`, positioned at `(0, 243, -202)` looking downward. Its suitability for top-down play has not been verified.
- Player actor is named "Player" at `Scene.scene:106-133`, positioned near `(0, 79, -149)`.
- Bullet ("Bullet", `Scene.scene:198`) and Enemy ("Enemy", `Scene.scene:168`) actors exist (both `IsActive: false`, spawned at runtime).
- EventsManager is attached to the Player (`Scene.scene:134-147`).
- `FlaxWorld.IsKeyHeld()` supports `"Left Mouse"` (mouse branch) and keyboard keys via `Enum.TryParse`; Space was recently fixed (commit `aba35e7`).
- `camera looks at` / `camera follows` words are parked for Task 9 (DESIGN.md §7, §9; TASKS.md Task 9 Part B). Not part of Task 4.

## Tasks

1. **Verify camera exists and is active** — Confirm a `FlaxEngine.Camera` actor named "Camera" exists in `Scene.scene` and is the scene's active/main camera. If missing, add one.
2. **Position camera for top-down view** — Set camera translation above the play area (e.g., Y ≈ 200–300) and orientation looking down toward the Player at `(0, 79, -149)` so the whole play area is visible. Tune by feel in the editor.
3. **Confirm SpawnPoint exists** — `Game.events` line 11 references `spawn Enemy at SpawnPoint`; verify a `SpawnPoint` actor exists in the scene. If missing, add one near a safe spawn location.
4. **Editor verification** — Open project in Flax Editor, press Play, confirm: Player is visible, camera shows top-down view of play area, WASD moves Player, LMB spawns Bullet + plays sound, Enemy spawns and moves toward Player.

## Design decisions

- No new C# words needed for Task 4 — the camera is set up purely in the scene file (static transform).
- `camera follows` is deliberately out of scope (Task 9 Part B). If the user wants the camera to track the Player, that's a separate feature request.
- Camera position/orientation tuned by feel in the editor; no hardcoded follow behavior.

## Validation

- Build compiles with 0 errors.
- Press Play in Flax Editor: camera shows Player from top-down angle, WASD moves Player, LMB fires, Enemy spawns and chases.
- `play sound "shot"` works on both LMB and Space (already fixed in commit `aba35e7`).

## Open questions (out of scope for this plan)

- Should `camera follows Player` be implemented now? → No, parked for Task 9 Part B per DESIGN.md §7 and TASKS.md.
