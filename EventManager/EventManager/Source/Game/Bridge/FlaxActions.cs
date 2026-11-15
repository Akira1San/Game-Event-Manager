using System;
using System.Collections.Generic;
using FlaxEngine;

namespace Game
{
    public class OngoingMove
    {
        public Actor Subject;
        public Actor Target;
        public double Speed = 5;
        public bool FaceTarget;
        public bool FaceForward;
        public bool Goto;
        /// <summary>NavMesh waypoints from the last <c>FindPath</c> (null = plain target move).</summary>
        public Vector3[] Path;
        /// <summary>Index of the waypoint the subject is walking toward.</summary>
        public int PathIndex;
        /// <summary>World target for <c>goto there</c> (used when Target is null).</summary>
        public Vector3 PositionTarget;
        /// <summary>True when this move walks to a world position (<c>goto there</c>) rather
        /// than to a named actor.</summary>
        public bool HasPositionTarget;
        /// <summary>How this subject moves (transform/character/dynamic/kinematic), resolved
        /// once when the move starts so every tick follows the same body rule.</summary>
        public PhysicsMoveMode Mode = PhysicsMoveMode.Transform;
        /// <summary>True when the body carries the move instead of the transform.</summary>
        public bool UsesPhysics => Mode != PhysicsMoveMode.Transform;
        /// <summary>True when reaching the last waypoint also finishes the unit's active order.</summary>
        public bool CompletesOrder;
        /// <summary>True when arriving also raises <c>when me arrives at there</c>. An attack
        /// that stops at range is a position move but not a click-to-walk arrival.</summary>
        public bool ArrivesAtThere = true;
    }

    /// <summary>A <c>push ... for &lt;n&gt; seconds</c> that is still shoving its target —
    /// TK4 knockback keeps moving the victim for a beat instead of one frame.</summary>
    public class OngoingPush
    {
        public Actor Target;
        public string Dir = "";
        public double Strength = 5;
        /// <summary>Seconds left; counted down each tick in <see cref="FlaxActions.Step"/>.</summary>
        public double Remaining;
    }

    public static class FlaxActions
    {
    public static float DeltaTime;
    public static readonly List<OngoingMove> Moves = new List<OngoingMove>();
    /// <summary>Sustained pushes (<c>push &lt;thing&gt; &lt;dir&gt; strength &lt;n&gt; for &lt;s&gt;
    /// seconds</c>), applied every tick until their time runs out.</summary>
    public static readonly List<OngoingPush> Pushes = new List<OngoingPush>();
    /// <summary>In-flight moving platforms, ping-ponging along a world axis. Advanced each
    /// tick in <see cref="Step"/> and applied through each subject's <see cref="PhysicsMoveMode"/>.</summary>
    public static readonly List<PhysicsMovement.OngoingPlatform> Platforms = new List<PhysicsMovement.OngoingPlatform>();
    /// <summary>Actors a <c>move</c> word ran for this tick (transient: cleared at the start of
    /// each <c>Step</c>, so <c>me is moving</c> is true only while motion is actually happening).</summary>
    public static readonly HashSet<Actor> MovedThisTick = new HashSet<Actor>();
    /// <summary>Units whose next queued order should start moving after the current Step's
    /// move loop, so promoting an order cannot disturb the list being iterated.</summary>
    private static readonly List<Actor> PendingOrderMoves = new List<Actor>();
        private static readonly HashSet<string> WarnedOnce = new HashSet<string>();
        public static Action<string> Warn = FlaxActions.WarnOnce;
        /// <summary>Animation state per actor (current/previous clip + the virtual graphs that play them).</summary>
        private static readonly Dictionary<object, AnimState> AnimStates = new Dictionary<object, AnimState>();
        /// <summary>Vertical speed per character actor. <c>CharacterController.Move</c> carries no
        /// velocity state, so the event runtime keeps the falling speed between ticks.</summary>
        private static readonly Dictionary<Actor, float> CharacterFallSpeeds = new Dictionary<Actor, float>();
        /// <summary>Sources spawned by <c>play sound</c>, with the actor whose sheet started them.</summary>
        private static readonly List<(Actor Owner, AudioSource Source)> ActiveSounds = new List<(Actor, AudioSource)>();

        /// <summary>One in-flight <c>tween</c> plus the control it writes to (UI tweens only).</summary>
        private sealed class LiveTween
        {
            public Actor Subject;
            public UIControl Ui;
            public TweenTrack Track;
        }
        /// <summary>Coroutine-style tween list, advanced every tick in <see cref="Step"/>.</summary>
        private static readonly List<LiveTween> LiveTweens = new List<LiveTween>();
        /// <summary>Clip key (see <see cref="AssetNames"/>) to asset id, built once per play session.</summary>
        private static Dictionary<string, Guid> SoundIndexCache;
        /// <summary>Animation asset key/alias to asset id, built once per play session.</summary>
        private static Dictionary<string, Guid> AnimIndexCache;
        /// <summary>Full animation asset keys (no aliases), for the friendly "I don't know the anim" list.</summary>
        private static List<string> AnimFullNames;
        /// <summary>One tracked particle effect on an actor: the effect, its normalized asset key, the name as
        /// written in the event, whether it is a one-shot (non-looping), and whether it was playing last tick.</summary>
        private sealed class ParticleState
        {
            public ParticleEffect Effect;
            public string Key;
            public string Name;
            public bool OneShot;
            public bool WasPlaying;
        }
        /// <summary>Particles currently playing per actor, used to detect when a one-shot finishes
        /// and raise <c>when particle "&lt;name&gt;" ends</c>.</summary>
        private static readonly Dictionary<object, ParticleState> ParticleStates = new Dictionary<object, ParticleState>();
        /// <summary>Actor the camera keeps centered (<c>camera follows &lt;thing&gt;</c>).</summary>
        internal static Actor CameraFollowTarget;
        /// <summary>Camera position minus the target position, captured when the follow starts.</summary>
        internal static Vector3 CameraFollowOffset;
        /// <summary>Actor the camera aims at (<c>camera looks at &lt;thing&gt;</c>).</summary>
        internal static Actor CameraLookAtTarget;
        /// <summary>Actor the camera snaps to each tick (<c>camera at &lt;thing&gt;</c>).</summary>
        internal static Actor CameraAtTarget;
        /// <summary>Default top-down offset applied when snapping via <c>camera at</c>, so
        /// framing/zoom stays consistent with the player camera.</summary>
        internal static Vector3 CameraAtOffset;
        /// <summary>World position of the last mouse click on bare ground — the <c>goto there</c> target.</summary>
        public static Vector3? LastClickPosition;

        /// <summary>Current and previous clip for one actor, with the virtual graphs that play them.</summary>
        private sealed class AnimState
        {
            public string Clip;          // normalized asset key, used for "is it already playing?"
            public string Name;          // name as written in the event, pushed to `when anim ... ends`
            public bool Loop;
            public bool OneShot;
            public double EndTime;
            public AnimationGraph Graph;
            public AnimationGraph PreviousGraph;
            public string PreviousClip;
            public string PreviousName;
            public bool PreviousLoop;
        }

        public static void WarnOnce(string message)
        {
            if (WarnedOnce.Add(message))
                Debug.LogWarning(message);
        }

        /// <summary>Actors whose models are hidden by fog of war (Task 18 Part F).</summary>
        private static readonly HashSet<Actor> FogHiddenActors = new HashSet<Actor>();
        /// <summary>Each model's draw pass before fog hid it, so visibility restores it.</summary>
        private static readonly Dictionary<Actor, DrawPass> FogSavedDrawModes =
            new Dictionary<Actor, DrawPass>();

        /// <summary>
        /// Shows or hides an actor's models for fog of war (Task 18 Part F). Only rendering
        /// changes: the actor stays active, so its event sheet and triggers keep running.
        /// </summary>
        public static void SetFogVisibility(Actor actor, bool visible)
        {
            if (actor == null)
                return;
            if (visible)
            {
                if (FogHiddenActors.Remove(actor))
                    RestoreFogDrawModes(actor);
                return;
            }
            if (FogHiddenActors.Add(actor))
                HideFogDrawModes(actor);
        }

        /// <summary>Drops a destroyed actor's fog bookkeeping without touching its models.</summary>
        public static void ClearFogVisibility(Actor actor)
        {
            if (actor == null)
                return;
            FogHiddenActors.Remove(actor);
            ForgetFogDrawModes(actor);
        }

        private static void HideFogDrawModes(Actor node)
        {
            if (node == null)
                return;
            if (node is StaticModel staticModel)
            {
                if (!FogSavedDrawModes.ContainsKey(staticModel))
                    FogSavedDrawModes[staticModel] = staticModel.DrawModes;
                staticModel.DrawModes = DrawPass.None;
            }
            else if (node is AnimatedModel animatedModel)
            {
                if (!FogSavedDrawModes.ContainsKey(animatedModel))
                    FogSavedDrawModes[animatedModel] = animatedModel.DrawModes;
                animatedModel.DrawModes = DrawPass.None;
            }
            foreach (Actor child in node.Children)
                HideFogDrawModes(child);
        }

        private static void RestoreFogDrawModes(Actor node)
        {
            if (node == null)
                return;
            if (node is StaticModel staticModel && FogSavedDrawModes.TryGetValue(staticModel, out DrawPass staticMode))
            {
                staticModel.DrawModes = staticMode;
                FogSavedDrawModes.Remove(staticModel);
            }
            else if (node is AnimatedModel animatedModel && FogSavedDrawModes.TryGetValue(animatedModel, out DrawPass animMode))
            {
                animatedModel.DrawModes = animMode;
                FogSavedDrawModes.Remove(animatedModel);
            }
            foreach (Actor child in node.Children)
                RestoreFogDrawModes(child);
        }

        private static void ForgetFogDrawModes(Actor node)
        {
            if (node == null)
                return;
            FogSavedDrawModes.Remove(node);
            foreach (Actor child in node.Children)
                ForgetFogDrawModes(child);
        }

        public static void ClearSession()
        {
            Moves.Clear();
            FogHiddenActors.Clear();
            FogSavedDrawModes.Clear();
            Pushes.Clear();
            Platforms.Clear();
            ActiveSounds.Clear();
            LiveTweens.Clear();
            CharacterFallSpeeds.Clear();
            SoundIndexCache = null;
            AnimIndexCache = null;
            AnimFullNames = null;
            ParticleStates.Clear();
            CameraFollowTarget = null;
            CameraLookAtTarget = null;
            CameraAtTarget = null;
            CameraAtOffset = Vector3.Zero;
            foreach (AnimState state in AnimStates.Values)
            {
                if (state.Graph != null)
                    FlaxEngine.Object.Destroy(state.Graph);
                if (state.PreviousGraph != null)
                    FlaxEngine.Object.Destroy(state.PreviousGraph);
            }
            AnimStates.Clear();
        }

        /// <summary>True when <paramref name="actor"/> is actively moving this tick: an ongoing
        /// <c>goto</c>/<c>face</c> move in <see cref="Moves"/>, or a <c>move</c> word that ran this
        /// tick (<see cref="MovedThisTick"/>).</summary>
        public static bool IsMoving(Actor actor)
        {
            if (actor == null)
                return false;
            if (MovedThisTick.Contains(actor))
                return true;
            foreach (var move in Moves)
                if (move.Subject == actor)
                    return true;
            return false;
        }

        public static object ResolveThing(EventContext context, string thing)
        {
            if (thing == "me")
                return context.Me;
            if (thing == "other")
                return context.Other;
            if (context.Runtime != null)
            {
                // Task 16 Part F: `player 2` / `Player2` is that actor, while a bare `player`
                // and `nearest player` mean whichever player is closest to me.
                if (Players.TryReadNumber(thing, out int playerNumber))
                    return context.Runtime.FindPlayer(playerNumber);
                if (Players.IsNearestReference(thing))
                    return context.Runtime.NearestPlayer(context.Me);
            }
            if (thing.StartsWith("nearest ", StringComparison.OrdinalIgnoreCase))
                return NearestTo(context, thing.Substring("nearest ".Length));
            if (thing.StartsWith("random ", StringComparison.OrdinalIgnoreCase))
                return RandomOf(thing.Substring("random ".Length));
            object fromSheets = context.Runtime.FindNamedActor(thing);
            if (fromSheets != null)
                return fromSheets;
            return Level.FindActor(thing);
        }

        private static object NearestTo(EventContext context, string thing)
        {
            var me = context.Me as Actor;
            var candidates = ActorsNamed(thing);
            Actor best = null;
            double bestDistance = double.MaxValue;
            foreach (Actor candidate in candidates)
            {
                double distance = me != null
                    ? Vector3.Distance(me.Position, candidate.Position)
                    : candidate.Position.Length;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }
            if (best != null)
                return best;
            WarnOnce($"I couldn't find any \u201C{thing}\u201D to pick the nearest of.");
            return null;
        }

        private static object RandomOf(string thing)
        {
            var candidates = ActorsNamed(thing);
            if (candidates.Count == 0)
            {
                WarnOnce($"I couldn't find any \u201C{thing}\u201D to pick a random one of.");
                return null;
            }
            int index = (int)(Rng.NextDouble() * candidates.Count);
            if (index >= candidates.Count)
                index = candidates.Count - 1;
            return candidates[index];
        }

        private static List<Actor> ActorsNamed(string name)
        {
            var found = new List<Actor>();
            var scenes = Level.Scenes;
            for (int s = 0; s < scenes.Length; s++)
            {
                var scene = scenes[s];
                if (scene == null)
                    continue;
                CollectNamed(scene, name, found);
            }
            return found;
        }

        private static void CollectNamed(Actor node, string name, List<Actor> found)
        {
            foreach (Actor child in node.Children)
            {
                if (child == null)
                    continue;
                if (child.Name == name && child.IsActive)
                    found.Add(child);
                CollectNamed(child, name, found);
            }
        }

        public static void Step(float deltaTime)
        {
            DeltaTime = deltaTime;
            MovedThisTick.Clear();
            for (int i = ActiveSounds.Count - 1; i >= 0; i--)
                if (ActiveSounds[i].Source == null)
                    ActiveSounds.RemoveAt(i);
            for (int i = Moves.Count - 1; i >= 0; i--)
            {
                OngoingMove move = Moves[i];
                if (move.Subject == null || move.Subject.Scene == null)
                {
                    Moves.RemoveAt(i);
                    continue;
                }
                // `goto there` deliberately has no actor Target — only a clicked PositionTarget.
                // Keep it alive; named-target and face moves still need their actor.
                if (!move.HasPositionTarget && (move.Target == null || move.Target.Scene == null))
                {
                    Moves.RemoveAt(i);
                    continue;
                }
                if (move.FaceTarget)
                    move.Subject.LookAt(move.Target.Position);
                else if (move.Goto)
                {
                    if (move.Path == null || move.PathIndex >= move.Path.Length)
                    {
                        FinishGoto(move);
                        Moves.RemoveAt(i);
                        continue;
                    }
                    Vector3 waypoint = move.Path[move.PathIndex];
                    // A waypoint on ground this unit cannot cross ends the walk with
                    // `when path blocked` instead of running into the water (Task 22 Part B).
                    if (TerrainBlocksStep(move.Subject, waypoint))
                    {
                        var blockedRuntime = EventsManager.Instance?.Runtime;
                        if (blockedRuntime != null)
                            blockedRuntime.Orders.ReportPathBlocked(blockedRuntime, move.Subject);
                        Moves.RemoveAt(i);
                        continue;
                    }
                    Vector3 delta = waypoint - move.Subject.Position;
                    float length = delta.Length;
                    bool reached = length < 0.001f ||
                                   PhysicsMovement.ReachesWaypoint(length, move.Speed, DeltaTime);
                    if (length >= 0.001f)
                    {
                        // Full speed on the approach; a landing speed on the tick that reaches
                        // the waypoint, so a body neither overshoots nor stalls for a frame.
                        double speed = PhysicsMovement.PathSpeed(length, move.Speed, DeltaTime);
                        MoveSubject(move.Subject, delta / length, speed, move.Mode);
                        if (move.FaceForward)
                            move.Subject.LookAt(waypoint);
                    }
                    if (reached)
                    {
                        // Only the transform path teleports; bodies are swept/moved into place.
                        if (move.Mode == PhysicsMoveMode.Transform)
                            move.Subject.Position = waypoint;
                        move.PathIndex++;
                        if (move.PathIndex >= move.Path.Length)
                        {
                            FinishGoto(move);
                            Moves.RemoveAt(i);
                        }
                    }
                }
            }

            // Orders promoted while the move loop was running start here, after the loop,
            // so they cannot mutate the list mid-iteration.
            if (PendingOrderMoves.Count > 0)
            {
                var pending = new List<Actor>(PendingOrderMoves);
                PendingOrderMoves.Clear();
                var orderRuntime = EventsManager.Instance?.Runtime;
                if (orderRuntime != null)
                {
                    foreach (Actor actor in pending)
                    {
                        UnitOrder next = orderRuntime.Orders.CurrentOrderOf(actor);
                        if (next != null && actor != null && actor.Scene != null)
                            BeginOrderMove(orderRuntime, actor, next, 5);
                    }
                }
            }

            // Sustained pushes: keep shoving for the authored time, so a bullet's knockback
            // lasts long enough to see instead of a single 1/60 s nudge.
            for (int i = Pushes.Count - 1; i >= 0; i--)
            {
                OngoingPush push = Pushes[i];
                double remaining = PhysicsMovement.PushRemaining(push.Remaining, DeltaTime);
                if (push.Target == null || push.Target.Scene == null || remaining <= 0)
                {
                    Pushes.RemoveAt(i);
                    continue;
                }
                push.Remaining = remaining;
                ApplyPush(push.Target, push.Dir, push.Strength);
            }

            // Moving platforms: advance each ping-pong sweep and apply the per-tick delta
            // through the subject's PhysicsMoveMode. Snap to the exact endpoint at each leg
            // so there is no drift across legs.
            for (int i = Platforms.Count - 1; i >= 0; i--)
            {
                PhysicsMovement.OngoingPlatform platform = Platforms[i];
                Actor actor = platform.Subject as Actor;
                if (actor == null || actor.Scene == null)
                {
                    Platforms.RemoveAt(i);
                    continue;
                }
                int legDirection = platform.Direction;
                bool legDone = platform.Advance(DeltaTime);
                double start = AxisValue(platform.StartX, platform.StartY, platform.StartZ, platform.Axis);
                Vector3 target = actor.Position;
                SetAxisValue(ref target, platform.Axis, start + platform.AxisOffset);
                Vector3 delta = target - actor.Position;
                ApplyPlatformDelta(actor, delta, platform.Mode, DeltaTime);
                if (legDone)
                {
                    // Snap to the exact endpoint so there is no drift across legs.
                    double endpointOffset = legDirection > 0 ? platform.Distance : 0.0;
                    Vector3 exact = actor.Position;
                    SetAxisValue(ref exact, platform.Axis, start + endpointOffset);
                    actor.Position = exact;
                    if (platform.Mode == PhysicsMoveMode.Dynamic)
                    {
                        RigidBody body = FindBody(actor);
                        if (body != null)
                            body.LinearVelocity = Vector3.Zero;
                    }
                }
            }

            // Tweens: advance every in-flight tween, write the interpolated value, and fire
            // `when tween <thing> ends` on the tick it completes (final value is applied first).
            for (int i = LiveTweens.Count - 1; i >= 0; i--)
            {
                LiveTween live = LiveTweens[i];
                if (live.Subject == null || live.Subject.Scene == null)
                {
                    LiveTweens.RemoveAt(i);
                    continue;
                }
                bool done = live.Track.Advance(deltaTime);
                ApplyTween(live);
                if (!done)
                    continue;
                LiveTweens.RemoveAt(i);
                EventsManager.Instance?.HandleTweenEnd(live.Subject, live.Track.Name);
            }

            // One-shots: fire `when anim "<clip>" ends` once, after the clip's length.
            // Collect first: the trigger may immediately play another clip (mutating AnimStates).
            List<(Actor Actor, string Clip)> ended = null;
            foreach (var pair in AnimStates)
            {
                AnimState state = pair.Value;
                if (!state.OneShot || Time.GameTime < state.EndTime)
                    continue;
                state.OneShot = false;
                if (pair.Key is Actor actor)
                    (ended ??= new List<(Actor, string)>()).Add((actor, state.Name));
            }
            if (ended != null)
            {
                foreach (var (actor, clip) in ended)
                    EventsManager.Instance?.HandleAnimEnd(actor, clip);
            }

            // One-shot particles: when a tracked non-looping effect stops emitting on its own,
            // fire `when particle "<name>" ends` on its actor's sheet(s).
            var particleEnded = new List<(Actor Actor, string Name)>();
            foreach (var pair in ParticleStates)
            {
                var actor = pair.Key as Actor;
                var state = pair.Value;
                if (actor == null || actor.Scene == null || state.Effect == null || state.Effect.Scene == null)
                {
                    particleEnded.Add((actor, state.Name));
                    continue;
                }
                bool nowPlaying = state.Effect.ParticlesCount > 0;
                if (state.OneShot && state.WasPlaying && !nowPlaying)
                    particleEnded.Add((actor, state.Name));
                state.WasPlaying = nowPlaying;
            }
            foreach (var (actor, name) in particleEnded)
            {
                EventsManager.Instance?.HandleParticleEnd(actor, name);
                ParticleStates.Remove(actor);
            }

            // `wander`: walk each wandering actor along its current random heading, and pick a
            // new heading when the leg's ticks run out.
            var wanderRuntime = EventsManager.Instance?.Runtime;
            if (wanderRuntime != null)
                StepWanderers(wanderRuntime);

            // `camera follows <thing>`: translate the camera by the target's movement, keeping
            // the offset captured when the follow started (the authored framing never changes).
            if (CameraFollowTarget != null && CameraFollowTarget.Scene == null)
            {
            CameraFollowTarget = null;
            CameraLookAtTarget = null;
            }
            if (CameraFollowTarget != null)
            {
                Actor camera = FlaxWorld.FindCamera();
                if (camera != null)
                    camera.Position = CameraFollowTarget.Position + CameraFollowOffset;
            }

            // `camera looks at <thing>`: keep the scene camera aimed at the target each tick,
            // so the aim tracks a moving target and switching targets is smooth.
            if (CameraLookAtTarget != null && CameraLookAtTarget.Scene == null)
            {
                CameraLookAtTarget = null;
            }
            if (CameraLookAtTarget != null)
            {
                Actor camera = FlaxWorld.FindCamera();
                if (camera != null)
                    LookAtNoRoll(camera, CameraLookAtTarget.Position);
            }

            // `camera at <thing>`: snap the camera to the target's position each tick
            // (with the default top-down offset so zoom stays consistent).
            if (CameraAtTarget != null && CameraAtTarget.Scene == null)
            {
                CameraAtTarget = null;
            }
            if (CameraAtTarget != null)
            {
                Actor camera = FlaxWorld.FindCamera();
                if (camera != null)
                    camera.Position = CameraAtTarget.Position + CameraAtOffset;
            }
        }

        public static void Execute(EventContext context, ActionSpec spec)
        {
            switch (spec.Verb)
            {
                case ActionVerb.Move:
                {
                    var me = context.Me as Actor;
                    if (me == null)
                    {
                        WarnOnce("\u201Cmove me\u201D needs to run on an actor's sheet — the global Game sheet has no body to move.");
                        return;
                    }
                    MovedThisTick.Add(me);
                    context.Runtime.Wander.Clear(me);
                    bool physics = context.Sheet != null && context.Sheet.UsePhysicsMovement;
                    PhysicsMoveMode mode = ResolveMoveMode(me, physics);
                    if (!string.IsNullOrEmpty(spec.Thing))
                    {
                        var target = ResolveThing(context, spec.Thing) as Actor;
                        if (target != null)
                        {
                            Vector3 delta = target.Position - me.Position;
                            float length = delta.Length;
                            if (length > 0.001f)
                                MoveSubject(me, delta / length, ResolveNumber(context, spec), mode);
                        }
                        break;
                    }
                    MoveSubject(me, DirectionOf(me, spec.Dir), ResolveNumber(context, spec), mode);
                    break;
                }
                case ActionVerb.StopMoving:
                {
                    var mover = context.Me as Actor;
                    for (int i = Moves.Count - 1; i >= 0; i--)
                        if (Moves[i].Subject == mover && !Moves[i].FaceTarget)
                            Moves.RemoveAt(i);
                    // A dynamic body keeps its velocity until something brakes it.
                    if (mover != null && context.Sheet != null && context.Sheet.UsePhysicsMovement)
                        BrakeBody(mover);
                    context.Runtime.Wander.Clear(mover);
                    break;
                }
                case ActionVerb.Wander:
                {
                    var me = context.Me as Actor;
                    if (me == null)
                    {
                        WarnOnce("\u201Cwander\u201D needs to run on an actor's sheet \u2014 the global Game sheet has no body to walk.");
                        break;
                    }
                    // `wander speed 5 duration 2`: a fresh random heading every 20-60 ticks (or
                    // the authored duration); the walk itself happens in Step.
                    context.Runtime.Wander.Start(me, ResolveNumber(context, spec), spec.Duration);
                    break;
                }
                case ActionVerb.Still:
                {
                    var me = context.Me as Actor;
                    if (me == null)
                        break;
                    StopMovingSubject(me);
                    context.Runtime.Wander.SetStill(me);
                    // A still actor keeps facing its target; nearest Player is the useful default
                    // and an explicit `face Player` later just replaces this tracker.
                    var target = ResolveThing(context, "nearest Player") as Actor;
                    if (target != null)
                        Remember(new OngoingMove { Subject = me, Target = target, FaceTarget = true });
                    break;
                }
                case ActionVerb.Select:
                    ExecuteSelect(context, spec);
                    break;
                case ActionVerb.Deselect:
                    ExecuteDeselect(context, spec);
                    break;
                case ActionVerb.Focus:
                    SetControlFocus(spec.Thing, true);
                    break;
                case ActionVerb.Unfocus:
                    SetControlFocus(spec.Thing, false);
                    break;
                case ActionVerb.Navigate:
                    if (EventsManager.Instance == null || !EventsManager.Instance.NavigateFocus(spec.Dir))
                        WarnOnce($"\u201Cnavigate {spec.Dir}\u201D needs a focusable UI control in the scene — add a Button, TextBox or Slider, or tick Auto Focus on a custom control.");
                    break;
                case ActionVerb.Tween:
                    StartTween(context, spec);
                    break;
                case ActionVerb.MovePlatform:
                    StartPlatform(context, spec);
                    break;
                case ActionVerb.Turn:
                {
                    var me = context.Me as Actor;
                    if (me == null)
                        return;
                    if (!PhysicsMovement.IsTurnDirection(spec.Dir))
                    {
                        WarnOnce($"\u201Cturn\u201D needs \u201Cleft\u201D or \u201Cright\u201D, like \u201Cturn left degrees 90\u201D.");
                        break;
                    }
                    // Rotation only: yaw around the actor's own up axis, never a position change.
                    double degrees = PhysicsMovement.SignedTurnDegrees(spec.Dir, ResolveNumber(context, spec));
                    me.Orientation = Quaternion.Multiply(me.Orientation, Quaternion.Euler(0, (float)degrees, 0));
                    break;
                }
                case ActionVerb.LookAt:
                {
                    var target = ResolveThing(context, spec.Thing) as Actor;
                    var me = context.Me as Actor;
                    if (me == null)
                        break;
                    if (target == null)
                    {
                        WarnOnce($"I couldn't find \u201C{spec.Thing}\u201D to look at.");
                        break;
                    }
                    // Rotation only; a one-shot snap that never touches Position.
                    me.LookAt(target.Position);
                    break;
                }
                case ActionVerb.Face:
                {
                    var target = ResolveThing(context, spec.Thing) as Actor;
                    var me = context.Me as Actor;
                    if (me == null)
                        break;
                    if (target == null)
                    {
                        WarnOnce($"I couldn't find \u201C{spec.Thing}\u201D to face.");
                        break;
                    }
                    // Continuous rotation-only tracker; a new face target replaces the old one.
                    Remember(new OngoingMove { Subject = me, Target = target, FaceTarget = true });
                    break;
                }
                case ActionVerb.Teleport:
                {
                    var me = context.Me as Actor;
                    if (me == null)
                        break;
                    var to = ResolveThing(context, spec.To) as Actor;
                    if (to == null)
                    {
                        WarnOnce($"I couldn't find \u201C{spec.To}\u201D to teleport to.");
                        break;
                    }
                    TeleportSubject(me, to.Position);
                    break;
                }
                case ActionVerb.Push:
                {
                    var target = ResolveThing(context, spec.Thing) as Actor;
                    if (target == null)
                    {
                        WarnOnce($"I couldn't find \u201C{spec.Thing}\u201D to push.");
                        break;
                    }
                    double strength = ResolveNumber(context, spec);
                    ApplyPush(target, spec.Dir, strength);
                    // `for <n> seconds` keeps shoving on later ticks (TK4 knockback). Without it
                    // this stays the historical single-tick nudge.
                    if (spec.Duration > 0)
                    {
                        for (int i = Pushes.Count - 1; i >= 0; i--)
                            if (Pushes[i].Target == target)
                                Pushes.RemoveAt(i);
                        Pushes.Add(new OngoingPush
                        {
                            Target = target,
                            Dir = spec.Dir,
                            Strength = strength,
                            Remaining = spec.Duration,
                        });
                    }
                    break;
                }
                case ActionVerb.Parent:
                {
                    var child = ResolveThing(context, spec.Thing) as Actor;
                    var newParent = ResolveThing(context, spec.To) as Actor;
                    if (child != null && newParent != null)
                        child.Parent = newParent;
                    else if (child == null)
                        WarnOnce($"I couldn't find \u201C{spec.Thing}\u201D to parent.");
                    else
                        WarnOnce($"I couldn't find \u201C{spec.To}\u201D to parent \u201C{spec.Thing}\u201D to.");
                    break;
                }
                case ActionVerb.Unparent:
                {
                    var child = ResolveThing(context, spec.Thing) as Actor;
                    if (child != null)
                        child.Parent = null;
                    else
                        WarnOnce($"I couldn't find \u201C{spec.Thing}\u201D to unparent.");
                    break;
                }
                case ActionVerb.Scale:
                {
                    var target = ResolveThing(context, spec.Thing) as Actor;
                    if (target != null)
                    {
                        float s = (float)ResolveNumber(context, spec);
                        target.Scale = new Vector3(s, s, s);
                    }
                    else
                        WarnOnce($"I couldn't find \u201C{spec.Thing}\u201D to scale.");
                    break;
                }
                case ActionVerb.Goto:
                {
                    var me = context.Me as Actor;
                    if (me == null)
                    {
                        WarnOnce("\u201Cgoto\u201D needs to run on an actor's sheet — the global Game sheet has no body to move.");
                        break;
                    }
                    context.Runtime.Wander.Clear(me);
                    if (spec.Thing == "there")
                    {
                        var clickPos = LastClickPosition;
                        if (!clickPos.HasValue)
                        {
                            WarnOnce("\u201Cgoto there\u201D needs a clicked position first — click somewhere in the scene to set it.");
                            break;
                        }
                        if (!Navigation.FindPath(me.Position, clickPos.Value, out Vector3[] clickPath) || clickPath == null || clickPath.Length == 0)
                        {
                            WarnOnce($"No NavMesh path from \u201C{me.Name}\u201D to the clicked spot — bake a NavMesh for this level first.");
                            break;
                        }
                        Remember(new OngoingMove
                        {
                            Subject = me,
                            Goto = true,
                            HasPositionTarget = true,
                            PositionTarget = clickPos.Value,
                            Path = clickPath,
                            PathIndex = 0,
                            Speed = spec.Number,
                            Mode = ResolveMoveMode(me, context.Sheet != null && context.Sheet.UsePhysicsMovement),
                        });
                        break;
                    }
                    var target = ResolveThing(context, spec.Thing) as Actor;
                    if (target == null)
                    {
                        WarnOnce($"I couldn't find \u201C{spec.Thing}\u201D to goto it.");
                        break;
                    }
                    if (!Navigation.FindPath(me.Position, target.Position, out Vector3[] path) || path == null || path.Length == 0)
                    {
                        // Off the navmesh (pushed, or spawned there): walk a tick toward the
                        // closest navigable point so a later `goto` can path again, instead of
                        // warning once and freezing the chase forever.
                        if (!StepOntoNavMesh(context, me, spec.Number))
                            WarnOnce($"No NavMesh path from \u201C{me.Name}\u201D to \u201C{target.Name}\u201D — bake a NavMesh for this level first.");
                        break;
                    }
                    Remember(new OngoingMove
                    {
                        Subject = me,
                        Target = target,
                        Goto = true,
                        FaceForward = spec.FaceForward,
                        Path = path,
                        PathIndex = 0,
                        Speed = spec.Number,
                        Mode = ResolveMoveMode(me, context.Sheet != null && context.Sheet.UsePhysicsMovement),
                    });
                    break;
                }
                case ActionVerb.Destroy:
                {
                    object target = ResolveThing(context, spec.Thing);
                    if (target is Actor actor)
                    {
                        // Raise BuildingDestroyed only for actors placed as buildings, so
                        // `destroy Explosion` / `destroy other` stay ordinary destructions.
                        bool wasBuilding = context.Runtime.Construction.IsBuilding(actor);
                        string building = context.Runtime.Construction.BuildingOf(actor);
                        context.Runtime.Construction.Destroyed(context.Variables, actor);
                        context.Runtime.Production.Destroyed(actor);
                        context.Runtime.Orders.Destroyed(actor);
                        if (wasBuilding && !string.IsNullOrEmpty(building))
                        {
                            context.Runtime.RaisePushAll(new PushSignal
                            {
                                Kind = TriggerKind.BuildingDestroyed,
                                Name = building,
                                Other = actor,
                            });
                        }
                        ForgetSubject(context, actor);
                        FlaxEngine.Object.Destroy(actor, 0f);
                    }
                    break;
                }
                case ActionVerb.Bounce:
                    BounceProjectile(context);
                    break;
                case ActionVerb.Explode:
                    ExplodeProjectile(context, spec);
                    break;
                case ActionVerb.Spawn:
                    Spawn(context, spec);
                    break;
                case ActionVerb.PlaceBuilding:
                    Place(context, spec);
                    break;
                case ActionVerb.CancelBuild:
                    CancelBuild(context, spec);
                    break;
                case ActionVerb.Train:
                    Train(context, spec);
                    break;
                case ActionVerb.CancelTraining:
                    CancelTraining(context, spec);
                    break;
                case ActionVerb.AttackMove:
                    StartAttackMove(context, spec);
                    break;
                case ActionVerb.AttackOrder:
                    StartAttack(context, spec);
                    break;
                case ActionVerb.StopOrder:
                    StopOrder(context);
                    break;
                case ActionVerb.HoldPosition:
                    HoldPosition(context);
                    break;
                case ActionVerb.AddOrderToQueue:
                    AddOrderToQueue(context);
                    break;
                case ActionVerb.ClearOrders:
                    ClearOrders(context);
                    break;
                case ActionVerb.SetRallyPoint:
                    SetRallyPoint(context, spec);
                    break;
                case ActionVerb.SpawnPrefab:
                    SpawnPrefab(context, spec);
                    break;
                case ActionVerb.SpawnScene:
                    SpawnScene(context, spec);
                    break;
                case ActionVerb.AddScene:
                    AddScene(context, spec);
                    break;
                case ActionVerb.UnloadScene:
                    UnloadScene(context, spec);
                    break;
                case ActionVerb.PlaySound:
                    PlaySound(context, spec);
                    break;
                case ActionVerb.StopSound:
                {
                    var me = context.Me as Actor;

                    // The actor's own AudioSource (the one you drop on it in the editor).
                    if (me != null)
                        me.FindActor<AudioSource>()?.Stop();

                    // And everything `play sound` spawned for this actor (all, from the global sheet).
                    for (int i = ActiveSounds.Count - 1; i >= 0; i--)
                    {
                        var (owner, source) = ActiveSounds[i];
                        if (source == null)
                        {
                            ActiveSounds.RemoveAt(i);
                            continue;
                        }
                        if (me != null && owner != me)
                            continue;
                        source.Stop();
                        FlaxEngine.Object.Destroy(source);
                        ActiveSounds.RemoveAt(i);
                    }
                    break;
                }
                case ActionVerb.PlayParticle:
                    PlayParticle(context, spec);
                    break;
                case ActionVerb.StopParticle:
                    StopParticle(context, spec);
                    break;
                case ActionVerb.SetLightIntensity:
                    SetLightIntensity(context, spec);
                    break;
                case ActionVerb.SetLightColor:
                    SetLightColor(context, spec);
                    break;
                case ActionVerb.SetLightRange:
                    SetLightRange(context, spec);
                    break;
                case ActionVerb.EnableLight:
                    SetLightEnabled(context, spec, true);
                    break;
                case ActionVerb.DisableLight:
                    SetLightEnabled(context, spec, false);
                    break;
                case ActionVerb.ToggleLight:
                    ToggleLight(context, spec);
                    break;
                case ActionVerb.PlayAnim:
                    PlayAnim(context, spec);
                    break;
                case ActionVerb.LoopAnim:
                    LoopAnim(context, spec);
                    break;
                case ActionVerb.StopAnim:
                    StopAnim(context, spec);
                    break;
                case ActionVerb.CameraFollow:
                    StartCameraFollow(context, spec);
                    break;
                case ActionVerb.CameraLooksAt:
                    StartCameraLookAt(context, spec);
                    break;
                case ActionVerb.CameraAt:
                    StartCameraAt(context, spec);
                    break;
                case ActionVerb.CameraSwitch:
                    StartCameraSwitch(context, spec);
                    break;
                case ActionVerb.CameraFree:
                    StopCameraFollow();
                    break;
                case ActionVerb.Hide:
                    SetActive(context, spec.Thing, false);
                    break;
                case ActionVerb.Show:
                    SetActive(context, spec.Thing, true);
                    break;
                case ActionVerb.ShowText:
                    ShowHudText(spec.Thing);
                    break;
                case ActionVerb.HideText:
                    HideHudText();
                    break;
                case ActionVerb.RestartScene:
                {
                    Scene[] scenes = Level.Scenes;
                    if (scenes.Length > 0)
                        ReplaceScene(scenes[0].ID);
                    break;
                }
                case ActionVerb.LoadScene:
                    LoadSceneByName(spec.Thing);
                    break;
                case ActionVerb.QuitGame:
                    Debug.LogWarning("QuitGame action triggered — calling Engine.Exit");
                    Engine.Exit(0, FatalErrorType.None);
                    break;
                case ActionVerb.Print:
                    Debug.LogWarning($"Event print: {spec.Thing}");
                    break;
                case ActionVerb.SetTimeScale:
                {
                    float s = (float)ResolveNumber(context, spec);
                    Time.TimeScale = s;
                    Debug.LogWarning($"Event print: time scale set to {s}");
                    break;
                }
                case ActionVerb.RevealArea:
                    context.Runtime.Fog.RevealAround(context.Me, ResolveNumber(context, spec));
                    break;
                case ActionVerb.HideArea:
                    context.Runtime.Fog.HideAround(context.Me, ResolveNumber(context, spec));
                    break;
                case ActionVerb.SetVisibility:
                    context.Runtime.Fog.SetVisionRadius(context.Me, ResolveNumber(context, spec));
                    break;
                case ActionVerb.MarkTerrain:
                    MarkTerrain(context, spec);
                    break;
            }
        }

        /// <summary>
        /// <c>select me</c>, <c>select all Marine</c>, <c>select units near me by 5</c>.
        /// The "all"/"near" forms replace the current selection (RTS group select); the
        /// single-target form adds to it.
        /// </summary>
        private static void ExecuteSelect(EventContext context, ActionSpec spec)
        {
            Selection selection = context.Runtime.Selection;
            if (spec.All)
            {
                // `select all NameBox` selects the text inside a TextBox. It falls through
                // to the RTS unit-type meaning below when the name isn't a text box.
                if (SelectAllText(spec.Thing))
                    return;
                var matches = new List<object>();
                foreach (object other in context.Runtime.World.Actors())
                {
                    if (other == null)
                        continue;
                    if (context.Runtime.World.NameOf(other) == spec.Thing)
                        matches.Add(other);
                }
                if (matches.Count == 0)
                    WarnOnce($"I couldn't find any \u201C{spec.Thing}\u201D to select.");
                selection.ReplaceSelection(matches);
                return;
            }
            if (spec.Near)
            {
                object anchor = ResolveThing(context, spec.Thing);
                if (anchor == null)
                {
                    WarnOnce($"I couldn't find \u201C{spec.Thing}\u201D to select units near.");
                    return;
                }
                var matches = new List<object>();
                foreach (object other in context.Runtime.World.Actors())
                {
                    if (other == null)
                        continue;
                    if (context.Runtime.World.Distance2D(anchor, other) <= spec.Distance)
                        matches.Add(other);
                }
                selection.ReplaceSelection(matches);
                return;
            }
            object target = ResolveThing(context, spec.Thing);
            if (target == null)
            {
                WarnOnce($"I couldn't find \u201C{spec.Thing}\u201D to select.");
                return;
            }
            context.Runtime.SelectActor(target);
        }

        /// <summary><c>deselect me</c> / <c>deselect all</c>.</summary>
        private static void ExecuteDeselect(EventContext context, ActionSpec spec)
        {
            if (spec.All)
            {
                context.Runtime.Selection.Clear();
                return;
            }
            object target = ResolveThing(context, spec.Thing);
            if (target == null)
            {
                WarnOnce($"I couldn't find \u201C{spec.Thing}\u201D to deselect.");
                return;
            }
            context.Runtime.DeselectActor(target);
        }

        private static void Remember(OngoingMove move)
        {
            // A new goto for the same subject replaces any previous goto (target may differ).
            if (move.Goto)
            {
                for (int i = Moves.Count - 1; i >= 0; i--)
                    if (Moves[i].Subject == move.Subject && Moves[i].Goto)
                        Moves.RemoveAt(i);

                // If subject is already at the target (within ArrivalSlack), clear the arrival contact
                // so PollArrival can re-arm when the same waypoint is chosen again.
                if (move.Subject != null && move.Target != null)
                {
                    // Horizontal (XZ) distance keeps arrival re-arming consistent with the
                    // 2D PollArrival check, so a height gap can't hold the contact open.
                    double sx = move.Subject.Position.X - move.Target.Position.X;
                    double sz = move.Subject.Position.Z - move.Target.Position.Z;
                    double distance = System.Math.Sqrt(sx * sx + sz * sz);
                    if (distance <= EventRuntime.ArrivalSlack)
                        EventRuntime.ClearArrivalContact(move.Subject, move.Target);
                }
            }
            else
            {
                // A new face target replaces the previous tracker: two trackers would fight
                // over the same orientation every tick.
                if (move.FaceTarget)
                {
                    for (int i = Moves.Count - 1; i >= 0; i--)
                        if (Moves[i].Subject == move.Subject && Moves[i].FaceTarget)
                            Moves.RemoveAt(i);
                }
                foreach (OngoingMove existing in Moves)
                {
                    if (existing.Subject == move.Subject && existing.Target == move.Target &&
                        existing.FaceTarget == move.FaceTarget && existing.FaceForward == move.FaceForward && existing.Goto == move.Goto)
                        return;
                }
            }
            Moves.Add(move);
        }

        /// <summary>
        /// Picks how this subject moves this tick: transform (default), character controller,
        /// dynamic body or kinematic body. Resolved once per move so every tick follows the same
        /// rule and a destroyed/removed body simply falls back to transform.
        /// </summary>
        private static PhysicsMoveMode ResolveMoveMode(Actor subject, bool physics)
        {
            CharacterController character = physics ? EventSheet.FindCharacterController(subject) : null;
            RigidBody body = physics ? FindBody(subject) : null;
            PhysicsMoveMode mode = PhysicsMovement.ModeFor(
                physics, character != null, body != null, body != null && body.IsKinematic);

            // The controller must carry (or be above) the sheet actor. A controller tucked under
            // a plain parent would move the visual while `me` stays put — pathfinding and
            // `arrives at` read `me.Position`, so fall back to the transform path and say why.
            if (mode == PhysicsMoveMode.Character && !IsAncestorOrSelf(character, subject))
            {
                WarnOnce($"\"{subject.Name}\" uses physics movement, but its CharacterController is a child — the events can't follow it. Put the EventSheet on the CharacterController actor with the mesh under it.");
                return PhysicsMoveMode.Transform;
            }

            // The event runtime feeds gravity itself (Task 12 C) so a grounded character is
            // deterministic and `goto` can land exactly. Turn the controller's own gravity off
            // rather than applying it twice.
            if (mode == PhysicsMoveMode.Character && character.AutoGravity)
            {
                character.AutoGravity = false;
                WarnOnce($"\"{subject.Name}\": physics movement feeds CharacterController gravity, so its AutoGravity was turned off (otherwise it would fall twice as fast).");
            }

            // A fast dynamic body must not tunnel through a wall between ticks. The body stays
            // under the event runtime's control, so enabling CCD is always the right default.
            if (mode == PhysicsMoveMode.Dynamic && body != null && !body.UseCCD)
                body.UseCCD = true;

            return mode;
        }

        /// <summary>
        /// Moves an actor for one tick in the chosen mode. Transform assigns Position;
        /// a CharacterController walks with collide-and-slide <c>Move</c>; a dynamic RigidBody
        /// uses velocity; a kinematic RigidBody gets a swept move, so collisions and CCD apply.
        /// </summary>
        /// <summary>Stops the actor's walking moves (an ongoing `face` tracker stays) and brakes a
        /// physics body, used by `still`.</summary>
        private static void StopMovingSubject(Actor mover)
        {
            if (mover == null)
                return;
            for (int i = Moves.Count - 1; i >= 0; i--)
                if (Moves[i].Subject == mover && !Moves[i].FaceTarget)
                    Moves.RemoveAt(i);
            var sheet = EventsManager.Instance?.Runtime?.FindSheetFor(mover);
            if (sheet != null && sheet.UsePhysicsMovement)
                BrakeBody(mover);
        }

        /// <summary>
        /// Walks one tick toward the closest point on the navmesh when the actor is stranded off
        /// it (the usual result of being pushed into geometry). Returns false when there is
        /// nothing to fix, so the caller can report the real path failure.
        /// </summary>
        private static bool StepOntoNavMesh(EventContext context, Actor actor, double speed)
        {
            if (actor == null || !Navigation.FindClosestPoint(actor.Position, out Vector3 onMesh))
                return false;
            Vector3 off = onMesh - actor.Position;
            off.Y = 0f;
            if (off.Length <= WanderNavMeshLeash)
                return false;
            var sheet = context.Sheet;
            PhysicsMoveMode mode = ResolveMoveMode(actor, sheet != null && sheet.UsePhysicsMovement);
            MoveSubject(actor, Vector3.Normalize(off), speed > 0 ? speed : 5, mode);
            MovedThisTick.Add(actor);
            return true;
        }

        /// <summary>
        /// `wander`: walk each wandering actor one tick along its current random heading. The
        /// heading and its 20-60 tick life live in <see cref="Wanderer"/> so the timing is
        /// testable; this only turns degrees into a world direction.
        /// </summary>
        private static void StepWanderers(EventRuntime runtime)
        {
            foreach (object key in runtime.Wander.ActiveActors())
            {
                var actor = key as Actor;
                if (actor == null || actor.Scene == null)
                {
                    runtime.Wander.Clear(key);
                    continue;
                }
                runtime.Wander.Advance(actor, DeltaTime);
                if (!runtime.Wander.TryGetLeg(actor, out WanderLeg leg))
                    continue;
                Vector3 direction = WanderHeading(actor, leg);

                // A wall in front ends the leg instead of walking through it: transform moves
                // ignore colliders, and a body shoved inside geometry also loses line of sight
                // (the ray starts in the wall), which reads as "the zombie can't see me".
                double step = leg.Speed * DeltaTime + WanderProbe;
                if (WanderBlocked(actor, direction, (float)step))
                {
                    // New heading next tick. Turn to face it only when it is walkable: turning on
                    // every blocked tick is what made a body pinned to a wall spin in place.
                    leg.AngleDegrees = Wanderer.RandomAngle();
                    Vector3 next = WanderHeading(actor, leg);
                    if (!WanderBlocked(actor, next, (float)step))
                        actor.LookAt(actor.Position + next);
                    continue;
                }

                var sheet = runtime.FindSheetFor(actor);
                PhysicsMoveMode mode = ResolveMoveMode(actor, sheet != null && sheet.UsePhysicsMovement);
                MovedThisTick.Add(actor);
                MoveSubject(actor, direction, leg.Speed, mode);
                // Wander walks the way `goto ... facing forward` does: the body turns to face
                // the heading it is walking, so a shambling zombie never slides sideways.
                actor.LookAt(actor.Position + direction);
            }
        }

        /// <summary>How far a wanderer may drift off the baked navmesh before it is steered
        /// back. Small gaps are height/edge noise; a big one means the walk left the walkable
        /// area, where `goto` can no longer path and the enemy effectively gets lost.</summary>
        private const float WanderNavMeshLeash = 50f;

        /// <summary>Extra distance probed ahead of a wander step, so it stops before the wall.</summary>
        private const float WanderProbe = 2f;

        /// <summary>
        /// Half the widest horizontal extent of an actor's collider, and the height of the
        /// collider's middle above the actor's origin. 0 means "no collider" — a plain mesh
        /// actor keeps moving exactly as before.
        ///
        /// Transform movement writes <c>Position</c>, so PhysX never resolves anything for it: an
        /// actor carrying a box walks that box straight into a wall. The old guard probed a fixed
        /// 8 cm ahead, which is smaller than half a zombie's box, so the body still sank in — and
        /// a RigidBody under it then got ejected and spun while it freed itself. Probing by the
        /// actor's own size is what stops the box at the surface instead.
        /// </summary>
        private static bool TryColliderFootprint(Actor actor, out float radius, out float centreY)
        {
            radius = 0f;
            centreY = 0f;
            if (actor == null)
                return false;
            GatherCollider(actor, ref radius, ref centreY);
            return radius > 0f;
        }

        private static void GatherCollider(Actor node, ref float radius, ref float centreY)
        {
            if (node == null || !node.IsActive)
                return;
            if (node is BoxCollider box)
            {
                float halfWidth = Math.Max(Math.Abs(box.Size.X * node.Scale.X), Math.Abs(box.Size.Z * node.Scale.Z)) * 0.5f;
                if (halfWidth > radius)
                {
                    radius = halfWidth;
                    float localY = node.Parent == null ? 0f : node.Position.Y - node.Parent.Position.Y;
                    centreY = localY + box.Center.Y * node.Scale.Y;
                }
            }
            else if (node is SphereCollider sphere)
            {
                float sphereRadius = Math.Abs(sphere.Radius * node.Scale.X);
                if (sphereRadius > radius)
                {
                    radius = sphereRadius;
                    centreY = node.Parent == null ? 0f : node.Position.Y - node.Parent.Position.Y;
                }
            }
            foreach (Actor child in node.Children)
                GatherCollider(child, ref radius, ref centreY);
        }

        /// <summary>The point a wall probe should start from: the middle of the actor's collider.</summary>
        private static Vector3 ColliderCastPoint(Actor actor)
        {
            return TryColliderFootprint(actor, out _, out float centreY)
                ? actor.Position + Vector3.Up * centreY
                : actor.Position;
        }

        /// <summary>
        /// True when a transform step of <paramref name="step"/> along <paramref name="direction"/>
        /// would push the actor's own collider into something solid. Actors without a collider are
        /// never blocked (nothing of theirs can intersect); the step is measured from the collider's
        /// centre and includes its radius, so the box stops at the wall instead of overlapping it.
        /// </summary>
        private static bool StepBlocked(Actor actor, Vector3 direction, float step)
        {
            if (!TryColliderFootprint(actor, out float radius, out _))
                return false;
            var heading = new Vector3(direction.X, 0f, direction.Z);
            if (heading.LengthSquared < 1e-6f)
                return false;
            heading = Vector3.Normalize(heading);
            Vector3 start = ColliderCastPoint(actor);
            if (!Physics.LineCastAll(start, start + heading * (step + radius), out var hits, ~0u, true))
                return false;
            foreach (var hit in hits)
            {
                var hitActor = hit.Collider as Actor;
                if (hitActor == null)
                    return true; // geometry without an actor (terrain, a static collider, ...)
                if (!ReferenceEquals(hitActor, actor) && !IsAncestorOrSelf(actor, hitActor))
                    return true;
            }
            return false;
        }

        /// <summary>The world heading for one wander leg.</summary>
        private static Vector3 WanderDirection(WanderLeg leg)
        {
            double radians = leg.AngleDegrees * Math.PI / 180.0;
            return new Vector3((float)Math.Sin(radians), 0f, (float)Math.Cos(radians));
        }

        /// <summary>
        /// The leg's heading, unless the actor has drifted off the navmesh — then the closest
        /// navigable point becomes the heading, so a wanderer walks itself back onto the mesh
        /// (and can chase again) instead of wandering further away.
        /// </summary>
        private static Vector3 WanderHeading(Actor actor, WanderLeg leg)
        {
            Vector3 forward = WanderDirection(leg);
            if (!Navigation.FindClosestPoint(actor.Position, out Vector3 onMesh))
                return forward; // no navmesh in this level: plain straight-line wandering
            Vector3 off = onMesh - actor.Position;
            off.Y = 0f;
            if (off.Length <= WanderNavMeshLeash)
                return forward;
            return Vector3.Normalize(off);
        }

        /// <summary>True when something that is not this actor (a wall, a prop) sits within
        /// <paramref name="distance"/> along <paramref name="heading"/>. An actor with a collider
        /// is probed from the middle of that collider and by its own half-width
        /// (<see cref="StepBlocked"/>), so the box stops at the wall instead of half inside it.</summary>
        private static bool WanderBlocked(Actor actor, Vector3 heading, float distance)
        {
            if (TryColliderFootprint(actor, out _, out _))
                return StepBlocked(actor, heading, distance);
            if (!Physics.LineCastAll(actor.Position, actor.Position + heading * distance, out var hits, ~0u, true))
                return false;
            foreach (var hit in hits)
            {
                var hitActor = hit.Collider as Actor;
                if (hitActor == null)
                    return true; // geometry without an actor (a collider child, terrain, ...)
                if (!ReferenceEquals(hitActor, actor) && !IsAncestorOrSelf(actor, hitActor))
                    return true;
            }
            return false;
        }

        private static void MoveSubject(Actor subject, Vector3 direction, double speed, PhysicsMoveMode mode)
        {
            float step = (float)speed * DeltaTime;
            // Terrain (Task 22 Part B): a unit never steps onto ground its own type cannot cross.
            // Checked for every mode, before the body is told to move.
            if (TerrainBlocksStep(subject, subject.Position + direction * step))
                return;
            switch (mode)
            {
                case PhysicsMoveMode.Character:
                {
                    CharacterController character = EventSheet.FindCharacterController(subject);
                    if (character == null)
                        break; // body vanished mid-move: leave the actor where it is

                    // Grounded characters walk; airborne ones fall straight down (no air
                    // steering). A short ray below the capsule respects the character's slope
                    // limit, and `IsGrounded` covers contact cases the ray is too short to see.
                    bool grounded = IsCharacterGrounded(character, subject);
                    CharacterFallSpeeds.TryGetValue(subject, out float fallSpeed);
                    PhysicsMovement.CharacterStep(
                        direction.X, direction.Y, direction.Z, speed, fallSpeed, grounded,
                        Math.Abs(Physics.Gravity.Y), PhysicsMovement.GroundStickSpeed, DeltaTime,
                        out double cx, out double cy, out double cz, out double newFallSpeed);
                    CharacterFallSpeeds[subject] = (float)newFallSpeed;
                    character.Move(new Vector3((float)cx, (float)cy, (float)cz));
                    break;
                }
                case PhysicsMoveMode.Dynamic:
                {
                    RigidBody body = FindBody(subject);
                    if (body == null)
                    {
                        subject.Position = subject.Position + direction * step;
                        break;
                    }
                    if (!IsAncestorOrSelf(body, subject))
                        WarnOnce($"\"{subject.Name}\" uses physics movement, but its RigidBody is a child — the mesh won't follow. Put the EventSheet on the RigidBody actor with the mesh under it.");
                    PhysicsMovement.DynamicVelocity(
                        direction.X, direction.Y, direction.Z, speed, body.LinearVelocity.Y,
                        out double vx, out double vy, out double vz);
                    body.LinearVelocity = new Vector3((float)vx, (float)vy, (float)vz);
                    break;
                }
                case PhysicsMoveMode.Kinematic:
                {
                    // A swept move: PhysX carries the body along and reports trigger/collision
                    // contacts on the way instead of teleporting through them.
                    RigidBody body = FindBody(subject);
                    if (body != null && IsAncestorOrSelf(body, subject))
                        body.AddMovement(direction * step);
                    else
                        subject.AddMovement(direction * step);
                    break;
                }
                default:
                    // `Position +=` is a teleport: PhysX never stops it, so a collider-bearing
                    // actor would end up inside the wall (and a RigidBody under it would be
                    // ejected with a spin). Stop at the surface instead; the owner (`wander`,
                    // `goto`) re-aims on its next tick, so it walks away rather than through.
                    if (!StepBlocked(subject, direction, step))
                        subject.Position = subject.Position + direction * step;
                    break;
            }
        }

        /// <summary>
        /// Ground probe for a walking character. A short downward ray from just below the
        /// capsule (within the sheet's Ground Check Distance) rejects slopes steeper than the
        /// controller's own limit; when the ray sees nothing, the controller's last-move
        /// <c>IsGrounded</c> is the fallback (contact offset / collider edge).
        /// </summary>
        private static bool IsCharacterGrounded(CharacterController character, Actor subject)
        {
            float groundCheck = GroundCheckDistanceFor(subject);
            if (groundCheck > 0f)
            {
                float feet = (float)PhysicsMovement.FeetY(
                    character.Position.Y,
                    character.OriginMode == CharacterController.OriginModes.Base,
                    character.Height,
                    character.Radius);
                // Start a hair below the feet so the ray cannot hit the character's own capsule;
                // triggers are ignored so a pickup volume isn't mistaken for ground.
                Vector3 origin = new Vector3(character.Position.X, feet - 0.5f, character.Position.Z);
                if (Physics.RayCast(origin, Vector3.Down, out RayCastHit hit, groundCheck, ~0u, false))
                    return PhysicsMovement.IsWalkableSlope(hit.Normal.Y, character.SlopeLimit);
            }
            return character.IsGrounded;
        }

        /// <summary>The actor sheet's authored ground-check distance, or the character default.</summary>
        private static float GroundCheckDistanceFor(Actor subject)
        {
            SheetInstance sheet = EventsManager.Instance?.Runtime?.FindSheetFor(subject);
            return sheet != null
                ? sheet.GroundCheckDistance
                : (float)PhysicsMovement.DefaultGroundCheckDistance;
        }

        /// <summary>Stop a physics-driven actor: brake a dynamic body's horizontal velocity
        /// (vertical speed is kept, so a fall keeps falling). A CharacterController has no
        /// settable velocity — its per-tick <c>Move</c> simply stops, so there is nothing to do.</summary>
        private static void BrakeBody(Actor subject)
        {
            if (EventSheet.FindCharacterController(subject) != null)
                return;
            RigidBody body = FindBody(subject);
            if (body == null || body.IsKinematic)
                return;
            PhysicsMovement.BrakeVelocity(body.LinearVelocity.Y, out double vx, out double vy, out double vz);
            body.LinearVelocity = new Vector3((float)vx, (float)vy, (float)vz);
        }

        /// <summary>
        /// Warp an actor to a spawn/checkpoint. Teleport deliberately bypasses physics: every
        /// ongoing move for the subject is cancelled (so a path or face tracker cannot drag it
        /// back), the character's stored fall speed is cleared, and a dynamic body is put at rest
        /// so it does not carry its old velocity through the warp.
        /// </summary>
        private static void TeleportSubject(Actor subject, Vector3 position)
        {
            for (int i = Moves.Count - 1; i >= 0; i--)
                if (Moves[i].Subject == subject)
                    Moves.RemoveAt(i);
            for (int i = Pushes.Count - 1; i >= 0; i--)
                if (Pushes[i].Target == subject)
                    Pushes.RemoveAt(i);
            CharacterFallSpeeds.Remove(subject);

            RigidBody body = FindBody(subject);
            if (body != null && !body.IsKinematic)
            {
                body.LinearVelocity = Vector3.Zero;
                body.AngularVelocity = Vector3.Zero;
            }
            subject.Position = position;
        }

        /// <summary>One tick of <c>push</c> on a target, through whatever body it has: dynamic =
        /// impulse, kinematic = swept move, CharacterController = collide-and-slide, else a
        /// transform nudge.</summary>
        private static void ApplyPush(Actor target, string direction, double strength)
        {
            Vector3 dir = DirectionOf(target, direction);
            float step = (float)strength * DeltaTime;
            switch (ResolvePushMode(target))
            {
                case PhysicsPushMode.Impulse:
                {
                    RigidBody body = FindBody(target);
                    if (body != null)
                    {
                        body.WakeUp();
                        body.AddForce(dir * (float)strength, ForceMode.Impulse);
                    }
                    else
                        target.Position = target.Position + dir * step;
                    break;
                }
                case PhysicsPushMode.Sweep:
                {
                    RigidBody body = FindBody(target);
                    if (body != null && IsAncestorOrSelf(body, target))
                        body.AddMovement(dir * step);
                    else
                        target.AddMovement(dir * step);
                    break;
                }
                case PhysicsPushMode.Character:
                {
                    CharacterController character = EventSheet.FindCharacterController(target);
                    if (character != null)
                        character.Move(dir * step);
                    else
                        target.Position = target.Position + dir * step;
                    break;
                }
                default:
                    target.Position = target.Position + dir * step;
                    break;
            }
        }

        /// <summary>
        /// How <c>push</c> reaches its target: the target's own EventSheet opt-in and body pick
        /// the mode (dynamic = impulse, kinematic = swept move, character = collide-and-slide,
        /// otherwise a transform nudge).
        /// </summary>
        private static PhysicsPushMode ResolvePushMode(Actor target)
        {
            bool physics = EventsManager.Instance?.Runtime?.FindSheetFor(target)?.UsePhysicsMovement ?? false;
            CharacterController character = physics ? EventSheet.FindCharacterController(target) : null;
            RigidBody body = physics ? FindBody(target) : null;
            if (character != null && !IsAncestorOrSelf(character, target))
            {
                WarnOnce($"\"{target.Name}\" uses physics movement, but its CharacterController is a child — push can't move it. Put the EventSheet on the CharacterController actor with the mesh under it.");
                return PhysicsPushMode.Transform;
            }
            return PhysicsMovement.PushModeFor(
                physics, character != null, body != null, body != null && body.IsKinematic);
        }

        /// <summary>Finish a path: raise <c>when me arrives at there</c> for click-walks, then
        /// snap/brake the subject. Used by both the empty-path and last-waypoint exits.</summary>
        private static void FinishGoto(OngoingMove move)
        {
            if (move.CompletesOrder)
            {
                var runtime = EventsManager.Instance?.Runtime;
                if (runtime != null)
                {
                    runtime.Orders.Complete(runtime.Variables, runtime, move.Subject);
                    if (runtime.Orders.HasOrders(move.Subject) && !PendingOrderMoves.Contains(move.Subject))
                        PendingOrderMoves.Add(move.Subject);
                }
            }
            if (move.HasPositionTarget && move.ArrivesAtThere)
                RaiseArriveAt(move.Subject, "there");
            ArriveAt(move);
        }

        private static void ArriveAt(OngoingMove move)
        {
            if (move.UsesPhysics)
                BrakeBody(move.Subject);
            else if (move.Target != null)
                move.Subject.Position = move.Target.Position;
            else if (move.HasPositionTarget)
                move.Subject.Position = move.PositionTarget;
        }

        /// <summary>Push an arrives-at-something signal to the subject's sheet(s), used by
        /// goto there to raise `when me arrives at there`.</summary>
        private static void RaiseArriveAt(Actor subject, string name)
        {
            EventsManager.Instance?.Runtime.RaisePush(subject, new PushSignal { Kind = TriggerKind.Arrives, Name = name });
        }

        /// <summary>
        /// The RigidBody that drives this actor: the actor itself (the recommended setup — the
        /// EventSheet on the body, mesh and colliders under it), a parent body, or a child body.
        /// </summary>
        private static RigidBody FindBody(Actor actor)
        {
            if (actor == null)
                return null;
            for (Actor node = actor; node != null && !(node is Scene); node = node.Parent)
                if (node is RigidBody body)
                    return body;
            return actor.FindActor<RigidBody>();
        }

        private static bool IsAncestorOrSelf(Actor candidate, Actor node)
        {
            for (Actor parent = node; parent != null && !(parent is Scene); parent = parent.Parent)
                if (ReferenceEquals(parent, candidate))
                    return true;
            return false;
        }

        private static Vector3 DirectionOf(Actor actor, string dir)
        {
            switch (dir)
            {
                case "forward":
                    return Vector3.Transform(Vector3.Forward, actor.Orientation);
                case "back":
                case "backward":
                    return -Vector3.Transform(Vector3.Forward, actor.Orientation);
                case "left":
                    return Vector3.Transform(Vector3.Left, actor.Orientation);
                case "right":
                    return Vector3.Transform(Vector3.Right, actor.Orientation);
                case "up":
                    return Vector3.Up;
                case "down":
                    return Vector3.Down;
                default:
                    return Vector3.Zero;
            }
        }

        private static double ResolveNumber(EventContext context, ActionSpec spec)
        {
            if (spec.AmountNode != null)
                return context.Evaluate(spec.AmountNode).AsNumber();
            return spec.Number;
        }

        private static void SetActive(EventContext context, string thing, bool on)
        {
            if (ResolveThing(context, thing) is Actor actor)
            {
                actor.IsActive = on;
                if (on)
                    EventsManager.Instance?.AttachSpawnedActor(actor);
            }
            else
                WarnOnce($"I couldn't find the thing \u201C{thing}\u201D to {(on ? "show" : "hide")}.");
        }

        private static void Spawn(EventContext context, ActionSpec spec)
        {
            // `spawn ... offset <n>` slides the clone along the spot's forward axis; a plain
            // spawn resolves to 0 and keeps the exact old placement.
            double offset = spec.AmountNode != null ? context.Evaluate(spec.AmountNode).AsNumber() : 0;
            // `spawn Bullet` fires whatever the sheet currently holds (Task 16 Part C): the
            // weapon data's bullet type replaces the generic template name, so a Shotgun's
            // clone is its pellet actor while an empty-handed sheet still clones "Bullet".
            string template = spec.Thing;
            WeaponStats weapon = null;
            bool isBullet = AssetNames.Key(template) == "bullet";
            if (isBullet)
            {
                weapon = WeaponData.ForActor(context);
                if (weapon != null && !string.IsNullOrEmpty(weapon.Bullet))
                    template = weapon.Bullet;
            }
            // Part C.5: `Multiplier` pellets per shot, fanned across `Spread` degrees.
            int pellets = weapon == null ? 1 : Math.Max(1, (int)Math.Round(weapon.Multiplier));
            double[] yaws = BulletMath.SpreadAngles(weapon == null ? 0 : weapon.Spread, pellets);
            string spawnedSound = null;
            for (int i = 0; i < pellets; i++)
            {
                Actor clone = SpawnClone(context, template, spec.To, offset);
                if (clone == null)
                    return; // one warning from SpawnClone is enough, even for a whole spread
                // Task 16 Part F: a shot remembers who fired it, so a player's death can be
                // credited back to the other player (`when player 1 kills player 2`).
                if (isBullet && context.Runtime != null)
                    context.Runtime.Variables.SetLocal(clone, "shooter",
                        ValueBox.OfText(context.Runtime.NameOfActor(context.Me)));
                if (isBullet && weapon != null)
                {
                    ApplyWeaponToBullet(context, clone, weapon, pellets > 1);
                    if (Math.Abs(yaws[i]) > 0.0001)
                        clone.Orientation = Quaternion.Multiply(clone.Orientation,
                            Quaternion.Euler(0, (float)yaws[i], 0));
                }
                EventsManager.Instance?.AttachSpawnedActor(clone);
                // A shot or a blast is heard the moment it appears (Task 16 Part B): enemies react
                // to gunfire without the shooter's sheet having to remember a `play sound` line.
                // The weapon's own sound wins when it has one, so a shotgun doesn't sound like a
                // pistol; one sound per shot, not one per pellet.
                spawnedSound ??= weapon != null && !string.IsNullOrEmpty(weapon.Sound)
                    ? weapon.Sound
                    : SpawnSoundName(template);
                if (spawnedSound != null)
                    EmitSound(spawnedSound, clone.Position, context.Me as Actor ?? clone);
            }
        }

        /// <summary>
        /// Copies the held weapon's projectile data onto a freshly spawned bullet, so the bullet's
        /// own sheet can act on it without looking the weapon up again: `my BulletSpeed`, `my Life`,
        /// `my damage`, `my knockback`, `my blast` and the `my Bounce` / `my Explosive` /
        /// `my Pierce` flags (Task 16 Part C.5).
        /// </summary>
        private static void ApplyWeaponToBullet(EventContext context, Actor clone, WeaponStats weapon, bool fromSpread)
        {
            EventVariables variables = context.Runtime != null ? context.Runtime.Variables : null;
            if (variables == null)
                return;
            variables.SetLocal(clone, "BulletSpeed", ValueBox.OfNumber(weapon.Speed));
            variables.SetLocal(clone, "Life", ValueBox.OfNumber(weapon.Life));
            variables.SetLocal(clone, "TimeLeft", ValueBox.OfNumber(weapon.Life));
            // lowercase `damage` is what `other's damage` reads on the victim's sheet.
            variables.SetLocal(clone, "damage", ValueBox.OfNumber(weapon.Damage));
            variables.SetLocal(clone, "knockback", ValueBox.OfNumber(weapon.Knockback));
            variables.SetLocal(clone, "blast", ValueBox.OfNumber(weapon.Blast));
            variables.SetLocal(clone, "Bounce", ValueBox.OfBoolean(weapon.Bounce));
            variables.SetLocal(clone, "Explosive", ValueBox.OfBoolean(weapon.Explosive));
            variables.SetLocal(clone, "Pierce", ValueBox.OfBoolean(weapon.Pierce));
            // Marked so an explosion can skip its own siblings in the spread.
            variables.SetLocal(clone, "fromSpread", ValueBox.OfBoolean(fromSpread));
        }

        /// <summary>
        /// <c>bounce</c> — a projectile reflects off what it just hit and keeps flying (Task 16
        /// Part C.5). The wall's normal comes from a short ray along the travel direction; the
        /// mirrored heading replaces the actor's yaw, and the clone steps just clear of the
        /// surface so the next tick is not still touching it.
        /// </summary>
        private static void BounceProjectile(EventContext context)
        {
            var me = context.Me as Actor;
            if (me == null)
                return;
            Vector3 forward = Vector3.Transform(Vector3.Forward, me.Orientation);
            double normalX = -forward.X;
            double normalZ = -forward.Z;
            if (Physics.RayCast(me.Position, forward, out RayCastHit hit, 120f, ~0u, false))
            {
                normalX = hit.Normal.X;
                normalZ = hit.Normal.Z;
            }
            BulletMath.Reflect(forward.X, forward.Z, normalX, normalZ, out double outX, out double outZ);
            double yaw = Math.Atan2(outX, outZ) * 180.0 / Math.PI;
            me.Orientation = Quaternion.Euler(0, (float)yaw, 0);
            me.Position += new Vector3((float)(outX * 8), 0f, (float)(outZ * 8));
        }

        /// <summary>
        /// <c>explode</c> / <c>explode radius &lt;n&gt;</c> — a blast where the projectile stopped
        /// (Task 16 Part C.5): the scene's Explosion actor plays the particle, every other actor
        /// inside the radius takes a <c>"damage"</c> message whose sender carries the weapon's
        /// <c>damage</c> (so `other's damage` on the victim reads the real number), and the
        /// projectile removes itself. The radius is the qualifier, else the bullet's `my blast`,
        /// else 150 cm.
        /// </summary>
        private static void ExplodeProjectile(EventContext context, ActionSpec spec)
        {
            var me = context.Me as Actor;
            if (me == null)
                return;
            double blast = spec.AmountNode != null ? ResolveNumber(context, spec) : context.ReadMy("blast").AsNumber();
            if (blast <= 0)
                blast = 150;
            // The blast is a world event: the Explosion template shows it and the sound bus
            // carries it, so enemies with `when me hears "explosion"` react too.
            Actor burst = SpawnClone(context, "Explosion", "me", 0);
            if (burst != null)
                EventsManager.Instance?.AttachSpawnedActor(burst);
            EmitSound("explosion", me.Position, me, Math.Max(blast, SoundBus.DefaultRadius));
            if (context.Runtime != null && context.World != null)
            {
                foreach (object other in context.World.Actors())
                {
                    if (other == null || ReferenceEquals(other, me))
                        continue;
                    if (other is Actor actor && (!actor.IsActive || ReferenceEquals(actor, me)))
                        continue;
                    // Don't chain-react through the rest of a shotgun spread or other bullets.
                    if (context.Runtime.Variables.HasLocal(other, "BulletSpeed"))
                        continue;
                    if (context.World.Distance(me, other) > blast)
                        continue;
                    context.Runtime.RaisePush(other, new PushSignal
                    {
                        Kind = TriggerKind.Message,
                        Name = "damage",
                        Other = me,
                    });
                }
            }
            ForgetSubject(context, me);
            FlaxEngine.Object.Destroy(me, 0f);
        }

        /// <summary>Drops the per-actor bookkeeping a destroyed actor leaves behind.</summary>
        private static void ForgetSubject(EventContext context, Actor actor)
        {
            for (int i = Moves.Count - 1; i >= 0; i--)
                if (Moves[i].Subject == actor || Moves[i].Target == actor)
                    Moves.RemoveAt(i);
            for (int i = Pushes.Count - 1; i >= 0; i--)
                if (Pushes[i].Target == actor)
                    Pushes.RemoveAt(i);
            ClearActor(actor);
            context.Runtime.Selection.Forget(actor);
            ClearFogVisibility(actor);
        }

        /// <summary>The noise a spawned thing makes as it appears, or null for a quiet one.</summary>
        private static string SpawnSoundName(string templateName)
        {
            switch (AssetNames.Key(templateName))
            {
                case "bullet":
                    return "gunshot";
                case "explosion":
                    return "explosion";
                default:
                    return null;
            }
        }

        /// <summary>Clones the template actor named <paramref name="templateName"/> and places
        /// it at <paramref name="at"/>. Returns null (after warning once) when the template is
        /// missing. Callers attach the clone's event sheet when they want it.</summary>
        private static Actor SpawnClone(EventContext context, string templateName, string at, double offset = 0)
        {
            Actor template = Level.FindActor(templateName);
            if (template == null)
            {
                WarnOnce($"To spawn \"{templateName}\" first place an actor with that name in the scene (hidden if you like — it stages the clones).");
                return null;
            }
            Actor spawnAt = ResolveThing(context, at) as Actor;
            var clone = template.Clone();
            clone.IsActive = true;
            if (spawnAt != null)
            {
                clone.Position = spawnAt.Position;
                clone.Orientation = spawnAt.Orientation;
                if (offset != 0)
                {
                    // Flatten to the ground plane so a bullet never spawns above or below the
                    // shooter, however the spot is pitched.
                    Vector3 forward = Vector3.Transform(Vector3.Forward, spawnAt.Orientation);
                    PhysicsMovement.SpawnOffset(forward.X, forward.Z, offset, out double offsetX, out double offsetZ);
                    clone.Position += new Vector3((float)offsetX, 0f, (float)offsetZ);
                }
            }
            Level.SpawnActor(clone);
            // `my spawnedFrom` names the template, and the sheet fallback can use it when a
            // cloned actor's own name has no matching .events file.
            context.Runtime.MarkSpawned(clone, templateName);
            ApplyRally(context, spawnAt, clone);
            return clone;
        }

        /// <summary><c>place &lt;building&gt; at &lt;target&gt;</c> — spawn a building clone,
        /// optionally snapped to a grid cell. Free-form placement keeps the target's exact
        /// position; <c>on grid</c> snaps X/Z to cell centres and reserves the footprint.</summary>
        private static void Place(EventContext context, ActionSpec spec)
        {
            Actor template = Level.FindActor(spec.Thing);
            if (template == null)
            {
                WarnOnce($"To place \"{spec.Thing}\" first place an actor with that name in the scene (hidden if you like — it stages the clones).");
                return;
            }

            Vector3 position = ResolvePlacePosition(context, spec.To, out Quaternion orientation);
            var clone = template.Clone();
            clone.IsActive = true;
            clone.Position = position;
            clone.Orientation = orientation;

            if (spec.OnGrid)
            {
                double size = ConstructionManager.ResolveGridSize(context.Variables, spec.HasGridSize, spec.GridSize);
                var snapped = new Vector3(
                    (float)ConstructionManager.SnapToGrid(position.X, size),
                    position.Y,
                    (float)ConstructionManager.SnapToGrid(position.Z, size));
                if (!context.Runtime.Construction.TryReserveCells(clone, spec.Thing, context.Variables,
                        snapped.X, snapped.Z, size))
                {
                    FlaxEngine.Object.Destroy(clone);
                    WarnOnce($"That grid cell is already occupied, so \u201C{spec.Thing}\u201D was not placed.");
                    return;
                }
                clone.Position = snapped;
            }

            Level.SpawnActor(clone);
            EventsManager.Instance?.AttachSpawnedActor(clone);
            // Terrain (Task 22 Part B): buildings only stand on ground their own type crosses —
            // checked after the clone's sheet ran, so `set my unit type to "sea"` on that sheet
            // counts (a shipyard on water, a barracks on land).
            TerrainMap terrain = context.Runtime.Terrain;
            terrain.ResolveCellSize(context.Variables);
            string buildingType = UnitTypes.TypeOf(context.Runtime.Variables, clone);
            if (!terrain.IsPassable(buildingType, clone.Position.X, clone.Position.Z))
            {
                WarnOnce($"\u201C{spec.Thing}\u201D is a {buildingType} building and that spot is {UnitTypes.GroundName(terrain.KindAt(clone.Position.X, clone.Position.Z))}, so it was not placed.");
                context.Runtime.Construction.ReleaseCells(clone);
                FlaxEngine.Object.Destroy(clone);
                return;
            }
            context.Runtime.Construction.RegisterPlaced(clone, spec.Thing, context.Variables);
            PlayConstructionAnimation(clone);
            context.Runtime.RaisePushAll(new PushSignal
            {
                Kind = TriggerKind.BuildingPlaced,
                Name = spec.Thing,
                Other = clone,
            });
        }

        /// <summary>Where a <c>place</c> target points: <c>Mouse</c> uses the cursor ray (nearest
        /// collider, else the y = 0 ground plane); actors use their position and orientation.</summary>
        private static Vector3 ResolvePlacePosition(EventContext context, string target, out Quaternion orientation)
        {
            orientation = Quaternion.Identity;
            if (string.Equals(target, "Mouse", StringComparison.OrdinalIgnoreCase))
            {
                if (FlaxWorld.TryGetMouseRay(Input.MousePosition, out Ray ray))
                {
                    if (Physics.LineCastAll(ray.Position, ray.Position + ray.Direction * 10000f, out var hits, ~0u, true))
                    {
                        float nearestDistance = float.MaxValue;
                        PhysicsColliderActor nearest = null;
                        foreach (var hit in hits)
                        {
                            if (hit.Collider != null && hit.Distance < nearestDistance)
                            {
                                nearestDistance = hit.Distance;
                                nearest = hit.Collider;
                            }
                        }
                        if (nearest != null)
                            return ray.Position + ray.Direction * nearestDistance;
                    }
                    // No collider: intersect the cursor ray with the y = 0 ground plane.
                    if (ray.Direction.Y < -0.0001f)
                    {
                        float along = -ray.Position.Y / ray.Direction.Y;
                        if (along > 0)
                            return ray.Position + ray.Direction * along;
                    }
                }
                WarnOnce("I couldn't work out where the mouse is pointing, so the building was placed at the origin.");
                return Vector3.Zero;
            }

            if (ResolveThing(context, target) is Actor actor)
            {
                orientation = actor.Orientation;
                return actor.Position;
            }

            WarnOnce($"I couldn't find \u201C{target}\u201D to place the building at.");
            if (context.Me is Actor me)
            {
                orientation = me.Orientation;
                return me.Position;
            }
            return Vector3.Zero;
        }

        /// <summary>Best-effort construction animation: plays the clone's scene animation when it
        /// has a <see cref="SceneAnimationPlayer"/>. Missing players/clips are cosmetic and silent.</summary>
        private static void PlayConstructionAnimation(Actor clone)
        {
            try
            {
                SceneAnimationPlayer player = clone as SceneAnimationPlayer ?? clone.FindActor<SceneAnimationPlayer>();
                if (player != null && player.Animation != null)
                    player.Play();
            }
            catch
            {
                // Cosmetic only: never let an animation problem break placement.
            }
        }

        /// <summary><c>cancel build</c> — stops construction on <c>me</c> or a named building,
        /// frees its grid cells and destroys the actor silently (no BuildingDestroyed signal).</summary>
        private static void CancelBuild(EventContext context, ActionSpec spec)
        {
            Actor target = spec.Thing.Length > 0
                ? ResolveThing(context, spec.Thing) as Actor
                : context.Me as Actor;
            if (target == null)
            {
                WarnOnce("I couldn't find the building to cancel.");
                return;
            }
            if (!context.Runtime.Construction.Cancel(context.Variables, target))
            {
                WarnOnce($"\u201C{target.Name}\u201D isn't under construction, so there is nothing to cancel.");
                return;
            }
            for (int i = Moves.Count - 1; i >= 0; i--)
                if (Moves[i].Subject == target || Moves[i].Target == target)
                    Moves.RemoveAt(i);
            ClearActor(target);
            context.Runtime.Selection.Forget(target);
            context.Runtime.Production.Destroyed(target);
            context.Runtime.Orders.Destroyed(target);
            FlaxEngine.Object.Destroy(target, 0f);
        }

        /// <summary><c>train &lt;unit&gt; at &lt;building&gt;</c> — adds a unit to that
        /// building's production queue. A missing <c>at</c> target means the acting building.</summary>
        private static void Train(EventContext context, ActionSpec spec)
        {
            Actor building = ResolveProductionBuilding(context, spec.To);
            if (building == null)
            {
                WarnOnce(string.IsNullOrEmpty(spec.To)
                    ? "\u201Ctrain\u201D needs a production building \u2014 run it on the building's sheet or write \u201Ctrain Marine at Barracks\u201D."
                    : $"I couldn't find the building \u201C{spec.To}\u201D to train \u201C{spec.Thing}\u201D at.");
                return;
            }

            string buildingName = context.Runtime.Construction.BuildingOf(building);
            if (string.IsNullOrEmpty(buildingName))
                buildingName = building.Name;
            context.Runtime.Production.Enqueue(context.Variables, context.Runtime, building,
                buildingName, spec.Thing);
        }

        /// <summary><c>cancel training &lt;unit&gt;</c> — removes that unit from the acting
        /// building's queue (or a named building's queue with <c>at &lt;building&gt;</c>).</summary>
        private static void CancelTraining(EventContext context, ActionSpec spec)
        {
            Actor building = ResolveProductionBuilding(context, spec.To);
            if (building == null)
            {
                WarnOnce(string.IsNullOrEmpty(spec.To)
                    ? "\u201Ccancel training\u201D needs a production building \u2014 run it on the building's sheet or write \u201Ccancel training Marine at Barracks\u201D."
                    : $"I couldn't find the building \u201C{spec.To}\u201D to cancel training at.");
                return;
            }
            if (!context.Runtime.Production.Cancel(context.Runtime, building, spec.Thing))
                WarnOnce($"\u201C{building.Name}\u201D has no \u201C{spec.Thing}\u201D in its production queue.");
        }

        /// <summary>The building a production action names, falling back to the acting actor.</summary>
        private static Actor ResolveProductionBuilding(EventContext context, string thing)
        {
            if (!string.IsNullOrEmpty(thing))
                return ResolveThing(context, thing) as Actor;
            return context.Me as Actor;
        }

        /// <summary><c>attack-move to &lt;target&gt;</c> — walk toward a target, ready to
        /// engage anything met on the way. The order completes on arrival (Task 18 Part E).</summary>
        private static void StartAttackMove(EventContext context, ActionSpec spec)
        {
            Actor me = context.Me as Actor;
            if (me == null)
            {
                WarnOnce("\u201Cattack-move\u201D needs to run on a unit's sheet \u2014 the global Game sheet has no body to move.");
                return;
            }
            if (!TryResolveOrderTarget(context, spec.Thing, out object target, out string targetName, out bool isPosition))
                return;
            context.Runtime.Wander.Clear(me);
            var order = new UnitOrder
            {
                Kind = UnitOrderKind.AttackMove,
                Name = "attack-move",
                Target = target,
                TargetName = targetName,
                HasPositionTarget = isPosition,
            };
            context.Runtime.Orders.StartOrder(context.Variables, context.Runtime, me, order);
            BeginOrderMove(context.Runtime, me, order, 5);
        }

        /// <summary><c>attack &lt;target&gt;</c> — close to the edge of this unit's own attack
        /// range and stop there; a target already in range is not walked into. The order stays
        /// active so the sheet's <c>me is in range of …</c> line decides when to strike.</summary>
        private static void StartAttack(EventContext context, ActionSpec spec)
        {
            Actor me = context.Me as Actor;
            if (me == null)
            {
                WarnOnce("\u201Cattack\u201D needs to run on a unit's sheet \u2014 the global Game sheet has no body to move.");
                return;
            }
            if (!TryResolveOrderTarget(context, spec.Thing, out object target, out string targetName, out bool isPosition))
                return;
            context.Runtime.Wander.Clear(me);
            var order = new UnitOrder
            {
                Kind = UnitOrderKind.Attack,
                Name = "attack",
                Target = target,
                TargetName = targetName,
                HasPositionTarget = isPosition,
            };
            context.Runtime.Orders.StartOrder(context.Variables, context.Runtime, me, order);
            // A `ranged` unit (Task 23 Part A) stops at its attack range — declared, or the
            // ranged default. A melee unit without a declared range keeps the old behaviour and
            // walks into contact.
            double range = OrderManager.StopDistanceFor(context);
            Actor targetActor = target as Actor;
            if (range > 0 && targetActor != null && targetActor.Scene != null)
            {
                double dx = me.Position.X - targetActor.Position.X;
                double dz = me.Position.Z - targetActor.Position.Z;
                if (System.Math.Sqrt(dx * dx + dz * dz) <= range)
                    return;
                BeginOrderMove(context.Runtime, me, order, 5, range);
                return;
            }
            BeginOrderMove(context.Runtime, me, order, 5);
        }

        /// <summary><c>stop</c> — cancel the current and queued orders and leave the unit idle.</summary>
        private static void StopOrder(EventContext context)
        {
            Actor me = context.Me as Actor;
            if (me == null)
            {
                WarnOnce("\u201Cstop\u201D needs to run on a unit's sheet \u2014 the global Game sheet has no body to stop.");
                return;
            }
            StopMovingSubject(me);
            context.Runtime.Wander.Clear(me);
            context.Runtime.Orders.Stop(context.Variables, me);
        }

        /// <summary><c>hold position</c> — drop queued orders and freeze in place.</summary>
        private static void HoldPosition(EventContext context)
        {
            Actor me = context.Me as Actor;
            if (me == null)
            {
                WarnOnce("\u201Chold position\u201D needs to run on a unit's sheet \u2014 the global Game sheet has no body to hold.");
                return;
            }
            StopMovingSubject(me);
            context.Runtime.Wander.SetStill(me);
            context.Runtime.Orders.Hold(context.Variables, me);
        }

        /// <summary><c>add order to queue</c> — append a copy of the current order behind it.</summary>
        private static void AddOrderToQueue(EventContext context)
        {
            Actor me = context.Me as Actor;
            UnitOrder current = me == null ? null : context.Runtime.Orders.CurrentOrderOf(me);
            if (current == null)
            {
                WarnOnce("\u201Cadd order to queue\u201D needs an active order to copy \u2014 issue a move or attack first.");
                return;
            }
            context.Runtime.Orders.AddOrder(context.Variables, context.Runtime, me, new UnitOrder
            {
                Kind = current.Kind,
                Name = current.Name,
                Target = current.Target,
                TargetName = current.TargetName,
                HasPositionTarget = current.HasPositionTarget,
            });
        }

        /// <summary><c>clear orders</c> — silently drop the current and queued orders.</summary>
        private static void ClearOrders(EventContext context)
        {
            Actor me = context.Me as Actor;
            if (me == null)
                return;
            StopMovingSubject(me);
            context.Runtime.Wander.Clear(me);
            context.Runtime.Orders.Clear(context.Variables, me);
        }

        /// <summary><c>set rally point to &lt;target&gt;</c> — where this building's newly
        /// spawned units should walk.</summary>
        private static void SetRallyPoint(EventContext context, ActionSpec spec)
        {
            Actor me = context.Me as Actor;
            if (me == null)
            {
                WarnOnce("\u201Cset rally point\u201D needs to run on a building's sheet.");
                return;
            }
            if (!TryResolveOrderTarget(context, spec.Thing, out object target, out string targetName, out bool isPosition))
                return;
            context.Runtime.Orders.SetRallyPoint(me, target, isPosition ? "there" : targetName);
        }

        /// <summary>Resolves an order's target actor or the last clicked ground position.</summary>
        private static bool TryResolveOrderTarget(EventContext context, string thing,
            out object target, out string targetName, out bool isPosition)
        {
            target = null;
            targetName = thing ?? "";
            isPosition = false;
            if (string.IsNullOrEmpty(thing))
            {
                WarnOnce("an order needs a target, like \u201Cattack-move to Enemy\u201D.");
                return false;
            }
            if (thing == "there")
            {
                if (!LastClickPosition.HasValue)
                {
                    WarnOnce("\u201Cthere\u201D needs a clicked position first \u2014 click somewhere in the scene to set it.");
                    return false;
                }
                isPosition = true;
                return true;
            }
            target = ResolveThing(context, thing) as Actor;
            if (target == null)
            {
                WarnOnce($"I couldn't find \u201C{thing}\u201D to order.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// <c>mark water around Mouse by 300</c> / <c>mark land at Mouse</c> — paints the terrain
        /// grid used by movement and building placement (Task 22 Part B).
        /// </summary>
        private static void MarkTerrain(EventContext context, ActionSpec spec)
        {
            UnitTypes.Ground? ground = UnitTypes.GroundFrom(spec.Thing);
            if (ground == null)
            {
                WarnOnce($"I don't know the terrain \u201C{spec.Thing}\u201D \u2014 it is land, water or air, like `mark water around Mouse by 300`.");
                return;
            }
            Vector3 point = ResolvePlacePosition(context, spec.To, out _);
            double radius = spec.HasNumber ? System.Math.Max(0, spec.Number) : 0;
            TerrainMap terrain = context.Runtime.Terrain;
            terrain.ResolveCellSize(context.Variables);
            int cells = terrain.MarkArea(point.X, point.Z, radius, ground.Value);
            if (cells <= 0)
                WarnOnce("That terrain mark covered no cells \u2014 give `around` a size, like `by 300`.");
        }

        /// <summary>
        /// Whether the unit's own type may not step onto that point (Task 22 Part B). An
        /// unmarked map answers "yes, walk", so a scene without terrain is untouched.
        /// </summary>
        private static bool TerrainBlocksStep(Actor subject, Vector3 next)
        {
            EventRuntime runtime = EventsManager.Instance?.Runtime;
            TerrainMap terrain = runtime?.Terrain;
            if (subject == null || runtime == null || terrain == null || !terrain.Enabled)
                return false;
            terrain.ResolveCellSize(runtime.Variables);
            Vector3 now = subject.Position;
            return !terrain.CanStep(UnitTypes.TypeOf(runtime.Variables, subject),
                now.X, now.Z, next.X, next.Z);
        }

        /// <summary>Starts NavMesh movement for an order; a blocked path raises
        /// <c>when path blocked</c> instead of walking through walls. <paramref name="stopDistance"/>
        /// shortens the walk to that distance from the target (attack range).</summary>
        private static void BeginOrderMove(EventRuntime runtime, Actor me, UnitOrder order, double speed,
            double stopDistance = 0)
        {
            if (runtime == null || me == null || me.Scene == null || order == null)
                return;
            Actor targetActor = order.Target as Actor;
            Vector3 destination;
            bool positionTarget = order.HasPositionTarget || order.TargetName == "there";
            bool snapToTarget = false;
            bool arrivesAtThere = true;
            if (targetActor != null && targetActor.Scene != null)
            {
                destination = targetActor.Position;
                if (stopDistance > 0)
                {
                    OrderManager.ApproachPoint(me.Position.X, me.Position.Z,
                        targetActor.Position.X, targetActor.Position.Z, stopDistance,
                        out double stopX, out double stopZ);
                    destination = new Vector3((float)stopX, targetActor.Position.Y, (float)stopZ);
                    positionTarget = true;
                    arrivesAtThere = false;
                }
                else
                    snapToTarget = true;
            }
            else if (positionTarget)
            {
                if (!LastClickPosition.HasValue)
                    return;
                destination = LastClickPosition.Value;
            }
            else
                return;

            // A destination this unit's type can never stand on is a blocked path, not a walk
            // that stalls at the shoreline (Task 22 Part B).
            if (TerrainBlocksStep(me, destination))
            {
                runtime.Orders.ReportPathBlocked(runtime, me);
                return;
            }

            if (!Navigation.FindPath(me.Position, destination, out Vector3[] path) || path == null || path.Length == 0)
            {
                runtime.Orders.ReportPathBlocked(runtime, me);
                return;
            }

            Remember(new OngoingMove
            {
                Subject = me,
                Target = snapToTarget ? targetActor : null,
                Goto = true,
                HasPositionTarget = positionTarget,
                PositionTarget = destination,
                Path = path,
                PathIndex = 0,
                Speed = speed,
                Mode = ResolveMoveMode(me, runtime.FindSheetFor(me)?.UsePhysicsMovement ?? false),
                CompletesOrder = true,
                ArrivesAtThere = arrivesAtThere,
            });
        }

        /// <summary>After a spawn, send the new unit to its building's rally point if one is set.</summary>
        private static void ApplyRally(EventContext context, Actor spawnAt, Actor clone)
        {
            EventRuntime runtime = context.Runtime;
            if (spawnAt == null || clone == null || runtime == null || !runtime.Orders.HasRallyPoint(spawnAt))
                return;
            var order = new UnitOrder
            {
                Kind = UnitOrderKind.Move,
                Name = "move",
                Target = runtime.Orders.RallyPointOf(spawnAt),
                TargetName = runtime.Orders.RallyTargetNameOf(spawnAt),
                HasPositionTarget = runtime.Orders.RallyPointOf(spawnAt) == null &&
                                    runtime.Orders.RallyTargetNameOf(spawnAt) == "there",
            };
            runtime.Orders.StartOrder(context.Variables, runtime, clone, order);
            BeginOrderMove(runtime, clone, order, 5);
        }

        private static void SpawnPrefab(EventContext context, ActionSpec spec)
        {
            Prefab prefab = LoadPrefabAsset(spec.Thing);
            if (prefab == null)
            {
                WarnOnce($"Couldn't load prefab \u201C{spec.Thing}\u201D. Place it in Content or use a full asset path.");
                return;
            }
            Actor spawnAt = ResolveThing(context, spec.To) as Actor;
            if (spawnAt == null)
                WarnOnce($"I couldn't find the spot \u201C{spec.To}\u201D, so \u201C{spec.Thing}\u201D spawned at the origin.");
            Vector3 position = spawnAt != null ? spawnAt.Position : Vector3.Zero;
            Quaternion rotation = spawnAt != null ? spawnAt.Orientation : Quaternion.Identity;
            var instance = PrefabManager.SpawnPrefab(prefab, position, rotation);
            if (instance == null)
            {
                WarnOnce($"Failed to spawn prefab \u201C{spec.Thing}\u201D.");
                return;
            }
            string spot = spawnAt == null
                ? $"no spot found for \u201C{spec.To}\u201D"
                : $"spot \u201C{spawnAt.Name}\u201D at {spawnAt.Position}";
            Debug.LogWarning($"Event sheet: spawned prefab \u201C{spec.Thing}\u201D as \u201C{instance.Name}\u201D at {instance.Position} ({spot}) in scene \u201C{(instance.Scene == null ? "?" : instance.Scene.Name)}\u201D — {DescribeSpawn(instance)}.");
            SpawnedInstance(context, instance, spec.Thing);
        }

        /// <summary>Counts the actors and visible models in a spawned tree, so a spawn that
        /// works but stays invisible says so instead of leaving you hunting the viewport.</summary>
        private static string DescribeSpawn(Actor root)
        {
            int actors = 0;
            int models = 0;
            var empty = new List<string>();
            void Walk(Actor node)
            {
                if (node == null)
                    return;
                actors++;
                if (node is StaticModel staticModel)
                {
                    models++;
                    if (staticModel.Model == null)
                        empty.Add(staticModel.Name + " (no Model)");
                }
                else if (node is AnimatedModel animated)
                {
                    models++;
                    if (animated.SkinnedModel == null)
                        empty.Add(animated.Name + " (no Skinned Model)");
                }
                foreach (Actor child in node.Children)
                    Walk(child);
            }
            Walk(root);
            if (models == 0)
                return $"{actors} actor(s) but NO StaticModel/AnimatedModel inside — the prefab has nothing visible to show";
            if (empty.Count > 0)
                return $"{actors} actor(s), {models} model(s) but these have no mesh assigned: {string.Join(", ", empty)}";
            return $"{actors} actor(s), {models} model(s)";
        }

        /// <summary>Finishes a prefab spawn: remember the asset name (`my spawnedFrom`), give
        /// every actor in the prefab its event sheet (so attachments run `when me appears`),
        /// then give the root a name that does not collide with another spawn.</summary>
        private static void SpawnedInstance(EventContext context, Actor instance, string source)
        {
            context.Runtime.MarkSpawned(instance, source);
            EventsManager.Instance?.AttachSpawnedTree(instance);
            UniqueInstanceName(context, instance);
        }

        /// <summary>Renames a spawned root to the first free name ("Hero", "Hero 2", …) so
        /// several copies of one prefab stay individually targetable. The sheet has already
        /// been attached under the source name, so messages/name lookups keep working.</summary>
        private static void UniqueInstanceName(EventContext context, Actor instance)
        {
            if (instance == null)
                return;
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (object obj in context.Runtime.World.Actors())
            {
                if (!(obj is Actor actor) || actor == null || ReferenceEquals(actor, instance))
                    continue;
                if (!string.IsNullOrEmpty(actor.Name))
                    taken.Add(actor.Name);
            }
            string unique = SpawnNames.Unique(instance.Name, name => taken.Contains(name));
            if (!string.Equals(unique, instance.Name, StringComparison.Ordinal))
                instance.Name = unique;
        }

        /// <summary>Finds an imported <see cref="Prefab"/> by name, ignoring folder, extension
        /// and case (the same rules as `play sound`).</summary>
        private static Prefab LoadPrefabAsset(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            Prefab prefab = null;
            if (name.Contains("/") || name.Contains("\\") || name.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                prefab = Content.Load<Prefab>(name);
            if (prefab != null)
                return prefab;
            foreach (var id in Content.GetAllAssetsByType(typeof(Prefab)))
            {
                if (!Content.GetAssetInfo(id, out AssetInfo info))
                    continue;
                if (System.IO.Path.GetFileNameWithoutExtension(info.Path).Equals(name, StringComparison.OrdinalIgnoreCase))
                    return Content.Load<Prefab>(info.Path);
            }
            return null;
        }

        private static void SpawnScene(EventContext context, ActionSpec spec)
        {
            if (!TryFindSceneId(spec.Thing, out Guid id))
            {
                WarnOnce($"I couldn't find a scene named \u201C{spec.Thing}\u201D. Scenes: {SceneNames()}.");
                return;
            }

            // One instance per scene name: loading the same scene twice would leave two
            // scenes with the same name, so say so instead of moving the one already there.
            if (SceneLoadedAs(spec.Thing, id) != null)
            {
                WarnOnce($"The scene \u201C{spec.Thing}\u201D is already loaded, so there is no new copy to place.");
                return;
            }

            // Level.LoadScene is ADDITIVE: it keeps every loaded scene and adds this one.
            // (Only `load scene` / `restart scene` unload the others first.)
            Scene loaded = LoadSceneAdditive(id);
            if (loaded == null)
            {
                WarnOnce($"I couldn't find the scene \u201C{spec.Thing}\u201D after loading it, so its actors stay at their authored positions.");
                return;
            }

            // Move ONLY this scene's root actors to the spot, relative to where the scene
            // was authored — never the actors that were already in the level.
            Actor spawnAt = ResolveThing(context, spec.To) as Actor;
            if (spawnAt != null)
            {
                Vector3 delta = spawnAt.Position - loaded.Position;
                foreach (Actor child in loaded.Children)
                    if (child != null)
                        child.Position += delta;
            }

            // Fresh actors were not swept at scene start: mark and attach their sheets now, so
            // the hero (and a weapon child) runs its own `when me appears`.
            FinishLoadedScene(context, loaded, spec.Thing, sceneStarts: true);
        }

        /// <summary>Tags an instanced scene's whole actor tree with `my spawnedFrom`.</summary>
        private static void MarkSpawnedTree(EventRuntime runtime, Actor actor, string source)
        {
            if (actor == null)
                return;
            runtime.MarkSpawned(actor, source);
            foreach (Actor child in actor.Children)
                MarkSpawnedTree(runtime, child, source);
        }

        private static void ShowHudText(string text)
        {
            if (!SetLabel("GameText", text))
                WarnOnce($"I couldn't find the HUD text. Create a UIControl actor named \u201CGameText\u201D with a Label. Labels in this scene: {ControlActorNames<FlaxEngine.GUI.Label>()}.");
        }

        private static void HideHudText()
        {
            var label = FindLabel("GameText", out _);
            if (label != null)
                label.Visible = false;
            else
                WarnOnce($"I couldn't find the HUD text. Create a UIControl actor named \u201CGameText\u201D with a Label. Labels in this scene: {ControlActorNames<FlaxEngine.GUI.Label>()}.");
        }

        /// <summary>
        /// Finds the GUI control named by an event, cast to <typeparamref name="T"/>. The name
        /// is a UIControl actor, or a parent actor that owns exactly one matching control
        /// (a panel/canvas). <c>UIControl</c> is an Actor, so this uses <c>as</c>, never
        /// <c>GetScript&lt;UIControl&gt;</c>.
        /// </summary>
        private static T FindControl<T>(string thing, out UIControl ui) where T : FlaxEngine.GUI.Control
        {
            ui = null;
            if (string.IsNullOrEmpty(thing))
                return default;

            Actor actor = Level.FindActor(thing);
            if (actor == null)
                return default;

            ui = actor as UIControl;
            if (ui == null)
            {
                var found = new List<UIControl>();
                CollectControls<T>(actor, found);
                if (found.Count != 1)
                    return default;
                ui = found[0];
            }

            return ui.Control as T;
        }

        private static void CollectControls<T>(Actor node, List<UIControl> found) where T : FlaxEngine.GUI.Control
        {
            foreach (Actor child in node.Children)
            {
                if (child == null)
                    continue;
                if (child is UIControl ui && ui.Control is T)
                    found.Add(ui);
                CollectControls<T>(child, found);
            }
        }

        /// <summary>Names of the control actors in the scene, for "I couldn't find it" warnings.</summary>
        private static string ControlActorNames<T>() where T : FlaxEngine.GUI.Control
        {
            var names = new List<string>();
            Scene[] scenes = Level.Scenes;
            for (int s = 0; s < scenes.Length; s++)
                if (scenes[s] != null)
                    CollectControlNames<T>(scenes[s], names);
            return names.Count == 0 ? "(none)" : string.Join(", ", names);
        }

        private static void CollectControlNames<T>(Actor node, List<string> names) where T : FlaxEngine.GUI.Control
        {
            foreach (Actor child in node.Children)
            {
                if (child == null)
                    continue;
                if (child is UIControl ui && ui.Control is T)
                    names.Add(child.Name);
                CollectControlNames<T>(child, names);
            }
        }

        private static bool SetControl<T>(string thing, Action<T> apply) where T : FlaxEngine.GUI.Control
        {
            var control = FindControl<T>(thing, out UIControl ui);
            if (control == null)
                return false;
            // A `hide GameText` earlier in the session must not brick `show text`.
            if (!ui.IsActive)
                ui.IsActive = true;
            apply(control);
            return true;
        }

        /// <summary>Finds the GUI label named by an event.</summary>
        private static FlaxEngine.GUI.Label FindLabel(string thing, out UIControl ui)
            => FindControl<FlaxEngine.GUI.Label>(thing, out ui);

        private static bool SetLabel(string thing, string text)
            => SetControl<FlaxEngine.GUI.Label>(thing, label =>
            {
                label.Text = text; // Label.Text is a LocalizedString; string converts implicitly
                label.Visible = true;
            });

        /// <summary><c>tween &lt;thing&gt; position/scale/alpha/color to ... in ...</c>: starts a
        /// coroutine-style tween updated in <see cref="Step"/>.</summary>
        private static void StartTween(EventContext context, ActionSpec spec)
        {
            var subject = ResolveThing(context, spec.Thing) as Actor;
            if (subject == null)
            {
                WarnOnce($"I couldn't find \u201C{spec.Thing}\u201D to tween.");
                return;
            }
            UIControl ui = subject as UIControl ?? subject.FindActor<UIControl>();
            var track = new TweenTrack
            {
                Subject = subject,
                Name = subject.Name,
                Kind = spec.TweenProperty,
                Duration = spec.Duration,
                Easing = spec.Easing,
            };

            switch (spec.TweenProperty)
            {
                case TweenKind.Position:
                {
                    var target = ResolveThing(context, spec.TweenTarget) as Actor;
                    if (target == null)
                    {
                        WarnOnce($"I couldn't find \u201C{spec.TweenTarget}\u201D for \u201Ctween {spec.Thing} position\u201D.");
                        return;
                    }
                    Vector3 from = subject.Position;
                    Vector3 to = target.Position;
                    track.FromX = from.X; track.FromY = from.Y; track.FromZ = from.Z;
                    track.ToX = to.X; track.ToY = to.Y; track.ToZ = to.Z;
                    break;
                }
                case TweenKind.Scale:
                {
                    double scale = spec.TweenValue == null ? spec.Number : context.Evaluate(spec.TweenValue).AsNumber();
                    Vector3 from = subject.Scale;
                    track.FromX = from.X; track.FromY = from.Y; track.FromZ = from.Z;
                    track.ToX = scale; track.ToY = scale; track.ToZ = scale;
                    break;
                }
                case TweenKind.Alpha:
                {
                    if (ui == null || ui.Control == null)
                    {
                        WarnOnce($"\u201Ctween {spec.Thing} alpha\u201D needs an actor with a UI control on a UIControl.");
                        return;
                    }
                    if (!TryGetControlColor(ui.Control, out Color alphaFrom))
                    {
                        WarnOnce($"\u201Ctween {spec.Thing} alpha\u201D needs a Label or Image control (that is where UI alpha lives).");
                        return;
                    }
                    double alpha = spec.TweenValue == null ? spec.Number : context.Evaluate(spec.TweenValue).AsNumber();
                    if (alpha < 0) alpha = 0;
                    if (alpha > 1) alpha = 1;
                    track.FromValue = alphaFrom.A;
                    track.ToValue = alpha;
                    break;
                }
                case TweenKind.Color:
                {
                    if (ui == null || ui.Control == null)
                    {
                        WarnOnce($"\u201Ctween {spec.Thing} color\u201D needs an actor with a UI control on a UIControl.");
                        return;
                    }
                    if (!TryParseColor(spec.TweenColor, out Color wanted))
                    {
                        WarnOnce($"I couldn't parse \u201C{spec.TweenColor}\u201D as a tween color — use \u201C\"r g b\"\u201D or \u201C\"r g b a\"\u201D with 0-1 floats.");
                        return;
                    }
                    if (!TryGetControlColor(ui.Control, out Color from))
                    {
                        WarnOnce($"\u201Ctween {spec.Thing} color\u201D needs a Label or Image control (that is where UI color lives).");
                        return;
                    }
                    track.FromR = from.R; track.FromG = from.G; track.FromB = from.B; track.FromA = from.A;
                    track.ToR = wanted.R; track.ToG = wanted.G; track.ToB = wanted.B; track.ToA = wanted.A;
                    break;
                }
            }

            // A new tween of the same property replaces the old one, so events can retarget
            // a move without two tweens fighting over the same transform channel.
            for (int i = LiveTweens.Count - 1; i >= 0; i--)
                if (LiveTweens[i].Subject == subject && LiveTweens[i].Track.Kind == track.Kind)
                    LiveTweens.RemoveAt(i);

            var live = new LiveTween { Subject = subject, Ui = ui, Track = track };
            if (track.Duration <= 0)
            {
                track.Advance(0);
                ApplyTween(live);
                EventsManager.Instance?.HandleTweenEnd(subject, track.Name);
                return;
            }
            LiveTweens.Add(live);
        }

        /// <summary>Writes one tick's interpolated tween values onto the actor/control.</summary>
        private static void ApplyTween(LiveTween live)
        {
            TweenTrack track = live.Track;
            switch (track.Kind)
            {
                case TweenKind.Position:
                    live.Subject.Position = new Vector3((float)track.X, (float)track.Y, (float)track.Z);
                    break;
                case TweenKind.Scale:
                    live.Subject.Scale = new Vector3((float)track.X, (float)track.Y, (float)track.Z);
                    break;
                case TweenKind.Alpha:
                    if (live.Ui != null && live.Ui.Control != null &&
                        TryGetControlColor(live.Ui.Control, out Color alphaColor))
                    {
                        alphaColor.A = (float)track.Value;
                        SetControlColor(live.Ui.Control, alphaColor);
                    }
                    break;
                case TweenKind.Color:
                    if (live.Ui != null && live.Ui.Control != null)
                        SetControlColor(live.Ui.Control, new Color((float)track.R, (float)track.G, (float)track.B, (float)track.A));
                    break;
            }
        }

        /// <summary><c>moveplatform &lt;thing&gt; &lt;axis&gt; &lt;distance&gt; in &lt;time&gt;</c>: starts a
        /// ping-pong sweep along a world axis, tracked in <see cref="Platforms"/> and advanced
        /// each tick in <see cref="Step"/>.</summary>
        private static void StartPlatform(EventContext context, ActionSpec spec)
        {
            var subject = ResolveThing(context, spec.Thing) as Actor;
            if (subject == null)
            {
                WarnOnce($"I couldn't find \u201C{spec.Thing}\u201D to moveplatform.");
                return;
            }
            if (!spec.HasNumber || spec.Duration <= 0)
            {
                WarnOnce($"\u201Cmoveplatform {spec.Thing}\u201D needs a positive time in seconds.");
                return;
            }
            // A new platform for the same subject replaces the old one, so retargeting doesn't
            // stack two sweeps on the same actor.
            for (int i = Platforms.Count - 1; i >= 0; i--)
                if (ReferenceEquals(Platforms[i].Subject, subject))
                    Platforms.RemoveAt(i);

            Platforms.Add(new PhysicsMovement.OngoingPlatform
            {
                Subject = subject,
                Axis = spec.Dir,
                Distance = spec.Number,
                Duration = spec.Duration,
                Easing = spec.Easing,
                StartX = subject.Position.X,
                StartY = subject.Position.Y,
                StartZ = subject.Position.Z,
                Mode = ResolveMoveMode(subject, context.Sheet != null && context.Sheet.UsePhysicsMovement),
            });
        }

        private static double AxisValue(double x, double y, double z, string axis)
        {
            switch (axis)
            {
                case "x": return x;
                case "y": return y;
                case "z": return z;
                default: return 0;
            }
        }

        private static void SetAxisValue(ref Vector3 v, string axis, double value)
        {
            switch (axis)
            {
                case "x": v.X = (float)value; break;
                case "y": v.Y = (float)value; break;
                case "z": v.Z = (float)value; break;
            }
        }

        /// <summary>Applies one tick's platform delta through the subject's movement mode,
        /// mirroring <see cref="MoveSubject"/>'s mode selection.</summary>
        private static void ApplyPlatformDelta(Actor subject, Vector3 delta, PhysicsMoveMode mode, float deltaTime)
        {
            float step = deltaTime;
            switch (mode)
            {
                case PhysicsMoveMode.Transform:
                    subject.Position = subject.Position + delta;
                    break;
                case PhysicsMoveMode.Dynamic:
                {
                    RigidBody body = FindBody(subject);
                    if (body != null)
                        body.LinearVelocity = new Vector3(delta.X / step, delta.Y / step, delta.Z / step);
                    else
                        subject.Position = subject.Position + delta;
                    break;
                }
                case PhysicsMoveMode.Kinematic:
                {
                    RigidBody body = FindBody(subject);
                    if (body != null && IsAncestorOrSelf(body, subject))
                        body.AddMovement(delta);
                    else
                        subject.AddMovement(delta);
                    break;
                }
                case PhysicsMoveMode.Character:
                {
                    CharacterController character = EventSheet.FindCharacterController(subject);
                    if (character != null)
                        character.Move(delta);
                    else
                        subject.Position = subject.Position + delta;
                    break;
                }
            }
        }

        /// <summary>UI tint lives on Image.Color or Label.TextColor (Button is a Label).</summary>
        private static bool TryGetControlColor(FlaxEngine.GUI.Control control, out Color color)
        {
            if (control is FlaxEngine.GUI.Image image)
            {
                color = image.Color;
                return true;
            }
            if (control is FlaxEngine.GUI.Label label)
            {
                color = label.TextColor;
                return true;
            }
            color = Color.White;
            return false;
        }

        private static void SetControlColor(FlaxEngine.GUI.Control control, Color color)
        {
            if (control is FlaxEngine.GUI.Image image)
                image.Color = color;
            else if (control is FlaxEngine.GUI.Label label)
                label.TextColor = color;
        }

        private static void PlaySound(EventContext context, ActionSpec spec)
        {
            if (string.IsNullOrEmpty(spec.Thing))
            {
                WarnOnce("\u201Cplay sound\u201D needs a clip name, like \u201Cplay sound \"shot\"\u201D.");
                return;
            }

            var clip = LoadAudioClip(spec.Thing);
            if (clip == null)
            {
                WarnOnce($"Could not load sound clip \u201C{spec.Thing}\u201D. Imported clips: {AvailableSoundNames()}.");
                return;
            }

            var me = context.Me as Actor;
            var audio = new AudioSource
            {
                HideFlags = HideFlags.DontSave,
                Volume = 1,
                Clip = clip,
                Position = me != null ? me.Position : Vector3.Zero,
            };
            Level.SpawnActor(audio);
            audio.Play();
            ActiveSounds.Add((me, audio));
            FlaxEngine.Object.Destroy(audio, clip.Length);
            // The clip is a world event too, so `when me hears "shot" by 12` can answer it.
            EmitSound(AssetNames.Key(spec.Thing), audio.Position, me);
        }

        /// <summary>
        /// Puts a sound on the event bus for `when me hears` / `me hears` (Task 16 Part B).
        /// <paramref name="source"/> is the actor the noise came from — it becomes `other` in the
        /// listening sheet — and may be null for a sound with no body.
        /// </summary>
        public static void EmitSound(string name, Vector3 position, Actor source, double radius = SoundBus.DefaultRadius)
        {
            EventsManager.Instance?.EmitSound(name, position, radius, source);
        }

        /// <summary><c>play particle "&lt;name&gt;"</c>: plays the actor's <see cref="ParticleEffect"/>
        /// (or one on a child), resolved like sounds by asset key so "explosion" finds effects/explosion.flax.</summary>
        private static void PlayParticle(EventContext context, ActionSpec spec)
        {
            if (string.IsNullOrEmpty(spec.Thing))
            {
                WarnOnce("\u201Cplay particle\u201D needs a name in quotes, like \u201Cplay particle \"smoke\"\u201D.");
                return;
            }
            var me = context.Me as Actor;
            if (me == null)
            {
                WarnOnce("\u201Cplay particle\u201D needs to run on an actor's sheet — the global Game sheet has no body to attach particles to.");
                return;
            }
            var effect = FindParticleEffect(me);
            if (effect == null)
            {
                WarnOnce($"I couldn't find a ParticleEffect on \u201C{me.Name}\u201D to play \u201C{spec.Thing}\u201D.");
                return;
            }
            // A template actor staged before its asset is dragged in would otherwise be played
            // with no particle system at all: warn once with the fix instead of failing silently
            // every shot (Task 16 Part E adds three such templates to the TK4 scene).
            ParticleSystem system = effect.ParticleSystem;
            if (system == null)
            {
                WarnOnce($"\u201C{me.Name}\u201D has a ParticleEffect with no particle system asset, so \u201C{spec.Thing}\u201D has nothing to play — drag a .flax particle asset onto that ParticleEffect in the scene.");
                return;
            }
            effect.IsActive = true;
            effect.Play();
            string key = AssetNames.Key(spec.Thing);
            bool oneShot = !effect.IsLooping;
            ParticleStates[me] = new ParticleState
            {
                Effect = effect,
                Key = key,
                Name = spec.Thing.Trim(),
                OneShot = oneShot,
                WasPlaying = effect.ParticlesCount > 0,
            };
        }

        /// <summary><c>stop particle "&lt;name&gt;"</c>: stops the actor's matching particle effect.</summary>
        private static void StopParticle(EventContext context, ActionSpec spec)
        {
            if (string.IsNullOrEmpty(spec.Thing))
            {
                WarnOnce("\u201Cstop particle\u201D needs a name in quotes, like \u201Cstop particle \"smoke\"\u201D.");
                return;
            }
            var me = context.Me as Actor;
            if (me == null || !ParticleStates.TryGetValue(me, out var state) || state.Effect == null)
                return;
            if (!string.Equals(state.Key, AssetNames.Key(spec.Thing), StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(state.Name, spec.Thing, StringComparison.OrdinalIgnoreCase))
                return;
            state.Effect.Stop();
            ParticleStates.Remove(me);
        }

        /// <summary>The Light on this actor, or on one of its children (the recommended setup: the
        /// light sits under the actor the EventSheet is on).</summary>
        private static Light FindLight(Actor actor)
        {
            if (actor == null)
                return null;
            return actor as Light ?? actor.FindActor<Light>();
        }

        /// <summary><c>set light intensity to &lt;n&gt;</c>: sets <see cref="Light.Brightness"/>.</summary>
        private static void SetLightIntensity(EventContext context, ActionSpec spec)
        {
            if (!spec.HasNumber)
            {
                WarnOnce("\u201Cset light intensity to\u201D needs a number, like \u201Cset light intensity to 2\u201D.");
                return;
            }
            var me = context.Me as Actor;
            var light = FindLight(me);
            if (light == null)
            {
                WarnOnce($"I couldn't find a Light on \u201C{me?.Name}\u201D to set intensity on.");
                return;
            }
            light.Brightness = (float)spec.Number;
        }

        /// <summary><c>set light color to "r g b [a]"</c>: sets <see cref="Light.Color"/> (0-1 floats).</summary>
        private static void SetLightColor(EventContext context, ActionSpec spec)
        {
            if (string.IsNullOrEmpty(spec.Thing))
            {
                WarnOnce("\u201Cset light color to\u201D needs a color in quotes, like \u201Cset light color to \"1 0 0\"\u201D.");
                return;
            }
            var me = context.Me as Actor;
            var light = FindLight(me);
            if (light == null)
            {
                WarnOnce($"I couldn't find a Light on \u201C{me?.Name}\u201D to set color on.");
                return;
            }
            if (!TryParseColor(spec.Thing, out var color))
            {
                WarnOnce($"I couldn't parse \u201C{spec.Thing}\u201D as a light color — use \u201C\"r g b\"\u201D or \u201C\"r g b a\"\u201D with 0-1 floats.");
                return;
            }
            light.Color = color;
        }

        /// <summary><c>set light range to &lt;n&gt;</c>: sets <see cref="Light.ViewDistance"/>.</summary>
        private static void SetLightRange(EventContext context, ActionSpec spec)
        {
            if (!spec.HasNumber)
            {
                WarnOnce("\u201Cset light range to\u201D needs a number, like \u201Cset light range to 10\u201D.");
                return;
            }
            var me = context.Me as Actor;
            var light = FindLight(me);
            if (light == null)
            {
                WarnOnce($"I couldn't find a Light on \u201C{me?.Name}\u201D to set range on.");
                return;
            }
            light.ViewDistance = (float)spec.Number;
        }

        /// <summary><c>enable light</c> / <c>disable light</c>: sets <see cref="Actor.IsActive"/> on the light.</summary>
        private static void SetLightEnabled(EventContext context, ActionSpec spec, bool enabled)
        {
            var me = context.Me as Actor;
            var light = FindLight(me);
            if (light == null)
            {
                WarnOnce($"I couldn't find a Light on \u201C{me?.Name}\u201D to {(enabled ? "enable" : "disable")}.");
                return;
            }
            light.IsActive = enabled;
        }

        /// <summary><c>toggle light</c>: flips <see cref="Actor.IsActive"/> on the light.</summary>
        private static void ToggleLight(EventContext context, ActionSpec spec)
        {
            var me = context.Me as Actor;
            var light = FindLight(me);
            if (light == null)
            {
                WarnOnce($"I couldn't find a Light on \u201C{me?.Name}\u201D to toggle.");
                return;
            }
            light.IsActive = !light.IsActive;
        }

        private static bool TryParseColor(string text, out Color color)
        {
            color = Color.White;
            var parts = text.Split(' ');
            if (parts.Length != 3 && parts.Length != 4)
                return false;
            float[] vals = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!float.TryParse(parts[i], System.Globalization.NumberStyles.Any,
                                    System.Globalization.CultureInfo.InvariantCulture, out vals[i]) ||
                    vals[i] < 0 || vals[i] > 1)
                    return false;
            if (parts.Length == 3)
                color = new Color(vals[0], vals[1], vals[2]);
            else
                color = new Color(vals[0], vals[1], vals[2], vals[3]);
            return true;
        }

        public static void PlayStepSound(EventContext context, string material, string clipName)
        {
            var clip = LoadAudioClip(clipName);
            if (clip == null)
            {
                WarnOnce($"Could not load step sound clip \"{clipName}\" for material \"{material}\". Imported clips: {AvailableSoundNames()}.");
                return;
            }

            var me = context.Me as Actor;
            var audio = new AudioSource
            {
                HideFlags = HideFlags.DontSave,
                Volume = 1,
                Clip = clip,
                Position = me != null ? me.Position : Vector3.Zero,
            };
            Level.SpawnActor(audio);
            audio.Play();
            ActiveSounds.Add((me, audio));
            FlaxEngine.Object.Destroy(audio, clip.Length);
        }

        /// <summary>
        /// Resolves a clip by name, ignoring folder and extension: "shot", "shot.wav" and
        /// "sounds/shot.flax" all find the same imported <see cref="AudioClip"/>.
        /// </summary>
        private static AudioClip LoadAudioClip(string name)
        {
            var index = SoundIndex();
            return index.TryGetValue(AssetNames.Key(name), out Guid id) ? Content.Load<AudioClip>(id) : null;
        }

        /// <summary>
        /// Clip key to asset id, built from the asset registry once per play session.
        /// Loading by id never probes paths, so a missing name cannot spam "Missing file" errors.
        /// </summary>
        private static Dictionary<string, Guid> SoundIndex()
        {
            if (SoundIndexCache != null)
                return SoundIndexCache;

            var index = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (Guid id in Content.GetAllAssetsByType(typeof(AudioClip)))
            {
                if (!Content.GetAssetInfo(id, out AssetInfo info))
                    continue;
                string key = AssetNames.Key(info.Path);
                if (key.Length == 0 || index.ContainsKey(key))
                    continue;
                index[key] = id;
            }
            SoundIndexCache = index;
            return index;
        }

        private static string AvailableSoundNames()
        {
            var names = new List<string>(SoundIndex().Keys);
            if (names.Count == 0)
                return "(none)";
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(", ", names);
        }

        public static void SetText(string thing, string text)
        {
            if (string.IsNullOrEmpty(thing))
            {
                WarnOnce("\u201Cset text of\u201D needs the name of the thing whose text changes — like \u201Cset text of ScoreLabel to Score\u201D.");
                return;
            }
            if (SetLabel(thing, text))
                return;
            // A TextBox (input field) is not a Label; write its Text instead.
            var box = FindControl<FlaxEngine.GUI.TextBox>(thing, out UIControl boxUi);
            if (box != null)
            {
                if (boxUi != null && !boxUi.IsActive)
                    boxUi.IsActive = true;
                box.Text = text;
                return;
            }
            WarnOnce($"\u201Cset text of\u201D needs an actor with a Label or TextBox on a UIControl (I couldn't find one called \u201C{thing}\u201D). Labels in this scene: {ControlActorNames<FlaxEngine.GUI.Label>()}. Text boxes in this scene: {ControlActorNames<FlaxEngine.GUI.TextBox>()}.");
        }

        /// <summary><c>focus &lt;thing&gt;</c> / <c>unfocus &lt;thing&gt;</c>: moves keyboard focus
        /// to the GUI control on a UIControl actor (a TextBox, Button, Slider, ...).</summary>
        public static void SetControlFocus(string thing, bool focused)
        {
            if (string.IsNullOrEmpty(thing))
            {
                WarnOnce($"\u201C{(focused ? "focus" : "unfocus")}\u201D needs the name of the control — like \u201C{(focused ? "focus NameBox" : "unfocus NameBox")}\u201D.");
                return;
            }
            var control = FindControl<FlaxEngine.GUI.Control>(thing, out UIControl ui);
            if (control == null)
            {
                WarnOnce($"\u201C{(focused ? "focus" : "unfocus")} {thing}\u201D needs an actor with a UI control on a UIControl (I couldn't find one called \u201C{thing}\u201D).");
                return;
            }
            if (ui != null && !ui.IsActive)
                ui.IsActive = true;
            if (focused)
                control.Focus();
            else
                control.Defocus();
        }

        /// <summary><c>select all &lt;thing&gt;</c> on a TextBox highlights its whole text.
        /// Returns false when <paramref name="thing"/> isn't a text box, so the RTS
        /// "select all units" meaning can take over.</summary>
        public static bool SelectAllText(string thing)
        {
            var box = FindControl<FlaxEngine.GUI.TextBox>(thing, out _);
            if (box == null)
                return false;
            box.SelectAll();
            return true;
        }

        /// <summary><c>set button &lt;thing&gt; enabled/disabled</c>: flips <see cref="FlaxEngine.GUI.Control.Enabled"/>.</summary>
        public static void SetButtonEnabled(string thing, bool enabled)
        {
            if (string.IsNullOrEmpty(thing))
            {
                WarnOnce("\u201Cset button\u201D needs the name of the button — like \u201Cset button RestartButton enabled\u201D.");
                return;
            }
            if (!SetControl<FlaxEngine.GUI.Button>(thing, button => button.Enabled = enabled))
                WarnOnce($"\u201Cset button {thing} { (enabled ? "enabled" : "disabled") }\u201D needs an actor with a Button on a UIControl (I couldn't find one called \u201C{thing}\u201D). Buttons in this scene: {ControlActorNames<FlaxEngine.GUI.Button>()}.");
        }

        /// <summary><c>set button &lt;thing&gt; text to &lt;value&gt;</c>: a Button is a Label, so this reuses SetText.</summary>
        public static void SetButtonText(string thing, string text) => SetText(thing, text);

        /// <summary><c>set image of &lt;thing&gt; to &lt;sprite&gt;</c>: swaps the Image control's brush to the named sprite.</summary>
        public static void SetImageBrush(string thing, string sprite)
        {
            if (string.IsNullOrEmpty(thing) || string.IsNullOrEmpty(sprite))
            {
                WarnOnce("\u201Cset image of\u201D needs a thing and a sprite name — like \u201Cset image of HeartIcon to \"heart_full\"\u201D.");
                return;
            }
            var image = FindControl<FlaxEngine.GUI.Image>(thing, out UIControl ui);
            if (image == null)
            {
                WarnOnce($"\u201Cset image of {thing} to \"{sprite}\"\u201D needs an actor with an Image on a UIControl (I couldn't find one called \u201C{thing}\u201D). Images in this scene: {ControlActorNames<FlaxEngine.GUI.Image>()}.");
                return;
            }
            var texture = LoadTexture(sprite);
            if (texture == null)
            {
                WarnOnce($"I couldn't find a sprite texture named \u201C{sprite}\u201D for \u201Cset image of {thing}\u201D. Imported textures: {AvailableTextureNames()}.");
                return;
            }
            image.Brush = new FlaxEngine.GUI.TextureBrush { Texture = texture };
        }

        /// <summary><c>set color of &lt;thing&gt; to &lt;color&gt;</c>: tints a control ("r g b" or "r g b a", 0-1).</summary>
        public static void SetControlColor(string thing, string color)
        {
            if (string.IsNullOrEmpty(thing) || string.IsNullOrEmpty(color))
            {
                WarnOnce("\u201Cset color of\u201D needs a thing and a color — like \u201Cset color of HeartIcon to \"1 0 0\"\u201D.");
                return;
            }
            if (!TryParseColor(color, out var c))
            {
                WarnOnce($"I couldn't parse \u201C{color}\u201D as a control color — use \u201C\"r g b\"\u201D or \u201C\"r g b a\"\u201D with 0-1 floats.");
                return;
            }
            if (!SetControl<FlaxEngine.GUI.Image>(thing, control => control.Color = c))
                WarnOnce($"\u201Cset color of {thing}\u201D needs an actor with an Image on a UIControl (I couldn't find one called \u201C{thing}\u201D). Images in this scene: {ControlActorNames<FlaxEngine.GUI.Image>()}.");
        }

        /// <summary><c>set progress of &lt;thing&gt; to &lt;value&gt;</c>: fills a ProgressBar.
        /// Accepts 0-1 (e.g. 0.5) or 0-100 (e.g. 50) and normalizes to the bar's 0-1 range.</summary>
        public static void SetProgress(string thing, double value)
        {
            if (string.IsNullOrEmpty(thing))
            {
                WarnOnce("\u201Cset progress of\u201D needs the name of the progress bar — like \u201Cset progress of HealthBar to 0.5\u201D.");
                return;
            }
            double progress = value > 1 ? value / 100.0 : value;
            if (progress < 0) progress = 0;
            if (progress > 1) progress = 1;
            if (!SetControl<FlaxEngine.GUI.ProgressBar>(thing, bar =>
            {
                bar.Minimum = 0;
                bar.Maximum = 1;
                bar.Value = (float)progress;
            }))
                WarnOnce($"\u201Cset progress of {thing}\u201D needs an actor with a ProgressBar on a UIControl (I couldn't find one called \u201C{thing}\u201D). Progress bars in this scene: {ControlActorNames<FlaxEngine.GUI.ProgressBar>()}.");
        }

        /// <summary><c>set slider &lt;thing&gt; to &lt;value&gt;</c>: moves a Slider.
        /// Accepts 0-1 (e.g. 0.5) or 0-100 (e.g. 50) and normalizes to the slider's 0-100 range.</summary>
        public static void SetSlider(string thing, double value)
        {
            if (string.IsNullOrEmpty(thing))
            {
                WarnOnce("\u201Cset slider\u201D needs the name of the slider — like \u201Cset slider HealthBar to 50\u201D.");
                return;
            }
            double sliderValue = value <= 1 ? value * 100.0 : value;
            if (sliderValue < 0) sliderValue = 0;
            if (sliderValue > 100) sliderValue = 100;
            if (!SetControl<FlaxEngine.GUI.Slider>(thing, slider =>
            {
                slider.Minimum = 0;
                slider.Maximum = 100;
                slider.Value = (float)sliderValue;
            }))
                WarnOnce($"\u201Cset slider {thing}\u201D needs an actor with a Slider on a UIControl (I couldn't find one called \u201C{thing}\u201D). Sliders in this scene: {ControlActorNames<FlaxEngine.GUI.Slider>()}.");
        }

        /// <summary>Loads a texture by name, ignoring folder and extension (mirrors <see cref="LoadAudioClip"/>).</summary>
        private static FlaxEngine.Texture LoadTexture(string name)
        {
            var index = TextureIndex();
            return index.TryGetValue(AssetNames.Key(name), out Guid id) ? Content.Load<FlaxEngine.Texture>(id) : null;
        }

        private static Dictionary<string, Guid> TextureIndex()
        {
            var index = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (Guid id in Content.GetAllAssetsByType(typeof(FlaxEngine.Texture)))
            {
                if (!Content.GetAssetInfo(id, out AssetInfo info))
                    continue;
                string key = AssetNames.Key(info.Path);
                if (key.Length == 0 || index.ContainsKey(key))
                    continue;
                index[key] = id;
            }
            return index;
        }

        private static string AvailableTextureNames()
        {
            var names = new List<string>(TextureIndex().Keys);
            if (names.Count == 0)
                return "(none)";
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return string.Join(", ", names);
        }

        /// <summary>Replaces every loaded scene with one scene. Kept synchronous on purpose:
        /// <c>Level.ChangeSceneAsync</c> is background work, which would move `when scene
        /// starts` (and the actor sheets it triggers) to a later frame.</summary>
        private static void ReplaceScene(Guid sceneId)
        {
            // Unload all currently loaded scenes so the new one replaces them
            foreach (var scene in Level.Scenes)
            {
                if (scene == null)
                    continue;
                if (!Level.UnloadScene(scene.ID))
                    Debug.LogWarning($"I couldn't unload the scene \u201C{scene.Name}\u201D while switching scenes.");
            }
            Level.LoadScene(sceneId);
        }

        /// <summary>Resolves a scene name to its asset id, ignoring folder, extension and case:
        /// loaded scenes first, then the imported content database, then raw <c>.scene</c> files.
        /// Every scene word shares it, so they all resolve and report names the same way.</summary>
        private static bool TryFindSceneId(string name, out Guid id)
        {
            id = Guid.Empty;
            if (string.IsNullOrEmpty(name))
                return false;

            // 1. Already loaded (in memory).
            foreach (var scene in Level.Scenes)
                if (scene != null && scene.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    id = scene.ID;
                    return true;
                }

            // 2. Imported content database.
            foreach (var assetId in Content.GetAllAssetsByType(typeof(Scene)))
            {
                if (!Content.GetAssetInfo(assetId, out AssetInfo info))
                    continue;
                if (System.IO.Path.GetFileNameWithoutExtension(info.Path).Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    id = assetId;
                    return true;
                }
            }

            // 3. Raw .scene files the content database hasn't indexed.
            string contentDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Content");
            if (System.IO.Directory.Exists(contentDir))
            {
                foreach (var file in System.IO.Directory.GetFiles(contentDir, "*.scene", System.IO.SearchOption.AllDirectories))
                {
                    if (!System.IO.Path.GetFileNameWithoutExtension(file).Equals(name, StringComparison.OrdinalIgnoreCase))
                        continue;
                    string contentPath = "Content/" + file.Substring(contentDir.Length + 1).Replace('\\', '/');
                    var sceneAsset = Content.Load<SceneAsset>(contentPath, 0);
                    if (sceneAsset != null)
                    {
                        id = sceneAsset.ID;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// <c>next level</c> (Task 16 Part G): loads the scene the level order named. The
        /// scene replacement path is the same one `load scene` uses.
        /// </summary>
        public static bool LoadLevel(string name)
        {
            return LoadSceneByName(name);
        }

        /// <summary>
        /// The scene names that make up the level order `next level` walks. Flax 1.13 has no
        /// runtime access to the editor's build list, so this is every scene the content
        /// database knows (plus raw <c>.scene</c> files), de-duplicated and sorted — a stable
        /// order, and the loaded scene is always in it.
        /// </summary>
        public static List<string> KnownSceneNames()
        {
            var names = new List<string>();
            foreach (var scene in Level.Scenes)
                if (scene != null && !names.Contains(scene.Name))
                    names.Add(scene.Name);
            foreach (var id in Content.GetAllAssetsByType(typeof(Scene)))
            {
                if (!Content.GetAssetInfo(id, out AssetInfo info))
                    continue;
                string name = System.IO.Path.GetFileNameWithoutExtension(info.Path);
                if (!names.Contains(name))
                    names.Add(name);
            }
            string contentDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Content");
            if (System.IO.Directory.Exists(contentDir))
            {
                foreach (var file in System.IO.Directory.GetFiles(contentDir, "*.scene", System.IO.SearchOption.AllDirectories))
                {
                    string name = System.IO.Path.GetFileNameWithoutExtension(file);
                    if (!names.Contains(name))
                        names.Add(name);
                }
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        private static bool LoadSceneByName(string name)
        {
            if (!TryFindSceneId(name, out Guid id))
            {
                WarnOnce($"I couldn't find a scene named \u201C{name}\u201D. Scenes: {SceneNames()}.");
                return false;
            }
            ReplaceScene(id);
            return true;
        }

        /// <summary><c>add scene "HUD"</c> — loads a scene <b>on top of</b> the current one and
        /// leaves everything already in the level where it is. The overlay word (`load scene`
        /// is the replace one).</summary>
        private static void AddScene(EventContext context, ActionSpec spec)
        {
            if (!TryFindSceneId(spec.Thing, out Guid id))
            {
                WarnOnce($"I couldn't find a scene named \u201C{spec.Thing}\u201D. Scenes: {SceneNames()}.");
                return;
            }
            if (SceneLoadedAs(spec.Thing, id) != null)
            {
                WarnOnce($"The scene \u201C{spec.Thing}\u201D is already loaded \u2014 unload scene \u201C{spec.Thing}\u201D first to add a fresh copy.");
                return;
            }
            Scene loaded = LoadSceneAdditive(id);
            if (loaded == null)
            {
                WarnOnce($"I loaded \u201C{spec.Thing}\u201D but couldn't find the new scene, so its events won't run.");
                return;
            }
            FinishLoadedScene(context, loaded, spec.Thing, sceneStarts: true);
        }

        /// <summary><c>unload scene "HUD"</c> — removes one loaded scene (close an overlay).
        /// Never unloads the last scene: that would leave an empty world.</summary>
        private static void UnloadScene(EventContext context, ActionSpec spec)
        {
            Debug.LogWarning($"Event sheet: unloading “{spec.Thing}” — currently loaded: {LoadedSceneNames()}.");
            // A scene's internal root name can differ from its file name (a copied scene keeps
            // the old root name), so resolve the asset id too and match either.
            TryFindSceneId(spec.Thing, out Guid targetId);
            Scene target = SceneLoadedAs(spec.Thing, targetId);
            if (target == null)
            {
                WarnOnce($"I couldn't find a loaded scene named \u201C{spec.Thing}\u201D to unload. Loaded scenes: {LoadedSceneNames()}.");
                return;
            }
            if (Level.Scenes.Length <= 1)
            {
                WarnOnce($"\u201C{spec.Thing}\u201D is the only loaded scene \u2014 unloading it would leave nothing to play. Use load scene to switch instead.");
                return;
            }
            if (!Level.UnloadScene(target))
            {
                WarnOnce($"I couldn't unload the scene \u201C{spec.Thing}\u201D.");
                return;
            }
            Debug.LogWarning($"Event sheet: unloaded scene \u201C{target.Name}\u201D. Still loaded: {LoadedSceneNames()}.");
        }

        /// <summary>The loaded scene matching a name (or an asset id when one is given), or null.</summary>
        private static Scene SceneLoadedAs(string name, Guid id)
        {
            string wanted = System.IO.Path.GetFileNameWithoutExtension(name ?? "");
            foreach (var scene in Level.Scenes)
            {
                if (scene == null)
                    continue;
                if (id != Guid.Empty && scene.ID == id)
                    return scene;
                if (scene.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    scene.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                    return scene;
            }
            return null;
        }

        /// <summary>Loads a scene on top of the loaded ones and returns the new scene, matched by
        /// diffing the loaded set (falling back to the asset id).</summary>
        private static Scene LoadSceneAdditive(Guid id)
        {
            var before = new HashSet<Guid>();
            foreach (var scene in Level.Scenes)
                if (scene != null)
                    before.Add(scene.ID);
            Level.LoadScene(id);   // additive: keeps every loaded scene
            foreach (var scene in Level.Scenes)
                if (scene != null && !before.Contains(scene.ID))
                    return scene;
            return Level.FindScene(id);
        }

        /// <summary>Gives a freshly loaded scene's actors their event sheets and their
        /// `my spawnedFrom` marker, and (when asked) pushes `when scene starts` to just those
        /// sheets — the manager's own scene-start pass already ran for the old scenes.</summary>
        private static void FinishLoadedScene(EventContext context, Scene scene, string source, bool sceneStarts)
        {
            var actors = new List<object>();
            foreach (Actor child in scene.Children)
            {
                MarkSpawnedTree(context.Runtime, child, source);
                EventsManager.Instance?.AttachSpawnedTree(child);
                CollectTree(child, actors);
            }
            if (sceneStarts)
                context.Runtime.SceneStartedFor(actors);
        }

        private static void CollectTree(Actor actor, List<object> actors)
        {
            if (actor == null)
                return;
            actors.Add(actor);
            foreach (Actor child in actor.Children)
                CollectTree(child, actors);
        }

        private static string LoadedSceneNames()
        {
            var names = new List<string>();
            foreach (var scene in Level.Scenes)
                if (scene != null)
                    names.Add(scene.Name);
            return names.Count == 0 ? "(none)" : string.Join(", ", names);
        }

        private static string SceneNames()
        {
            var names = new List<string>();
            foreach (var scene in Level.Scenes)
                if (scene != null)
                    names.Add(scene.Name);
            foreach (var id in Content.GetAllAssetsByType(typeof(Scene)))
                if (Content.GetAssetInfo(id, out AssetInfo info))
                    names.Add(System.IO.Path.GetFileNameWithoutExtension(info.Path));
            return names.Count == 0 ? "(none)" : string.Join(", ", names);
        }

        /// <summary>Play an animation once by name on the actor's <see cref="FlaxEngine.AnimatedModel"/>.</summary>
        private static void PlayAnim(EventContext context, ActionSpec spec) => StartAnim(context, spec, false);

        /// <summary>Loop an animation by name on the actor's <see cref="FlaxEngine.AnimatedModel"/>.</summary>
        private static void LoopAnim(EventContext context, ActionSpec spec) => StartAnim(context, spec, true);

        /// <summary>
        /// Starts a clip on the actor's model. Flax 1.13 has no PlayState, so a virtual Anim
        /// Graph holding exactly this clip is created and assigned (the BGE "Action" idea).
        /// </summary>
        private static void StartAnim(EventContext context, ActionSpec spec, bool loop)
        {
            string word = loop ? "loop anim" : "play anim";
            var me = context.Me as Actor;
            if (me == null)
            {
                WarnOnce($"\u201C{word} \u201C{spec.Thing}\u201D\u201D needs to run on an actor's sheet — the global Game sheet has no model to animate.");
                return;
            }
            var model = ModelOf(me);
            if (model == null)
            {
                WarnOnce($"\u201C{word} \u201C{spec.Thing}\u201D\u201D needs an AnimatedModel on \u201C{me.Name}\u201D.");
                return;
            }
            if (model.SkinnedModel == null)
            {
                WarnOnce($"\u201C{word} \u201C{spec.Thing}\u201D\u201D needs a SkinnedModel assigned on \u201C{me.Name}\u201D.");
                return;
            }

            Animation anim = LoadAnimation(spec.Thing);
            if (anim == null)
            {
                WarnOnce($"I don't know the anim \u201C{spec.Thing}\u201D. This project has: {AvailableAnimNames()}.");
                return;
            }

            string clip = AssetNames.Key(spec.Thing);
            AnimStates.TryGetValue(me, out AnimState state);

            bool sameClip = state != null && state.Clip == clip;
            bool stillPlaying = state != null && (!state.OneShot || Time.GameTime < state.EndTime);
            if (sameClip && stillPlaying)
                return; // held triggers call this every tick — never restart mid-clip

            var graph = Content.CreateVirtualAsset<AnimationGraph>();
            if (graph == null)
            {
                WarnOnce($"Could not create an animation graph for \u201C{spec.Thing}\u201D on \u201C{me.Name}\u201D.");
                return;
            }
            if (graph.InitAsAnimation(model.SkinnedModel, anim, loop, false))
            {
                // Docs: true means "failed".
                FlaxEngine.Object.Destroy(graph);
                WarnOnce($"Could not play \u201C{spec.Thing}\u201D on \u201C{me.Name}\u201D — the clip and the skinned model may use different skeletons.");
                return;
            }

            model.AnimationGraph = graph;
            if (state?.PreviousGraph != null)
                FlaxEngine.Object.Destroy(state.PreviousGraph); // keep only two graphs alive

            AnimStates[me] = new AnimState
            {
                Clip = clip,
                Name = spec.Thing.Trim(),
                Loop = loop,
                OneShot = !loop,
                EndTime = loop ? double.MaxValue : Time.GameTime + anim.Length,
                Graph = graph,
                PreviousGraph = state?.Graph,
                PreviousClip = state?.Clip,
                PreviousName = state?.Name,
                PreviousLoop = state?.Loop ?? true,
            };
        }

        /// <summary>Stop the current animation and restore the previous one.</summary>
        private static void StopAnim(EventContext context, ActionSpec spec)
        {
            var me = context.Me as Actor;
            if (me == null)
            {
                WarnOnce("\u201Cstop anim\u201D needs to run on an actor's sheet — the global Game sheet has no model to animate.");
                return;
            }
            var model = ModelOf(me);
            if (model == null)
                return;

            AnimStates.TryGetValue(me, out AnimState state);
            if (state == null)
            {
                model.AnimationGraph = null; // nothing tracked: go to bind pose
                return;
            }

            if (state.PreviousGraph != null)
            {
                model.AnimationGraph = state.PreviousGraph;
                if (state.Graph != null)
                    FlaxEngine.Object.Destroy(state.Graph);
                AnimStates[me] = new AnimState
                {
                    Clip = state.PreviousClip ?? "",
                    Name = state.PreviousName ?? "",
                    Loop = state.PreviousLoop,
                    OneShot = false,
                    EndTime = double.MaxValue,
                    Graph = state.PreviousGraph,
                };
                return;
            }

            model.AnimationGraph = null;
            if (state.Graph != null)
                FlaxEngine.Object.Destroy(state.Graph);
            AnimStates.Remove(me);
        }

        /// <summary>
        /// <c>camera follows &lt;thing&gt;</c>: remember the current camera offset and keep it
        /// while the target moves, so whatever framing the scene author chose is preserved.
        /// </summary>
        private static void StartCameraFollow(EventContext context, ActionSpec spec)
        {
            var target = ResolveThing(context, spec.Thing) as Actor;
            if (target == null)
            {
                WarnOnce($"I couldn't find \u201C{spec.Thing}\u201D for the camera to follow.");
                return;
            }
            Actor camera = FlaxWorld.FindCamera();
            if (camera == null)
            {
                WarnOnce("\u201Ccamera follows\u201D needs a Camera actor in the scene.");
                return;
            }

            CameraFollowTarget = target;
            // Use the default top-down framing (CameraHeight/CameraDepth globals) so
            // "camera follows Player" always returns to the normal player camera framing,
            // regardless of where the camera was snapped to before (e.g. by camera at).
            float camHeight = (float)context.Runtime.Variables.GetGlobal("CameraHeight").AsNumber();
            float camDepth = (float)context.Runtime.Variables.GetGlobal("CameraDepth").AsNumber();
            CameraFollowOffset = new Vector3(0f, camHeight, camDepth);
            CameraLookAtTarget = null; // following and aiming don't fight each tick
            CameraAtTarget = null; // following and snapping don't fight each tick
            // Reset the tilt from the CameraPitch global (45° = the oblique framing older
            // scenes use, 90° = straight down for TK4), so the camera isn't left rotated
            // from a previous "camera looks at".
            float camPitch = (float)CameraFraming.ClampPitch(
                context.Runtime.Variables.GetGlobal("CameraPitch").AsNumber());
            Actor followCamera = FlaxWorld.FindCamera();
            if (followCamera != null)
                followCamera.Orientation = Quaternion.Euler(camPitch, 0.0f, 0.0f);
            context.Runtime.Variables.SetGlobal("CameraFollow", ValueBox.OfNumber(0));
        }

        /// <summary>Release the camera from any explicit follow target (camera free).</summary>
        private static void StopCameraFollow()
        {
            CameraFollowTarget = null;
            CameraLookAtTarget = null; // free the camera entirely
            CameraAtTarget = null; // free the camera entirely
            CameraAtOffset = Vector3.Zero;
        }

        /// <summary>
        /// <c>camera looks at &lt;thing&gt;</c>: aim the scene camera at the target each tick,
        /// and release any explicit follow so the aim isn't overwritten.
        /// </summary>
        private static void StartCameraLookAt(EventContext context, ActionSpec spec)
        {
            var target = ResolveThing(context, spec.Thing) as Actor;
            if (target == null)
            {
                WarnOnce($"I couldn't find \u201C{spec.Thing}\u201D for the camera to look at.");
                return;
            }
            CameraLookAtTarget = target;
            // Aiming elsewhere releases the follow — the two must not fight each tick.
            CameraFollowTarget = null;
            Actor lookCamera = FlaxWorld.FindCamera();
            if (lookCamera != null)
                LookAtNoRoll(lookCamera, target.Position);
        }

        /// <summary>
        /// <c>camera at &lt;thing&gt;</c>: snap the camera to the target's position each tick
        /// (offset 0), giving authored framings from dedicated camera actors like CameraDoor.
        /// Combines with <c>camera looks at</c> — position and aim are separate.
        /// </summary>
        private static void StartCameraAt(EventContext context, ActionSpec spec)
        {
            var target = ResolveThing(context, spec.Thing) as Actor;
            if (target == null)
            {
                WarnOnce($"I couldn't find \u201C{spec.Thing}\u201D for the camera to snap to.");
                return;
            }
            Actor camera = FlaxWorld.FindCamera();
            if (camera == null)
            {
                WarnOnce("\u201Ccamera at\u201D needs a Camera actor in the scene.");
                return;
            }
            CameraAtTarget = target;
            // Snapping elsewhere releases the follow — position sources must not fight.
            CameraFollowTarget = null;
            // Capture the default top-down framing so "camera at" keeps consistent zoom
            // with "camera follows Player" instead of snapping to exact target position.
            float camHeight = (float)context.Runtime.Variables.GetGlobal("CameraHeight").AsNumber();
            float camDepth = (float)context.Runtime.Variables.GetGlobal("CameraDepth").AsNumber();
            CameraAtOffset = new Vector3(0f, camHeight, camDepth);
            // Respect CameraPitch here too, so a snapped shot matches the follow framing.
            float camPitch = (float)CameraFraming.ClampPitch(
                context.Runtime.Variables.GetGlobal("CameraPitch").AsNumber());
            camera.Orientation = Quaternion.Euler(camPitch, 0.0f, 0.0f);
        }

        /// <summary>
        /// <c>camera switch &lt;thing&gt;</c>: replace the active camera — enable the named
        /// camera actor and disable every other camera in all scenes. Use authored Camera
        /// actors (e.g. CameraDoor, CameraChest, PlayerCamera) for per-shot framing.
        /// Releases any explicit follow/at/look so the switched camera is fully in control.
        /// </summary>
        private static void StartCameraSwitch(EventContext context, ActionSpec spec)
        {
            // Find the camera actor by name. Try sheet-bound resolution first, then fall
            // back to a plain Level.FindActor so cameras without EventSheets still work.
            var target = ResolveThing(context, spec.Thing) as FlaxEngine.Camera;
            if (target == null)
            {
                target = Level.FindActor(spec.Thing) as FlaxEngine.Camera;
            }
            if (target == null)
            {
                WarnOnce($"I couldn't find a Camera actor named \u201C{spec.Thing}\u201D.");
                return;
            }
            // Walk every actor in every scene, enabling only the chosen camera.
            foreach (var scene in Level.Scenes)
            {
                if (scene == null)
                    continue;
                DisableCamerasIn(scene, target);
            }
            // The switched camera is now in control — release all explicit camera targets.
            CameraFollowTarget = null;
            CameraLookAtTarget = null;
            CameraAtTarget = null;
            CameraAtOffset = Vector3.Zero;
        }

        /// <summary>
        /// Recursively walks the scene tree, enabling only <paramref name="keepEnabled"/>
        /// among all <see cref="FlaxEngine.Camera"/> actors and disabling the rest.</summary>
        private static void DisableCamerasIn(Actor node, FlaxEngine.Camera keepEnabled)
        {
            foreach (Actor child in node.Children)
            {
                if (child == null)
                    continue;
                var camera = child as FlaxEngine.Camera;
                if (camera != null)
                    camera.IsActive = ReferenceEquals(camera, keepEnabled);
                DisableCamerasIn(child, keepEnabled);
            }
        }

        /// <summary>
        /// Aim the camera at a world position without introducing roll (top-down friendly).
        /// Builds an orthonormal basis from the view direction and world up, then converts
        /// to a quaternion — this keeps the camera level (zero roll) while yawing towards
        /// the target, unlike <c>camera.LookAt</c> which can tilt the camera sideways.
        /// </summary>
        private static void LookAtNoRoll(Actor camera, Vector3 targetPosition)
        {
            Vector3 forward = targetPosition - camera.Position;
            forward.Y = 0;
            if (forward.LengthSquared <= 0.0001f)
                return;
            forward.Normalize();

            Vector3 worldUp = Vector3.Up;
            Vector3 right = Vector3.Cross(forward, worldUp);
            if (right.LengthSquared <= 0.0001f)
                return;
            right.Normalize();
            Vector3 up = Vector3.Cross(right, forward);

            // Rotation matrix columns: right, up, forward
            float m00 = right.X, m01 = up.X, m02 = forward.X;
            float m10 = right.Y, m11 = up.Y, m12 = forward.Y;
            float m20 = right.Z, m21 = up.Z, m22 = forward.Z;

            float trace = m00 + m11 + m22;
            float x, y, z, w;

            if (trace > 0.0f)
            {
                float s = (float)System.Math.Sqrt(trace + 1.0f) * 2.0f;
                w = 0.25f * s;
                x = (m21 - m12) / s;
                y = (m02 - m20) / s;
                z = (m10 - m01) / s;
            }
            else if ((m00 > m11) && (m00 > m22))
            {
                float s = (float)System.Math.Sqrt(1.0f + m00 - m11 - m22) * 2.0f;
                w = (m21 - m12) / s;
                x = 0.25f * s;
                y = (m01 + m10) / s;
                z = (m02 + m20) / s;
            }
            else if (m11 > m22)
            {
                float s = (float)System.Math.Sqrt(1.0f + m11 - m00 - m22) * 2.0f;
                w = (m02 - m20) / s;
                x = (m01 + m10) / s;
                y = 0.25f * s;
                z = (m12 + m21) / s;
            }
            else
            {
                float s = (float)System.Math.Sqrt(1.0f + m22 - m00 - m11) * 2.0f;
                w = (m10 - m01) / s;
                x = (m02 + m20) / s;
                y = (m12 + m21) / s;
                z = 0.25f * s;
            }

            camera.Orientation = new Quaternion(x, y, z, w);
        }

        /// <summary>
        /// The model to animate: the sheet may sit on the AnimatedModel itself (FindActor only
        /// looks at children), or on a parent actor with the model somewhere underneath.
        /// </summary>
        private static FlaxEngine.AnimatedModel ModelOf(Actor actor)
        {
            if (actor == null)
                return null;
            return actor as FlaxEngine.AnimatedModel ?? actor.FindActor<FlaxEngine.AnimatedModel>();
        }

        /// <summary>The ParticleEffect on this actor, or on one of its children (the recommended
        /// setup: the effect sits under the actor the EventSheet is on).</summary>
        private static ParticleEffect FindParticleEffect(Actor actor)
        {
            if (actor == null)
                return null;
            return actor as ParticleEffect ?? actor.FindActor<ParticleEffect>();
        }

        public static void ClearActor(object actor)
        {
            if (actor == null) return;
            if (actor is Actor body)
                CharacterFallSpeeds.Remove(body);
            if (AnimStates.TryGetValue(actor, out AnimState state))
            {
                if (state.Graph != null)
                    FlaxEngine.Object.Destroy(state.Graph);
                if (state.PreviousGraph != null)
                    FlaxEngine.Object.Destroy(state.PreviousGraph);
                AnimStates.Remove(actor);
            }
            ParticleStates.Remove(actor);
        }

        /// <summary>Animation asset key/alias to asset id, built from the registry once per play session.</summary>
        private static Dictionary<string, Guid> AnimIndex()
        {
            if (AnimIndexCache != null)
                return AnimIndexCache;

            var index = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            var full = new List<string>();
            foreach (Guid id in Content.GetAllAssetsByType(typeof(Animation)))
            {
                if (!Content.GetAssetInfo(id, out AssetInfo info))
                    continue;
                string key = AssetNames.Key(info.Path);
                if (key.Length == 0)
                    continue;
                if (!full.Contains(key))
                    full.Add(key);
                foreach (string alias in AssetNames.Suffixes(key))
                {
                    if (alias.Length == 0 || index.ContainsKey(alias))
                        continue; // first asset wins
                    index[alias] = id;
                }
            }

            AnimIndexCache = index;
            AnimFullNames = full;
            return index;
        }

        /// <summary>Resolves a clip by name, ignoring folder, extension and the character prefix.</summary>
        private static Animation LoadAnimation(string name)
        {
            return AnimIndex().TryGetValue(AssetNames.Key(name), out Guid id)
                ? Content.Load<Animation>(id)
                : null;
        }

        private static string AvailableAnimNames()
        {
            AnimIndex();
            var names = new List<string>(AnimFullNames);
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names.Count == 0 ? "(none)" : string.Join(", ", names);
        }

    }
}
