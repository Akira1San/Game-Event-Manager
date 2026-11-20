using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// One save's worth of per-actor state (Task 30 Part C): every real sheet's source, identity,
    /// place in the world, per-object variables, bag, equipment and current state. Part B put the
    /// globals and the scene back; this half puts the actors standing in that scene back.
    ///
    /// A load runs after the scene has reloaded, so every scene actor is a fresh object whose
    /// sheet has just run its authored defaults and its <c>when scene starts</c> code. The save is
    /// therefore applied over that fresh sheet: per-object variables are merged (a saved value
    /// wins, an authored default the save never wrote stays), while the bag and the equipment are
    /// replaced, because "what the actor carried at save time" is not something to merge with what
    /// the reload handed it.
    ///
    /// Restored sheets do not re-run <c>when me appears</c>. The runtime queues that trigger when a
    /// sheet is added, and a rebuilt sheet is added during the load; the queue and the pending
    /// waits are cleared and the saved state is written to both current and previous state, so a
    /// load behaves like a moment in the run rather than a brand-new actor arriving.
    /// </summary>
    public static class SaveActors
    {
        /// <summary>What a restore did: how many actors it put back, how many sheets it rebuilt,
        /// and the saved actor names this scene could not provide.</summary>
        public class Result
        {
            public int Restored;
            public int Rebuilt;
            public List<string> Missing = new List<string>();
        }

        /// <summary>
        /// Every real sheet as one save record, sorted by actor name so two runs with the same
        /// actors write the same file. The engine-free world has no transform or active flag, so
        /// the bridge supplies those readers; without them a record gets the identity transform
        /// and counts as active.
        /// </summary>
        public static List<SaveActor> Capture(EventRuntime runtime,
            Func<object, SaveTransform> readTransform = null,
            Func<object, bool> readActive = null)
        {
            var saved = new List<SaveActor>();
            if (runtime == null)
                return saved;

            var real = new List<SheetInstance>();
            foreach (SheetInstance sheet in runtime.Sheets)
            {
                if (sheet == null || sheet.Actor == null || sheet.Actor is GlobalHost)
                    continue;
                real.Add(sheet);
            }
            real.Sort((a, b) =>
            {
                int byName = string.CompareOrdinal(a.ActorName ?? "", b.ActorName ?? "");
                return byName != 0 ? byName : string.CompareOrdinal(a.SourceName ?? "", b.SourceName ?? "");
            });

            foreach (SheetInstance sheet in real)
                saved.Add(CaptureOne(runtime, sheet, readTransform, readActive));
            return saved;
        }

        /// <summary>One sheet's record. Public so a later part can save an actor on its own.</summary>
        public static SaveActor CaptureOne(EventRuntime runtime, SheetInstance sheet,
            Func<object, SaveTransform> readTransform = null,
            Func<object, bool> readActive = null)
        {
            if (runtime == null || sheet == null)
                return new SaveActor();

            string actorName = string.IsNullOrEmpty(sheet.ActorName)
                ? runtime.NameOfActor(sheet.Actor)
                : sheet.ActorName;

            var saved = new SaveActor
            {
                Source = sheet.SourceName ?? "",
                Actor = actorName ?? "",
                Player = sheet.Player,
                Physics = sheet.UsePhysicsMovement,
                Active = readActive == null || readActive(sheet.Actor),
                Transform = readTransform?.Invoke(sheet.Actor) ?? new SaveTransform(),
                State = string.IsNullOrEmpty(sheet.CurrentState) ? "default" : sheet.CurrentState,
                SpawnedFrom = runtime.SpawnedFrom(sheet.Actor) ?? "",
            };
            saved.Variables.AddRange(CaptureLocals(runtime.Variables, sheet.Actor));
            foreach (string thing in runtime.BagNames(sheet.Actor))
            {
                saved.Bag.Add(new SaveBagEntry
                {
                    Thing = thing,
                    Count = runtime.BagCount(sheet.Actor, thing),
                });
            }
            saved.Equipped.AddRange(runtime.EquippedNames(sheet.Actor));
            return saved;
        }

        /// <summary>
        /// Applies a save's actor records after the scene has reloaded.
        ///
        /// <paramref name="findActor"/> turns a saved actor name into the live object (the bridge
        /// passes <c>Level.FindActor</c>; the engine-free tests pass their own map). A name this
        /// scene cannot provide is reported in <see cref="Result.Missing"/>. A live actor whose
        /// sheet is missing is rebuilt from <paramref name="programForSource"/> when the save named
        /// a source, so a scene actor whose sheet failed to attach still comes back with its state.
        ///
        /// <paramref name="applyTransform"/> and <paramref name="applyActive"/> are the bridge's
        /// engine calls; without them the load still restores the sheet half, which is what the
        /// engine-free runtime can do on its own.
        /// </summary>
        public static Result Restore(EventRuntime runtime, SaveData data,
            Func<string, object> findActor,
            Func<string, ProgramFile> programForSource = null,
            Action<object, SaveTransform> applyTransform = null,
            Action<object, bool> applyActive = null,
            Action<string> warn = null)
        {
            if (runtime == null)
                throw new ArgumentNullException(nameof(runtime));
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            data.EnsureSections();
            warn ??= _ => { };
            var result = new Result();

            foreach (SaveActor saved in data.Actors)
            {
                if (saved == null)
                    continue;
                string name = saved.Actor ?? "";
                object actor = string.IsNullOrEmpty(name)
                    ? null
                    : (findActor != null ? findActor(name) : runtime.FindNamedActor(name));
                if (actor == null)
                {
                    if (name.Length == 0)
                        warn("The save has an actor record with no name, so it was skipped.");
                    else
                        warn($"The save has a sheet for \u201C{name}\u201D, but this scene has no actor by that name, so it was skipped.");
                    result.Missing.Add(name);
                    continue;
                }

                applyTransform?.Invoke(actor, saved.Transform ?? new SaveTransform());
                applyActive?.Invoke(actor, saved.Active);

                // The variables are merged over the reloaded sheet: the save's value wins, an
                // authored default it never wrote stays. Bags and equipment are replaced.
                foreach (SaveVariable variable in saved.Variables)
                {
                    if (variable == null || string.IsNullOrEmpty(variable.Name))
                        continue;
                    runtime.Variables.SetLocal(actor, variable.Name, variable.ToBox());
                }

                runtime.ClearBag(actor);
                foreach (SaveBagEntry entry in saved.Bag)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.Thing))
                        continue;
                    runtime.SetBagCount(actor, entry.Thing, entry.Count);
                }

                runtime.ClearEquipment(actor);
                foreach (string thing in saved.Equipped)
                {
                    if (!string.IsNullOrEmpty(thing))
                        runtime.EquipItem(actor, thing);
                }

                if (!string.IsNullOrEmpty(saved.SpawnedFrom))
                    runtime.MarkSpawned(actor, saved.SpawnedFrom);

                SheetInstance sheet = runtime.FindSheetFor(actor);
                if (sheet == null && !string.IsNullOrEmpty(saved.Source) && programForSource != null)
                {
                    ProgramFile program = programForSource(saved.Source);
                    if (program != null)
                    {
                        sheet = runtime.AddSheet(actor, program, saved.Source);
                        result.Rebuilt++;
                    }
                }

                if (sheet == null)
                {
                    // No sheet to hold a state, but the rest (position, variables, bag, gear)
                    // still belongs to the actor and was put back above.
                    if (!string.IsNullOrEmpty(saved.Source))
                        warn($"The save has a sheet for \u201C{name}\u201D from \u201C{saved.Source}\u201D, but there is no sheet to put its state back on.");
                }
                else
                {
                    sheet.Player = saved.Player;
                    sheet.UsePhysicsMovement = saved.Physics;
                    string state = string.IsNullOrEmpty(saved.State) ? "default" : saved.State;
                    // A load is not an arrival: drop the Appears push a rebuilt sheet queued and
                    // any wait the reloaded sheet had started, then write both state fields so
                    // `when state is` does not see a false entry edge on the next tick.
                    sheet.PushQueue.Clear();
                    sheet.Waiting.Clear();
                    sheet.CurrentState = state;
                    sheet.LastState = state;
                }

                result.Restored++;
            }

            return result;
        }

        private static IEnumerable<SaveVariable> CaptureLocals(EventVariables variables, object actor)
        {
            var saved = new List<SaveVariable>();
            if (variables == null)
                return saved;
            foreach (string name in variables.LocalNames(actor))
                saved.Add(SaveVariable.FromBox(name, variables.GetLocal(actor, name)));
            return saved;
        }
    }
}
