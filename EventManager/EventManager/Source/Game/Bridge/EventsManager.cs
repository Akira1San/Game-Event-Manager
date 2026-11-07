using System;
using System.Collections.Generic;
using System.Linq;
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
        private bool _rightMouseDownLastFrame;
        private bool _middleMouseDownLastFrame;
        private UIControl _hoveredUI;
        private Color _hoveredBaseColor;
        /// <summary>Last-tick pressed state per UI button, used to raise press/release edge triggers.</summary>
        private readonly Dictionary<Actor, bool> _buttonPressedLastTick = new Dictionary<Actor, bool>();
        /// <summary>Last click time per actor, used to detect double-clicks.</summary>
        private readonly Dictionary<Actor, double> _buttonLastClickTime = new Dictionary<Actor, double>();
        private const double DoubleClickSeconds = 0.3;

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

        /// <summary>A particle one-shot finished on this actor — push a ParticleEnd trigger to its sheet.</summary>
        public void HandleParticleEnd(Actor actor, string particleName)
        {
            if (actor == null || string.IsNullOrEmpty(particleName))
                return;
            Runtime.RaisePush(actor, new PushSignal { Kind = TriggerKind.ParticleEnd, Name = particleName });
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
            Runtime.OnSetButtonEnabled = (context, thing, enabled) =>
                FlaxActions.SetButtonEnabled(thing, enabled);
            Runtime.OnSetImageBrush = (context, thing, sprite) =>
                FlaxActions.SetImageBrush(thing, sprite);
            Runtime.OnSetControlColor = (context, thing, color) =>
                FlaxActions.SetControlColor(thing, color);
            Runtime.OnPlayStepSound = (context, material, clipName) =>
                FlaxActions.PlayStepSound(context, material, clipName);

            // RTS selection presentation (Task 18 Part A): rings follow the selection and
            // the 1–0 control-group indicators refresh whenever a group changes.
            Runtime.Selection.OnActorSelected = target => SelectionView.SetRing(target, true);
            Runtime.Selection.OnActorDeselected = target => SelectionView.SetRing(target, false);
            Runtime.Selection.OnSelectionChanged = () => SelectionView.UpdateControlGroupIndicators(Runtime.Selection);
            Runtime.Selection.OnControlGroupsChanged = () => SelectionView.UpdateControlGroupIndicators(Runtime.Selection);

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

            Runtime.Variables.SetGlobal("cameraFollow", ValueBox.OfNumber(0));
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
            SheetInstance existing = Runtime.FindSheetFor(actor);
            if (existing != null)
            {
                // Keep the opt-in and step-sound map in sync when a component re-registers
                // (clone, scene wake).
                existing.UsePhysicsMovement = component.UsePhysicsMovement;
                existing.GroundCheckDistance = component.ReadCharacterBodySettings().GroundCheckDistance;
                SyncStepSoundMap(existing, component.StepSoundMap);
                return;
            }
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
            SheetInstance sheet = Runtime.AddSheet(actor, program.File, source);
            sheet.UsePhysicsMovement = component.UsePhysicsMovement;
            sheet.GroundCheckDistance = component.ReadCharacterBodySettings().GroundCheckDistance;
            SyncStepSoundMap(sheet, component.StepSoundMap);
        }

        /// <summary>Copies the component's inspector-authored material→clip list onto the
        /// runtime sheet so <c>play step sound "&lt;material&gt;"</c> can resolve clips.</summary>
        private void SyncStepSoundMap(SheetInstance sheet, List<EventSheet.StepSoundEntry> entries)
        {
            sheet.StepSoundMap.Clear();
            if (entries == null)
                return;
            foreach (var entry in entries)
            {
                string material = entry.Material == null ? "" : entry.Material.Trim();
                string clip = entry.Clip == null ? "" : entry.Clip.Trim();
                if (material.Length == 0 || clip.Length == 0)
                    continue;
                sheet.StepSoundMap[material] = clip;
            }
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
            Runtime.Selection.Reset();
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
            UpdateButtons();
            UpdateCameraFollow();
            UpdatePlayerLookAtMouse();
            Runtime.Tick(Time.GameTime);
        }

        private void UpdateCameraFollow()
        {
            var camera = FlaxWorld.FindCamera();
            if (camera == null)
                return;

            // Explicit follow: camera follows <thing> (set by StartCameraFollow).
            var target = FlaxActions.CameraFollowTarget;
            if (target != null && target.Scene != null)
            {
                camera.Position = target.Position + FlaxActions.CameraFollowOffset;
                return;
            }

            // Legacy auto-follow: only if explicitly enabled via events.
            if (Runtime.Variables.GetGlobal("cameraFollow").AsNumber() <= 0)
                return;
            var player = Level.FindActor("Player") as Actor;
            if (player == null)
                return;
            float height = (float)Runtime.Variables.GetGlobal("cameraHeight").AsNumber();
            float depth = (float)Runtime.Variables.GetGlobal("cameraDepth").AsNumber();
            camera.Position = player.Position + new Vector3(0f, height, depth);
        }

        private void UpdatePlayerLookAtMouse()
        {
            var player = Level.FindActor("Player") as Actor;
            var camera = FlaxWorld.FindCamera();
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
            Float2 mousePos = Input.MousePosition;
            Float2 screenSize = Screen.Size;
            Float2 screenCenter = screenSize * 0.5f;

            foreach (string key in ActionWords.KeyNamesForScanning)
            {
                bool held = _world.IsKeyHeld(key);
                _heldLastTick.TryGetValue(key, out bool wasHeld);
                if (held && !wasHeld)
                {
                    var press = new PushSignal { Kind = TriggerKind.KeyPressed, Name = key };
                    Runtime.RaisePushAll(press);
                    // Pressing 1–0 recalls that control group (classic RTS behaviour).
                    if (Selection.TryGroupKey(key, out int group))
                        Runtime.SelectControlGroup(group);
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

            // Hover detection (every frame) with visual text-color feedback
            UpdateHover(mousePos);

            // Mouse click detection for `when <thing> is clicked` / right / middle
            bool leftMouseDown = Input.GetMouseButtonDown(MouseButton.Left);
            if (leftMouseDown && !_leftMouseDownLastFrame)
            {
                HandleMouseClick(mousePos, screenCenter, TriggerKind.Clicked);
                var clickWorld = RaycastToWorld(mousePos);
                if (clickWorld.HasValue)
                    FlaxActions.LastClickPosition = clickWorld.Value;
            }
            _leftMouseDownLastFrame = leftMouseDown;

            bool rightMouseDown = Input.GetMouseButtonDown(MouseButton.Right);
            if (rightMouseDown && !_rightMouseDownLastFrame)
                HandleMouseClick(mousePos, screenCenter, TriggerKind.RightClicked);
            _rightMouseDownLastFrame = rightMouseDown;

            bool middleMouseDown = Input.GetMouseButtonDown(MouseButton.Middle);
            if (middleMouseDown && !_middleMouseDownLastFrame)
                HandleMouseClick(mousePos, screenCenter, TriggerKind.MiddleClicked);
            _middleMouseDownLastFrame = middleMouseDown;
        }

        /// <summary>Per-frame hover tracking: when the mouse enters a UIControl, switch its
        /// label to the highlighted color and raise a Hovered push; when it leaves, restore
        /// the base color.</summary>
        private void UpdateHover(Float2 mousePos)
        {
            UIControl newHovered = null;
            foreach (Actor actor in SceneActors())
            {
                if (actor == null || !actor.IsActive)
                    continue;
                var ui = actor as UIControl;
                if (ui == null)
                    continue;
                if (MouseOverUIControl(ui, mousePos))
                {
                    newHovered = ui;
                    break;
                }
            }

            if (newHovered == _hoveredUI)
                return;

            // Leave previous
            if (_hoveredUI != null)
            {
                var prevLabel = _hoveredUI.Control as FlaxEngine.GUI.Label;
                if (prevLabel != null)
                    prevLabel.TextColor = _hoveredBaseColor;
            }

            // Enter new
            _hoveredUI = newHovered;
            if (_hoveredUI != null)
            {
                var label = _hoveredUI.Control as FlaxEngine.GUI.Label;
                if (label != null)
                {
                    _hoveredBaseColor = label.TextColor;
                    label.TextColor = label.TextColorHighlighted;
                    Runtime.RaisePush(_hoveredUI, new PushSignal { Kind = TriggerKind.Hovered, Name = _hoveredUI.Name });
                }
            }
        }

        /// <summary>Raises <paramref name="kind"/> on the actor under the cursor. UIControls
        /// are hit-tested in screen space; everything else (the ground, a chest, ...) is
        /// hit-tested with a camera-through-mouse physics raycast — "the ground is just
        /// another actor", so <c>when Ground is clicked</c> works on any actor.</summary>
        private void HandleMouseClick(Float2 mousePos, Float2 screenCenter, TriggerKind kind)
        {
            var actors = SceneActors();
            bool uiHit = false;
            foreach (Actor actor in actors)
            {
                if (actor == null || !actor.IsActive)
                    continue;
                var ui = actor as UIControl;
                if (ui == null)
                    continue;
                if (MouseOverUIControl(ui, mousePos))
                {
                    uiHit = true;
                    // Clicks are broadcast by name: the clicked actor's own `when me is clicked`
                    // fires, and other sheets can listen with `when Ground is clicked`.
                    HandleClick(actor, kind);
                }
            }
            if (uiHit)
                return;

            // 3D actors under the cursor: raycast from the scene camera through the mouse.
            if (!FlaxWorld.TryGetMouseRay(mousePos, out Ray ray))
                return;
            if (!Physics.LineCastAll(ray.Position, ray.Position + ray.Direction * 10000f, out var hits, ~0u, true))
                return;
            Actor bestActor = null;
            float best = float.MaxValue;
            foreach (var hit in hits)
            {
                if (hit.Distance < best)
                {
                    best = hit.Distance;
                    bestActor = ActorOfHit(hit.Collider);
                }
            }
            if (bestActor != null)
                HandleClick(bestActor, kind);
        }

        /// <summary>Raises a click (or double-click) on <paramref name="actor"/>. A second left
        /// click on the same actor within <see cref="DoubleClickSeconds"/> becomes a
        /// <c>DoubleClicked</c> trigger instead of a second <c>Clicked</c>.</summary>
        private void HandleClick(Actor actor, TriggerKind kind)
        {
            if (actor == null)
                return;
            double now = Time.GameTime;
            bool doubleClick = kind == TriggerKind.Clicked &&
                _buttonLastClickTime.TryGetValue(actor, out double last) && now - last < DoubleClickSeconds;
            _buttonLastClickTime[actor] = now;
            Runtime.RaiseClick(actor, doubleClick ? TriggerKind.DoubleClicked : kind);
        }

        /// <summary>Polls every UI Button for press/release edges, raising <c>ButtonPressed</c>/
        /// <c>ButtonReleased</c> triggers, and prunes stale click/press tracking.</summary>
        private void UpdateButtons()
        {
            var dead = new List<Actor>();
            foreach (Actor actor in SceneActors())
            {
                if (actor == null || !actor.IsActive)
                {
                    dead.Add(actor);
                    continue;
                }
                var ui = actor as UIControl;
                if (ui == null)
                    continue;
                var button = ui.Control as FlaxEngine.GUI.Button;
                if (button == null)
                    continue;
                bool pressed = button.IsPressed;
                _buttonPressedLastTick.TryGetValue(actor, out bool wasPressed);
                if (pressed && !wasPressed)
                    Runtime.RaisePushAll(new PushSignal { Kind = TriggerKind.ButtonPressed, Name = actor.Name, Other = actor });
                else if (!pressed && wasPressed)
                    Runtime.RaisePushAll(new PushSignal { Kind = TriggerKind.ButtonReleased, Name = actor.Name, Other = actor });
                _buttonPressedLastTick[actor] = pressed;
            }
            foreach (Actor actor in dead)
                _buttonPressedLastTick.Remove(actor);
            // Prune actors whose scene is gone.
            foreach (var pair in _buttonPressedLastTick.ToArray())
                if (pair.Key == null || pair.Key.Scene == null)
                    _buttonPressedLastTick.Remove(pair.Key);
        }

        /// <summary>Walks up from a hit collider to the actor that carries an EventSheet, so
        /// 'when &lt;name&gt; is clicked' is delivered to the sheet on the actor (not merely to a
        /// collider child). Falls back to the collider actor itself.</summary>
        private static Actor ActorOfHit(PhysicsColliderActor collider)
        {
            if (collider == null)
                return null;
            Actor node = collider;
            while (node != null && !(node is Scene))
            {
                if (node.GetScript<EventSheet>() != null)
                    return node;
                node = node.Parent;
            }
            return collider;
        }

        /// <summary>Casts a ray from the scene camera through the mouse position and returns the
        /// first world hit point, or null when there is no camera or no hit. Used to turn a
        /// click on bare ground into the <c>goto there</c> target.</summary>
        private static Vector3? RaycastToWorld(Float2 mousePos)
        {
            if (!FlaxWorld.TryGetMouseRay(mousePos, out Ray ray))
                return null;
            if (!Physics.LineCastAll(ray.Position, ray.Position + ray.Direction * 10000f, out var hits, ~0u, true))
                return null;
            float best = float.MaxValue;
            Vector3? bestPoint = null;
            foreach (var hit in hits)
            {
                if (hit.Distance < best)
                {
                    best = hit.Distance;
                    bestPoint = hit.Point;
                }
            }
            return bestPoint;
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
