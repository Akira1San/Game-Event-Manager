using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// The global, scene, actor and spawned half of a save (Task 30 Parts B–D). It owns
    /// everything a run knows without recreating an object — every global, the scene that was
    /// playing, its additive overlays, the level's place in the run, the record of every sheet
    /// standing in it (<see cref="SaveActors"/>) and the prefab/clone roots that have to come back
    /// first (<see cref="SaveSpawns"/>).
    ///
    /// The order is the point. Reloading a scene runs that scene's <c>when scene starts</c> code,
    /// which may write globals and reset sheet state, and the runtime's own scene-start pass
    /// seeds <c>LevelName</c>, <c>LevelNumber</c> and <c>Time Elapsed</c>. A load therefore
    /// reloads every scene first, writes the saved globals next, recreates the spawned roots, and
    /// applies the actor records last; restored first, they would be overwritten by the level
    /// that was just reloaded.
    /// </summary>
    public static class SaveSnapshot
    {
        /// <summary>
        /// The save for the running game: the scene section the caller filled in (the bridge is
        /// the one that knows which scenes are loaded), every global, every real actor sheet, and
        /// every prefab/clone root. The RNG seed stays empty until Task 39 fills it.
        /// </summary>
        public static SaveData Capture(EventRuntime runtime, SaveScene scene,
            Func<object, SaveTransform> readTransform = null,
            Func<object, bool> readActive = null,
            Func<object, string> readParent = null)
        {
            if (runtime == null)
                throw new ArgumentNullException(nameof(runtime));
            var data = new SaveData();
            data.Scene = CaptureScene(runtime, scene?.Name, scene?.Additive);
            data.Globals.AddRange(CaptureGlobals(runtime.Variables));
            data.Actors.AddRange(SaveActors.Capture(runtime, readTransform, readActive));
            data.Spawned.AddRange(SaveSpawns.Capture(runtime, readTransform, readActive, readParent));
            return data;
        }

        /// <summary>
        /// The scene section: the level that was playing, its overlays in load order, and the
        /// run's level name/number read from the globals — the same values a sheet sees — with
        /// the level order's own state as the fallback for a runtime whose globals were wiped.
        /// </summary>
        public static SaveScene CaptureScene(EventRuntime runtime, string name, IEnumerable<string> additive)
        {
            if (runtime == null)
                throw new ArgumentNullException(nameof(runtime));
            var scene = new SaveScene { Name = name ?? "" };
            if (additive != null)
            {
                foreach (string overlay in additive)
                    AddOverlay(scene, overlay);
            }
            scene.LevelName = ReadText(runtime, Levels.NameVariable, runtime.Levels.Current ?? "");
            scene.LevelNumber = ReadNumber(runtime, Levels.NumberVariable, runtime.Levels.Number);
            return scene;
        }

        /// <summary>
        /// Every global as one section. Names are sorted so two runs that set the same globals
        /// write byte-identical files, and each value keeps its kind (number, text, boolean)
        /// through <see cref="SaveVariable"/>.
        /// </summary>
        public static List<SaveVariable> CaptureGlobals(EventVariables variables)
        {
            var saved = new List<SaveVariable>();
            if (variables == null)
                return saved;
            var names = new List<string>(variables.GlobalNames);
            names.Sort(StringComparer.Ordinal);
            foreach (string name in names)
                saved.Add(SaveVariable.FromBox(name, variables.GetGlobal(name)));
            return saved;
        }

        /// <summary>
        /// Reloads the saved scene, then its overlays, then writes the globals back, and only
        /// then applies the actor records. <paramref name="loadScene"/> replaces the loaded scenes
        /// with the level and returns false when that scene cannot be found;
        /// <paramref name="addScene"/> puts one overlay back on top. A save whose level is gone is
        /// refused before anything is changed — a half-loaded save is worse than no load.
        ///
        /// The actor parameters are passed through to <see cref="RestoreActors"/>: the bridge
        /// supplies the live-actor lookup and the engine transform/active calls, while an
        /// engine-free caller can leave them null and still restore the sheet half.
        /// </summary>
        public static bool Load(EventRuntime runtime, SaveData data, Func<string, bool> loadScene,
            Action<string> addScene = null, Action<string> warn = null,
            Func<string, object> findActor = null,
            Func<string, ProgramFile> programForSource = null,
            Action<object, SaveTransform> applyTransform = null,
            Action<object, bool> applyActive = null,
            Func<SaveSpawned, object> createSpawn = null,
            Action<object, SaveSpawned> finishSpawn = null,
            Action<object, object> setParent = null)
        {
            if (runtime == null)
                throw new ArgumentNullException(nameof(runtime));
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            data.EnsureSections();
            warn ??= _ => { };

            string sceneName = data.Scene.Name ?? "";
            if (sceneName.Length > 0)
            {
                if (loadScene == null || !loadScene(sceneName))
                {
                    warn($"The save was made in the scene \u201C{sceneName}\u201D, which isn't in this project, so the game was not loaded. The current game was left alone.");
                    return false;
                }
            }

            foreach (string overlay in data.Scene.Additive)
            {
                if (string.IsNullOrWhiteSpace(overlay))
                    continue;
                if (addScene == null)
                {
                    warn($"The save expects the scene \u201C{overlay}\u201D on top of its level, but there is no way to add scenes here, so it was left out.");
                    continue;
                }
                addScene(overlay);
            }

            RestoreGlobals(runtime, data);
            // Spawned roots have to exist before the actor records look them up by name.
            RestoreSpawns(runtime, data, createSpawn, finishSpawn, setParent,
                applyTransform, applyActive, findActor, warn);
            RestoreActors(runtime, data, findActor, programForSource, applyTransform, applyActive, warn);
            return true;
        }

        /// <summary>
        /// Recreates the save's prefab and clone roots before the per-actor records are applied
        /// (Task 30 Part D). The bridge supplies the engine factory and the naming/attachment
        /// step; scene-kind records are left to the scene section.
        /// </summary>
        public static SaveSpawns.Result RestoreSpawns(EventRuntime runtime, SaveData data,
            Func<SaveSpawned, object> create = null,
            Action<object, SaveSpawned> finish = null,
            Action<object, object> setParent = null,
            Action<object, SaveTransform> applyTransform = null,
            Action<object, bool> applyActive = null,
            Func<string, object> findActor = null,
            Action<string> warn = null)
        {
            if (runtime == null)
                throw new ArgumentNullException(nameof(runtime));
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            return SaveSpawns.Restore(runtime, data, create, finish, setParent,
                applyTransform, applyActive, findActor, warn);
        }

        /// <summary>
        /// Puts every saved sheet back onto the reloaded scene: transform and active flag through
        /// the bridge, then the per-object variables, bag, equipment and state (Task 30 Part C).
        /// A saved actor this scene cannot provide is warned about and skipped.
        /// </summary>
        public static SaveActors.Result RestoreActors(EventRuntime runtime, SaveData data,
            Func<string, object> findActor = null,
            Func<string, ProgramFile> programForSource = null,
            Action<object, SaveTransform> applyTransform = null,
            Action<object, bool> applyActive = null,
            Action<string> warn = null)
        {
            if (runtime == null)
                throw new ArgumentNullException(nameof(runtime));
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            return SaveActors.Restore(runtime, data, findActor, programForSource,
                applyTransform, applyActive, warn);
        }

        /// <summary>
        /// Puts the globals back and resumes the level's clock. The globals are replaced, not
        /// merged: a global the save never had is gone, exactly like the run it came from.
        /// </summary>
        public static void RestoreGlobals(EventRuntime runtime, SaveData data)
        {
            if (runtime == null)
                throw new ArgumentNullException(nameof(runtime));
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            data.EnsureSections();

            EventVariables variables = runtime.Variables;
            variables.WipeGlobals();
            foreach (SaveVariable saved in data.Globals)
            {
                if (saved == null || string.IsNullOrEmpty(saved.Name))
                    continue;
                variables.SetGlobal(saved.Name, saved.ToBox());
            }

            // The scene section is the save's own record of the level, so it wins over a
            // hand-edited LevelName global; when it has nothing to say, the global that was just
            // restored is the next best answer.
            string levelName = data.Scene.LevelName;
            if (string.IsNullOrEmpty(levelName) && variables.HasGlobal(Levels.NameVariable))
                levelName = variables.GetGlobal(Levels.NameVariable).AsText();

            int levelNumber = data.Scene.LevelNumber;
            if (levelNumber <= 0 && variables.HasGlobal(Levels.NumberVariable))
                levelNumber = (int)Math.Round(variables.GetGlobal(Levels.NumberVariable).AsNumber());

            double elapsed = variables.HasGlobal(Levels.TimeElapsedVariable)
                ? variables.GetGlobal(Levels.TimeElapsedVariable).AsNumber()
                : 0;
            runtime.Levels.Resume(levelName, levelNumber, elapsed, runtime.Time, variables);
        }

        private static void AddOverlay(SaveScene scene, string overlay)
        {
            if (string.IsNullOrWhiteSpace(overlay))
                return;
            string trimmed = overlay.Trim();
            if (string.Equals(trimmed, scene.Name, StringComparison.OrdinalIgnoreCase))
                return;
            foreach (string existing in scene.Additive)
            {
                if (string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            scene.Additive.Add(trimmed);
        }

        private static string ReadText(EventRuntime runtime, string name, string fallback)
        {
            if (runtime.Variables.HasGlobal(name))
                return runtime.Variables.GetGlobal(name).AsText();
            return fallback ?? "";
        }

        private static int ReadNumber(EventRuntime runtime, string name, int fallback)
        {
            if (!runtime.Variables.HasGlobal(name))
                return fallback;
            return (int)Math.Round(runtime.Variables.GetGlobal(name).AsNumber());
        }
    }
}
