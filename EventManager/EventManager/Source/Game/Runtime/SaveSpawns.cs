using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// The spawned-actor half of a save (Task 30 Part D): the prefab and clone roots the run
    /// created, so a load can recreate them before the per-actor records (Part C) put their
    /// sheets back.
    ///
    /// Only a root is recorded. A prefab or clone brings its own children back, and a scene that
    /// was added or spawned is already part of the scene section (Part B), which re-adds it with
    /// its whole tree. The runtime remembers the kind when a spawn happens
    /// (<see cref="EventRuntime.MarkSpawnedRoot"/>), so capture never has to guess whether a name
    /// was a prefab asset, a clone template or a scene.
    ///
    /// A load recreates prefabs first and clones second, then parents every root (once all of them
    /// exist) and only then writes the saved transforms and active flags. The saved name is
    /// applied verbatim: that is what the actor records in Part C are keyed by, and the spawn
    /// naming that produced the name ("Hero", "Hero 2", …) already made it unique in the run.
    /// </summary>
    public static class SaveSpawns
    {
        /// <summary>What a restore did: roots recreated, roots the save could not provide, and
        /// records deliberately left to another section.</summary>
        public class Result
        {
            public int Recreated;
            public List<string> Failed = new List<string>();
            public List<string> Skipped = new List<string>();
        }

        /// <summary>
        /// Every spawned root as one record, sorted by actor name so two runs with the same
        /// spawns write the same file. The engine-free runtime has no transform, active flag or
        /// parent, so the bridge supplies those readers; without them a record gets the identity
        /// transform, counts as active and has no parent.
        /// </summary>
        public static List<SaveSpawned> Capture(EventRuntime runtime,
            Func<object, SaveTransform> readTransform = null,
            Func<object, bool> readActive = null,
            Func<object, string> readParent = null)
        {
            var saved = new List<SaveSpawned>();
            if (runtime == null)
                return saved;

            var roots = new List<object>();
            foreach (object actor in runtime.SpawnedRoots())
            {
                if (actor != null)
                    roots.Add(actor);
            }
            roots.Sort((a, b) =>
            {
                int byName = string.CompareOrdinal(runtime.NameOfActor(a) ?? "", runtime.NameOfActor(b) ?? "");
                return byName != 0
                    ? byName
                    : string.CompareOrdinal(runtime.SpawnedFrom(a) ?? "", runtime.SpawnedFrom(b) ?? "");
            });

            foreach (object actor in roots)
            {
                string source = runtime.SpawnedFrom(actor) ?? "";
                string kind = runtime.SpawnKindOf(actor) ?? "";
                if (kind.Length == 0 || source.Length == 0)
                    continue;
                saved.Add(new SaveSpawned
                {
                    Kind = kind,
                    Source = source,
                    Name = runtime.NameOfActor(actor) ?? "",
                    Parent = readParent?.Invoke(actor) ?? "",
                    Active = readActive == null || readActive(actor),
                    Transform = readTransform?.Invoke(actor) ?? new SaveTransform(),
                });
            }
            return saved;
        }

        /// <summary>
        /// Recreates the save's prefab and clone roots before the actor records are applied.
        ///
        /// <paramref name="create"/> is the bridge's engine half: it loads a prefab asset or
        /// clones a template actor and returns the new root, or null when the source is gone. A
        /// root is marked spawned as soon as it exists, so a second save of the same run still
        /// knows what it is. <paramref name="finish"/> then names it and attaches its sheet tree,
        /// because both need the engine object. Parents are attached only after every root exists,
        /// and transforms are written after parenting so a parent cannot move the child afterwards.
        ///
        /// Scene-kind records are skipped: the scene section already re-adds those scenes with
        /// their trees. This build never writes them, but a hand-edited save should not load the
        /// same scene twice.
        /// </summary>
        public static Result Restore(EventRuntime runtime, SaveData data,
            Func<SaveSpawned, object> create,
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
            data.EnsureSections();
            warn ??= _ => { };
            var result = new Result();
            if (create == null)
            {
                if (data.Spawned.Count > 0)
                    warn($"The save has {data.Spawned.Count} spawned actor(s), but there is no way to recreate them here, so they were left out.");
                return result;
            }

            var created = new List<(SaveSpawned Record, object Actor)>();
            // Prefabs first: a clone's template may be a prefab root that this same load creates,
            // so the clone must be able to find it.
            AddCreated(runtime, data, SaveSpawned.KindPrefab, create, finish, warn, result, created);
            AddCreated(runtime, data, SaveSpawned.KindClone, create, finish, warn, result, created);
            foreach (SaveSpawned saved in data.Spawned)
            {
                if (saved == null)
                    continue;
                string kind = (saved.Kind ?? "").Trim().ToLowerInvariant();
                if (kind == SaveSpawned.KindScene)
                    result.Skipped.Add(saved.Name ?? "");
                else if (kind != SaveSpawned.KindPrefab && kind != SaveSpawned.KindClone)
                {
                    warn($"The save says \u201C{saved.Name}\u201D was spawned as \u201C{saved.Kind}\u201D, which this build does not know how to recreate, so it was skipped.");
                    result.Skipped.Add(saved.Name ?? "");
                }
            }

            // Parents after every root exists, so a child can name a root that was created later.
            foreach (var entry in created)
            {
                string parentName = entry.Record.Parent ?? "";
                if (parentName.Length == 0)
                    continue;
                object parent = findActor != null ? findActor(parentName) : runtime.FindNamedActor(parentName);
                if (parent == null)
                {
                    warn($"The save's spawned \u201C{entry.Record.Name}\u201D was parented to \u201C{parentName}\u201D, which isn't here, so it stays at the scene root.");
                    continue;
                }
                setParent?.Invoke(entry.Actor, parent);
            }

            // Transforms last: setting a parent can move a child, and the save's world transform
            // is the one that matters.
            foreach (var entry in created)
            {
                applyTransform?.Invoke(entry.Actor, entry.Record.Transform ?? new SaveTransform());
                applyActive?.Invoke(entry.Actor, entry.Record.Active);
            }

            result.Recreated = created.Count;
            return result;
        }

        private static void AddCreated(EventRuntime runtime, SaveData data, string kind,
            Func<SaveSpawned, object> create, Action<object, SaveSpawned> finish, Action<string> warn,
            Result result, List<(SaveSpawned Record, object Actor)> created)
        {
            foreach (SaveSpawned saved in data.Spawned)
            {
                if (saved == null)
                    continue;
                if (!string.Equals((saved.Kind ?? "").Trim(), kind, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.IsNullOrEmpty(saved.Source))
                {
                    warn($"The save's spawned \u201C{saved.Name}\u201D has no source to recreate it from, so it was skipped.");
                    result.Failed.Add(saved.Name ?? "");
                    continue;
                }

                object actor = create(saved);
                if (actor == null)
                {
                    warn($"The save's spawned \u201C{saved.Name}\u201D from \u201C{saved.Source}\u201D could not be recreated, so it was skipped.");
                    result.Failed.Add(saved.Name ?? "");
                    continue;
                }

                runtime.MarkSpawnedRoot(actor, saved.Source, kind);
                finish?.Invoke(actor, saved);
                created.Add((saved, actor));
            }
        }
    }
}
