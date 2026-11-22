using System;

namespace Game
{
    /// <summary>
    /// The slot half of Task 30 (Part E): the words <c>save game</c>, <c>load game</c>,
    /// <c>delete save</c> and <c>autosave</c> as one engine-free coordinator. It owns slot
    /// normalization, reading/writing/deleting through <see cref="SaveFile"/>, the mapping from
    /// a file status to one friendly sentence, and raising <c>when game saves</c> /
    /// <c>when game loads</c> after the work is done.
    ///
    /// The bridge supplies the engine delegates: which scene is playing, how to read a
    /// transform, how to reload a scene, and how to recreate a prefab. Every failure comes back
    /// as a <see cref="Result"/> with a message the player can act on; nothing here throws at a
    /// sheet that wrote a slot name with a typo in it.
    /// </summary>
    public static class SaveGames
    {
        /// <summary>What one save/load/delete attempted and how it ended.</summary>
        public enum Outcome
        {
            /// <summary>The slot was written and <c>when game saves</c> was raised.</summary>
            Saved,
            /// <summary>The slot was read and restored, and <c>when game loads</c> was raised.</summary>
            Loaded,
            /// <summary>The slot file was removed.</summary>
            Deleted,
            /// <summary>That slot has no file on disk. Not a crash — there was nothing to do.</summary>
            Missing,
            /// <summary>The file exists but is not a readable save.</summary>
            Damaged,
            /// <summary>The file was written by a newer build; the running game was left alone.</summary>
            NewerVersion,
            /// <summary>The save is readable but cannot be restored here (e.g. its scene is gone).</summary>
            Refused,
            /// <summary>The runtime has no project folder, or the disk itself failed.</summary>
            Failed,
        }

        /// <summary>One attempt: what happened, the normalized slot, and a sentence to show.</summary>
        public class Result
        {
            public Outcome Outcome;
            public string Slot = "";
            public string Message = "";

            /// <summary>True for the three outcomes that did something.</summary>
            public bool Ok => Outcome == Outcome.Saved ||
                              Outcome == Outcome.Loaded ||
                              Outcome == Outcome.Deleted;
        }

        /// <summary>
        /// <c>save game</c> / <c>save game to "slot"</c>: captures the whole run and writes it to
        /// the slot, then raises <c>when game saves</c> with the slot name. A blank slot means
        /// <see cref="SaveFile.DefaultSlot"/>.
        /// </summary>
        public static Result Save(EventRuntime runtime, string slot, SaveScene scene = null,
            Func<object, SaveTransform> readTransform = null,
            Func<object, bool> readActive = null,
            Func<object, string> readParent = null,
            Action<string> warn = null)
        {
            string normalized = SaveFile.NormalizeSlot(slot);
            string folder = ProjectFolder(runtime, warn, normalized, out Result failed);
            if (failed != null)
                return failed;
            try
            {
                SaveData data = SaveSnapshot.Capture(runtime, scene, readTransform, readActive, readParent);
                SaveFile.Write(folder, normalized, data);
                runtime.RaisePushAll(new PushSignal
                {
                    Kind = TriggerKind.GameSaves,
                    Name = normalized,
                });
                return new Result { Outcome = Outcome.Saved, Slot = normalized };
            }
            catch (Exception error)
            {
                return Report(warn, new Result
                {
                    Outcome = Outcome.Failed,
                    Slot = normalized,
                    Message = $"I couldn't save slot \u201C{normalized}\u201D ({error.Message}). The game keeps running.",
                });
            }
        }

        /// <summary>
        /// <c>autosave</c>: the same capture as <see cref="Save"/>, always into
        /// <see cref="SaveFile.AutosaveSlot"/>. A checkpoint can never overwrite a save the
        /// player made on purpose, and <c>load game from "autosave"</c> resumes it.
        /// </summary>
        public static Result Autosave(EventRuntime runtime, SaveScene scene = null,
            Func<object, SaveTransform> readTransform = null,
            Func<object, bool> readActive = null,
            Func<object, string> readParent = null,
            Action<string> warn = null)
        {
            return Save(runtime, SaveFile.AutosaveSlot, scene, readTransform, readActive, readParent, warn);
        }

        /// <summary>
        /// <c>load game</c> / <c>load game from "slot"</c>: reads the slot first, so a missing,
        /// damaged or newer file never changes the running game, then hands the data to
        /// <see cref="SaveSnapshot.Load"/>. Only a complete restore raises <c>when game loads</c>.
        /// </summary>
        public static Result Load(EventRuntime runtime, string slot,
            Func<string, bool> loadScene = null,
            Action<string> addScene = null,
            Action<string> warn = null,
            Func<string, object> findActor = null,
            Func<string, ProgramFile> programForSource = null,
            Action<object, SaveTransform> applyTransform = null,
            Action<object, bool> applyActive = null,
            Func<SaveSpawned, object> createSpawn = null,
            Action<object, SaveSpawned> finishSpawn = null,
            Action<object, object> setParent = null)
        {
            string normalized = SaveFile.NormalizeSlot(slot);
            string folder = ProjectFolder(runtime, warn, normalized, out Result failed);
            if (failed != null)
                return failed;

            SaveFileResult read = SaveFile.TryRead(folder, normalized);
            if (!read.Loaded)
            {
                return Report(warn, new Result
                {
                    Outcome = OutcomeOf(read.Status),
                    Slot = normalized,
                    Message = read.Message,
                });
            }

            bool loaded;
            try
            {
                loaded = SaveSnapshot.Load(runtime, read.Data, loadScene, addScene, warn,
                    findActor, programForSource, applyTransform, applyActive,
                    createSpawn, finishSpawn, setParent);
            }
            catch (Exception error)
            {
                return Report(warn, new Result
                {
                    Outcome = Outcome.Failed,
                    Slot = normalized,
                    Message = $"I couldn't load slot \u201C{normalized}\u201D ({error.Message}). The game may be partway through the load.",
                });
            }

            if (!loaded)
            {
                // SaveSnapshot.Load has already said which scene is missing; a second warning
                // would only repeat it. The result still carries the refusal for a UI binding.
                return new Result
                {
                    Outcome = Outcome.Refused,
                    Slot = normalized,
                    Message = $"The save in slot \u201C{normalized}\u201D could not be restored, so the current game was left alone.",
                };
            }

            runtime.RaisePushAll(new PushSignal
            {
                Kind = TriggerKind.GameLoads,
                Name = normalized,
            });
            return Report(warn, new Result
            {
                Outcome = Outcome.Loaded,
                Slot = normalized,
                // An older schema still loads; this is the migration warning, not a failure.
                Message = read.HasWarning ? read.Message : "",
            });
        }

        /// <summary>
        /// <c>delete save "slot"</c>: removes one slot file. A slot that is already gone says so
        /// instead of pretending it deleted something.
        /// </summary>
        public static Result Delete(EventRuntime runtime, string slot, Action<string> warn = null)
        {
            string normalized = SaveFile.NormalizeSlot(slot);
            string folder = ProjectFolder(runtime, warn, normalized, out Result failed);
            if (failed != null)
                return failed;
            try
            {
                if (!SaveFile.Delete(folder, normalized))
                {
                    return Report(warn, new Result
                    {
                        Outcome = Outcome.Missing,
                        Slot = normalized,
                        Message = $"There is no save in slot \u201C{normalized}\u201D to delete.",
                    });
                }
                return new Result { Outcome = Outcome.Deleted, Slot = normalized };
            }
            catch (Exception error)
            {
                return Report(warn, new Result
                {
                    Outcome = Outcome.Failed,
                    Slot = normalized,
                    Message = $"I couldn't delete slot \u201C{normalized}\u201D ({error.Message}).",
                });
            }
        }

        /// <summary>The project folder every slot lives under, or a failure result when absent.</summary>
        private static string ProjectFolder(EventRuntime runtime, Action<string> warn, string slot,
            out Result failed)
        {
            failed = null;
            if (runtime == null)
            {
                failed = Report(warn, new Result
                {
                    Outcome = Outcome.Failed,
                    Slot = slot,
                    Message = "Saving needs the running game, so the slot was not touched.",
                });
                return "";
            }
            if (string.IsNullOrEmpty(runtime.SaveProjectFolder))
            {
                failed = Report(warn, new Result
                {
                    Outcome = Outcome.Failed,
                    Slot = slot,
                    Message = "I couldn't find the project folder, so there is nowhere to keep saves. Saving is off for this session.",
                });
                return "";
            }
            return runtime.SaveProjectFolder;
        }

        private static Outcome OutcomeOf(SaveFileStatus status)
        {
            switch (status)
            {
                case SaveFileStatus.Missing:
                    return Outcome.Missing;
                case SaveFileStatus.Corrupt:
                    return Outcome.Damaged;
                case SaveFileStatus.NewerVersion:
                    return Outcome.NewerVersion;
                default:
                    return Outcome.Failed;
            }
        }

        /// <summary>Raises one warning for any message worth showing, and returns the same result.</summary>
        private static Result Report(Action<string> warn, Result result)
        {
            if (warn != null && !string.IsNullOrEmpty(result.Message))
                warn(result.Message);
            return result;
        }
    }
}
