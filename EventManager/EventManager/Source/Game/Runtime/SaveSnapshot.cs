using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// The global and scene half of a save (Task 30 Part B). Parts C and D fill the actor and
    /// spawned records; this file owns everything a run knows without looking at a single actor —
    /// every global, the scene that was playing, its additive overlays and the level's place in
    /// the run — and the order a load puts them back in.
    ///
    /// The order is the point. Reloading a scene runs that scene's <c>when scene starts</c> code,
    /// which may write globals, and the runtime's own scene-start pass seeds <c>LevelName</c>,
    /// <c>LevelNumber</c> and <c>Time Elapsed</c>. A load therefore reloads every scene first and
    /// writes the saved globals last; restored first, they would be overwritten by the level
    /// that was just reloaded.
    /// </summary>
    public static class SaveSnapshot
    {
        /// <summary>
        /// The save for the running game: the scene section the caller filled in (the bridge is
        /// the one that knows which scenes are loaded) plus every global. Actors, spawned records
        /// and the RNG seed stay empty until Parts C, D and Task 39 fill them.
        /// </summary>
        public static SaveData Capture(EventRuntime runtime, SaveScene scene)
        {
            if (runtime == null)
                throw new ArgumentNullException(nameof(runtime));
            var data = new SaveData();
            data.Scene = CaptureScene(runtime, scene?.Name, scene?.Additive);
            data.Globals.AddRange(CaptureGlobals(runtime.Variables));
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
        /// Reloads the saved scene, then its overlays, and only then writes the globals back.
        /// <paramref name="loadScene"/> replaces the loaded scenes with the level and returns false
        /// when that scene cannot be found; <paramref name="addScene"/> puts one overlay back on
        /// top. A save whose level is gone is refused before anything is changed — a half-loaded
        /// save is worse than no load.
        /// </summary>
        public static bool Load(EventRuntime runtime, SaveData data, Func<string, bool> loadScene,
            Action<string> addScene = null, Action<string> warn = null)
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
            return true;
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
