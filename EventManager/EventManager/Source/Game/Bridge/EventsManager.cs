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
        /// <summary>The cursors this scene authors (<c>Cursor_&lt;Name&gt;</c>) and their art
        /// (Task 48 Part C). Rescanned a few times a second after the tick.</summary>
        public readonly CursorCatalog Cursors = new CursorCatalog();
        private readonly FlaxWorld _world = new FlaxWorld();
        private readonly Dictionary<string, bool> _heldLastTick = new Dictionary<string, bool>();
        /// <summary>"player actionId" → was that gamepad button held last tick.</summary>
        private readonly Dictionary<string, bool> _gamepadLast = new Dictionary<string, bool>();
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
        /// <summary>Focusable UI controls in the scene, refreshed every frame (Task 15 Part E).</summary>
        private readonly List<UIControl> _focusControls = new List<UIControl>();
        /// <summary>The UI control focus was on last frame, used to raise focus edges.</summary>
        private UIControl _focusedUI;
        /// <summary>Last-tick held state of the Navigate* input actions (keyboard arrows / D-pad).</summary>
        private readonly Dictionary<string, bool> _navigationLastHeld = new Dictionary<string, bool>();
        /// <summary>Hover/focus tint bookkeeping per UIControl, so colors are only written on transitions.</summary>
        private readonly Dictionary<UIControl, UiVisualState> _uiVisualStates = new Dictionary<UIControl, UiVisualState>();
        private static readonly Color FocusTint = new Color(1f, 0.84f, 0.25f, 1f);
        private static readonly string[] NavigationActions = { "NavigateUp", "NavigateDown", "NavigateLeft", "NavigateRight" };
        private static readonly FocusDirection[] NavigationDirections =
        {
            FocusDirection.Up, FocusDirection.Down, FocusDirection.Left, FocusDirection.Right,
        };

        /// <summary>Per-control tint state: colors are captured when hover/focus starts and
        /// restored when it ends, so tweens and `set color of` are not overwritten every frame.</summary>
        private sealed class UiVisualState
        {
            public bool Seen;
            public bool Focused;
            public bool Hovered;
            public bool HasText;
            public bool HasImage;
            public Color TextBase;
            public Color ImageBase;
        }
        /// <summary>The card button the left mouse went down on, so its release raises
        /// <c>ButtonReleased</c> for the same actor even if the cursor moved off it.</summary>
        private UIControl _pressedButton;
        /// <summary>The card buttons the last poll saw, so a layout change is logged once. A card
        /// button that does nothing is hidden by a sheet, has no GUI control at all, or is
        /// disabled, and that line names which.</summary>
        private string _buttonPolledSignature;
        /// <summary>Last click time per actor, used to detect double-clicks.</summary>
        private readonly Dictionary<Actor, double> _buttonLastClickTime = new Dictionary<Actor, double>();
        /// <summary>Last-tick TextBox contents, used to raise text-change edges.</summary>
        private readonly Dictionary<Actor, string> _textLastValue = new Dictionary<Actor, string>();
        private const double DoubleClickSeconds = 0.3;
        /// <summary>Scene actor that shows where an aimed click would land (Task 27 Part A).</summary>
        private const string AimMarkerName = "AimMarker";
        private Actor _aimMarker;
        private bool _aimMarkerSearched;
        /// <summary>Left button went down on bare ground, so a drag may become a marquee
        /// (Task 29 Part B). A press on a unit, a building or a UIControl never sets this.</summary>
        private bool _boxPressed;
        /// <summary>The press grew past <see cref="BoxSelect.DragThreshold"/>: a marquee is showing.</summary>
        private bool _boxDragging;
        /// <summary>Where the button went down, in screen pixels.</summary>
        private Float2 _boxStart;
        /// <summary>Shift as it was when the drag began, so the modifier is read once, not at release.</summary>
        private bool _boxShift;

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

        /// <summary>
        /// Records a noise for the hearing system (Task 16 Part B). The bus keeps it for a fraction
        /// of a second and <see cref="EventRuntime.Tick"/> expires it, so `when me hears` sees the
        /// sounds made this frame and nothing stale.
        /// </summary>
        public void EmitSound(string name, Vector3 position, double radius, Actor source)
        {
            Runtime.Sounds.Emit(name, source, position.X, position.Y, position.Z, radius, Time.GameTime);
        }

        /// <summary>A tween finished — broadcast <c>when tween &lt;thing&gt; ends</c>. Broadcast
        /// (not just the subject's sheet) so a named listener on the Game sheet also hears it.</summary>
        public void HandleTweenEnd(Actor actor, string tweenName)
        {
            if (actor == null || string.IsNullOrEmpty(tweenName))
                return;
            Runtime.RaisePushAll(new PushSignal { Kind = TriggerKind.TweenEnd, Name = tweenName, Other = actor });
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
            Runtime.OnSetText = (context, thing, value, reveal) =>
            {
                FlaxActions.SetText(thing, value.AsText(), reveal);
            };
            Runtime.OnSetButtonEnabled = (context, thing, enabled) =>
                FlaxActions.SetButtonEnabled(thing, enabled);
            Runtime.OnSetImageBrush = (context, thing, sprite) =>
                FlaxActions.SetImageBrush(thing, sprite);
            Runtime.OnSetControlColor = (context, thing, color) =>
                FlaxActions.SetControlColor(thing, color);
            Runtime.OnSetProgress = (context, thing, value, reveal) =>
                FlaxActions.SetProgress(thing, value.AsNumber(), reveal);
            Runtime.OnSetSlider = (context, thing, value) =>
                FlaxActions.SetSlider(thing, value.AsNumber());
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
            // The resource list is read before the other sheets are parsed, so a resource name or
            // spelling a game declares here is understood by every sheet it writes. Orphan
            // resource names simply stay global variables, so this never breaks a scene.
            ResourceManager.LoadData(EventFiles.LoadOne(_gameFilesFolder, ResourceManager.FileName)?.File);
            var programs = EventFiles.LoadAll(_gameFilesFolder);
            _programs = programs;
            // Typed enemy stats come from the data file, read before any sheet runs so an
            // enemy's `me appears` never sees half a type (Task 16 Part D).
            EnemyTypes.LoadData(ProgramNamed("EnemyTypes")?.File);
            // Weapon stats come from the data file too, so `spawn Bullet` already knows the
            // current weapon's bullet type on the first shot (Task 16 Part C).
            WeaponData.LoadData(ProgramNamed("WeaponData")?.File);
            // Spell definitions (Task 9 E3) and the item/affix tables (Task 9 E7) load the same
            // way, so `cast "Fireball"` and `spawn Ring rarity "Rare"` are complete on the first
            // tick and a data edit takes effect on the next reload.
            SpellData.LoadData(ProgramNamed(SpellData.FileName)?.File);
            ItemData.LoadData(ProgramNamed(ItemData.FileName)?.File);
            AffixData.LoadData(ProgramNamed(AffixData.FileName)?.File);
            // Attack matchups come from their own data file, so `me can attack other` is settled
            // before the first shot (Task 23 Part B).
            Matchups.LoadData(ProgramNamed(Matchups.FileName)?.File);
            // Upgrade definitions (time, cost, required building, level cap) load the same way, so
            // `research "Stim Pack"` knows its price before the first order (Task 27 Part D).
            Upgrades.LoadData(ProgramNamed(Upgrades.FileName)?.File);
            RememberSignatures(programs);
            SweepComponents();
            AttachPrograms(programs, startedNow: true);
            _booted = true;
            Level.ActorDeleted += ActorGoneAway;
            // Scenes load additively (`load scene` keeps the current one), so this manager can
            // outlive the scene its aim marker came from: a later scene may author its own
            // `AimMarker`, and the actor cached from the old scene is dead (Task 27 Part A).
            Level.SceneLoaded += SceneLoadedAim;

            if (!_sessionBegan)
            {
                _sessionBegan = true;
                Runtime.SessionStarted();
            }
            // Level progression (Task 16 Part G): the order `next level` walks comes from the
            // project's scenes, and the scene that just loaded is the current level.
            Runtime.Levels.SetOrder(FlaxActions.KnownSceneNames());
            Runtime.OnLevelAdvance = name => FlaxActions.LoadLevel(name);
            Runtime.SceneStarted(Level.ScenesCount > 0 ? Level.GetScene(0).Name : "");

            // Camera framing lives in globals so events can author it (variable names start
            // with a capital letter): `set CameraPitch to 90` before `camera follows me`.
            Runtime.Variables.SetGlobal("CameraFollow", ValueBox.OfNumber(0));
            Runtime.Variables.SetGlobal("CameraHeight", ValueBox.OfNumber(260));
            Runtime.Variables.SetGlobal("CameraDepth", ValueBox.OfNumber(-170));
            // How far the follow camera tilts down: 45° is the oblique framing existing scenes
            // use; 90° is straight down (TK4). Centring needs depth = -height / tan(pitch).
            Runtime.Variables.SetGlobal("CameraPitch", ValueBox.OfNumber(CameraFraming.DefaultPitch));
            Runtime.Variables.SetGlobal("LookAtMouse", ValueBox.OfNumber(0));
            // Volume groups (Task 31 Part B): authored globals so a settings UI can bind and
            // write them; the bridge applies them to every source it owns each tick.
            Runtime.Variables.SetGlobal(AudioMix.MasterVariable, ValueBox.OfNumber(1));
            Runtime.Variables.SetGlobal(AudioMix.MusicVariable, ValueBox.OfNumber(1));
            Runtime.Variables.SetGlobal(AudioMix.SoundVariable, ValueBox.OfNumber(1));
            Runtime.Audio.SetMasterVolume(1);
            Runtime.Audio.SetSoundVolume(1);
            Runtime.Audio.SetMusicVolume(1);
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
            ResourceManager.LoadData(EventFiles.LoadOne(_gameFilesFolder, ResourceManager.FileName)?.File);
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
            ResourceManager.LoadData(EventFiles.LoadOne(_gameFilesFolder, ResourceManager.FileName)?.File);
            EnemyTypes.LoadData(ProgramNamed("EnemyTypes")?.File);
            WeaponData.LoadData(ProgramNamed("WeaponData")?.File);
            SpellData.LoadData(ProgramNamed(SpellData.FileName)?.File);
            ItemData.LoadData(ProgramNamed(ItemData.FileName)?.File);
            AffixData.LoadData(ProgramNamed(AffixData.FileName)?.File);
            Matchups.LoadData(ProgramNamed(Matchups.FileName)?.File);
            Upgrades.LoadData(ProgramNamed(Upgrades.FileName)?.File);
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
                // UI controls share one sheet per scene: the scene's own file when it has one
                // (Menu.scene → Menu.events, TK4Options.scene → TK4Options.events), otherwise
                // Menu.events. No PlayButton.events/TurnLeftLabel.events exists.
                string source = actor.Name;
                if (actor is UIControl && ProgramNamed(actor.Name) == null)
                    source = UiSheetName();
                AttachByName(actor, source);
            }
        }

        /// <summary>The .events file UI controls share: the scene's own name when it has one
        /// (<c>Menu.scene</c> → Menu.events, <c>TK4Options.scene</c> → TK4Options.events), else
        /// <c>Menu</c> — no PlayButton.events/TurnLeftLabel.events exists.</summary>
        private string UiSheetName()
        {
            string scene = Level.ScenesCount > 0 ? Level.GetScene(0).Name : null;
            if (!string.IsNullOrEmpty(scene) && ProgramNamed(scene) != null)
                return scene;
            return "Menu";
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
            // Name first, then the spawn source: a renamed prefab instance ("Hero 2") must
            // still pick up Hero.events, which is what `spawn prefab "Hero"` marked on it.
            string source = actor.Name;
            if (ProgramNamed(source) == null)
            {
                string spawnedFrom = Runtime.SpawnedFrom(actor);
                if (!string.IsNullOrEmpty(spawnedFrom) && ProgramNamed(spawnedFrom) != null)
                    source = spawnedFrom;
            }
            AttachByName(actor, source);
        }

        /// <summary>Runtime spawns with a hierarchy (a prefab or a freshly instanced scene):
        /// every actor in the tree gets its sheet, so a weapon/attachment child runs its own
        /// <c>when me appears</c> even though it was never swept at scene start.</summary>
        public void AttachSpawnedTree(Actor actor)
        {
            if (actor == null)
                return;
            AttachSpawnedActor(actor);
            foreach (Actor child in actor.Children)
                AttachSpawnedTree(child);
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
            Level.SceneLoaded -= SceneLoadedAim;
            // The pointer is engine-wide state: stopping play must not leave it hidden on a
            // cursor the game drew (Task 48 Part B).
            CursorView.Reset();
            Cursors.Reset();
            FlaxActions.ClearSession();
            _components.Clear();
            Runtime.Selection.Reset();
            Runtime.Construction.Reset();
            Runtime.Orders.Reset();
            Runtime.Fog.Reset();
            Runtime.Terrain.Reset();
            Runtime.Sounds.Clear();
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
            UpdateAim();
            UpdateButtons();
            UpdateTextControls();
            UpdateFocus();
            UpdatePlayerLookAtMouse();
            // Fog needs this tick's positions before it can raise its reveal/hide edges, and
            // the draw modes need the refreshed grid after the tick (Task 18 Part F).
            UpdateFogActors();
            Runtime.Tick(Time.GameTime);
            // Cursor art is scene content (Task 48 Part C): rescan after the tick so a sheet that
            // changed `my Cursor fps` this frame is seen by the next scan, which the view draws.
            Cursors.Update(Runtime.Variables, Time.GameTime);
            UpdateFogVisibility();
            // Follow last: the tick is what moves the player (`move forward` lands immediately),
            // so a camera snapped before it trailed the subject by one frame and shimmered.
            UpdateCameraFollow();
            // Free-pan (Task 26) last of all: it owns the camera only in free mode, and it needs
            // this tick's mouse and key state.
            UpdateCameraPan(delta);
            // The minimap (Task 24) samples fog, terrain and the selection after the tick, so
            // its dots and fog match what the scene is showing this frame.
            MinimapView.Update(Runtime);
            // Selection boxes (Task 32 Part B) after the camera too: they are screen-space, so
            // they have to be projected with the camera this frame ends up drawing with.
            SelectionBoxesView.Update(Runtime);
            // The pointer is screen-space as well: hovering is a raycast through this frame's
            // camera, and aim, fog visibility and the marquee drag are all settled by now.
            UpdateCursor();
        }

        /// <summary>
        /// Asks the automatic cursor sources and draws the answer (Task 48 Part D). The scene's
        /// own art decides everything: when it authors no <c>Cursor_&lt;Name&gt;</c> actor the
        /// library holds only <c>default</c>, and this returns before spending a raycast or
        /// touching the pointer, so a project that never mentions cursors behaves as it did
        /// before the feature.
        /// </summary>
        private void UpdateCursor()
        {
            CursorLibrary library = Cursors.Library;
            // Nothing authored and nothing asked for: no raycast, no pointer writes.
            if (Cursors.Count <= 1 && Runtime.CursorStack.Depth == 0 &&
                string.IsNullOrEmpty(Runtime.CursorName) && !Runtime.CursorHidden)
                return;

            CursorRules rules = CursorRules.Read(Runtime.Variables);
            string hover = null;
            string aim = null;
            if (rules.Enabled && Cursors.Count > 1 && Engine.HasGameViewportFocus)
            {
                // Over a UI control the pointer belongs to the button, not to the world under it.
                if (_hoveredUI == null)
                    hover = Ask(library, rules.Hover(CurrentHoverKind(), _boxDragging));
                // An armed aim outranks the pointer's incidental state; `cancel aim`, a timeout
                // and a landed pick all drop it, so leaving the mode restores the default even if
                // the sheet that armed it never pops anything.
                if (Runtime.Targeting.IsAiming)
                    aim = Ask(library, rules.Aiming());
            }
            CursorState answer = CursorState.For(library, Runtime.CursorStack, hover, aim,
                Runtime.CursorName, Runtime.CursorHidden);
            CursorView.Apply(answer, Cursors);
            WriteCursorGlobal(answer);
        }

        /// <summary>
        /// Keeps the `Cursor` global in step with the drawn answer (Task 48 Part E), so
        /// `my cursor` reads the name in effect and `when my cursor changes` fires on a change.
        /// Written only when it differs, so the change watcher has one edge per change.
        /// </summary>
        private void WriteCursorGlobal(CursorState answer)
        {
            string name = answer == null || answer.Hidden ? "" : answer.Name;
            string before = Runtime.Variables.GetGlobal(EventRuntime.CursorGlobal).AsText();
            if (!string.Equals(before, name, System.StringComparison.OrdinalIgnoreCase))
                Runtime.Variables.SetGlobal(EventRuntime.CursorGlobal, ValueBox.OfText(name));
        }

        /// <summary>Only ask for a cursor the scene actually authored; a name the library does not
        /// have would resolve to <c>default</c> with nothing to draw, so the rule stays silent.</summary>
        private static string Ask(CursorLibrary library, string name)
        {
            return name != null && library.Has(name) ? name : null;
        }

        /// <summary>
        /// What the pointer is over, in the terms the cursor table understands (Task 48 Part D).
        /// Hostility and visibility use the same flags as right-click orders and the marquee, so a
        /// fogged enemy is not an attack cursor on unexplored ground, and a prop that cannot be
        /// picked reads as a hotspot rather than as a unit.
        /// </summary>
        private CursorHoverKind CurrentHoverKind()
        {
            object actor = _world.HoveredActor();
            if (actor == null)
                return CursorHoverKind.None;

            var variables = Runtime.Variables;
            bool hasSheet = Runtime.FindSheetFor(actor) != null;
            bool selectable = ClickSelect.IsSelectable(variables, actor, hasSheet);
            bool hostile = RightClickOrders.IsHostile(variables, actor);
            bool visible = !Runtime.Fog.Enabled ||
                           Runtime.Fog.IsVisibleTo(Runtime.Fog.LocalPlayer, actor);
            return CursorRules.KindOf(selectable, hasSheet && !selectable, hostile, visible);
        }

        /// <summary>
        /// RTS free-pan (Task 26 Parts B/C): once <c>camera free</c> armed free-pan mode, the
        /// camera moves with the mouse parked at a screen edge and with the arrow keys / WASD,
        /// at a speed that slows down as the player zooms in. Disabled edges and keys leave the
        /// camera alone, and authored globals set the feel.
        /// </summary>
        private void UpdateCameraPan(float delta)
        {
            var variables = Runtime.Variables;
            if (variables.GetGlobal(CameraRig.FreeVariable).AsNumber() <= 0)
                return;

            double zoom = CameraRig.ZoomFrom(variables);
            double directionX = 0;
            double directionZ = 0;

            // A box select (Task 29 Part B) owns the mouse while it is being dragged: edge
            // panning would slide the world under the rectangle the player is drawing.
            if (!_boxDragging && variables.GetGlobal(CameraRig.EdgePanVariable).AsNumber() > 0)
            {
                Float2 mouse = Input.MousePosition;
                Float2 size = Screen.Size;
                if (size.X > 0 && size.Y > 0 &&
                    CameraRig.EdgeDirection(mouse.X, mouse.Y, size.X, size.Y,
                        variables.GetGlobal(CameraRig.EdgeThicknessVariable).AsNumber(),
                        out double edgeX, out double edgeZ))
                {
                    directionX += edgeX;
                    directionZ += edgeZ;
                }
            }

            bool left = _world.IsKeyHeld("Left Arrow") || _world.IsKeyHeld("A");
            bool right = _world.IsKeyHeld("Right Arrow") || _world.IsKeyHeld("D");
            bool forward = _world.IsKeyHeld("Up Arrow") || _world.IsKeyHeld("W");
            bool backward = _world.IsKeyHeld("Down Arrow") || _world.IsKeyHeld("S");
            if (CameraRig.KeyDirection(left, right, forward, backward,
                out double keyX, out double keyZ))
            {
                directionX += keyX;
                directionZ += keyZ;
            }

            if (directionX != 0 || directionZ != 0)
            {
                double focusX = variables.GetGlobal(CameraRig.PanXVariable).AsNumber();
                double focusZ = variables.GetGlobal(CameraRig.PanZVariable).AsNumber();
                double speed = CameraRig.PanSpeedFor(
                    variables.GetGlobal(CameraRig.PanSpeedVariable).AsNumber(), zoom);
                CameraRig.Step(focusX, focusZ, directionX, directionZ, speed * delta,
                    out double x, out double z);
                variables.SetGlobal(CameraRig.PanXVariable, ValueBox.OfNumber(x));
                variables.SetGlobal(CameraRig.PanZVariable, ValueBox.OfNumber(z));
            }

            // Settles the position even when nothing moved, so a zoom change lands at once.
            FlaxActions.ApplyFreeCamera(Runtime);
        }

        /// <summary>
        /// Registers every scene actor with the fog grid: its player (`my Player`, default 0),
        /// vision radius (`my VisionRadius`, falling back to the global `VisionRadius`) and, for
        /// buildings, a footprint from `my Footprint` or the actor's bounding radius.
        /// </summary>
        private void UpdateFogActors()
        {
            FogOfWarManager fog = Runtime.Fog;
            if (fog == null)
                return;
            fog.Enabled = !Runtime.Variables.HasGlobal(FogOfWarManager.EnabledVariable) ||
                          Runtime.Variables.GetGlobal(FogOfWarManager.EnabledVariable).AsNumber() != 0;
            double globalRadius = Runtime.Variables.HasGlobal(FogOfWarManager.VisionRadiusVariable)
                ? Runtime.Variables.GetGlobal(FogOfWarManager.VisionRadiusVariable).AsNumber()
                : 0;
            var alive = new HashSet<object>();
            foreach (object item in Runtime.World.Actors())
            {
                if (!(item is Actor actor) || actor == null || actor.Scene == null)
                    continue;
                alive.Add(actor);
                int player = (int)ReadFogNumber(actor, FogOfWarManager.PlayerVariable, 0);
                double radius = ReadFogNumber(actor, FogOfWarManager.VisionRadiusVariable, globalRadius);
                bool building = Runtime.Construction.IsBuilding(actor);
                double footprint = building
                    ? ReadFogNumber(actor, FogOfWarManager.FootprintVariable, actor.BoundingSphere.Radius)
                    : 0;
                fog.SetActor(actor, player, actor.Position.X, actor.Position.Z, radius, footprint, footprint);
            }
            fog.Prune(alive);
        }

        private double ReadFogNumber(Actor actor, string name, double fallback)
        {
            return Runtime.Variables.HasLocal(actor, name)
                ? Runtime.Variables.GetLocal(actor, name).AsNumber()
                : fallback;
        }

        /// <summary>
        /// Applies the fog grid to rendering: enemy units outside active vision are hidden,
        /// while buildings stay visible once their cell is explored. Own units are always
        /// drawn. Event sheets keep running either way.
        /// </summary>
        private void UpdateFogVisibility()
        {
            FogOfWarManager fog = Runtime.Fog;
            if (fog == null)
                return;
            foreach (object item in Runtime.World.Actors())
            {
                if (!(item is Actor actor) || actor == null)
                    continue;
                bool visible = true;
                if (fog.Enabled && fog.TryGetActor(actor, out FogActor info))
                {
                    if (info.Player == fog.LocalPlayer)
                        visible = true;
                    else if (Runtime.Construction.IsBuilding(actor))
                        visible = fog.IsExplored(fog.LocalPlayer, info.X, info.Z);
                    else
                        visible = fog.IsVisibleTo(fog.LocalPlayer, actor);
                }
                FlaxActions.SetFogVisibility(actor, visible);
            }
        }

        private void UpdateCameraFollow()
        {
            var camera = FlaxWorld.FindCamera();
            if (camera == null)
                return;

            // Free-pan mode (Task 26) owns the camera; UpdateCameraPan places it.
            if (Runtime.Variables.GetGlobal(CameraRig.FreeVariable).AsNumber() > 0)
                return;

            // Explicit follow: camera follows <thing> (set by StartCameraFollow).
            var target = FlaxActions.CameraFollowTarget;
            if (target != null && target.Scene != null)
            {
                // Zoom (Task 26 Part A) pulls the same framing closer or further away.
                float scale = (float)CameraRig.ScaleFor(CameraRig.ZoomFrom(Runtime.Variables));
                camera.Position = target.Position + new Vector3(
                    FlaxActions.CameraFollowOffset.X,
                    FlaxActions.CameraFollowOffset.Y * scale,
                    FlaxActions.CameraFollowOffset.Z * scale);
                return;
            }

            // Legacy auto-follow: only if explicitly enabled via events.
            if (Runtime.Variables.GetGlobal("CameraFollow").AsNumber() <= 0)
                return;
            var player = Level.FindActor("Player") as Actor;
            if (player == null)
                return;
            double zoom = CameraRig.ZoomFrom(Runtime.Variables);
            float height = (float)Runtime.Variables.GetGlobal("CameraHeight").AsNumber();
            float depth = (float)Runtime.Variables.GetGlobal("CameraDepth").AsNumber();
            float zoomScale = (float)CameraRig.ScaleFor(zoom);
            camera.Position = player.Position + new Vector3(0f, height * zoomScale, depth * zoomScale);
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

            // Escape drops target-pick mode instead of reaching the sheets (Task 27 Part A).
            // When nothing is aiming it stays an ordinary key press, so menus are unchanged.
            bool escapedAim = Input.GetKeyDown(KeyboardKeys.Escape) && Runtime.CancelAim();

            // Escape also drops an in-flight marquee (Task 29 Part B): the press already did the
            // click's work, so a cancelled drag leaves the scene as a plain click would.
            if (Input.GetKeyDown(KeyboardKeys.Escape) && _boxPressed)
                CancelBoxSelect();

            foreach (string key in ActionWords.KeyNamesForScanning)
            {
                bool held = _world.IsKeyHeld(key);
                _heldLastTick.TryGetValue(key, out bool wasHeld);
                if (held && !wasHeld)
                {
                    if (escapedAim && key == "Escape")
                    {
                        _heldLastTick[key] = held;
                        continue;
                    }
                    // A capture (`rebind player 2 Shoot`) swallows this press: the menu is
                    // binding a key, not playing.
                    if (Runtime.TryCompleteCapture(key, null))
                    {
                        _heldLastTick[key] = held;
                        continue;
                    }
                    // Key signals carry their owner, so player 1's sheet ignores player 2's
                    // keys; unbound global keys (P, Escape) carry 0 and reach every sheet.
                    var press = new PushSignal
                    {
                        Kind = TriggerKind.KeyPressed,
                        Name = key,
                        PlayerA = Runtime.PlayerForKey(key),
                    };
                    Runtime.RaisePushAll(press);
                    // Pressing 1–0 recalls that control group, and Ctrl+1–0 assigns the current
                    // selection to it (classic RTS behaviour). Assign is a no-op with nothing
                    // selected, so a stray Ctrl+digit never wipes a group.
                    if (Selection.TryGroupKey(key, out int group))
                    {
                        if (_world.IsKeyHeld("Ctrl"))
                            Runtime.AssignControlGroup(group);
                        else
                            Runtime.SelectControlGroup(group);
                    }
                }
                else if (wasHeld && !held)
                {
                    Runtime.RaisePushAll(new PushSignal
                    {
                        Kind = TriggerKind.KeyReleased,
                        Name = key,
                        PlayerA = Runtime.PlayerForKey(key),
                    });
                }
                _heldLastTick[key] = held;
            }
            GamepadEdges();

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
                // While aiming, a left click resolves the pick (Task 27 Part A). A landed pick
                // consumes the click; a UI click or an out-of-range pick is ignored and the
                // click still runs its ordinary path.
                if (!TryAimPick(mousePos))
                {
                    bool tookClick = HandleMouseClick(mousePos, screenCenter, TriggerKind.Clicked,
                        out _, out _);
                    var clickWorld = RaycastToWorld(mousePos);
                    if (clickWorld.HasValue)
                        FlaxActions.LastClickPosition = clickWorld.Value;
                    // Box select (Task 29 Part B) only starts on bare ground: a press that landed
                    // on a unit, a building or a UIControl keeps its Part A behaviour and can
                    // never turn into a drag.
                    if (!tookClick)
                        BeginBoxSelect(mousePos);
                }
            }

            // A card button reports its release even if the cursor left it, so `when <button> is
            // pressed` always has a matching release.
            if (_pressedButton != null && !Input.GetMouseButton(MouseButton.Left))
            {
                UIControl pressed = _pressedButton;
                _pressedButton = null;
                Debug.Log($"Button: {pressed.Name} released.");
                if (pressed.Scene != null)
                    Runtime.RaisePushAll(new PushSignal { Kind = TriggerKind.ButtonReleased, Name = pressed.Name, Other = pressed });
            }
            _leftMouseDownLastFrame = leftMouseDown;

            // The marquee follows the mouse while the button is held and lands on release.
            if (_boxPressed)
            {
                if (!Input.GetMouseButton(MouseButton.Left))
                    EndBoxSelect(mousePos);
                else
                    UpdateBoxSelect(mousePos);
            }

            bool rightMouseDown = Input.GetMouseButtonDown(MouseButton.Right);
            if (rightMouseDown && !_rightMouseDownLastFrame)
            {
                // A right click cancels an aim instead of issuing an order.
                if (!Runtime.CancelAim())
                {
                    // `there` means where the last click landed, whichever button made it
                    // (Task 29 Part C), so the point is recorded before the click is broadcast
                    // and before any order is issued from it.
                    Vector3? clickPoint = RightClickPoint(mousePos);
                    if (clickPoint.HasValue)
                        FlaxActions.LastClickPosition = clickPoint.Value;
                    HandleMouseClick(mousePos, screenCenter, TriggerKind.RightClicked,
                        out Actor rightTarget, out bool rightUiHit);
                    // A UI control took the click: its authored trigger runs and nothing else, so
                    // a command-card button never also orders the selection. Shift appends to
                    // each unit's queue instead of replacing it.
                    if (!rightUiHit)
                    {
                        // Scenery (the floor, a wall, a prop) catches the ray but carries no event
                        // sheet, so it is bare ground, not a friendly actor: the aim pick already
                        // draws the line there. Without this the floor classified as a healthy
                        // ally, which `RightClickOrders.Resolve` answers with "no order" — so a
                        // right click on the ground moved nobody while a click at the sky did.
                        bool targetIsActor = rightTarget != null && Runtime.FindSheetFor(rightTarget) != null;
                        FlaxActions.IssueDefaultOrder(Runtime, targetIsActor ? rightTarget : null,
                            clickPoint.HasValue, clickPoint ?? Vector3.Zero, Input.GetKey(KeyboardKeys.Shift));
                    }
                }
            }
            _rightMouseDownLastFrame = rightMouseDown;

            bool middleMouseDown = Input.GetMouseButtonDown(MouseButton.Middle);
            if (middleMouseDown && !_middleMouseDownLastFrame)
                HandleMouseClick(mousePos, screenCenter, TriggerKind.MiddleClicked, out _, out _);
            _middleMouseDownLastFrame = middleMouseDown;
        }

        /// <summary>
        /// Turns gamepad buttons into the same key signals the keyboard raises (Task 16 input
        /// follow-ups): player 1 reads pad 0 and player 2 pad 1, and each button pushes the key
        /// that action is bound to — so `when Shoot is pressed` fires for a controller too.
        /// </summary>
        private void GamepadEdges()
        {
            for (int player = 1; player <= PlayerInput.MaxPlayers; player++)
            {
                int pad = PlayerInput.GamepadFor(player);
                if (Input.GetGamepad(pad) == null)
                    continue; // no controller plugged into this slot
                foreach (InputAction action in PlayerInput.Actions)
                {
                    string button = Runtime.Input.ButtonFor(player, action.Name);
                    if (!FlaxWorld.TryGetGamepadButton(button, out GamepadButton mapped))
                        continue;
                    bool held = Input.GetGamepadButton(pad, mapped, FlaxWorld.GamepadDeadZone);
                    string slot = player + " " + action.Id;
                    _gamepadLast.TryGetValue(slot, out bool wasHeld);
                    if (held && !wasHeld)
                    {
                        if (Runtime.TryCompleteCapture(null, button))
                        {
                            _gamepadLast[slot] = held;
                            continue;
                        }
                        Runtime.RaisePushAll(new PushSignal
                        {
                            Kind = TriggerKind.KeyPressed,
                            Name = Runtime.Input.KeyFor(player, action.Name),
                            PlayerA = player,
                        });
                    }
                    else if (wasHeld && !held)
                    {
                        Runtime.RaisePushAll(new PushSignal
                        {
                            Kind = TriggerKind.KeyReleased,
                            Name = Runtime.Input.KeyFor(player, action.Name),
                            PlayerA = player,
                        });
                    }
                    _gamepadLast[slot] = held;
                }
            }
        }

        /// <summary>Per-frame hover tracking: when the mouse enters a UIControl, raise a
        /// Hovered push and remember it. The color change is applied by
        /// <see cref="UpdateControlVisuals"/> together with the focus tint.</summary>
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

            // Enter new (leaving is handled by UpdateControlVisuals restoring the base color).
            _hoveredUI = newHovered;
            if (_hoveredUI != null)
                Runtime.RaisePush(_hoveredUI, new PushSignal { Kind = TriggerKind.Hovered, Name = _hoveredUI.Name });
        }

        /// <summary>
        /// Resolves a left click while <c>aim</c> is armed (Task 27 Part A). True only when the
        /// pick landed, so the click is consumed; a UI hit, a missing ray, or a pick the aim
        /// refuses (out of reach, wrong kind, unseen) returns false and the click keeps its
        /// ordinary behaviour. A hit that belongs to no event sheet is bare ground, so it becomes
        /// a point pick rather than a click on the world actor.
        /// </summary>
        private bool TryAimPick(Float2 mousePos)
        {
            if (!Runtime.Targeting.IsAiming)
                return false;
            if (MouseOverAnyUI(mousePos))
                return false;
            if (!FlaxWorld.TryGetMouseRay(mousePos, out Ray ray))
                return false;
            if (!Physics.LineCastAll(ray.Position, ray.Position + ray.Direction * 10000f, out var hits, ~0u, true))
                return false;

            Actor bestActor = null;
            Vector3 bestPoint = Vector3.Zero;
            float best = float.MaxValue;
            foreach (var hit in hits)
            {
                if (hit.Distance >= best)
                    continue;
                best = hit.Distance;
                bestPoint = hit.Point;
                bestActor = ActorOfHit(hit.Collider);
            }

            bool unit = bestActor != null && bestActor.GetScript<EventSheet>() != null;
            if (unit)
                return Runtime.PickAimActor(bestActor, bestActor.Position.X, bestActor.Position.Z);
            if (best < float.MaxValue)
                return Runtime.PickAimPoint(bestPoint.X, bestPoint.Z);
            return false;
        }

        /// <summary>True when the cursor is over any UI control — an aim never steals a
        /// command-card button click (the button runs and the aim stays armed).</summary>
        private bool MouseOverAnyUI(Float2 mousePos)
        {
            foreach (Actor actor in SceneActors())
            {
                if (actor == null || !actor.IsActive)
                    continue;
                if (actor is UIControl ui && MouseOverUIControl(ui, mousePos))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Keeps the aim visuals honest (Task 27 Part A): the caster's position is refreshed so
        /// the reach check follows a walking caster, and the optional scene <c>AimMarker</c> actor
        /// sits under the cursor while aiming and hides when the mode ends.
        /// </summary>
        private void UpdateAim()
        {
            TargetingSession aim = Runtime.Targeting.Aiming;
            Actor marker = FindAimMarker();
            if (aim == null)
            {
                if (marker != null && marker.IsActive)
                    marker.IsActive = false;
                return;
            }
            if (aim.Caster is Actor caster && caster.Scene != null)
                Runtime.Targeting.MoveCaster(caster, caster.Position.X, caster.Position.Z);
            if (marker == null)
                return;
            Vector3? spot = RaycastToWorld(Input.MousePosition);
            if (!spot.HasValue)
            {
                marker.IsActive = false;
                return;
            }
            marker.IsActive = true;
            marker.Position = spot.Value;
        }

        /// <summary>The optional <c>AimMarker</c> scene actor, looked up again after a scene load.</summary>
        private Actor FindAimMarker()
        {
            if (_aimMarker != null && _aimMarker.Scene != null)
                return _aimMarker;
            if (_aimMarker != null)
                _aimMarker = null;
            if (!_aimMarkerSearched)
                _aimMarker = Level.FindActor(AimMarkerName);
            _aimMarkerSearched = true;
            return _aimMarker;
        }

        /// <summary>A scene finished loading: drop the marker cached from the scene that came
        /// before it and search the freshly loaded scenes, so an <c>AimMarker</c> authored in a
        /// later scene is picked up instead of the stale actor. It starts hidden and only shows
        /// while an aim is armed (<see cref="UpdateAim"/>).</summary>
        private void SceneLoadedAim(Scene scene, Guid sceneId)
        {
            _aimMarker = null;
            _aimMarkerSearched = false;
            Actor marker = FindAimMarker();
            if (marker != null)
                marker.IsActive = false;
        }

        /// <summary>Raises <paramref name="kind"/> on the actor under the cursor. UIControls
        /// are hit-tested in screen space; everything else (the ground, a chest, ...) is
        /// hit-tested with a camera-through-mouse physics raycast — "the ground is just
        /// another actor", so <c>when Ground is clicked</c> works on any actor.
        ///
        /// Returns true when the click belonged to a UI control or a selectable actor, and false
        /// when it landed on bare ground (or scenery), which is what lets a drag select begin
        /// there (Task 29 Part B). <paramref name="worldTarget"/> is the 3D actor under the
        /// cursor (null for a UI hit or a miss) and <paramref name="uiHit"/> says whether a
        /// control swallowed the click, which is what tells the right click (Task 29 Part C) not
        /// to order the selection under a card button.</summary>
        private bool HandleMouseClick(Float2 mousePos, Float2 screenCenter, TriggerKind kind,
            out Actor worldTarget, out bool uiHit)
        {
            var actors = SceneActors();
            worldTarget = null;
            uiHit = false;
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
                    // A card button is pressed from this hit test rather than from Flax's
                    // IsPressed, because the click is already known to be on it: that keeps a
                    // button pressable when its control never receives mouse events. A disabled
                    // button stays silent, which is the card's own "not available" answer.
                    if (kind == TriggerKind.Clicked && ui.Control is FlaxEngine.GUI.Button button)
                    {
                        if (button.Enabled)
                        {
                            Debug.Log($"Click: {actor.Name} (UI button) pressed.");
                            _pressedButton = ui;
                            Runtime.RaisePushAll(new PushSignal { Kind = TriggerKind.ButtonPressed, Name = actor.Name, Other = actor });
                        }
                        else
                        {
                            Debug.Log($"Click: {actor.Name} (UI button) is disabled.");
                        }
                    }
                    // Clicks are broadcast by name: the clicked actor's own `when me is clicked`
                    // fires, and other sheets can listen with `when Ground is clicked`.
                    HandleClick(actor, kind);
                }
            }
            if (uiHit)
                return true;

            // 3D actors under the cursor: raycast from the scene camera through the mouse.
            if (!FlaxWorld.TryGetMouseRay(mousePos, out Ray ray))
                return false;
            Actor bestActor = null;
            if (Physics.LineCastAll(ray.Position, ray.Position + ray.Direction * 10000f, out var hits, ~0u, true))
            {
                float best = float.MaxValue;
                foreach (var hit in hits)
                {
                    if (hit.Distance < best)
                    {
                        best = hit.Distance;
                        bestActor = ActorOfHit(hit.Collider);
                    }
                }
            }
            if (bestActor != null)
            {
                worldTarget = bestActor;
                HandleWorldClick(bestActor, kind);
                bool hasSheet = Runtime.FindSheetFor(bestActor) != null;
                return ClickSelect.IsSelectable(Runtime.Variables, bestActor, hasSheet);
            }
            // Bare ground (or nothing at all) on a plain left click clears the selection
            // (Task 29 Part A); Shift keeps it, so a shift-click miss is harmless.
            if (kind == TriggerKind.Clicked)
                Runtime.SelectOnClick(null, Input.GetKey(KeyboardKeys.Shift));
            return false;
        }

        /// <summary>Raises a click (or double-click) on <paramref name="actor"/>. A second left
        /// click on the same actor within <see cref="DoubleClickSeconds"/> becomes a
        /// <c>DoubleClicked</c> trigger instead of a second <c>Clicked</c>.</summary>
        private void HandleClick(Actor actor, TriggerKind kind)
        {
            if (actor == null)
                return;
            bool doubleClick = RecordClick(actor, kind);
            Runtime.RaiseClick(actor, doubleClick ? TriggerKind.DoubleClicked : kind);
        }

        /// <summary>
        /// A click in the 3D world. The default StarCraft selection runs first (Task 29 Part A)
        /// and the authored click trigger second, so a sheet's <c>when me is clicked</c> already
        /// sees <c>me is selected</c> and can still overrule the default (pushes run later in
        /// the tick, so an authored <c>deselect all</c> wins as it always did).
        /// </summary>
        private void HandleWorldClick(Actor actor, TriggerKind kind)
        {
            if (actor == null)
                return;
            bool doubleClick = RecordClick(actor, kind);
            if (kind == TriggerKind.Clicked)
            {
                bool shift = Input.GetKey(KeyboardKeys.Shift);
                if (doubleClick)
                    SelectSameKindOnScreen(actor, shift);
                else
                    Runtime.SelectOnClick(actor, shift);
            }
            Runtime.RaiseClick(actor, doubleClick ? TriggerKind.DoubleClicked : kind);
        }

        /// <summary>Double click: every unit on screen driven by the same <c>.events</c> file
        /// joins the selection (Shift adds them to the existing one).</summary>
        private void SelectSameKindOnScreen(Actor reference, bool shift)
        {
            var visible = new List<object>();
            FlaxEngine.Camera camera = FlaxWorld.FindCamera();
            foreach (Actor actor in SceneActors())
            {
                if (actor == null || !actor.IsActive || actor is UIControl)
                    continue;
                if (Runtime.FindSheetFor(actor) == null)
                    continue;
                // A camera-less scene (a tool window, a test) selects by kind alone.
                if (camera != null && !camera.IsPointOnView(actor.Position))
                    continue;
                visible.Add(actor);
            }
            Runtime.SelectSameKind(reference, visible, shift);
        }

        /// <summary>Starts tracking a possible drag (Task 29 Part B). Nothing is drawn yet —
        /// the rectangle appears only once the mouse clears <see cref="BoxSelect.DragThreshold"/>,
        /// so a shaky click stays a click.</summary>
        private void BeginBoxSelect(Float2 pressPosition)
        {
            _boxPressed = true;
            _boxDragging = false;
            _boxStart = pressPosition;
            _boxShift = Input.GetKey(KeyboardKeys.Shift);
        }

        /// <summary>Extends the marquee while the button is down.</summary>
        private void UpdateBoxSelect(Float2 mousePos)
        {
            if (!_boxDragging &&
                !BoxSelect.IsDrag(_boxStart.X, _boxStart.Y, mousePos.X, mousePos.Y))
                return;
            _boxDragging = true;
            MarqueeView.Show(Runtime, _boxStart, mousePos);
        }

        /// <summary>
        /// Releases the drag: past the threshold the marquee selects (Shift keeps the old
        /// selection and adds), below it the press was only a click and has already done its
        /// Part A work, so nothing else happens.
        /// </summary>
        private void EndBoxSelect(Float2 releasePosition)
        {
            bool dragging = _boxDragging;
            _boxPressed = false;
            _boxDragging = false;
            MarqueeView.Hide();
            if (!dragging)
                return;
            var marquee = BoxScreenRect.FromCorners(
                _boxStart.X, _boxStart.Y, releasePosition.X, releasePosition.Y);
            Runtime.SelectInBox(marquee, MarqueeView.Candidates(Runtime), _boxShift);
        }

        /// <summary>Escape during a drag: drop the rectangle and select nothing.</summary>
        private void CancelBoxSelect()
        {
            _boxPressed = false;
            _boxDragging = false;
            MarqueeView.Hide();
        }

        /// <summary>Records a click on an actor and answers whether it was the second left click
        /// inside <see cref="DoubleClickSeconds"/>.</summary>
        private bool RecordClick(Actor actor, TriggerKind kind)
        {
            double now = Time.GameTime;
            bool doubleClick = kind == TriggerKind.Clicked &&
                _buttonLastClickTime.TryGetValue(actor, out double last) && now - last < DoubleClickSeconds;
            _buttonLastClickTime[actor] = now;
            return doubleClick;
        }

        /// <summary>
        /// Reports the card's layout, once per change. A press is not read from Flax's
        /// <c>Button.IsPressed</c> any more: a click the manager already knows landed on a button
        /// raises <c>ButtonPressed</c> itself (see <see cref="HandleMouseClick"/>), so an order
        /// runs even when the button's control never saw the mouse. This method exists to say
        /// which buttons are in which state, because the three ways a card button does nothing
        /// all look the same from the log otherwise.
        /// </summary>
        private void UpdateButtons()
        {
            var shown = new List<string>();
            var hidden = new List<string>();
            var unbuilt = new List<string>();
            foreach (Actor actor in SceneActors())
            {
                var ui = actor as UIControl;
                if (ui == null)
                    continue;
                // A card button that lost its control has no type left to recognise it by, so a
                // name ending in "Button" counts too.
                bool isButton = ui.Control is FlaxEngine.GUI.Button ||
                    (ui.Control == null && actor.Name.EndsWith("Button"));
                if (!isButton)
                    continue;
                if (!actor.IsActive)
                    hidden.Add(actor.Name);
                else if (ui.Control == null)
                    unbuilt.Add(actor.Name);
                else
                    shown.Add(actor.Name);
            }
            string signature = string.Join("|", shown);
            if (signature == _buttonPolledSignature)
                return;
            _buttonPolledSignature = signature;
            Debug.Log($"Buttons shown: [{string.Join(", ", shown)}]; hidden: [{string.Join(", ", hidden)}]; no control: [{string.Join(", ", unbuilt)}].");
        }

        /// <summary>Polls every UI TextBox for text edits and raises <c>TextChanged</c> /
        /// <c>Submitted</c> triggers. Focus edges come from <see cref="UpdateFocus"/> so
        /// mouse, keyboard and gamepad focus all take the same path.</summary>
        private void UpdateTextControls()
        {
            var dead = new List<Actor>();
            bool enterDown = Input.GetKeyDown(KeyboardKeys.Return);
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
                var box = ui.Control as FlaxEngine.GUI.TextBox;
                if (box == null)
                    continue;

                string text = box.Text ?? "";
                if (_textLastValue.TryGetValue(actor, out string previousText) && previousText != text)
                    Runtime.RaisePush(actor, new PushSignal { Kind = TriggerKind.TextChanged, Name = actor.Name, Other = actor });
                _textLastValue[actor] = text;

                bool focused = box.IsFocused;
                if (focused && enterDown)
                    Runtime.RaisePush(actor, new PushSignal { Kind = TriggerKind.Submitted, Name = actor.Name, Other = actor });
            }
            foreach (Actor actor in dead)
                _textLastValue.Remove(actor);
            foreach (var pair in _textLastValue.ToArray())
                if (pair.Key == null || pair.Key.Scene == null)
                    _textLastValue.Remove(pair.Key);
        }

        // ------------------------------------------------------------------
        // Task 15 Part E: focus manager (keyboard arrows / gamepad D-pad)
        // ------------------------------------------------------------------

        /// <summary>Refreshes the focusable-control list, moves focus on arrow/D-pad input,
        /// applies the hover/focus tint, and broadcasts focus enter/exit edges.</summary>
        private void UpdateFocus()
        {
            RefreshFocusControls();
            UIControl focused = FindFocusedControl();

            // Poll every frame even while typing, so a held arrow does not become a fresh
            // edge the moment the text box loses focus.
            FocusDirection? direction = PollNavigationDirection();
            bool editingText = focused != null && focused.Control is FlaxEngine.GUI.TextBox box && box.IsEditing;
            if (direction.HasValue && !editingText && _focusControls.Count > 0)
            {
                MoveFocus(direction.Value);
                focused = FindFocusedControl();
            }

            UpdateControlVisuals();

            if (ReferenceEquals(focused, _focusedUI))
                return;
            if (_focusedUI != null)
            {
                Runtime.RaisePushAll(new PushSignal
                {
                    Kind = TriggerKind.LostFocus,
                    Name = _focusedUI.Name,
                    Other = _focusedUI,
                });
            }
            _focusedUI = focused;
            if (_focusedUI != null)
            {
                Runtime.RaisePushAll(new PushSignal
                {
                    Kind = TriggerKind.Focused,
                    Name = _focusedUI.Name,
                    Other = _focusedUI,
                });
            }
        }

        /// <summary><c>navigate up/down/left/right</c>: moves focus to the nearest control in
        /// that direction. Returns false when the scene has nothing focusable.</summary>
        public bool NavigateFocus(string directionWord)
        {
            FocusDirection? direction = FocusNavigation.ParseDirection(directionWord);
            if (!direction.HasValue)
                return false;
            RefreshFocusControls();
            if (_focusControls.Count == 0)
                return false;
            MoveFocus(direction.Value);
            return true;
        }

        private void RefreshFocusControls()
        {
            _focusControls.Clear();
            foreach (Actor actor in SceneActors())
            {
                var ui = actor as UIControl;
                if (IsFocusable(ui))
                    _focusControls.Add(ui);
            }
        }

        /// <summary>Buttons, TextBoxes and Sliders are focusable; any custom control can opt in
        /// with Flax's <c>Auto Focus</c> flag.</summary>
        private static bool IsFocusable(UIControl ui)
        {
            if (ui == null || !ui.IsActiveInHierarchy || ui.Control == null)
                return false;
            FlaxEngine.GUI.Control control = ui.Control;
            if (!control.Enabled || !control.VisibleInHierarchy)
                return false;
            return control is FlaxEngine.GUI.Button ||
                   control is FlaxEngine.GUI.TextBox ||
                   control is FlaxEngine.GUI.Slider ||
                   control.AutoFocus;
        }

        private UIControl FindFocusedControl()
        {
            foreach (UIControl ui in _focusControls)
                if (ui != null && ui.Control != null && ui.Control.IsFocused)
                    return ui;
            return null;
        }

        /// <summary>One navigation step per press: keyboard arrow keys and gamepad D-pad both
        /// arrive through the Navigate* actions in the project's input settings.</summary>
        private FocusDirection? PollNavigationDirection()
        {
            for (int i = 0; i < NavigationActions.Length; i++)
            {
                string action = NavigationActions[i];
                bool held = Input.GetAction(action);
                _navigationLastHeld.TryGetValue(action, out bool wasHeld);
                _navigationLastHeld[action] = held;
                if (held && !wasHeld)
                    return NavigationDirections[i];
            }
            return null;
        }

        private bool MoveFocus(FocusDirection direction)
        {
            var candidates = new List<FocusCandidate>(_focusControls.Count);
            UIControl currentUI = FindFocusedControl();
            FocusCandidate current = null;
            foreach (UIControl ui in _focusControls)
            {
                FocusCandidate candidate = ToCandidate(ui);
                if (candidate == null)
                    continue;
                candidates.Add(candidate);
                if (ReferenceEquals(ui, currentUI))
                    current = candidate;
            }
            if (candidates.Count == 0)
                return false;

            FocusCandidate next = FocusNavigation.Choose(candidates, current, direction);
            if (next == null && current == null)
                next = FocusNavigation.First(candidates);
            if (next?.Subject is UIControl nextUI && nextUI.Control != null)
                nextUI.Control.Focus();
            return true;
        }

        private static FocusCandidate ToCandidate(UIControl ui)
        {
            if (ui == null || ui.Control == null)
                return null;
            float width = Math.Max(ui.Control.Size.X, 1f);
            float height = Math.Max(ui.Control.Size.Y, 1f);
            return new FocusCandidate
            {
                Subject = ui,
                Name = ui.Name,
                // UIControl.Position is the control's screen-space pivot (the same center the
                // hover hit test uses); screen Y grows downward, so flip the world Y.
                CenterX = ui.Position.X,
                CenterY = -ui.Position.Y,
                HalfWidth = width * 0.5f,
                HalfHeight = height * 0.5f,
            };
        }

        /// <summary>Applies hover/focus colors on state changes only, so a tween or
        /// <c>set color of</c> is not stomped every frame.</summary>
        private void UpdateControlVisuals()
        {
            var alive = new HashSet<UIControl>();
            foreach (Actor actor in SceneActors())
            {
                var ui = actor as UIControl;
                if (ui == null || ui.Control == null)
                    continue;
                alive.Add(ui);

                bool focused = ui.Control.IsFocused;
                bool hovered = ReferenceEquals(_hoveredUI, ui);
                if (!_uiVisualStates.TryGetValue(ui, out UiVisualState state))
                {
                    state = new UiVisualState();
                    _uiVisualStates[ui] = state;
                }

                if (!state.Seen)
                {
                    state.Seen = true;
                    state.Focused = focused;
                    state.Hovered = hovered;
                    if (focused || hovered)
                    {
                        CaptureBaseColors(ui.Control, state);
                        ApplyVisual(ui.Control, state, focused, hovered);
                    }
                    continue;
                }

                if (focused == state.Focused && hovered == state.Hovered)
                    continue;

                bool wasHighlighted = state.Focused || state.Hovered;
                bool isHighlighted = focused || hovered;
                if (!wasHighlighted && isHighlighted)
                    CaptureBaseColors(ui.Control, state);
                state.Focused = focused;
                state.Hovered = hovered;
                ApplyVisual(ui.Control, state, focused, hovered);
            }

            foreach (var pair in _uiVisualStates.ToArray())
                if (!alive.Contains(pair.Key))
                    _uiVisualStates.Remove(pair.Key);
        }

        private static void CaptureBaseColors(FlaxEngine.GUI.Control control, UiVisualState state)
        {
            if (control is FlaxEngine.GUI.Label label)
            {
                state.HasText = true;
                state.TextBase = label.TextColor;
            }
            if (control is FlaxEngine.GUI.Image image)
            {
                state.HasImage = true;
                state.ImageBase = image.Color;
            }
        }

        private static void ApplyVisual(
            FlaxEngine.GUI.Control control,
            UiVisualState state,
            bool focused,
            bool hovered)
        {
            if (control is FlaxEngine.GUI.Label label && state.HasText)
            {
                // Flax 1.13 has no TextColorFocused; the focus tint is our own highlight.
                label.TextColor = focused
                    ? FocusTint
                    : hovered ? label.TextColorHighlighted : state.TextBase;
            }
            if (control is FlaxEngine.GUI.Image image && state.HasImage)
            {
                image.Color = focused
                    ? FocusTint
                    : hovered ? image.MouseOverColor : state.ImageBase;
            }
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

        /// <summary>The world point a right click points at (Task 29 Part C): the nearest
        /// collider under the cursor, falling back to the y = 0 ground plane so an order still
        /// has a destination over a floor with no collider. The left click keeps its stricter
        /// collider-only raycast, so a click at the sky still records no position.</summary>
        private static Vector3? RightClickPoint(Float2 mousePos)
        {
            Vector3? hit = RaycastToWorld(mousePos);
            if (hit.HasValue)
                return hit;
            if (!FlaxWorld.TryGetMouseRay(mousePos, out Ray ray) || ray.Direction.Y >= -0.0001f)
                return null;
            float along = -ray.Position.Y / ray.Direction.Y;
            return along > 0 ? ray.Position + ray.Direction * along : (Vector3?)null;
        }

        /// <summary>Screen-space hit test for a UIControl on a screen-space canvas. Flax's own
        /// <see cref="FlaxEngine.GUI.Control.IsMouseOver"/> answers it exactly when the canvas is
        /// receiving mouse events; the rectangle from the control's real size answers it from the
        /// actor transform when it is not. `ui.Position` is the control's top-left corner (X in
        /// screen pixels, Y world up), so Y is flipped — the old text-sized box treated that
        /// corner as a centre, which left the right half of a 190 px wide button unclickable.</summary>
        private static bool MouseOverUIControl(UIControl ui, Float2 mousePos)
        {
            var control = ui.Control;
            if (control == null)
                return false;
            // A draw-only overlay (the selection boxes, the drag rectangle) covers the canvas but
            // owns none of it. Without this the full-canvas rectangle below answered "over UI"
            // everywhere once a unit was selected, so every click was swallowed as a UI hit and
            // clicking bare ground could never clear the selection.
            if (control is ICosmeticControl)
                return false;
            if (control.IsMouseOver)
                return true;

            Float2 topLeft = new Float2(ui.Position.X, -ui.Position.Y);
            float width = control.Width > 0 ? control.Width : 120f;
            float height = control.Height > 0 ? control.Height : 40f;
            return mousePos.X >= topLeft.X && mousePos.X <= topLeft.X + width &&
                   mousePos.Y >= topLeft.Y && mousePos.Y <= topLeft.Y + height;
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
