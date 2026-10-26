using System;
using System.Collections.Generic;
using FlaxEngine;

namespace Game
{
    public class EventsManager : Script
    {
        public static EventsManager Instance;

        public EventRuntime Runtime = new EventRuntime();
        private readonly FlaxWorld _world = new FlaxWorld();
        private readonly Dictionary<string, bool> _heldLastTick = new Dictionary<string, bool>();
        private string _gameFilesFolder;
        private readonly Dictionary<string, string> _fileSignatures = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _shownErrors = new Dictionary<string, string>();
        private List<EventFileResult> _programs = new List<EventFileResult>();
        private float _nextWatchCheck;

        public void HandleTouch(Actor me, Actor other, bool leaving)
        {
            if (other == null || leaving)
                return;
            Runtime.PushContact(TriggerKind.Touches, me, other);
            Runtime.PushContact(TriggerKind.Overlaps, me, other);
        }

        public override void OnStart()
        {
            Instance = this;
            FlaxActions.Warn = FlaxActions.WarnOnce;
            Runtime.World = _world;
            Runtime.OnRuntimeWarning = message => Debug.LogWarning("Event sheet: " + message);
            Runtime.OnRawAction = (context, words) =>
                FlaxActions.Execute(context, ActionWords.Parse(words, CancelOneTime));
            Runtime.OnSetText = (context, thing, value) =>
            {
                FlaxActions.SetText(thing, value.AsText());
            };

            _gameFilesFolder = EventFiles.LocateGameFiles(System.IO.Directory.GetCurrentDirectory())
                ?? EventFiles.LocateGameFiles(AppContext.BaseDirectory);
            if (_gameFilesFolder == null)
            {
                Debug.LogWarning("I couldn't find a GameFiles folder with .events files — events won't run this scene.");
                return;
            }
            var programs = EventFiles.LoadAll(_gameFilesFolder);
            _programs = programs;
            RememberSignatures(programs);
            AttachPrograms(programs, startedNow: true);
            Level.ActorDeleted += ActorGoneAway;

            Runtime.SessionStarted();
            Runtime.SceneStarted();

            Runtime.Variables.SetGlobal("cameraFollow", ValueBox.OfNumber(1));
            Runtime.Variables.SetGlobal("cameraHeight", ValueBox.OfNumber(260));
            Runtime.Variables.SetGlobal("cameraDepth", ValueBox.OfNumber(-170));
            Runtime.Variables.SetGlobal("LookAtMouse", ValueBox.OfNumber(0));
        }

        private void RememberSignatures(List<EventFileResult> programs)
        {
            _fileSignatures.Clear();
            foreach (EventFileResult program in programs)
                _fileSignatures[program.Name] = FileSignature(program.Path);
        }

        private static string FileSignature(string path)
        {
            try
            {
                return System.IO.File.GetLastWriteTimeUtc(path).Ticks.ToString();
            }
            catch
            {
                return "";
            }
        }

        private void CheckForChanges()
        {
            if (_gameFilesFolder == null || Time.UnscaledGameTime < _nextWatchCheck)
                return;
            _nextWatchCheck = Time.UnscaledGameTime + 0.25f;
            var programs = EventFiles.LoadAll(_gameFilesFolder);
            bool changed = false;
            foreach (EventFileResult program in programs)
            {
                string signature = FileSignature(program.Path);
                _fileSignatures.TryGetValue(program.Name, out string known);
                if (known != null && known == signature)
                    continue;
                changed = true;
                break;
            }
            if (!changed)
                return;
            RememberSignatures(programs);
            ApplyReload(programs);
        }

        private void ApplyReload(List<EventFileResult> programs)
        {
            _programs = programs;
            _shownErrors.Clear();
            var byName = new Dictionary<string, EventFileResult>();
            foreach (EventFileResult program in programs)
                byName[program.Name] = program;
            foreach (SheetInstance sheet in Runtime.Sheets)
            {
                bool global = sheet.ActorName == "Game";
                byName.TryGetValue(global ? "Game" : sheet.ActorName, out EventFileResult fresh);
                if (fresh == null)
                    continue;
                if (!string.IsNullOrEmpty(fresh.Error))
                {
                    if (_shownErrors.TryGetValue(fresh.Name, out string previous) && previous == fresh.Error)
                        continue;
                    _shownErrors[fresh.Name] = fresh.Error;
                    Debug.LogWarning($"Event sheet: your latest edit to {fresh.Name}.events has a problem, so the old version keeps playing for now:\n    {fresh.Error}");
                    continue;
                }
                _shownErrors.Remove(fresh.Name);
                Runtime.SwapSheetProgram(sheet, fresh.File);
            }
            AttachPrograms(programs, startedNow: false);
        }

        private void AttachPrograms(List<EventFileResult> programs, bool startedNow)
        {
            foreach (EventFileResult program in programs)
            {
                if (!string.IsNullOrEmpty(program.Error))
                {
                    ShowFileError(program, startedNow);
                    continue;
                }
                if (program.Name == "Game")
                {
                    if (!HasGlobalSheet())
                        Runtime.AddSheet(new GlobalHost(), program.File);
                    continue;
                }
                foreach (Actor actor in SceneActors())
                {
                    if (actor.Name != program.Name || !actor.IsActive)
                        continue;
                    AttachSpawnedActor(actor);
                }
            }
        }

        public void AttachSpawnedActor(Actor actor)
        {
            if (actor == null || actor.Name == "Game")
                return;
            foreach (SheetInstance sheet in Runtime.Sheets)
            {
                if (ReferenceEquals(sheet.Actor, actor))
                    return;
            }
            foreach (EventFileResult program in _programs)
            {
                if (program.Name != actor.Name || !string.IsNullOrEmpty(program.Error))
                    continue;
                Runtime.AddSheet(actor, program.File);
                StickerTo(actor);
                return;
            }
        }

        private void ActorGoneAway(Actor actor)
        {
            Runtime.RemoveActorSheet(actor);
        }

        private void ShowFileError(EventFileResult program, bool startedNow)
        {
            string head = startedNow
                ? $"I found a problem in {program.Name}.events and I'm skipping it for now"
                : $"your latest edit to {program.Name}.events has a problem, the old version keeps playing";
            if (_shownErrors.TryGetValue(program.Name, out string previous) && previous == program.Error && !startedNow)
                return;
            _shownErrors[program.Name] = program.Error;
            Debug.LogWarning($"Event sheet: {head}:\n    {program.Error}");
        }

        private bool HasGlobalSheet()
        {
            foreach (SheetInstance sheet in Runtime.Sheets)
                if (sheet.Actor is GlobalHost)
                    return true;
            return false;
        }

        private void CancelOneTime(string message)
        {
            FlaxActions.WarnOnce("Event sheet: " + message);
        }

        public override void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            Level.ActorDeleted -= ActorGoneAway;
            FlaxActions.ClearSession();
            Runtime.RemoveAllSheets();
        }

        public override void OnUpdate()
        {
            float delta = (float)(Time.DeltaTime > 0 ? Time.DeltaTime : 0.0001);
            FlaxActions.DeltaTime = delta;
            FlaxActions.Step(delta);
            CheckForChanges();
            InputEdges();
            UpdateCameraFollow();
            UpdatePlayerLookAtMouse();
            Runtime.Tick(Time.GameTime);
        }

        private void UpdateCameraFollow()
        {
            var player = Level.FindActor("Player") as Actor;
            var camera = Level.FindActor("Camera") as Actor;
            if (player == null || camera == null)
                return;
            if (Runtime.Variables.GetGlobal("cameraFollow").AsNumber() <= 0)
                return;
            float height = (float)Runtime.Variables.GetGlobal("cameraHeight").AsNumber();
            float depth = (float)Runtime.Variables.GetGlobal("cameraDepth").AsNumber();
            camera.Position = player.Position + new Vector3(0f, height, depth);
        }

        private void UpdatePlayerLookAtMouse()
        {
            var player = Level.FindActor("Player") as Actor;
            var camera = Level.FindActor("Camera") as Actor;
            if (player == null || camera == null)
                return;
            if (Runtime.Variables.GetGlobal("LookAtMouse").AsNumber() <= 0)
                return;

            Float2 mousePos = Input.MousePosition;
            Float2 screenSize = Screen.Size;
            if (screenSize.LengthSquared <= 0)
                return;

            // Normalize mouse to -1..1 screen space (Y flipped: up is positive)
            float nx = (mousePos.X - screenSize.X * 0.5f) / (screenSize.X * 0.5f);
            float ny = (screenSize.Y * 0.5f - mousePos.Y) / (screenSize.Y * 0.5f);

            // Camera world axes, flattened to the ground plane (XZ)
            Vector3 camRight = Vector3.Transform(Vector3.Right, camera.Orientation);
            Vector3 camForward = Vector3.Transform(Vector3.Forward, camera.Orientation);
            camRight.Y = 0;
            camForward.Y = 0;
            if (camRight.LengthSquared > 0)
                camRight.Normalize();
            if (camForward.LengthSquared > 0)
                camForward.Normalize();

            Vector3 targetDir = camRight * nx + camForward * ny;
            if (targetDir.LengthSquared > 0.0001f)
            {
                targetDir.Normalize();
                player.LookAt(player.Position + targetDir);
            }
        }

        private void InputEdges()
        {
            foreach (string key in ActionWords.KeyNamesForScanning)
            {
                bool held = _world.IsKeyHeld(key);
                _heldLastTick.TryGetValue(key, out bool wasHeld);
                if (held && !wasHeld)
                {
                    var press = new PushSignal { Kind = TriggerKind.KeyPressed, Name = key };
                    Runtime.RaisePushAll(press);
                }
                else if (wasHeld && !held)
                {
                    Runtime.RaisePushAll(new PushSignal { Kind = TriggerKind.KeyReleased, Name = key });
                }
                _heldLastTick[key] = held;
            }
            float wheel = Input.MouseScrollDelta;
            if (wheel > 0)
                Runtime.RaisePushAll(new PushSignal { Kind = TriggerKind.WheelScrollUp });
            else if (wheel < 0)
                Runtime.RaisePushAll(new PushSignal { Kind = TriggerKind.WheelScrollDown });
        }

        private static void StickerTo(Actor actor)
        {
            var sticker = actor.AddScript<EventSheet>();
            sticker?.Setup(Instance);
        }

        private static List<Actor> SceneActors()
        {
            var found = new List<Actor>();
            var scenes = Level.Scenes;
            for (int s = 0; s < scenes.Length; s++)
            {
                var scene = scenes[s];
                if (scene == null)
                    continue;
                Collect(scene, found);
            }
            return found;
        }

        private static void Collect(Actor node, List<Actor> found)
        {
            foreach (Actor child in node.Children)
            {
                if (child == null)
                    continue;
                found.Add(child);
                Collect(child, found);
            }
        }
    }
}
