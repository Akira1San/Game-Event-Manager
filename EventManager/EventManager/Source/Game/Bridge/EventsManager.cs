using System;
using System.Collections.Generic;
using FlaxEngine;

namespace Game
{
    public enum EventContactMode
    {
        /// <summary>Distance checks only — works with no colliders at all.</summary>
        Distance,
        /// <summary>Flax physics trigger events only — needs the Bullet/Enemy collider setup.</summary>
        Colliders,
        /// <summary>Both sources, deduped, while comparing them.</summary>
        Both,
    }

    public class EventsManager : Script
    {
        public static EventsManager Instance;

        public EventRuntime Runtime = new EventRuntime();
        private readonly FlaxWorld _world = new FlaxWorld();
        private readonly Dictionary<string, bool> _heldLastTick = new Dictionary<string, bool>();
        private string _gameFilesFolder;
        private readonly Dictionary<string, string> _fileSignatures = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _shownErrors = new Dictionary<string, string>();
        private readonly HashSet<string> _missingShown = new HashSet<string>();
        private readonly List<EventSheet> _components = new List<EventSheet>();
        private List<EventFileResult> _programs = new List<EventFileResult>();
        private bool _booted;
        /// <summary>True once <c>when game starts</c> has fired this play session, so it runs
        /// only in the very first scene; <c>when scene starts</c> fires on every scene load.</summary>
        private static bool _sessionBegan;
        private float _nextWatchCheck;
        private bool _leftMouseDownLastFrame;

        [Tooltip("How touches/overlaps are detected. Distance needs no colliders; Colliders uses Flax trigger events; Both compares them without double-firing.")]
        public EventContactMode ContactMode = EventContactMode.Both;

        public void HandleTouch(Actor me, Actor other, bool leaving)
        {
            if (other == null || leaving)
                return;
            if (ContactMode == EventContactMode.Distance)
                return;
            Runtime.PushContact(TriggerKind.Touches, me, other);
            Runtime.PushContact(TriggerKind.Overlaps, me, other);
        }

        /// <summary>An animation one-shot finished on this actor — push an AnimEnd trigger to its sheet.</summary>
        public void HandleAnimEnd(Actor actor, string animName)
        {
            if (actor == null || string.IsNullOrEmpty(animName))
                return;
            Runtime.RaisePush(actor, new PushSignal { Kind = TriggerKind.AnimEnd, Name = animName });
        }

        public override void OnStart()
        {
            // Exactly one EventsManager per scene may run. A second one (usually an accident —
            // adding the manager to an actor that gets cloned) would double every trigger and,
            // because Flax Clone() copies scripts, multiply itself with every spawned copy.
            if (Instance != null && Instance != this && Instance) // `Instance` truthy = still a live engine object
            {
                Debug.LogWarning($"Event sheet: two EventsManager scripts in this scene — the one on \"{(Actor == null ? "an actor" : Actor.Name)}\" is ignored. Keep exactly one, somewhere permanent (never on a template that gets spawned).");
                Enabled = false;
                return;
            }
            Instance = this;
            FlaxActions.Warn = FlaxActions.WarnOnce;
            Runtime.World = _world;
            Runtime.PollOverlaps = true; // overlaps is distance-based in every mode
            Runtime.PollTouches = ContactMode != EventContactMode.Colliders;
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
            SweepComponents();
            AttachPrograms(programs, startedNow: true);
            _booted = true;
            Level.ActorDeleted += ActorGoneAway;

            if (!_sessionBegan)
            {
                _sessionBegan = true;
                Runtime.SessionStarted();
            }
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
            _missingShown.Clear();
            var byName = new Dictionary<string, EventFileResult>();
            foreach (EventFileResult program in programs)
                byName[program.Name] = program;
            foreach (SheetInstance sheet in Runtime.Sheets)
            {
                byName.TryGetValue(sheet.SourceName, out EventFileResult fresh);
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
            _programs = programs;
            foreach (EventFileResult program in programs)
            {
                if (!string.IsNullOrEmpty(program.Error))
                {
                    ShowFileError(program, startedNow);
                    continue;
                }
                if (program.Name == "Game" && !HasGlobalSheet())
                    Runtime.AddSheet(new GlobalHost(), program.File);
            }
            // Explicit components win: every registered EventSheet points at its file.
            foreach (EventSheet component in _components)
                TryAttachComponent(component);
            // Name fallback: only actors without any EventSheet component.
            foreach (Actor actor in SceneActors())
            {
                if (!actor.IsActive)
                    continue;
                if (actor.GetScript<EventSheet>() != null)
                    continue;
                if (Runtime.FindSheetFor(actor) != null)
                    continue;
                // UI buttons share Menu.events (defines `when <button> is clicked`);
                // no PlayButton.events/QuitButton.events exists, so fall back to Menu.
                string source = actor.Name;
                if (actor is UIControl && ProgramNamed(actor.Name) == null)
                    source = "Menu";
                AttachByName(actor, source);
            }
        }

        /// <summary>An EventSheet component appeared (scene sweep, editor-added, clone). Give it its
        /// event tree if it doesn't have one yet; priority rules live here.</summary>
        public void RegisterSheet(EventSheet component)
        {
            if (component == null || component.Actor == null)
                return;
            if (_components.Contains(component))
                return;
            _components.Add(component);
            TryAttachComponent(component);
        }

        public void UnregisterSheet(EventSheet component)
        {
            _components.Remove(component);
            if (component != null && component.Actor != null)
                Runtime.RemoveActorSheet(component.Actor);
        }

        private void TryAttachComponent(EventSheet component)
        {
            Actor actor = component == null ? null : component.Actor;
            if (actor == null)
                return;
            if (Runtime.FindSheetFor(actor) != null)
                return;
            string source = component.SourceName();
            EventFileResult program = ProgramNamed(source);
            if (program == null)
            {
                WarnMissingFile(source, actor.Name);
                return;
            }
            if (!string.IsNullOrEmpty(program.Error))
            {
                ShowFileError(program, !_booted);
                return;
            }
            _missingShown.Remove(source);
            Runtime.AddSheet(actor, program.File, source);
        }

        /// <summary>Name-based fallback: attach <paramref name="source"/>.events to one actor by
        /// standing up an EventSheet component for it, so clones inherit logic too.</summary>
        private void AttachByName(Actor actor, string source)
        {
            EventFileResult program = ProgramNamed(source);
            if (program == null || !string.IsNullOrEmpty(program.Error))
                return;
            var added = actor.AddScript<EventSheet>();
            if (added == null)
            {
                Runtime.AddSheet(actor, program.File, source);
                return;
            }
            added.EventFile = source;
            if (!_components.Contains(added))
                _components.Add(added);
            TryAttachComponent(added);
        }

        /// <summary>Runtime spawns / show: the actor carries its component (clones copy scripts),
        /// so component-first wins; name fallback otherwise.</summary>
        public void AttachSpawnedActor(Actor actor)
        {
            if (actor == null || actor.Name == "Game")
                return;
            if (Runtime.FindSheetFor(actor) != null)
                return;
            var component = actor.GetScript<EventSheet>();
            if (component != null)
            {
                RegisterSheet(component);
                return;
            }
            AttachByName(actor, actor.Name);
        }

        private EventFileResult ProgramNamed(string sourceName)
        {
            foreach (EventFileResult program in _programs)
                if (program.Name == sourceName)
                    return program;
            return null;
        }

        private void WarnMissingFile(string source, string actorName)
        {
            if (_missingShown.Contains(source))
                return;
            _missingShown.Add(source);
            var names = new List<string>();
            foreach (EventFileResult program in _programs)
                names.Add(program.Name);
            string suggestion = EventFiles.SuggestFileName(source, names);
            string hint = suggestion == null ? "" : $" Did you mean \"{suggestion}.events\"?";
            Debug.LogWarning($"Event sheet: the Event Sheet on \"{actorName}\" points at \"{source}.events\", but no such file exists in the GameFiles folder.{hint} I'll attach it the moment that file shows up.");
        }

        private void ActorGoneAway(Actor actor)
        {
            for (int i = _components.Count - 1; i >= 0; i--)
            {
                var component = _components[i];
                if (component == null || ReferenceEquals(component.Actor, actor))
                    _components.RemoveAt(i);
            }
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
            _components.Clear();
            Runtime.RemoveAllSheets();
        }

        public override void OnUpdate()
        {
            if (Instance != this)
                return; // a disabled duplicate or a re-enabled stray never ticks
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

            // Mouse click detection for `when <thing> is clicked`
            bool leftMouseDown = Input.GetMouseButtonDown(MouseButton.Left);
            if (leftMouseDown && !_leftMouseDownLastFrame)
            {
                Float2 mousePos = Input.MousePosition;
                Float2 screenSize = Screen.Size;
                Float2 screenCenter = screenSize * 0.5f;
                var actors = SceneActors();
                int uiCount = 0;
                foreach (Actor actor in actors)
                {
                    if (actor == null || !actor.IsActive)
                        continue;
                    var ui = actor as UIControl;
                    if (ui == null)
                        continue;
                    uiCount++;
                    if (MouseOverUIControl(ui, mousePos))
                    {
                        Runtime.RaisePush(actor, new PushSignal { Kind = TriggerKind.Clicked, Name = actor.Name });
                        Debug.LogWarning($"Clicked UI actor: {actor.Name}");
                    }
                    else
                    {
                        // Debug: show computed screen-space center vs mouse so coordinate
                        // conversion can be verified in-editor.
                        string text = ui.Control != null ? ((FlaxEngine.GUI.Label)ui.Control).Text ?? "" : "";
                        Float2 center = MouseControlCenter(ui);
                        Debug.LogWarning($"  No hit: actor={actor.Name}, text=\"{text}\", center=({center.X:0},{center.Y:0}), uiPos=({ui.Position.X:0},{ui.Position.Y:0},{ui.Position.Z:0}), mouse=({mousePos.X:0},{mousePos.Y:0}), screenCenter=({screenCenter.X:0},{screenCenter.Y:0})");
                    }
                }
                Debug.LogWarning($"Mouse down: actors={actors.Count}, uiControls={uiCount}, mouse=({mousePos.X:0},{mousePos.Y:0}), screenCenter=({screenCenter.X:0},{screenCenter.Y:0})");
            }
            _leftMouseDownLastFrame = leftMouseDown;
        }

        /// <summary>Screen-space center of a UIControl (top-left origin, Y down), matching
        /// Input.MousePosition. Actor.Position is already world-space (includes the parent
        /// UICanvas transform); for UI on a screen-space canvas the X component lines up with
        /// screen space while Y is world up, so we flip Y only. We deliberately do NOT add
        /// Screen.Size*0.5 (that would double-count) and do NOT re-walk the parent chain
        /// (Position already includes it — walking would double-count).</summary>
        private static Float2 MouseControlCenter(UIControl ui)
        {
            return new Float2(ui.Position.X, -ui.Position.Y);
        }

        /// <summary>Approximate screen-space hit test for a UIControl on a screen-space canvas.</summary>
        private static bool MouseOverUIControl(UIControl ui, Float2 mousePos)
        {
            if (ui.Control == null)
                return false;
            var label = ui.Control as FlaxEngine.GUI.Label;
            if (label == null)
                return false;

            Float2 center = MouseControlCenter(ui);

            // Estimate hit box from text size (generous minimum so small buttons are easy to hit)
            string text = label.Text ?? "";
            float fontSize = label.Font.Size > 0 ? label.Font.Size : 14f;
            float textWidth = text.Length * fontSize * 0.6f;
            float textHeight = fontSize * 1.2f;
            Float2 size = new Float2(Math.Max(textWidth, 120f), Math.Max(textHeight, 40f));
            Float2 halfSize = size * 0.5f;

            bool xHit = mousePos.X >= center.X - halfSize.X && mousePos.X <= center.X + halfSize.X;
            bool yHit = mousePos.Y >= center.Y - halfSize.Y && mousePos.Y <= center.Y + halfSize.Y;

            return xHit && yHit;
        }

        /// <summary>Collect every EventSheet component currently on scene actors, so manager and
        /// component `OnStart` can happen in either order.</summary>
        private void SweepComponents()
        {
            foreach (Actor actor in SceneActors())
            {
                foreach (EventSheet component in actor.GetScripts<EventSheet>())
                {
                    if (component == null || _components.Contains(component))
                        continue;
                    _components.Add(component);
                }
            }
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
