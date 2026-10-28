# Task Debug — Task 6 Part A Scene Words

This document helps debug the `when <thing> is clicked` and `load scene` issues.

## Issue 1: `load scene "Level1"` — "Scenes: Scene."

**Symptom:** First press of Enter shows `[Warning] I couldn't find a scene named "Level1". Scenes: Scene.`

**Cause:** `Menu.scene` and `Level1.scene` were created manually as files but have not been imported by the Flax editor's asset system yet. `Content.GetAllAssetsByType(typeof(Scene))` only returns assets the editor has imported — currently only `Scene` (the original `Scene.scene`).

**Fix:** Open the project in the Flax editor. The editor should auto-import new `.scene` files. Once imported, `Content.GetAllAssetsByType(typeof(Scene))` will return both `Menu` and `Level1`, and `load scene "Level1"` will work.

**Verify in editor:**
1. Open EventManager.flaxproj in Flax editor
2. Wait for asset import to finish (check the bottom status bar)
3. Check Content folder — `Menu.scene` and `Level1.scene` should appear with scene icons
4. Run the game and press Enter — `load scene "Level1"` should work

**Debug output to watch:**
- `[Warning] I couldn't find a scene named "Level1". Scenes: Scene.` — Level1.scene not imported yet
- No warning + scene changes — working

---

## Issue 2: Mouse click not firing `when <thing> is clicked`

**Symptom:** Clicking PlayButton or QuitButton does nothing.

**Debug output to watch** (added in `EventsManager.InputEdges()`):

When you click the left mouse button, the editor log should show:

```
[Warning] Mouse down: actors=N, uiControls=M, mouse=(X,Y)
[Warning] Clicked UI actor: PlayButton   (if the hit test matches)
```

**What the values mean:**
- `actors` — total active actors in the scene (should be > 0)
- `uiControls` — how many actors cast to `UIControl` (if 0, UIControls aren't deriving from Actor or aren't active)
- `mouse` — the mouse position from `Input.MousePosition`
- `Clicked UI actor: <name>` — if a UIControl was under the mouse

**If `uiControls` is 0:**
- `UIControl` may not derive from `Actor` in this Flax version, OR
- UIControl actors may be inactive (`IsActive == false`), OR
- UIControls may not be children of scene roots in `SceneActors()`

**If `uiControls` > 0 but no `Clicked UI actor`:**
- The coordinate math in `MouseOverUIControl()` is wrong — `ui.Position` may not be in screen-space coordinates matching `Input.MousePosition`
- The estimated button size (from text length × font size) may be too small

**If nothing appears at all:**
- `Input.GetMouseButtonDown(MouseButton.Left)` may not fire — try `Input.GetKeyDown(KeyboardKeys.MouseLeft)` or check if mouse input is enabled

---

## Issue 3: Enter key works on second press but not first

**Symptom:** First Enter press shows the "scene not found" warning. Second Enter press shows nothing.

**Cause:** `WarnOnce()` only shows the warning once. On the second press, `LoadSceneByName` still returns false (scene not imported), but the warning is suppressed.

**Fix:** Same as Issue 1 — import the scenes in the Flax editor.

---

## Quick Test Steps

1. Open Flax editor with EventManager.flaxproj
2. Let asset import complete
3. Press Play in editor
4. In the Menu scene:
   - Press **Enter** — should load Level1 (if Level1.scene is imported)
   - Press **Q** or **Escape** — should quit the game
   - Click **PlayButton** — should load Level1 (if mouse detection works)
   - Click **QuitButton** — should quit the game (if mouse detection works)
5. Check the editor console for `[Warning]` messages matching the patterns above
