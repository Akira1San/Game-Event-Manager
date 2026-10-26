# Plan: Fix `play sound` audio loading path for Flax 1.13

## Problem

`Content.Load<AudioClip>("shot")` resolves to the wrong filesystem path:
`C:\Users/plame/Documents/akira/New/EventManager/EventManager/shot`
(project root) instead of the Content folder:
`C:\Users/plame/Documents/akira/New/EventManager/EventManager/Content/shot`.

The user sees `shot` in Flax's Content browser at `eventmanager/content/shot`, confirming the asset exists but `Content.Load<AudioClip>()` isn't finding it because it's looking relative to the project root, not the Content folder.

## Root Cause Hypothesis

`Content.Load<T>(string path)` in Flax 1.13 likely expects a path relative to the Content directory OR requires the `.wav` extension, OR the asset isn't fully imported as a Flax asset (just a raw file sitting in the folder).

## Changes Needed

### 1. Investigate correct `Content.Load` path format (research)

- Check Flax 1.13 docs/source for `Content.Load<T>()` path resolution rules
- Determine if path should be: `"shot"`, `"Content/shot"`, `"shot.wav"`, `"Content/shot.wav"`, or asset GUID
- Verify `shot.wav` is imported as a Flax asset in Content browser (not just a raw file)

### 2. Fix `PlaySound()` in `FlaxActions.cs`

File: `EventManager/EventManager/Source/Game/Bridge/FlaxActions.cs`

The current `PlaySound()` (added in prior session) spawns a temporary `AudioSource` actor each call:
```csharp
var clip = Content.Load<AudioClip>(spec.Thing);
```
This needs the correct path string based on research result #1.

### 3. Verify `Player.events` sound trigger

File: `EventManager/EventManager/Source/Game/GameFiles\Player.events`

Lines 39 and 42 currently: `play sound "shot"` — confirm this matches whatever path format `Content.Load` expects.

### 4. Editor setup validation

- Ensure `shot.wav` is imported as a Flax asset (right-click → Import in Content browser)
- Ensure an `AudioListener` exists in the scene
- Restart editor after C# rebuild (C# changes don't hot-reload)

## Validation

- Build passes with 0 errors
- Pressing LMB or Space plays `shot` sound without "Could not load sound clip" warning
- No "Missing file" error in log

## Open Question (RESOLVED)

`Content.Load<T>(string path)` in Flax 1.13 editor resolves paths relative to the project root (CWD) and requires an existing file (`FileExists` check) before consulting the asset registry. `Content.Load<AudioClip>("shot")` looks for `project/shot` — which does not exist — so it returns null.

The imported asset is `Content/shot.flax` (raw `eaglaxle-gun-shot-1-530788.wav` imported as `shot.flax`; referenced by GUID `240fe7f047519c9d72bbe58f06a9c681` in `Content/Scene.scene:166`).

Fix: `PlaySound()` now tries candidate paths `"Content/<name>.flax"`, `"Content/<name>"`, then `<name>`, using the first that loads. `play sound "shot"` resolves to `Content/shot.flax`.