## Known Differences & Workarounds

| TK Feature | EventManager Difference | Workaround |
|---|---|---|
| `set text of ScoreLabel to Score` | Assumes UI widget named `ScoreLabel` | Ensure Flax UI Text component has matching name, or use `get name` to dynamically reference |
| `show text "..."` / `hide text` | Global text show/hide | Can target specific text by name: `set text of LabelName to "..."` then `show text "LabelName"` |
| `restart scene` | Scene management | Verify Flax `Application.LoadLevel()` or scene management API matches |
| `unless Score is 5` | Condition check | Works same way - numeric comparison of global variable |
| `spawn Enemy at SpawnPoint` | Entity spawning | Ensure `SpawnPoint` actor exists in scene with proper tag/name |

## UI Variable States Summary

| State | TK Code | EventManager Code |
|---|---|---|
| **Game start** | `set Score to 0 \n set Lives to 3` | `set Score to 0 \n set Lives to 3` |
| **Intro text** | `show text "Shoot the enemies!" \n wait 2 seconds \n hide text` | `show text "Shoot the enemies!" \n wait 2 seconds \n hide text` |
| **HUD update** | `when Score changes \n set text of ScoreLabel to Score` | `when Score changes \n set text of ScoreLabel to Score` |
| **HUD update** | `when Lives changes \n set text of LivesLabel to Lives` | `when Lives changes \n set text of LivesLabel to Lives` |
| **Win condition** | `when Score becomes 5 \n show text "You win!" \n wait 3 seconds \n restart scene` | `when Score becomes 5 \n show text "You win!" \n wait 3 seconds \n restart scene` |
| **Lose condition** | `when Lives becomes 0 \n show text "Game Over" \n wait 3 seconds \n restart scene` | `when Lives becomes 0 \n show text "Game Over" \n wait 3 seconds \n restart scene` |