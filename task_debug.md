# Task Debug — Task 6 Part A Scene Words

This document helps debug the `when <thing> is clicked` and `load scene` issues.

## Issue 1: `load scene "Level1"` — "Scenes: Scene."

**Symptom:** Pressing Enter shows `[Warning] I couldn't find a scene named "Level1". Scenes: Scene.`

**Cause:** `Menu.scene` and `Level1.scene` were hand-written as JSON files, but Flax needs valid imported scene assets. `Content.GetAllAssetsByType(typeof(Scene))` only returns scenes the editor has imported — currently only `Scene` (the original `Scene.scene`).

**Fix:** Create the scenes through the **Flax editor UI** (most reliable):

1. Open `EventManager.flaxproj` in the Flax editor
2. **Create Menu scene:** File → New Scene → save as `Content/Menu.scene`
   - Add UICanvas actor
   - Add UIControl actors named `PlayButton`, `QuitButton`, `GameText` as children of UICanvas
   - Add `EventsManager` script on a scene-root actor named `Menu`
3. **Create Level1 scene:** File → New Scene → save as `Content/Level1.scene`
   - Add `ScoreLabel` UIControl under UICanvas
   - Add `EventsManager` script on a scene-root actor named `Level1`
4. Wait for the editor to import the new scenes (check Content browser)
5. Run the game — `load scene "Level1"` should now find the imported `Level1` scene asset

**Why hand-written .scene files may not work:** Flax scene assets are serialized binary/bloated JSON with engine-specific GUIDs and references. Hand-written files may be missing required fields or have wrong GUID formats, so the editor skips them during import.

**Verify in editor Content browser:** You should see `Menu.scene` and `Level1.scene` with scene icons. If they don't appear, the editor isn't recognizing them as valid scene assets.

**Debug output to watch:**
- `[Warning] I couldn't find a scene named "Level1". Scenes: Scene.` — Level1 not imported yet
- No warning + scene changes — working

---

## Issue 2: Mouse click not firing `when <thing> is clicked`

**Symptom:** Clicking PlayButton or QuitButton does nothing, but the debug log shows mouse down events.

**Debug output** (added in `EventsManager.InputEdges()`):

When you left-click, the editor log shows:

```
[Warning] Mouse down: actors=N, uiControls=M, mouse=(X,Y), screenCenter=(CX,CY)
[Warning]   No hit: actor=PlayButton, text="Play", center=(CX,CY), mouse=(X,Y)
[Warning]   No hit: actor=QuitButton, text="Quit", center=(CX,CY), mouse=(X,Y)
[Warning]   No hit: actor=GameText, text="", center=(CX,CY), mouse=(X,Y)
```

**What to look for:**
- `uiControls=3` means UIControl casting works (good)
- `center=(CX,CY)` vs `mouse=(X,Y)` — compare these values
- If `center` is far from where the button actually appears on screen, the coordinate math is wrong
- The hit test tries both Y-down and Y-up orientations

**What the numbers mean:**
- `screenCenter` = window width/2, height/2 (e.g., 640, 360 for 1280x720)
- `center` = screenCenter + accumulated UIControl.Position from parent chain
- `mouse` = Input.MousePosition (screen-space, top-left origin)

**If you click PlayButton and see `No hit` for PlayButton:**
- Report the exact values: `center`, `mouse`, `screenCenter`
- The hit box might be too small (minimum 120x40 now) or the center might be wrong

**If you see nothing at all when clicking:**
- `Input.GetMouseButtonDown(MouseButton.Left)` may not fire in editor play mode
- Try clicking in the game view, not the editor viewport

---

## Issue 3: Enter key works on second press but not first

**Symptom:** First Enter press shows the "scene not found" warning. Second Enter press shows nothing.

**Cause:** `WarnOnce()` only shows the warning once. On the second press, `LoadSceneByName` still returns false (scene not imported), but the warning is suppressed.

**Fix:** Same as Issue 1 — import/create the scenes in the Flax editor.

---

## Quick Test Steps

1. Open Flax editor with EventManager.flaxproj
2. Create Menu.scene and Level1.scene through the editor UI (see Issue 1)
3. Press Play in editor
4. In the Menu scene:
   - Press **Enter** — should load Level1 (if Level1.scene exists)
   - Press **Q** or **Escape** — should quit the game
   - Click **PlayButton** — should load Level1 (if mouse detection works)
   - Click **QuitButton** — should quit the game (if mouse detection works)
5. Check the editor console for `[Warning]` messages matching the patterns above
