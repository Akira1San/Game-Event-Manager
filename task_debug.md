# Task Debug — Task 6 Part A Scene Words

This document helps debug the `when <thing> is clicked` and `load scene` issues.

## Issue 1: `load scene "Level1"` — "Scenes: Scene."

**Symptom:** Pressing Enter shows `[Warning] I couldn't find a scene named "Level1". Scenes: Scene.`

**Cause:** `Content.GetAllAssetsByType(typeof(Scene))` only returns scenes imported by the Flax editor. If your `.scene` files aren't showing up in the debug output's "Scene asset paths" list, Flax hasn't imported them as Scene assets.

**Fix — try these in order:**

### 1. Move scenes to root Content folder (most likely fix)
Flax may not scan subfolders for scene assets. Move your scenes:
- `Content/scenes/menu_test.scene` → `Content/menu_test.scene`
- `Content/scenes/load_level_test.scene` → `Content/load_level_test.scene`
- Then in Content browser: RMB → **Reimport** on both files
- Update `Menu.events` if needed: `load scene "load_level_test"` (base name, no path)

### 2. Create scenes directly in Content browser at root
- Content browser → RMB → New Scene → name it `Menu`
- Content browser → RMB → New Scene → name it `Level1`
- Add UICanvas + ScoreLabel + EventsManager to Level1
- Update `Menu.events`: `load scene "Level1"`
- Reimport and test

### 3. Check Build Settings
Open `EventManager.flaxproj` in Flax editor → File → Build Settings → check if there's a "Scenes in Build" list that needs your scenes added.

### 4. Restart the Flax editor
Force a full re-import of all assets.

**Debug output to watch:**
- `[Warning] I couldn't find a scene named "load_level_test". Scenes: Scene. Scene asset paths: ...` — compare the paths listed against your scene file paths
- If `load_level_test` appears in `Scene asset paths` but with a different name → update `Menu.events` to match
- If nothing appears → scenes not imported yet

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
