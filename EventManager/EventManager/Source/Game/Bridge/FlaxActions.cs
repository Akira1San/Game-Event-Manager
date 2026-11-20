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

    /// <summary>An in-progress <c>climb</c>: the ladder it holds, the ladder's span, the pace and
    /// the direction (+1 up, −1 down). Advanced every tick in <see cref="FlaxActions.Step"/>.</summary>
    public class OngoingClimb
    {
        public Actor Ladder;
        public double Speed = PhysicsMovement.DefaultClimbSpeed;
        public int Direction = 1;
        public double Bottom;
        public double Top;
    }

    public static class FlaxActions
    {
    public static float DeltaTime;
    public static readonly List<OngoingMove> Moves = new List<OngoingMove>();
    /// <summary>Sustained pushes (<c>push &lt;thing&gt; &lt;dir&gt; strength &lt;n&gt; for &lt;s&gt;
    /// seconds</c>), applied every tick until their time runs out.</summary>
    public static readonly List<OngoingPush> Pushes = new List<OngoingPush>();
    /// <summary>Actors on a ladder, keyed by climber. A climb owns X/Z and Y while it lasts:
    /// every tick locks the actor onto the ladder's axis and drives its height.</summary>
    public static readonly Dictionary<Actor, OngoingClimb> Climbs = new Dictionary<Actor, OngoingClimb>();
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
        /// <summary>Playback speed per actor (Task 33 Part A). It belongs to the actor, not the
        /// clip, so it survives clip changes; the bridge writes it to
        /// <see cref="AnimatedModel.UpdateSpeed"/>, which Flax applies to the running graph live.</summary>
        private static readonly Dictionary<Actor, float> AnimSpeeds = new Dictionary<Actor, float>();
        /// <summary>Monotonic play token per clip start (Task 33 Part B). `when anim ... reaches ...%`
        /// fires once per token, so every `play anim` / `loop anim` re-arms it — restarting the same
        /// clip, changing speed or letting it loop again does not.</summary>
        private static int AnimPlayCounter;
        /// <summary>Vertical speed per character actor. <c>CharacterController.Move</c> carries no
        /// velocity state, so the event runtime keeps the falling speed between ticks.</summary>
        private static readonly Dictionary<Actor, float> CharacterFallSpeeds = new Dictionary<Actor, float>();
        /// <summary>Characters in the air because of `jump`. The floor is still right under them at
        /// take-off, so the ground probe would say "grounded"; this flag is what makes the jump
        /// read as airborne until it lands, and what refuses a second take-off mid-air.</summary>
        private static readonly HashSet<Actor> JumpingCharacters = new HashSet<Actor>();

        /// <summary>A scripted jump arc for an actor with no body (or a kinematic one): height
        /// above the take-off Y, vertical speed, and where the arc started.</summary>
        private sealed class ScriptedJump
        {
            public double BaseY;
            public double Height;
            public double VelocityY;
        }

        /// <summary>In-flight scripted jump arcs, advanced every tick in <see cref="Step"/>.</summary>
        private static readonly Dictionary<Actor, ScriptedJump> ScriptedJumps = new Dictionary<Actor, ScriptedJump>();
        /// <summary>Sources spawned by <c>play sound</c>, with the actor whose sheet started them.</summary>
        private static readonly List<(Actor Owner, AudioSource Source)> ActiveSounds = new List<(Actor, AudioSource)>();
        /// <summary>The single music player behind `play music` / `loop music` (Task 31). It is
        /// recreated after a scene change, because a Flax scene unload takes its actors with it.</summary>
        private static AudioSource MusicSource;
        /// <summary>True once the current music source was seen playing: a track that then stops
        /// has ended (rather than never having started), so `when music ends` fires exactly once.</summary>
        private static bool MusicWasPlaying;
        /// <summary>The music level this bridge last wrote to the `MusicVolume` global, so a sheet
        /// (or a settings slider) writing the global directly is noticed as a change.</summary>
        private static double MusicGlobalSeen = -1;

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

        /// <summary>Ground height each flying actor's <c>my Altitude</c> is measured from
        /// (Task 28 Part A). An actor that writes the altitude back to 0 lands on this Y and
        /// leaves the table, so ground actors never appear here.</summary>
        private static readonly Dictionary<Actor, float> AltitudeGrounds = new Dictionary<Actor, float>();

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
            public double StartTime;     // when the current clip started, for re-timing at a new speed
            public double Length;        // clip length in seconds (0 for a loop, which has no end)
            public int Play;             // play token, used to fire a percent trigger once per play
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
            StopMusicSource();
            MusicWasPlaying = false;
            MusicGlobalSeen = -1;
            LiveTweens.Clear();
            CharacterFallSpeeds.Clear();
            JumpingCharacters.Clear();
            ScriptedJumps.Clear();
            Climbs.Clear();
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
            AnimSpeeds.Clear();
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
            StepAltitude();
            StepAudio(deltaTime);
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
                    if (!IsFlyingActor(move.Subject) && TerrainBlocksStep(move.Subject, waypoint))
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
                            FaceHeading(move.Subject, delta);
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
                            BeginOrderMove(orderRuntime, actor, next,
                                OrderManager.MoveSpeedOf(orderRuntime.Variables, actor));
                    }
                }
            }

            // Ladders: hold the climber on the ladder's axis and drive its height, clamped to the
            // ladder's span. Reaching either end ends the climb so the sheet can step off.
            if (Climbs.Count > 0)
            {
                var climbs = new List<KeyValuePair<Actor, OngoingClimb>>(Climbs);
                foreach (var pair in climbs)
                {
                    Actor climber = pair.Key;
                    OngoingClimb climb = pair.Value;
                    if (climber == null || climber.Scene == null || climb.Ladder == null || climb.Ladder.Scene == null)
                    {
                        StopClimb(climber);
                        continue;
                    }
                    double height = PhysicsMovement.ClimbStep(
                        climber.Position.Y, climb.Bottom, climb.Top, climb.Speed, climb.Direction, DeltaTime);
                    Vector3 ladderSpot = climb.Ladder.Position;
                    Vector3 target = new Vector3(ladderSpot.X, (float)height, ladderSpot.Z);
                    CharacterController character = EventSheet.FindCharacterController(climber);
                    if (character != null && IsAncestorOrSelf(character, climber))
                        character.Move(target - climber.Position);
                    else
                        climber.Position = target;
                    MovedThisTick.Add(climber);
                    if (PhysicsMovement.ClimbFinished(height, climb.Bottom, climb.Top, climb.Direction))
                        StopClimb(climber);
                }
            }

            // Scripted jumps: an actor with no physics body (or a kinematic one) rises and falls
            // along its own little arc, so `jump` works in the plain transform mode too.
            if (ScriptedJumps.Count > 0)
            {
                var jumps = new List<KeyValuePair<Actor, ScriptedJump>>(ScriptedJumps);
                foreach (var pair in jumps)
                {
                    Actor jumper = pair.Key;
                    ScriptedJump jump = pair.Value;
                    if (jumper == null || jumper.Scene == null)
                    {
                        ScriptedJumps.Remove(jumper);
                        continue;
                    }
                    PhysicsMovement.JumpStep(jump.Height, jump.VelocityY, Math.Abs(Physics.Gravity.Y), DeltaTime,
                        out double height, out double velocity);
                    jump.Height = height;
                    jump.VelocityY = velocity;
                    jumper.Position = new Vector3(jumper.Position.X, (float)(jump.BaseY + height), jumper.Position.Z);
                    MovedThisTick.Add(jumper);
                    if (PhysicsMovement.JumpLanded(jump.Height, jump.VelocityY))
                        ScriptedJumps.Remove(jumper);
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
            // `selected units ...` / `selected buildings ...` / `selected ...` (Task 29 Part D):
            // the order runs once per selected actor, with `me` bound to that actor, so a single
            // command-card button commands the whole selection. A card button on one unit's sheet
            // and a card button on the shared Game sheet therefore behave the same.
            if (spec.HasSelectionScope)
            {
                ExecuteOnSelection(context, spec);
                return;
            }
            switch (spec.Verb)
            {
                case ActionVerb.Jump:
                    Jump(context, spec);
                    break;

                case ActionVerb.Climb:
                    StartClimb(context, spec);
                    break;

                case ActionVerb.StopClimbing:
                    StopClimb(context.Me as Actor);
                    break;

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
                            double speed = ResolveNumber(context, spec);
                            Vector3 heading;
                            if (spec.AroundWalls)
                            {
                                // `move toward <thing> ... around walls` (Task 7 G2): steer around
                                // what blocks the chase. One probe while clear; a short fan or a
                                // wall tangent only when it is blocked.
                                heading = SteerAroundWalls(context, me, target, speed);
                            }
                            else
                            {
                                // A plain chase ends any wall negotiation from an earlier line.
                                context.Runtime.Steering.Clear(me);
                                // Ground chase: use the XZ delta only. A player standing higher than
                                // the enemy (a raised platform, another actor's pivot at chest
                                // height) would otherwise tilt the walk upward and lift the body off
                                // the floor instead of closing the horizontal gap.
                                var delta = new Vector3(
                                    target.Position.X - me.Position.X, 0f,
                                    target.Position.Z - me.Position.Z);
                                float length = delta.Length;
                                heading = length > 0.001f ? delta / length : Vector3.Zero;
                            }
                            if (heading != Vector3.Zero)
                            {
                                // Other bodies are not walls for a chase (two enemies used to
                                // wedge); space them with a small sidestep instead.
                                heading = NudgeAroundNeighbours(context, me, target, heading);
                                MoveSubject(me, heading, speed, mode, ignoreCharacters: true);
                                if (spec.FaceForward)
                                    FaceHeading(me, heading);
                            }
                        }
                        else
                        {
                            context.Runtime.Steering.Clear(me);
                        }
                        break;
                    }
                    context.Runtime.Steering.Clear(me);
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
                    // A cancelled chase forgets its committed side, so the next one starts fresh.
                    context.Runtime.Steering.Clear(mover);
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
                case ActionVerb.SaveCheckpoint:
                {
                    var me = context.Me as Actor;
                    if (me == null)
                    {
                        WarnOnce("\u201Csave checkpoint\u201D needs to run on an actor's sheet — the global Game sheet has no body to remember.");
                        break;
                    }
                    // The runtime cannot read a transform, so the pose is taken here and written
                    // as four per-actor variables that a save file already carries (Task 36 F).
                    Quaternion orientation = me.Orientation;
                    var pose = new CheckpointPose
                    {
                        X = me.Position.X,
                        Y = me.Position.Y,
                        Z = me.Position.Z,
                        Yaw = Checkpoints.YawOf(orientation.X, orientation.Y, orientation.Z, orientation.W),
                    };
                    Checkpoints.Save(context.Variables, me, pose);
                    break;
                }
                case ActionVerb.Respawn:
                {
                    var me = context.Me as Actor;
                    if (me == null)
                    {
                        WarnOnce("\u201Crespawn\u201D needs to run on an actor's sheet — the global Game sheet has no body to move.");
                        break;
                    }
                    var at = ResolveThing(context, spec.To) as Actor;
                    if (at == null)
                    {
                        WarnOnce($"I couldn't find \u201C{spec.To}\u201D to respawn at.");
                        break;
                    }
                    // Same warp as `teleport me to`: moves, pushes, fall speed, the jump flags and
                    // any climb are cleared and a dynamic body is stopped. Health, state and
                    // animation stay the sheet's business.
                    TeleportSubject(me, at.Position);
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
                    // A `goto` with no `speed` walks at the unit's own `my Speed` (100 for a sheet
                    // that never set one), so a Marine that wrote `set my Speed to 90` moves at 90
                    // rather than the bare fallback 5; an explicit `speed <n>` still wins.
                    double gotoSpeed = spec.AmountNode == null
                        ? OrderManager.MoveSpeedOf(context.Variables, me)
                        : spec.Number;
                    context.Runtime.Wander.Clear(me);
                    if (spec.Thing == "there")
                    {
                        var clickPos = LastClickPosition;
                        if (!clickPos.HasValue)
                        {
                            WarnOnce("\u201Cgoto there\u201D needs a clicked position first — click somewhere in the scene to set it.");
                            break;
                        }
                        // Selected units take their own formation slot around the click
                        // (Task 23 Part C) instead of all walking to one point.
                        Vector3 clickTarget = FormationSlot(context.Runtime, me, clickPos.Value);
                        // A flyer takes the straight line and never asks the NavMesh (Task 28 Part B).
                        if (IsFlyingActor(me))
                        {
                            RememberDirectMove(me, clickTarget, gotoSpeed);
                            break;
                        }
                        NavRouteAnswer clickAnswer = QueryNavRoute(me.Position, clickTarget,
                            out Vector3[] clickPath);
                        if (NavRoute.Stops(clickAnswer))
                        {
                            // The mesh answered and the way is closed: stop here, and let the
                            // sheet hear `when path blocked` for the walk that did not happen.
                            context.Runtime.Orders.ReportPathBlocked(context.Runtime, me);
                            WarnPathBlocked(me, "the clicked spot");
                            break;
                        }
                        if (NavRoute.WalksStraight(clickAnswer))
                        {
                            // A click just off the baked mesh, a mesh Flax is still baking, or a
                            // level with no mesh at all: nothing is in the way, so walk one
                            // straight waypoint in the same body mode a route would have used.
                            WarnNoNavRoute(me, "the clicked spot");
                            clickPath = DirectRoute(clickTarget);
                        }
                        Remember(new OngoingMove
                        {
                            Subject = me,
                            Goto = true,
                            HasPositionTarget = true,
                            PositionTarget = clickTarget,
                            Path = clickPath,
                            PathIndex = 0,
                            Speed = gotoSpeed,
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
                    // An air-versus-air chase meets at the target's height (Task 28 Part B), and
                    // the route is the straight line rather than a NavMesh path.
                    if (IsFlyingActor(me))
                    {
                        ChaseAltitude(context.Runtime, me, target);
                        RememberDirectMove(me, target.Position, gotoSpeed, spec.FaceForward);
                        break;
                    }
                    NavRouteAnswer chaseAnswer = QueryNavRoute(me.Position, target.Position,
                        out Vector3[] path);
                    if (NavRoute.Stops(chaseAnswer))
                    {
                        // Both ends are on the mesh and there is no way between them: the chase
                        // really stops, and the sheet hears `when path blocked`.
                        context.Runtime.Orders.ReportPathBlocked(context.Runtime, me);
                        WarnPathBlocked(me, $"\u201C{target.Name}\u201D");
                        break;
                    }
                    if (NavRoute.WalksStraight(chaseAnswer))
                    {
                        // Off the navmesh (pushed, or spawned there): walk a tick toward the
                        // closest navigable point so a later `goto` can path again, instead of
                        // freezing the chase forever. With nothing on the mesh to walk to, the
                        // chase carries on along the straight line rather than stopping dead.
                        if (StepOntoNavMesh(context, me, gotoSpeed))
                            break;
                        WarnNoNavRoute(me, $"\u201C{target.Name}\u201D");
                        path = DirectRoute(target.Position);
                    }
                    Remember(new OngoingMove
                    {
                        Subject = me,
                        Target = target,
                        Goto = true,
                        FaceForward = spec.FaceForward,
                        Path = path,
                        PathIndex = 0,
                        Speed = gotoSpeed,
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
                case ActionVerb.Fly:
                    FlyTo(context, spec);
                    break;
                case ActionVerb.LiftOff:
                    LiftOffBuilding(context, spec);
                    break;
                case ActionVerb.Land:
                    LandBuilding(context);
                    break;
                case ActionVerb.Aim:
                    StartAim(context, spec);
                    break;
                case ActionVerb.CancelAim:
                    CancelAim(context);
                    break;
                case ActionVerb.DamageArea:
                    DamageArea(context, spec);
                    break;
                case ActionVerb.AddBuff:
                    BuffCommands.ExecuteAdd(context, spec);
                    break;
                case ActionVerb.RemoveBuff:
                    BuffCommands.ExecuteRemove(context, spec);
                    break;
                case ActionVerb.Spawn:
                    Spawn(context, spec);
                    break;
                case ActionVerb.Cast:
                    CastSpell(context, spec);
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
                case ActionVerb.Research:
                    ResearchUpgrade(context, spec);
                    break;
                case ActionVerb.CancelResearch:
                    CancelResearchUpgrade(context, spec);
                    break;
                case ActionVerb.PutOnCooldown:
                    PutOnCooldown(context, spec);
                    break;
                case ActionVerb.AttackMove:
                    StartAttackMove(context, spec);
                    break;
                case ActionVerb.AttackOrder:
                    StartAttack(context, spec);
                    break;
                case ActionVerb.Gather:
                    StartGather(context, spec);
                    break;
                case ActionVerb.Repair:
                    StartRepair(context, spec);
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
                case ActionVerb.PlayMusic:
                    StartMusic(context, spec, false);
                    break;
                case ActionVerb.LoopMusic:
                    StartMusic(context, spec, true);
                    break;
                case ActionVerb.StopMusic:
                    StopMusicSource();
                    context.Runtime.Audio.StopMusic();
                    break;
                case ActionVerb.FadeMusic:
                    // The bridge advances the ramp each tick, so the music source's volume is
                    // already the mix's level: no coroutine, no engine tween (Task 31 Part C).
                    context.Runtime.Audio.FadeMusicTo(spec.Number, spec.Duration);
                    break;
                case ActionVerb.DuckMusic:
                    context.Runtime.Audio.DuckMusic(spec.Number, spec.Duration);
                    break;
                case ActionVerb.SetMusicVolume:
                {
                    var mix = context.Runtime.Audio;
                    mix.SetMusicVolume(ResolveNumber(context, spec));
                    context.Runtime.Variables.SetGlobal(AudioMix.MusicVariable, ValueBox.OfNumber(mix.Music));
                    MusicGlobalSeen = mix.Music;
                    break;
                }
                case ActionVerb.SetSoundVolume:
                {
                    var mix = context.Runtime.Audio;
                    mix.SetSoundVolume(ResolveNumber(context, spec));
                    context.Runtime.Variables.SetGlobal(AudioMix.SoundVariable, ValueBox.OfNumber(mix.Sound));
                    break;
                }
                case ActionVerb.SetMasterVolume:
                {
                    var mix = context.Runtime.Audio;
                    mix.SetMasterVolume(ResolveNumber(context, spec));
                    context.Runtime.Variables.SetGlobal(AudioMix.MasterVariable, ValueBox.OfNumber(mix.Master));
                    break;
                }
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
                case ActionVerb.SetAnimSpeed:
                    SetAnimSpeed(context, spec);
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
                    FreeCamera(context);
                    break;
                case ActionVerb.ZoomCamera:
                    ZoomCamera(context, spec);
                    break;
                case ActionVerb.PanCamera:
                    PanCamera(context, spec);
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

        /// <summary>
        /// Runs one <c>selected ...</c> order for every selected actor of the asked-for kind.
        /// The spec is parsed once and the scope flag is cleared while it runs, so the per-actor
        /// dispatch is the ordinary action path and every verb (including its warnings, range and
        /// formation handling) behaves exactly as it does on a unit's own sheet. Warnings stay
        /// one-per-press because they are raised through <see cref="WarnOnce"/>.
        /// </summary>
        private static void ExecuteOnSelection(EventContext context, ActionSpec spec)
        {
            EventRuntime runtime = context.Runtime;
            if (runtime == null)
            {
                WarnOnce("\u201Cselected\u201D needs the running game \u2014 it reads the live selection.");
                return;
            }
            var targets = SelectionInfo.ForScope(runtime.Selection.Selected, spec.SelectionScope,
                runtime.IsBuildingActor);
            if (targets.Count == 0)
            {
                WarnOnce(spec.SelectionScope == SelectionOrderScope.Buildings
                    ? "\u201Cselected buildings\u201D needs a building selected \u2014 nothing was ordered."
                    : spec.SelectionScope == SelectionOrderScope.Units
                        ? "\u201Cselected units\u201D needs a unit selected \u2014 nothing was ordered."
                        : "\u201Cselected\u201D needs something selected \u2014 nothing was ordered.");
                return;
            }

            spec.HasSelectionScope = false;
            try
            {
                foreach (object item in targets)
                {
                    Actor actor = item as Actor;
                    if (actor == null || actor.Scene == null)
                        continue;
                    var perActor = new EventContext
                    {
                        Variables = context.Variables,
                        World = context.World,
                        Runtime = runtime,
                        Me = actor,
                        Other = context.Other,
                        Sheet = runtime.FindSheetFor(actor),
                    };
                    Execute(perActor, spec);
                }
            }
            finally
            {
                spec.HasSelectionScope = true;
            }
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
            if (!TryColliderExtents(actor, out float halfX, out float halfZ, out centreY))
                return false;
            radius = Math.Max(halfX, halfZ);
            return radius > 0f;
        }

        /// <summary>
        /// The two horizontal half-extents of an actor's widest collider. A heading only needs the
        /// extent it travels along (the support distance of the box), so the body stops at the wall
        /// it is actually facing instead of hovering a half-width away on its narrow side.
        /// </summary>
        private static bool TryColliderExtents(Actor actor, out float halfX, out float halfZ, out float centreY)
        {
            halfX = 0f;
            halfZ = 0f;
            centreY = 0f;
            if (actor == null)
                return false;
            GatherCollider(actor, ref halfX, ref halfZ, ref centreY);
            return halfX > 0f || halfZ > 0f;
        }

        private static void GatherCollider(Actor node, ref float halfX, ref float halfZ, ref float centreY)
        {
            if (node == null || !node.IsActive)
                return;
            if (node is BoxCollider box)
            {
                float boxX = Math.Abs(box.Size.X * node.Scale.X) * 0.5f;
                float boxZ = Math.Abs(box.Size.Z * node.Scale.Z) * 0.5f;
                if (Math.Max(boxX, boxZ) > Math.Max(halfX, halfZ))
                {
                    float localY = node.Parent == null ? 0f : node.Position.Y - node.Parent.Position.Y;
                    centreY = localY + box.Center.Y * node.Scale.Y;
                }
                if (boxX > halfX)
                    halfX = boxX;
                if (boxZ > halfZ)
                    halfZ = boxZ;
            }
            else if (node is SphereCollider sphere)
            {
                float sphereRadius = Math.Abs(sphere.Radius * node.Scale.X);
                if (sphereRadius > Math.Max(halfX, halfZ))
                    centreY = node.Parent == null ? 0f : node.Position.Y - node.Parent.Position.Y;
                if (sphereRadius > halfX)
                    halfX = sphereRadius;
                if (sphereRadius > halfZ)
                    halfZ = sphereRadius;
            }
            foreach (Actor child in node.Children)
                GatherCollider(child, ref halfX, ref halfZ, ref centreY);
        }

        /// <summary>Distance from the collider's middle to its surface along a ground heading
        /// (the support function of the box).</summary>
        private static float ExtentAlong(float halfX, float halfZ, Vector3 heading)
        {
            return Math.Abs(heading.X) * halfX + Math.Abs(heading.Z) * halfZ;
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
        /// never blocked (nothing of theirs can intersect). The probe starts at the collider's
        /// middle and adds the extent the box presents **along that heading**, so a long-sided box
        /// stops at the wall instead of hovering a half-width away. Character colliders are ignored
        /// when <paramref name="ignoreCharacters"/> is set (the wall-aware chase), which is what
        /// lets two chasers pass each other instead of wedging; <see cref="Separation"/> then keeps
        /// them spaced.
        /// </summary>
        private static bool StepBlocked(Actor actor, Vector3 direction, float step, bool ignoreCharacters = false)
        {
            if (!TryColliderExtents(actor, out float halfX, out float halfZ, out _))
                return false;
            var heading = new Vector3(direction.X, 0f, direction.Z);
            if (heading.LengthSquared < 1e-6f)
                return false;
            heading = Vector3.Normalize(heading);
            float extent = ExtentAlong(halfX, halfZ, heading);
            Vector3 start = ColliderCastPoint(actor);
            if (!Physics.LineCastAll(start, start + heading * (step + extent), out var hits, ~0u, true))
                return false;
            foreach (var hit in hits)
            {
                var hitActor = hit.Collider as Actor;
                if (hitActor == null)
                    return true; // geometry without an actor (terrain, a static collider, ...)
                if (IsAncestorOrSelf(actor, hitActor))
                    continue; // the mover's own collider (or a child's)
                if (ignoreCharacters && IsCharacterActor(hitActor))
                    continue; // another character: spacing is Separation's job, not a wall
                return true;
            }
            return false;
        }

        /// <summary>
        /// True when a collider belongs to an event-driven actor (anything with an EventSheet on it
        /// or an ancestor) rather than to world geometry like the floor or a wall. Used to keep a
        /// chase from treating other bodies as walls.
        /// </summary>
        private static bool IsCharacterActor(Actor colliderActor)
        {
            EventRuntime runtime = EventsManager.Instance?.Runtime;
            if (runtime == null)
                return false;
            for (Actor node = colliderActor; node != null && !(node is Scene); node = node.Parent)
                if (runtime.FindSheetFor(node) != null)
                    return true;
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

        /// <summary>The actor's own <c>my Altitude</c> (0 when it never wrote one).</summary>
        private static double AltitudeOf(Actor actor)
        {
            var runtime = EventsManager.Instance?.Runtime;
            return runtime == null || actor == null ? 0 : Altitude.Of(runtime.Variables, actor);
        }

        /// <summary>True when this actor flies — <c>me is air</c> or an altitude above 0
        /// (Task 28 Part B).</summary>
        private static bool IsFlyingActor(Actor actor)
        {
            var runtime = EventsManager.Instance?.Runtime;
            return runtime != null && actor != null && AirPath.IsFlying(runtime.Variables, actor);
        }

        /// <summary>A flyer's route: one waypoint at the straight-line destination.</summary>
        private static Vector3[] DirectRoute(Vector3 spot)
        {
            return new[] { spot };
        }

        /// <summary>
        /// Asks the NavMesh about a walk and says which of the three things happened (Task 29
        /// play-check follow-up): a route, a genuine block, or a mesh that cannot answer.
        ///
        /// The plain query runs first, then the same query with both ends snapped onto the mesh —
        /// a body whose pivot sits just under the baked mesh, or a point a hair off it, answers
        /// "no path" on the plain query for a walk that is perfectly fine. The distinction between
        /// the last two answers is the point: snapping tells us whether the mesh actually covers
        /// the spots, and only a mesh that covers both and still finds no way through is a block
        /// the unit should stop for. A missing mesh, one Flax is still baking, and a point off the
        /// mesh are all "cannot answer" — the walk takes the straight line and no trigger fires.
        /// </summary>
        private static NavRouteAnswer QueryNavRoute(Vector3 start, Vector3 end, out Vector3[] path)
        {
            path = null;
            if (Navigation.FindPath(start, end, out Vector3[] direct) && direct != null && direct.Length > 0)
            {
                path = direct;
                return NavRouteAnswer.Found;
            }
            bool startOnMesh = Navigation.FindClosestPoint(start, out Vector3 onMeshStart);
            bool endOnMesh = Navigation.FindClosestPoint(end, out Vector3 onMeshEnd);
            double startOff = startOnMesh
                ? NavRoute.OffMesh(start.X, start.Z, onMeshStart.X, onMeshStart.Z)
                : double.PositiveInfinity;
            double endOff = endOnMesh
                ? NavRoute.OffMesh(end.X, end.Z, onMeshEnd.X, onMeshEnd.Z)
                : double.PositiveInfinity;
            bool snappedPathFound = false;
            if (startOnMesh && endOnMesh &&
                startOff <= NavRoute.OnMeshTolerance && endOff <= NavRoute.OnMeshTolerance &&
                Navigation.FindPath(onMeshStart, onMeshEnd, out Vector3[] snapped) &&
                snapped != null && snapped.Length > 0)
            {
                path = snapped;
                snappedPathFound = true;
            }
            NavRouteAnswer answer = NavRoute.Classify(Navigation.IsBuildingNavMesh, false,
                startOnMesh, startOff, endOnMesh, endOff, snappedPathFound);
            if (answer != NavRouteAnswer.Found)
                path = null;
            return answer;
        }

        /// <summary>
        /// Says once why the NavMesh could not answer, and that the walk is carrying on anyway:
        /// the usual causes are a missing or too small <c>NavMeshBoundsVolume</c> and a mesh Flax
        /// is still baking in the background, so the message names both. This is not a blocked
        /// path — nothing is in the way; the mesh just has no opinion — so it never raises
        /// <c>when path blocked</c>.
        /// </summary>
        private static void WarnNoNavRoute(Actor me, string where)
        {
            string building = Navigation.IsBuildingNavMesh ? " (the mesh is still building)" : "";
            string who = me == null ? "a unit" : $"\u201C{me.Name}\u201D";
            WarnOnce($"No NavMesh route from {who} to {where}{building} \u2014 check that a NavMeshBoundsVolume covers both points and that the floor under them is walkable. Walking the straight line instead.");
        }

        /// <summary>
        /// Says once that the mesh did answer and the way really is closed, so the unit stopped:
        /// the other half of <see cref="WarnNoNavRoute"/>, and the case that raised the trigger.
        /// </summary>
        private static void WarnPathBlocked(Actor me, string where)
        {
            string who = me == null ? "a unit" : $"\u201C{me.Name}\u201D";
            WarnOnce($"\u201C{who}\u201D has no way through to {where} \u2014 a wall, a prop or terrain marks are in the way. Stopped and raised `when path blocked`.");
        }

        /// <summary>
        /// Sends a flyer straight to a spot (Task 28 Part B): no NavMesh route, no terrain gate,
        /// and the height it holds is kept — the destination's Y is the ground under the unit,
        /// which <see cref="StepAltitude"/> turns back into ground + altitude.
        /// </summary>
        private static void RememberDirectMove(Actor me, Vector3 spot, double speed, bool faceForward = false)
        {
            double altitude = AltitudeOf(me);
            float ground = AltitudeGrounds.TryGetValue(me, out float known)
                ? known
                : (float)Altitude.GroundY(me.Position.Y, altitude);
            Vector3 destination = new Vector3(spot.X, ground, spot.Z);
            Remember(new OngoingMove
            {
                Subject = me,
                Goto = true,
                HasPositionTarget = true,
                PositionTarget = destination,
                Path = DirectRoute(destination),
                PathIndex = 0,
                Speed = speed,
                FaceForward = faceForward,
                Mode = PhysicsMoveMode.Transform,
            });
        }

        /// <summary>
        /// A flyer chasing a flyer climbs to its target's height (Task 28 Part B), so an
        /// air-versus-air chase ends at one altitude; a target on the ground leaves the height
        /// alone. The ground the height is measured from is captured before the change, so the
        /// unit rises or sinks in place instead of jumping.
        /// </summary>
        private static void ChaseAltitude(EventRuntime runtime, Actor me, Actor target)
        {
            if (runtime == null || me == null || target == null)
                return;
            double mine = AltitudeOf(me);
            double chase = AirPath.ChaseAltitudeFor(runtime.Variables, me, target);
            if (chase <= 0 || Math.Abs(chase - mine) <= 0.0001)
                return;
            AltitudeGrounds[me] = (float)Altitude.GroundY(me.Position.Y, mine);
            runtime.Variables.SetLocal(me, Altitude.Variable, ValueBox.OfNumber(chase));
        }

        /// <summary>
        /// <c>fly to &lt;spot&gt; at &lt;height&gt;</c> (Task 28 Part B): set <c>my Altitude</c> to the
        /// height and fly there in one line. <c>at 0</c> puts the unit down where it stands (and
        /// covers the rest on the ground), so one word both lands and moves.
        /// </summary>
        private static void FlyTo(EventContext context, ActionSpec spec)
        {
            var me = context.Me as Actor;
            if (me == null)
            {
                WarnOnce("\u201Cfly\u201D needs to run on an actor's sheet — the global Game sheet has no body to fly.");
                return;
            }
            double height = Math.Max(0, ResolveNumber(context, spec));
            float ground = (float)Altitude.GroundY(me.Position.Y, AltitudeOf(me));
            context.Variables.SetLocal(me, Altitude.Variable, ValueBox.OfNumber(height));
            if (height <= 0)
            {
                AltitudeGrounds.Remove(me);
                Vector3 landed = me.Position;
                landed.Y = ground;
                me.Position = landed;
            }
            else
                AltitudeGrounds[me] = ground;

            Vector3 spot;
            if (spec.Thing == "there")
            {
                if (!LastClickPosition.HasValue)
                {
                    WarnOnce("\u201Cfly there\u201D needs a clicked position first — click somewhere in the scene to set it.");
                    return;
                }
                spot = LastClickPosition.Value;
            }
            else if (ResolveThing(context, spec.Thing) is Actor target)
                spot = target.Position;
            else
            {
                WarnOnce($"I couldn't find \u201C{spec.Thing}\u201D to fly to.");
                return;
            }
            RememberDirectMove(me, spot, 5);
        }

        /// <summary>
        /// Keeps <c>my Altitude</c> honest once a tick (Task 28 Part A). A flying actor is pinned to
        /// its ground height plus its altitude; one whose sheet wrote the altitude back to 0 lands
        /// on that ground and leaves the table. Actors that never wrote an altitude are not touched
        /// at all, so `jump`, `platform` and physics still own their Y.
        /// </summary>
        private static void StepAltitude()
        {
            var runtime = EventsManager.Instance?.Runtime;
            if (runtime == null)
                return;
            if (AltitudeGrounds.Count > 0)
            {
                var gone = new List<Actor>();
                foreach (var pair in AltitudeGrounds)
                    if (pair.Key == null || pair.Key.Scene == null)
                        gone.Add(pair.Key);
                foreach (Actor dead in gone)
                    AltitudeGrounds.Remove(dead);
            }

            var seen = new HashSet<Actor>();
            foreach (SheetInstance sheet in runtime.Sheets)
            {
                Actor actor = sheet.Actor as Actor;
                if (actor == null || actor.Scene == null || !seen.Add(actor))
                    continue;
                double altitude = AltitudeOf(actor);
                bool flying = AltitudeGrounds.TryGetValue(actor, out float ground);
                if (altitude <= 0)
                {
                    if (!flying)
                        continue;
                    // The sheet wrote 0: put the unit down on the ground it was flying over.
                    AltitudeGrounds.Remove(actor);
                    Vector3 landed = actor.Position;
                    landed.Y = ground;
                    actor.Position = landed;
                    continue;
                }
                if (!flying)
                {
                    ground = (float)Altitude.GroundY(actor.Position.Y, altitude);
                    AltitudeGrounds[actor] = ground;
                }
                Vector3 pinned = actor.Position;
                pinned.Y = (float)Altitude.PinY(ground, altitude);
                actor.Position = pinned;
            }
        }

        private static void MoveSubject(Actor subject, Vector3 direction, double speed, PhysicsMoveMode mode,
            bool ignoreCharacters = false)
        {
            // A flying unit travels level (Task 28 Part A): navmesh waypoints and a target's own Y
            // never pull it down, and StepAltitude re-applies its height every tick.
            bool flying = IsFlyingActor(subject);
            if (flying)
                direction.Y = 0f;
            double wanted = speed * DeltaTime;
            float step = (float)PhysicsMovement.MoveStep(speed, DeltaTime);
            // A hitched (or paused) frame used to multiply the step by the hitch length, which
            // reads as a body lurching or jumping through a wall. Log it once per hitch so a jump
            // in the editor can be traced to a frame spike instead of guessed at.
            if (Math.Abs(wanted) > PhysicsMovement.MaxStepPerTick && Math.Abs(step) > 0.001f)
                Debug.Log($"[jump] \"{subject.Name}\" frame step clamped: wanted {wanted:0.###} at {speed:0.###} speed, dt {DeltaTime:0.###} s.");
            // A flyer clears terrain, props and buildings alike (Task 28 Part B): it travels the
            // XZ plane at its own height, so nothing under it is a wall and no body mode applies.
            if (flying)
            {
                subject.Position = subject.Position + direction * step;
                return;
            }
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
                    // A jump overrides ground contact for as long as the take-off speed lasts:
                    // the character is airborne even though the floor is still in reach.
                    bool jumping = JumpingCharacters.Contains(subject);
                    CharacterFallSpeeds.TryGetValue(subject, out float fallSpeed);
                    PhysicsMovement.CharacterStep(
                        direction.X, direction.Y, direction.Z, speed, fallSpeed, grounded && !jumping,
                        Math.Abs(Physics.Gravity.Y), PhysicsMovement.GroundStickSpeed, DeltaTime,
                        out double cx, out double cy, out double cz, out double newFallSpeed);
                    CharacterFallSpeeds[subject] = (float)newFallSpeed;
                    if (jumping && grounded && newFallSpeed <= 0f)
                        JumpingCharacters.Remove(subject); // landed: the next tick walks again
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
                    MoveTransformStep(subject, direction, step, ignoreCharacters);
                    break;
            }
        }

        /// <summary>
        /// One transform move, split into slices no longer than the collider's smallest half-extent
        /// and probed slice by slice. A single long step could otherwise cross a thin wall between
        /// two frames — the ray only proves the *end* of the step, not the gap it skipped over.
        /// </summary>
        private static void MoveTransformStep(Actor subject, Vector3 direction, float step, bool ignoreCharacters)
        {
            if (Math.Abs(step) < 1e-6f)
                return;
            TryColliderExtents(subject, out float halfX, out float halfZ, out _);
            float smallest = halfX > 0f && halfZ > 0f ? Math.Min(halfX, halfZ) : Math.Max(halfX, halfZ);
            float slice = Math.Max(1f, smallest * 0.5f);
            int slices = Math.Max(1, (int)Math.Ceiling(Math.Abs(step) / slice));
            float piece = step / slices;
            for (int i = 0; i < slices; i++)
            {
                if (StepBlocked(subject, direction, piece, ignoreCharacters))
                    return;
                subject.Position = subject.Position + direction * piece;
            }
        }

        /// <summary>Turn a body onto the heading it is walking (the `facing forward` qualifier
        /// added to `move toward`). Flattened, so a chase never tips the model into the floor.</summary>
        private static void FaceHeading(Actor actor, Vector3 heading)
        {
            var flat = new Vector3(heading.X, 0f, heading.Z);
            if (flat.LengthSquared < 1e-6f)
                return;
            actor.LookAt(actor.Position + flat);
        }

        /// <summary>
        /// Bends a chase heading away from nearby bodies (Task 7 G2 follow-up). Movement no longer
        /// treats characters as walls, so without this two chasers would stack on one spot — or,
        /// before that change, wedge against each other. The rule itself is pure
        /// (<see cref="Separation"/>); this only gathers the neighbours, skipping the chaser, its
        /// children and the thing it is chasing (an attacker still has to reach melee range).
        ///
        /// Only characters count. <see cref="IEventWorld.Actors"/> returns every actor in the scene,
        /// so the floor and walls are in it too; they carry colliders whose extents are far larger
        /// than a body (a wall's half-width can be 150 cm), which put every chaser "inside" their
        /// spacing range and bent the chase away from the level instead of the player.
        /// </summary>
        private static Vector3 NudgeAroundNeighbours(EventContext context, Actor me, Actor target, Vector3 heading)
        {
            IReadOnlyList<object> actors = context.Runtime.World.Actors();
            if (actors == null || actors.Count == 0)
                return heading;
            TryColliderFootprint(me, out float myRadius, out _);
            var neighbours = new List<SeparationNeighbour>();
            foreach (object item in actors)
            {
                if (!(item is Actor other) || other == null || other.Scene == null || !other.IsActive)
                    continue;
                if (ReferenceEquals(other, me) || IsAncestorOrSelf(me, other))
                    continue;
                if (target != null && (ReferenceEquals(other, target) || IsAncestorOrSelf(target, other)))
                    continue;
                if (!IsCharacterActor(other))
                    continue; // a wall or the floor: geometry is not a body to keep distance from
                if (!TryColliderFootprint(other, out float radius, out _))
                    continue;
                neighbours.Add(new SeparationNeighbour { X = other.Position.X, Z = other.Position.Z, Radius = radius });
            }
            if (!Separation.Push(me.Position.X, me.Position.Z, myRadius, neighbours,
                    heading.X, heading.Z, out double pushX, out double pushZ, out double weight))
                return heading;
            Separation.BlendHeading(heading.X, heading.Z, pushX, pushZ, weight, out double x, out double z);
            return new Vector3((float)x, 0f, (float)z);
        }

        /// <summary>
        /// `move toward &lt;thing&gt; ... around walls` (Task 7 Part G2): one steering tick for a
        /// direct chase. The straight probe is the only ray while the way is clear; when it is
        /// blocked (or the actor is stuck) the pure <see cref="WallSteering"/> rule picks a clear
        /// fan deviation or the wall tangent, and the result goes through the normal
        /// <see cref="MoveSubject"/>, so transform, dynamic, kinematic and character movement all
        /// behave the same. Returns the heading to move along (the straight bearing when nothing
        /// needs avoiding).
        /// </summary>
        private static Vector3 SteerAroundWalls(EventContext context, Actor me, Actor target, double speed)
        {
            WallSteering steering = context.Runtime.Steering;
            Vector3 delta = target.Position - me.Position;
            Vector3 bearing = new Vector3(delta.X, 0f, delta.Z);
            float distance = bearing.Length;
            if (distance < 0.001f)
            {
                steering.Clear(me);
                return Vector3.Zero;
            }
            bearing = Vector3.Normalize(bearing);

            // Probe by the actor's own half-width past the collider centre, so a box stops at the
            // wall instead of sinking half into it (the same rule `wander` uses).
            float radius = TryColliderFootprint(me, out float halfWidth, out _) ? halfWidth : 0f;
            float probe = Math.Min(distance, (float)WallSteering.Probe + radius);
            float lookahead = (float)WallSteering.Lookahead + radius;

            bool directClear = !WallAhead(me, target, bearing, probe, out Vector3 normal);

            Vector3 forward = Vector3.Transform(Vector3.Forward, me.Orientation);
            Vector3 flatForward = new Vector3(forward.X, 0f, forward.Z);
            if (flatForward.LengthSquared < 1e-8f)
                flatForward = bearing;

            var query = new SteerQuery
            {
                Actor = me,
                DeltaTime = DeltaTime,
                X = me.Position.X,
                Z = me.Position.Z,
                ForwardX = flatForward.X,
                ForwardZ = flatForward.Z,
                BearingX = bearing.X,
                BearingZ = bearing.Z,
                DirectClear = directClear,
                HasHitNormal = !directClear,
                HitNormalX = normal.X,
                HitNormalZ = normal.Z,
                // A fan ray is a wall probe at the same lookahead as the direct one: blocked only
                // by something solid that is neither the chaser nor the thing it is chasing.
                IsClear = deviation =>
                    !WallAhead(me, target, RotateHeading(bearing, deviation), lookahead, out _),
                // "Stuck" means moving less than a quarter of what this speed should cover, so a
                // slow chaser is not called stuck just because its steps are short.
                MinStep = Math.Max(0.001, Math.Abs(speed) * DeltaTime * 0.25),
            };

            SteerChoice choice = steering.Step(query);
            if (!choice.Steer)
                return bearing;
            return new Vector3((float)choice.HeadingX, 0f, (float)choice.HeadingZ);
        }

        /// <summary>
        /// One horizontal probe from the actor's collider centre. True when world geometry (not
        /// the actor, its children, the chase target or another character body) is in the way;
        /// <paramref name="normal"/> is the surface that blocked it, for the tangent fallback.
        /// </summary>
        private static bool WallAhead(
            Actor me, Actor ignore, Vector3 heading, float distance, out Vector3 normal)
        {
            normal = Vector3.Up;
            Vector3 flat = new Vector3(heading.X, 0f, heading.Z);
            if (distance <= 0f || flat.LengthSquared < 1e-8f)
                return false;
            flat = Vector3.Normalize(flat);
            Vector3 start = ColliderCastPoint(me);
            if (!Physics.LineCastAll(start, start + flat * distance, out var hits, ~0u, true))
                return false;
            foreach (var hit in hits)
            {
                var hitActor = hit.Collider as Actor;
                if (hitActor == null)
                {
                    normal = hit.Normal;
                    return true; // geometry without an actor (terrain, a bare collider, ...)
                }
                if (IsAncestorOrSelf(me, hitActor))
                    continue; // the chaser's own collider (or a child's)
                if (ignore != null && IsAncestorOrSelf(ignore, hitActor))
                    continue; // the target is not a wall
                if (IsCharacterActor(hitActor))
                    continue; // another body: separation handles it, steering only rounds geometry
                normal = hit.Normal;
                return true;
            }
            return false;
        }

        /// <summary>Rotates a ground heading by a signed number of degrees (+ = toward +X, the
        /// actor's right when it faces +Z), matching <see cref="WallSteering"/>'s deviation sign.</summary>
        private static Vector3 RotateHeading(Vector3 heading, double degrees)
        {
            double radians = degrees * Math.PI / 180.0;
            float cos = (float)Math.Cos(radians);
            float sin = (float)Math.Sin(radians);
            return new Vector3(
                heading.X * cos + heading.Z * sin,
                0f,
                heading.Z * cos - heading.X * sin);
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

        /// <summary>
        /// Ground probe behind `jump` and the `me is on ground` / `me is airborne` conditions.
        /// A CharacterController uses its own ray plus the controller's slope limit; every other
        /// actor gets a short downward ray from its pivot. Both ignore trigger volumes, and a
        /// `jump` that is still in the air reads as airborne even though the floor is inside the
        /// probe (that is what makes "no double jumps" work).
        /// </summary>
        public static bool IsGrounded(Actor actor)
        {
            if (actor == null || actor.Scene == null)
                return false;
            if (JumpingCharacters.Contains(actor) || ScriptedJumps.ContainsKey(actor))
                return false;
            CharacterController character = EventSheet.FindCharacterController(actor);
            if (character != null && IsAncestorOrSelf(character, actor))
                return IsCharacterGrounded(character, actor);
            float probe = GroundCheckDistanceFor(actor);
            if (probe <= 0f)
                return false;
            if (!Physics.RayCast(actor.Position, Vector3.Down, out RayCastHit hit, probe, ~0u, false))
                return false;
            var hitActor = hit.Collider as Actor;
            if (hitActor == null)
                return true; // terrain, or a collider with no actor above it
            return !ReferenceEquals(hitActor, actor) && !IsAncestorOrSelf(actor, hitActor);
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
            JumpingCharacters.Remove(subject);
            ScriptedJumps.Remove(subject);
            Climbs.Remove(subject);

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
            {
                BrakeBody(move.Subject);
                return;
            }
            // Snap to the point the path actually ended on, never to the target actor's origin:
            // a waypoint sitting at Y=0 is not the ground the leg walked to, and `goto <thing>`
            // must not copy the target's exact position just because its last leg finished.
            Vector3 destination;
            if (move.Path != null && move.Path.Length > 0)
                destination = move.Path[move.Path.Length - 1];
            else if (move.HasPositionTarget)
                destination = move.PositionTarget;
            else if (move.Target != null)
                destination = move.Target.Position;
            else
                return;
            float jump = (destination - move.Subject.Position).Length;
            if (jump > 2f)
                Debug.Log($"[jump] \"{move.Subject.Name}\" arrival snapped {jump:0.#} units onto the end of its path.");
            move.Subject.Position = destination;
            // A flying unit keeps its height over the ground it just arrived above, so `goto`
            // walks it along the plane without dropping it to the waypoint's Y (Task 28 Part A).
            double altitude = AltitudeOf(move.Subject);
            if (altitude > 0)
            {
                AltitudeGrounds[move.Subject] = destination.Y;
                Vector3 held = move.Subject.Position;
                held.Y = destination.Y + (float)altitude;
                move.Subject.Position = held;
            }
            else
            {
                AltitudeGrounds.Remove(move.Subject);
            }
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

        /// <summary>
        /// `jump` / `jump strength <n>` (Task 7 G4 phase 3): a vertical take-off, refused while
        /// the actor is already in the air so holding the key cannot chain jumps. A
        /// CharacterController gets the take-off as its stored vertical speed (the same number
        /// gravity then decays), a dynamic body gets <c>LinearVelocity.Y</c>, and a kinematic body
        /// or an actor with no body at all runs the scripted arc from <see cref="Step"/>.
        /// Plays a `Jump` clip when the actor has an AnimatedModel and the project has one.
        /// </summary>
        private static void Jump(EventContext context, ActionSpec spec)
        {
            var me = context.Me as Actor;
            if (me == null)
            {
                WarnOnce("\u201Cjump\u201D needs to run on an actor's sheet — the global Game sheet has no body to jump.");
                return;
            }
            if (!IsGrounded(me))
                return;
            double strength = PhysicsMovement.JumpVelocity(ResolveNumber(context, spec));
            PhysicsMoveMode mode = ResolveMoveMode(me, context.Sheet != null && context.Sheet.UsePhysicsMovement);

            // A jump anim is optional: a simple mesh has no model to animate, and a project
            // without a `Jump` clip should not warn on every hop.
            FlaxEngine.AnimatedModel model = ModelOf(me);
            if (model != null && model.SkinnedModel != null && LoadAnimation("Jump") != null)
                StartAnim(context, new ActionSpec { Thing = "Jump" }, false);

            switch (mode)
            {
                case PhysicsMoveMode.Character:
                    JumpingCharacters.Add(me);
                    CharacterFallSpeeds[me] = (float)strength;
                    break;
                case PhysicsMoveMode.Dynamic:
                {
                    RigidBody body = FindBody(me);
                    if (body == null || !IsAncestorOrSelf(body, me))
                    {
                        StartScriptedJump(me, strength);
                        break;
                    }
                    Vector3 velocity = body.LinearVelocity;
                    body.LinearVelocity = new Vector3(velocity.X, (float)strength, velocity.Z);
                    break;
                }
                default:
                    StartScriptedJump(me, strength);
                    break;
            }
        }

        /// <summary>
        /// `climb &lt;thing&gt;` (Task 7 G4 phase 4): take hold of a ladder actor. X/Z lock to the
        /// ladder's axis, Y is driven at the climb speed and clamped to the ladder's span, and
        /// reaching either end ends the climb — a sheet steps off the top with `move forward`.
        /// `climb down` reverses the drive; a sheet that keeps the word on a held key can also
        /// climb back down because the span clamp keeps it on the ladder.
        /// </summary>
        private static void StartClimb(EventContext context, ActionSpec spec)
        {
            var me = context.Me as Actor;
            if (me == null)
            {
                WarnOnce("\u201Cclimb\u201D needs to run on an actor's sheet — the global Game sheet has nothing to climb with.");
                return;
            }
            var ladder = ResolveThing(context, spec.Thing) as Actor;
            if (ladder == null)
            {
                WarnOnce($"I couldn't find the ladder \u201C{spec.Thing}\u201D to climb.");
                return;
            }
            double speed = Math.Abs(ResolveNumber(context, spec));
            if (speed < 1.0)
                speed = PhysicsMovement.DefaultClimbSpeed;
            // The climb owns the body: a walk or chase still pending must not fight it.
            StopMovingSubject(me);
            double bottom = ladder.Position.Y;
            Climbs[me] = new OngoingClimb
            {
                Ladder = ladder,
                Speed = speed,
                Direction = spec.Dir == "down" ? -1 : 1,
                Bottom = bottom,
                Top = bottom + LadderHeight(context, ladder),
            };
            MovedThisTick.Add(me);
        }

        /// <summary>True while the actor holds a ladder — the answer behind `me is climbing`.</summary>
        public static bool IsClimbing(Actor actor)
        {
            return actor != null && Climbs.ContainsKey(actor);
        }

        /// <summary>Lets go of the ladder. A dynamic body is put at rest so the climb speed does
        /// not carry over, and the character's stored fall speed starts fresh from let-go height.</summary>
        private static void StopClimb(Actor climber)
        {
            if (climber == null || !Climbs.Remove(climber))
                return;
            CharacterFallSpeeds.Remove(climber);
            RigidBody body = FindBody(climber);
            if (body != null && !body.IsKinematic)
                body.LinearVelocity = Vector3.Zero;
        }

        /// <summary>
        /// How tall a ladder is: `my LadderHeight` when the sheet authored one (centimetres), else
        /// the tallest collider under the ladder actor, else
        /// <see cref="PhysicsMovement.DefaultLadderHeight"/>. The span runs from the ladder actor's
        /// own Y upward, which matches how ladder props are placed (pivot at the base).
        /// </summary>
        private static double LadderHeight(EventContext context, Actor ladder)
        {
            double authored = context.ReadMy("LadderHeight").AsNumber();
            if (authored > 0.0)
                return authored;
            float tallest = 0f;
            GatherColliderHeight(ladder, ref tallest);
            return tallest > 0f ? tallest : PhysicsMovement.DefaultLadderHeight;
        }

        private static void GatherColliderHeight(Actor node, ref float tallest)
        {
            if (node == null || !node.IsActive)
                return;
            if (node is BoxCollider box)
                tallest = Math.Max(tallest, Math.Abs(box.Size.Y * node.Scale.Y));
            else if (node is SphereCollider sphere)
                tallest = Math.Max(tallest, Math.Abs(sphere.Radius * node.Scale.Y) * 2f);
            foreach (Actor child in node.Children)
                GatherColliderHeight(child, ref tallest);
        }

        /// <summary>Starts a scripted (no-body) jump arc from the actor's current height.</summary>
        private static void StartScriptedJump(Actor subject, double strength)
        {
            ScriptedJumps[subject] = new ScriptedJump
            {
                BaseY = subject.Position.Y,
                Height = 0.0,
                VelocityY = strength,
            };
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
                // `spawn Ring at me rarity "Rare" level 12` (Task 9 E7): roll the affixes onto
                // the clone before its sheet attaches, so `when me appears` already sees them.
                if (spec.HasRarity)
                    ApplyItemRoll(context, spec, clone);
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
        /// <c>cast "&lt;spell&gt;"</c> / <c>cast "&lt;spell&gt;" at &lt;thing&gt;</c> (Task 9 E3): the one
        /// action word of the spell system.
        ///
        /// Refuses quietly while the spell's cooldown is live or `my Mana` is short — a shortage
        /// is normal play, not an error. A successful cast spends the mana (clamped at 0 here,
        /// because the language has no `min()`), starts the cooldown, plays the spell's sound and
        /// then either clones the spell's bullet with its stats stamped on it, or — for a spell
        /// with no bullet — sends the message <c>"cast &lt;Spell&gt;"</c> so events decide what
        /// happens (a heal, a buff, a teleport).
        /// </summary>
        private static void CastSpell(EventContext context, ActionSpec spec)
        {
            string named = string.IsNullOrEmpty(spec.Spell) ? spec.Thing : spec.Spell;
            var me = context.Me as Actor;
            if (me == null)
                return;
            EventVariables variables = context.Runtime != null ? context.Runtime.Variables : null;
            if (!SpellData.TryGet(variables, named, out SpellStats spell))
            {
                string known = SpellData.KnownNames(variables);
                WarnOnce(string.IsNullOrEmpty(known)
                    ? $"I don't know the spell \u201C{named}\u201D — no SpellData.events defines any spells yet."
                    : $"I don't know the spell \u201C{named}\u201D. This project has: {known}.");
                return;
            }
            double now = context.Runtime != null ? context.Runtime.Time : 0;
            if (context.Runtime != null && context.Runtime.Spells.IsCooling(me, spell.Name, now))
                return;
            // `my Mana` is the purse (Task 9 E2 writes it). Read it without the "never set"
            // warning: a sheet that has not added the mana block yet simply cannot afford a spell.
            double mana = variables != null && variables.HasLocal(me, "Mana") ? context.ReadMy("Mana").AsNumber() : 0;
            if (mana < spell.Mana)
                return;
            context.WriteMy("Mana", ValueBox.OfNumber(Math.Max(0, mana - spell.Mana)));
            context.Runtime?.Spells.Start(me, spell.Name, spell.Cooldown, now);
            if (!string.IsNullOrEmpty(spell.Sound))
                EmitSound(spell.Sound, me.Position, me);

            object target = string.IsNullOrEmpty(spec.To) ? null : ResolveThing(context, spec.To);
            if (string.IsNullOrEmpty(spell.Bullet))
            {
                // No projectile: the spell IS the message. `when message "cast Heal" arrives`
                // runs on the target (or on the caster for a self spell).
                context.Runtime?.RaisePush(target ?? me, new PushSignal
                {
                    Kind = TriggerKind.Message,
                    Name = "cast " + spell.Name,
                    Other = me,
                });
                return;
            }

            string spot = string.IsNullOrEmpty(spec.To) ? "me" : spec.To;
            Actor clone = SpawnClone(context, spell.Bullet, spot, 0);
            if (clone == null)
                return;
            if (target is Actor targetActor)
                clone.LookAt(targetActor.Position);
            if (variables != null)
            {
                variables.SetLocal(clone, "BulletSpeed", ValueBox.OfNumber(spell.Speed));
                variables.SetLocal(clone, "Life", ValueBox.OfNumber(spell.Life));
                variables.SetLocal(clone, "TimeLeft", ValueBox.OfNumber(spell.Life));
                // lowercase `damage` is what `other's damage` reads on the victim's sheet.
                variables.SetLocal(clone, "damage", ValueBox.OfNumber(spell.Damage));
                variables.SetLocal(clone, "blast", ValueBox.OfNumber(spell.Blast));
                variables.SetLocal(clone, "Element", ValueBox.OfText(spell.Element));
                variables.SetLocal(clone, "Spell", ValueBox.OfText(spell.Name));
                variables.SetLocal(clone, "shooter",
                    ValueBox.OfText(context.Runtime != null ? context.Runtime.NameOfActor(me) : ""));
            }
            EventsManager.Instance?.AttachSpawnedActor(clone);
        }

        /// <summary>
        /// Rolls a spawned actor's item stats (Task 9 E7): base stats plus the rarity's affixes,
        /// written as the item's own `my` variables (so `my FireResist` reads on the item) and
        /// recorded in <see cref="ItemAffixes"/> so `equip` applies exactly these numbers and
        /// `drop`/unequip takes them back.
        /// </summary>
        private static void ApplyItemRoll(EventContext context, ActionSpec spec, Actor clone)
        {
            EventVariables variables = context.Runtime != null ? context.Runtime.Variables : null;
            if (variables == null)
                return;
            double level = 1;
            if (spec.HasItemLevel)
                level = spec.ItemLevel;
            else if (variables.HasGlobal("ItemLevel"))
                level = variables.GetGlobal("ItemLevel").AsNumber();
            ItemRoll roll = ItemGenerator.Roll(variables, spec.Thing, spec.Rarity, level);
            if (roll == null)
            {
                bool knownBase = ItemData.TryGet(variables, spec.Thing, out _);
                WarnOnce(knownBase
                    ? $"I don't know the rarity \u201C{spec.Rarity}\u201D. Try one of: {string.Join(", ", AffixData.RarityNames(variables))}."
                    : $"I don't know the item \u201C{spec.Thing}\u201D. Add its base to ItemData.events — it has: {ItemData.KnownNames()}.");
                return;
            }
            foreach (ItemStat stat in roll.Stats)
                variables.SetLocal(clone, stat.Stat, ValueBox.OfNumber(stat.Value));
            variables.SetLocal(clone, "Rarity", ValueBox.OfText(roll.Rarity));
            variables.SetLocal(clone, "ItemName", ValueBox.OfText(roll.DisplayName));
            variables.SetLocal(clone, "ItemLevel", ValueBox.OfNumber(roll.ItemLevel));
            context.Runtime.Affixes.Stamp(clone, roll);
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

        /// <summary>How wide `damage area` is when the line writes no `for radius`.</summary>
        private const double DefaultDamageAreaRadius = 150;

        /// <summary>
        /// <c>aim "&lt;ability&gt;"</c> (Task 27 Part A): arms target-pick mode for the acting
        /// unit. A left click then resolves it (<see cref="EventsManager"/> raycasts the ground
        /// and the actor under the cursor), a right click / Escape / <c>cancel aim</c> drops it,
        /// and it lapses after its timeout. Reach is <c>my attack range</c>, so a caster tuned
        /// with <c>set my attack range to 600</c> can click further; unit picks additionally
        /// obey the matchup table and the local player's fog, the same rules <c>me can attack</c>
        /// uses. Nothing here runs unless a sheet calls <c>aim</c>.
        /// </summary>
        private static void StartAim(EventContext context, ActionSpec spec)
        {
            var me = context.Me as Actor;
            if (me == null)
            {
                WarnOnce("\u201Caim\u201D needs to run on the caster's own sheet \u2014 the global Game sheet has no unit to aim with.");
                return;
            }
            double range = OrderManager.AttackRangeOf(context);
            context.Runtime?.Targeting.Begin(me, spec.Ability, spec.Accepts, range, spec.Duration,
                me.Position.X, me.Position.Z, actor => AimAcceptsUnit(context, me, actor));
        }

        /// <summary>The extra rule a unit pick must pass: visible in the fog and hittable per
        /// the matchup table. The caster itself is always a legal pick (a self-buff).</summary>
        private static bool AimAcceptsUnit(EventContext context, Actor caster, object actor)
        {
            if (actor == null)
                return false;
            if (ReferenceEquals(actor, caster))
                return true;
            if (actor is Actor other && (other.Scene == null || !other.IsActive))
                return false;
            EventRuntime runtime = context.Runtime;
            if (runtime != null && runtime.Fog.Enabled &&
                !runtime.Fog.IsVisibleTo(runtime.Fog.LocalPlayer, actor))
                return false;
            return Matchups.CanAttackBetween(context.Variables, caster, actor);
        }

        /// <summary><c>cancel aim</c> \u2014 leaves target-pick mode without a pick.</summary>
        private static void CancelAim(EventContext context)
        {
            EventRuntime runtime = context.Runtime;
            if (runtime == null)
                return;
            if (context.Me == null || !runtime.Targeting.Cancel(context.Me))
                runtime.CancelAim();
        }

        /// <summary>
        /// <c>damage area around &lt;spot&gt; by &lt;n&gt; for radius &lt;r&gt;</c> (Task 27
        /// Part A): the AoE primitive point abilities need, since <c>explode</c> belongs to a
        /// projectile and destroys the caster. Every other actor inside the radius takes a
        /// <c>"damage"</c> message from the caster, so the usual armour \u2192 shield \u2192 health
        /// pipeline and the Task 23 matchup gate apply unchanged; the caster is not destroyed
        /// and is not spared (a storm hits its own side too). No visual or sound of its own \u2014
        /// the sheet plays those, so one primitive serves every kind of blast.
        /// </summary>
        private static void DamageArea(EventContext context, ActionSpec spec)
        {
            EventRuntime runtime = context.Runtime;
            if (runtime == null || context.World == null)
                return;
            double amount = ResolveNumber(context, spec);
            double radius = spec.RadiusNode != null
                ? context.Evaluate(spec.RadiusNode).AsNumber()
                : spec.Radius;
            if (radius <= 0)
                radius = DefaultDamageAreaRadius;
            if (!TryResolveAreaSpot(context, spec.Thing, out double x, out double z))
                return;

            object sender = context.Me;
            foreach (object other in context.World.Actors())
            {
                if (other == null)
                    continue;
                if (other is Actor actor && (actor.Scene == null || !actor.IsActive))
                    continue;
                // Splash never chains through the rest of a shotgun spread or another bullet.
                if (runtime.Variables.HasLocal(other, "BulletSpeed"))
                    continue;
                if (context.World.DistanceToPoint(other, x, z) > radius)
                    continue;
                runtime.RaisePush(other, new PushSignal
                {
                    Kind = TriggerKind.Message,
                    Name = "damage",
                    Other = sender,
                });
            }
        }

        /// <summary>The spot a <c>damage area</c> is centred on: an actor, <c>there</c>, or the
        /// pick <c>aim</c> just made (<c>my aim target</c> / <c>my aim</c>).</summary>
        private static bool TryResolveAreaSpot(EventContext context, string spot, out double x, out double z)
        {
            x = 0;
            z = 0;
            if (spot == "my aim target" || spot == "my aim" || spot == "aim")
            {
                TargetingSession session = context.Runtime?.Targeting.SessionOf(context.Me)
                    ?? context.Runtime?.Targeting.Current;
                if (session == null || !session.HasPick)
                {
                    WarnOnce("\u201Cdamage area around my aim target\u201D needs an `aim` pick first \u2014 click a target while aiming.");
                    return false;
                }
                x = session.PickX;
                z = session.PickZ;
                return true;
            }
            if (spot == "there")
            {
                if (!LastClickPosition.HasValue)
                {
                    WarnOnce("\u201Cdamage area around there\u201D needs a clicked position first \u2014 click somewhere in the scene.");
                    return false;
                }
                x = LastClickPosition.Value.X;
                z = LastClickPosition.Value.Z;
                return true;
            }
            var target = ResolveThing(context, spot) as Actor;
            if (target == null)
            {
                WarnOnce($"I couldn't find \u201C{spot}\u201D to centre that damage area on.");
                return false;
            }
            x = target.Position.X;
            z = target.Position.Z;
            return true;
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
            // A destroyed actor takes its spell cooldowns and item roll with it, so a clone born
            // under the same name never inherits a stale timer or someone else's affixes.
            context.Runtime.Spells.Clear(actor);
            // Passing the variables lets a destroyed *worn* item hand back the stats it added;
            // otherwise a destroyed ring would leave a permanent bonus on its wearer.
            context.Runtime.Affixes.Forget(context.Runtime.Variables, actor);
            context.Runtime.Affixes.ForgetWearer(actor);
            context.Runtime.Selection.Forget(actor);
            context.Runtime.Targeting.Forget(actor);
            context.Runtime.LiftOff.Forget(actor);
            // A destroyed actor also ends its buffs, so nothing keeps ticking a dead object or
            // rewriting stats on it — and a clone born under the same name starts clean.
            context.Runtime.Buffs.Clear(context.Runtime.Variables, actor);
            // Shared ability timers die with the actor too (Task 27 Part E), so a respawn or a
            // clone never inherits an ability that is still supposed to be cooling down.
            context.Runtime.Cooldowns.Clear(actor);
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
            // cloned actor's own name has no matching .events file. The kind lets a save clone
            // this actor again instead of treating it as authored into the scene (Task 30 D).
            context.Runtime.MarkSpawnedRoot(clone, templateName, SaveSpawned.KindClone);
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
            context.Runtime.MarkSpawnedRoot(clone, spec.Thing, SaveSpawned.KindClone);
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

        /// <summary>
        /// <c>lift off</c> on a building's sheet (Task 28 Part C): rise to its <c>my Altitude</c>
        /// (or <c>LiftOffHeight</c>), pause its work and free its build-grid footprint. The ground
        /// it is measured from is captured first, because Part A pins from there every tick.
        /// </summary>
        private static void LiftOffBuilding(EventContext context, ActionSpec spec)
        {
            var me = context.Me as Actor;
            if (me == null)
            {
                WarnOnce("\u201Clift off\u201D needs to run on a building's sheet — the global Game sheet has no building to raise.");
                return;
            }
            EventRuntime runtime = context.Runtime;
            if (runtime.LiftOff.IsFlying(me))
            {
                WarnOnce($"\u201C{me.Name}\u201D is already airborne.");
                return;
            }
            // The height is measured from where it stands now, before `my Altitude` changes.
            if (!AltitudeGrounds.ContainsKey(me))
                AltitudeGrounds[me] = me.Position.Y;
            string building = runtime.Construction.BuildingOf(me);
            double requested = ResolveNumber(context, spec);
            if (requested <= 0)
                requested = AltitudeOf(me); // an authored `my Altitude` is the rise height
            if (!runtime.LiftOff.TryLift(context.Variables, runtime, me, building, requested,
                    runtime.Construction.OwnsCells(me)))
            {
                WarnOnce($"\u201C{me.Name}\u201D could not lift off.");
            }
        }

        /// <summary>
        /// <c>land</c> (Task 28 Part C): put the building down on ground its own unit type may
        /// stand on, re-reserving the footprint it freed if nobody took it. A refused landing
        /// keeps it flying and reports <c>when path blocked</c> with the name <c>land</c>.
        /// </summary>
        private static void LandBuilding(EventContext context)
        {
            var me = context.Me as Actor;
            if (me == null)
            {
                WarnOnce("\u201Cland\u201D needs to run on a building's sheet — the global Game sheet has no building to put down.");
                return;
            }
            EventRuntime runtime = context.Runtime;
            LiftOffManager lift = runtime.LiftOff;
            TerrainMap terrain = runtime.Terrain;
            terrain.ResolveCellSize(context.Variables);
            string unitType = UnitTypes.TypeOf(context.Variables, me);
            // `IsPassable`, not `CanStep`: the escape rule would otherwise wave a land building
            // standing over water through, which is exactly the landing this word must refuse.
            bool groundOk = terrain.IsPassable(unitType, me.Position.X, me.Position.Z);
            string ground = UnitTypes.GroundName(terrain.KindAt(me.Position.X, me.Position.Z));
            // A building that kept its cells — raised by `fly to … at …` instead of `lift off` —
            // still needs the dry run; its own cells count as free inside it.
            bool reserved = lift.ReservedOf(me) || runtime.Construction.OwnsCells(me);
            // No lift-off state means no template name on it; fall back to the placement table so
            // a building raised by `fly to … at …` re-takes its real footprint, not a bare 1x1.
            string building = lift.BuildingOf(me);
            if (building.Length == 0)
                building = runtime.Construction.BuildingOf(me);
            double size = ConstructionManager.ResolveGridSize(context.Variables, false, 0);
            bool gridFree = !reserved || runtime.Construction.CanReserveCells(me, building,
                context.Variables, me.Position.X, me.Position.Z, size);

            LandResult result = lift.TryLand(context.Variables, runtime, me, groundOk, gridFree);
            switch (result)
            {
                case LandResult.Landed:
                    if (reserved)
                    {
                        // Drop any cells it still holds at its old spot, then take the ones under
                        // where it touched down.
                        runtime.Construction.ReleaseCells(me);
                        runtime.Construction.TryReserveCells(me, building, context.Variables,
                            me.Position.X, me.Position.Z, size);
                    }
                    break; // Part A settles it on the ground it was flying over
                case LandResult.GroundRefused:
                    WarnOnce($"\u201C{me.Name}\u201D is a {unitType} building and that spot is {ground}, so it can't land there.");
                    break;
                case LandResult.GridRefused:
                    WarnOnce($"\u201C{me.Name}\u201D can't land there — the build grid is already taken.");
                    break;
                default:
                    WarnOnce($"\u201C{me.Name}\u201D is not airborne, so there is nothing to land.");
                    break;
            }
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
            string unit = spec.Thing;
            if (context.Runtime.Production.Enqueue(context.Variables, context.Runtime, building,
                    buildingName, unit))
                return;
            // The queue is full (Task 29 Part E): say so once rather than dropping the order
            // silently. A card's Train button greys out at the same cap.
            double cap = context.Runtime.Production.QueueCapOf(context.Variables, building);
            WarnOnce($"\u201C{buildingName}\u201D already has {cap} unit{(cap == 1 ? "" : "s")} queued \u2014 its production queue is full, so \u201C{unit}\u201D was not queued.");
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

        /// <summary>
        /// <c>research "&lt;upgrade&gt;" at &lt;building&gt;</c> — queue an upgrade for that building's
        /// owner, charging the cost up front so a cancel can refund (Task 27 Part D).
        /// </summary>
        private static void ResearchUpgrade(EventContext context, ActionSpec spec)
        {
            Actor building = ResolveProductionBuilding(context, spec.To);
            if (building == null)
            {
                WarnOnce(string.IsNullOrEmpty(spec.To)
                    ? "\u201Cresearch\u201D needs a building \u2014 run it on the building's sheet or write \u201Cresearch \"Stim Pack\" at Barracks\u201D."
                    : $"I couldn't find the building \u201C{spec.To}\u201D to research \u201C{spec.Thing}\u201D at.");
                return;
            }

            string buildingName = context.Runtime.Construction.BuildingOf(building);
            if (string.IsNullOrEmpty(buildingName))
                buildingName = building.Name;

            int player = PlayerForBuilding(context, building);
            ResearchStartResult result = context.Runtime.Research.Enqueue(context.Variables,
                context.Runtime, building, buildingName, spec.Thing, player);
            switch (result)
            {
                case ResearchStartResult.WrongBuilding:
                    WarnOnce($"\u201C{spec.Thing}\u201D has to be researched at \u201C{Upgrades.RequiresOf(spec.Thing)}\u201D, not \u201C{buildingName}\u201D.");
                    break;
                case ResearchStartResult.AlreadyQueued:
                    WarnOnce($"\u201C{buildingName}\u201D is already researching \u201C{spec.Thing}\u201D.");
                    break;
                case ResearchStartResult.AtMaxLevel:
                    WarnOnce($"\u201C{spec.Thing}\u201D is already at its highest level for {Players.ActorName(player + 1)}.");
                    break;
                case ResearchStartResult.CannotAfford:
                    WarnOnce($"{Players.ActorName(player + 1)} can't afford \u201C{spec.Thing}\u201D right now \u2014 check its cost globals or Upgrades.events.");
                    break;
                case ResearchStartResult.Invalid:
                    WarnOnce("\u201Cresearch\u201D needs an upgrade name \u2014 write \u201Cresearch \"Stim Pack\" at Barracks\u201D.");
                    break;
            }
        }

        /// <summary><c>cancel research &lt;upgrade&gt; [at &lt;building&gt;]</c> — remove it from the
        /// queue and refund what was paid.</summary>
        private static void CancelResearchUpgrade(EventContext context, ActionSpec spec)
        {
            Actor building = ResolveProductionBuilding(context, spec.To);
            if (building == null)
            {
                WarnOnce(string.IsNullOrEmpty(spec.To)
                    ? "\u201Ccancel research\u201D needs a building \u2014 run it on the building's sheet or write \u201Ccancel research \"Stim Pack\" at Barracks\u201D."
                    : $"I couldn't find the building \u201C{spec.To}\u201D to cancel research at.");
                return;
            }
            if (!context.Runtime.Research.Cancel(context.Variables, context.Runtime, building, spec.Thing))
                WarnOnce($"\u201C{building.Name}\u201D has no \u201C{spec.Thing}\u201D in its research queue.");
        }

        /// <summary><c>put "&lt;timer&gt;" on cooldown for &lt;n&gt; seconds</c> — start a named
        /// per-actor timer (Task 27 Part E). Only the acting actor can be cooled down.</summary>
        private static void PutOnCooldown(EventContext context, ActionSpec spec)
        {
            if (context.Me == null)
            {
                WarnOnce("\u201Cput \u2026 on cooldown\u201D needs an actor \u2014 run it on a unit's sheet, not the global Game sheet.");
                return;
            }
            context.Runtime.Cooldowns.Start(context.Me, spec.Thing, spec.Duration);
        }

        /// <summary>The player a building belongs to: its fog entry first, then the acting sheet.</summary>
        private static int PlayerForBuilding(EventContext context, Actor building)
        {
            if (context.Runtime.Fog.TryGetActor(building, out FogActor info))
                return info.Player < 0 ? 0 : info.Player;
            return Tech.PlayerIndexOf(context);
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
            CaptureOrderPoint(order, isPosition);
            context.Runtime.Orders.StartOrder(context.Variables, context.Runtime, me, order);
            BeginOrderMove(context.Runtime, me, order, OrderManager.MoveSpeedOf(context.Variables, me));
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
            // A pair the matchup table forbids is refused here (Task 28 Part D) instead of
            // walking the unit across the map to swing at something it cannot reach. One
            // warning names the global that would allow it. Only `attack` is gated this way;
            // `attack-move` still travels and lets the sheet decide what it meets.
            string refusal = AirTargeting.Refusal(context, target);
            if (refusal != null)
            {
                WarnOnce(refusal);
                return;
            }
            context.Runtime.Wander.Clear(me);
            var order = new UnitOrder
            {
                Kind = UnitOrderKind.Attack,
                Name = "attack",
                Target = target,
                TargetName = targetName,
                HasPositionTarget = isPosition,
            };
            CaptureOrderPoint(order, isPosition);
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
                BeginOrderMove(context.Runtime, me, order, OrderManager.MoveSpeedOf(context.Variables, me), range);
                return;
            }
            BeginOrderMove(context.Runtime, me, order, OrderManager.MoveSpeedOf(context.Variables, me));
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

        /// <summary><c>gather &lt;node&gt;</c> — walk a worker to a resource node and stop beside
        /// it. The economy stays authored (Task 30 Part B), so the word is the order and not the
        /// mining: on arrival `when gather order completes` fires and the node's own sheet ticks
        /// the income. The node is the order's target, so a queued or selection-wide gather walks
        /// to the node it was given for.</summary>
        private static void StartGather(EventContext context, ActionSpec spec)
        {
            Actor me = context.Me as Actor;
            if (me == null)
            {
                WarnOnce("\u201Cgather\u201D needs to run on a unit's sheet \u2014 the global Game sheet has no body to move.");
                return;
            }
            if (!TryResolveOrderTarget(context, spec.Thing, out object target, out string targetName, out bool isPosition))
                return;
            if (target is Actor node && !RightClickOrders.IsResourceNode(context.Variables, node))
                WarnOnce($"\u201C{node.Name}\u201D isn't tagged as a resource node \u2014 give it \u201Cset my NodeAmount to 1500\u201D before gathering from it.");
            context.Runtime.Wander.Clear(me);
            var order = new UnitOrder
            {
                Kind = UnitOrderKind.Gather,
                Name = "gather",
                Target = target,
                TargetName = targetName,
                HasPositionTarget = isPosition,
            };
            CaptureOrderPoint(order, isPosition);
            context.Runtime.Orders.StartOrder(context.Variables, context.Runtime, me, order);
            BeginOrderMove(context.Runtime, me, order, OrderManager.MoveSpeedOf(context.Variables, me),
                OrderManager.DefaultInteractionRange);
        }

        /// <summary><c>repair &lt;target&gt;</c> — walk to a damaged friendly unit or building and
        /// stop beside it, where the sheet's own `me is near …` block heals it. The repair itself
        /// stays authored, so a unit without a repair line simply walks over.</summary>
        private static void StartRepair(EventContext context, ActionSpec spec)
        {
            Actor me = context.Me as Actor;
            if (me == null)
            {
                WarnOnce("\u201Crepair\u201D needs to run on a unit's sheet \u2014 the global Game sheet has no body to move.");
                return;
            }
            if (!TryResolveOrderTarget(context, spec.Thing, out object target, out string targetName, out bool isPosition))
                return;
            if (target is Actor ally && !RightClickOrders.IsDamaged(context.Variables, ally))
                WarnOnce($"\u201C{ally.Name}\u201D isn't damaged \u2014 nothing to repair yet.");
            context.Runtime.Wander.Clear(me);
            var order = new UnitOrder
            {
                Kind = UnitOrderKind.Repair,
                Name = "repair",
                Target = target,
                TargetName = targetName,
                HasPositionTarget = isPosition,
            };
            CaptureOrderPoint(order, isPosition);
            context.Runtime.Orders.StartOrder(context.Variables, context.Runtime, me, order);
            BeginOrderMove(context.Runtime, me, order, OrderManager.MoveSpeedOf(context.Variables, me),
                OrderManager.DefaultInteractionRange);
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

        /// <summary>
        /// Remembers where <c>there</c> pointed when the order was written (Task 29 Part C), so
        /// the order owns its destination instead of reading the latest click when it starts.
        /// </summary>
        private static void CaptureOrderPoint(UnitOrder order, bool isPosition)
        {
            if (order == null || !isPosition || !LastClickPosition.HasValue)
                return;
            Vector3 point = LastClickPosition.Value;
            order.SetPoint(point.X, point.Y, point.Z);
        }

        /// <summary>
        /// The right click's default order (Task 29 Part C): one decision from
        /// <see cref="RightClickOrders"/> applied to every selected actor, so a drag-selected
        /// group all moves, attacks or sets a rally point. Shift appends to each unit's queue
        /// instead of replacing it, and a selection the table answers with "no order" is left
        /// alone. Returns how many actors took an order.
        /// The target's team comes from the fog grid's `my Player` and, when that separates
        /// nobody, from `my Hostile` (`enemy type` writes it); a fog-hidden target is ground.
        /// </summary>
        public static int IssueDefaultOrder(EventRuntime runtime, Actor target, bool hasPoint,
            Vector3 point, bool append)
        {
            if (runtime == null || runtime.Selection.Count == 0)
                return 0;
            var selected = new List<object>(runtime.Selection.Selected);
            int issued = 0;
            bool warnedRefusal = false;
            foreach (object item in selected)
            {
                Actor me = item as Actor;
                if (me == null || me.Scene == null)
                    continue;
                bool isBuilding = runtime.Construction.IsBuilding(me);
                bool lifted = runtime.LiftOff.IsFlying(me);
                bool samePlayer = SamePlayerAs(runtime, me, target);
                RightClickTarget kind = RightClickOrders.Classify(
                    target != null, hasPoint,
                    isBuilding: runtime.IsBuildingActor(target),
                    samePlayer: samePlayer,
                    isResourceNode: RightClickOrders.IsResourceNode(runtime.Variables, target),
                    hostile: RightClickOrders.IsDeclaredEnemy(runtime.Variables, me, target),
                    hidden: HiddenFromPlayer(runtime, samePlayer, target));
                DefaultOrderKind order = RightClickOrders.Resolve(
                    hasSelection: true, buildingSelection: isBuilding, liftedBuilding: lifted, target: kind,
                    targetDamaged: RightClickOrders.IsDamaged(runtime.Variables, target));
                if (order == DefaultOrderKind.None)
                    continue;

                var context = new EventContext
                {
                    Variables = runtime.Variables,
                    World = runtime.World,
                    Runtime = runtime,
                    Me = me,
                    Other = target,
                    Sheet = runtime.FindSheetFor(me),
                };

                if (order == DefaultOrderKind.Rally)
                {
                    runtime.Orders.SetRallyPoint(me, target, target == null ? "there" : target.Name,
                        hasPoint, point.X, point.Y, point.Z);
                    issued++;
                    continue;
                }

                if (order == DefaultOrderKind.Attack)
                {
                    // The same matchup gate an authored `attack` order uses (Task 28 Part D), so
                    // a ground unit is not walked over to swing at the sky. One warning for the
                    // whole click, not one per selected actor.
                    string refusal = AirTargeting.Refusal(context, target);
                    if (refusal != null)
                    {
                        if (!warnedRefusal)
                        {
                            WarnOnce(refusal);
                            warnedRefusal = true;
                        }
                        continue;
                    }
                }

                bool hadOrders = runtime.Orders.HasOrders(me);
                UnitOrderKind kindOfOrder = UnitOrderKindOf(order);
                var unitOrder = new UnitOrder
                {
                    Kind = kindOfOrder,
                    Name = RightClickOrders.OrderName(order),
                    Target = target,
                    TargetName = target == null ? "there" : target.Name,
                    HasPositionTarget = target == null && hasPoint,
                };
                if (target == null && hasPoint)
                    unitOrder.SetPoint(point.X, point.Y, point.Z);
                runtime.Wander.Clear(me);
                if (append)
                    runtime.Orders.AddOrder(runtime.Variables, runtime, me, unitOrder);
                else
                    runtime.Orders.StartOrder(runtime.Variables, runtime, me, unitOrder);
                issued++;
                // Appended behind a running order, it waits for the bridge's pending-order loop;
                // a replacing order always walks, so a second click retargets a moving unit.
                if (!RightClickOrders.StartsWalkNow(append, hadOrders))
                    continue;
                double range = kindOfOrder == UnitOrderKind.Attack ? OrderManager.StopDistanceFor(context)
                    : kindOfOrder == UnitOrderKind.Move ? 0
                    : OrderManager.DefaultInteractionRange;
                BeginOrderMove(runtime, me, unitOrder, OrderManager.MoveSpeedOf(context.Variables, me), range);
            }
            return issued;
        }

        /// <summary>
        /// The order kind a table decision writes. The queue, the order triggers and
        /// `my current order` all name it, so `gather` and `repair` read back as themselves
        /// instead of as a move.
        /// </summary>
        private static UnitOrderKind UnitOrderKindOf(DefaultOrderKind order)
        {
            switch (order)
            {
                case DefaultOrderKind.Attack:
                    return UnitOrderKind.Attack;
                case DefaultOrderKind.Gather:
                    return UnitOrderKind.Gather;
                case DefaultOrderKind.Repair:
                    return UnitOrderKind.Repair;
                default:
                    return UnitOrderKind.Move;
            }
        }

        /// <summary>
        /// Whether two actors belong to the same player as the fog grid records it (`my Player`,
        /// default 0). A scene that never wrote a player therefore reads as one team here, which
        /// is why <see cref="RightClickOrders.IsDeclaredEnemy"/> is the second half of the enemy
        /// test: `my Hostile` still marks the enemies in an unauthored scene.
        /// </summary>
        private static bool SamePlayerAs(EventRuntime runtime, Actor me, Actor other)
        {
            if (runtime == null || me == null || other == null)
                return true;
            return runtime.Fog.PlayerOf(me) == runtime.Fog.PlayerOf(other);
        }

        /// <summary>
        /// Whether fog is hiding <paramref name="other"/> from the local player. Fog only swaps
        /// draw passes, so a hidden unit's collider still catches the right click's ray; the
        /// click then reads as bare ground instead of ordering an attack on a unit nobody can
        /// see. Only an actor the grid knows and places on another team can be hidden, so a
        /// scene with no players authored (everyone on player 0) is never affected.
        /// </summary>
        private static bool HiddenFromPlayer(EventRuntime runtime, bool samePlayer, Actor other)
        {
            if (runtime == null || other == null || samePlayer)
                return false;
            FogOfWarManager fog = runtime.Fog;
            return fog != null && fog.Enabled && !fog.IsVisibleTo(fog.LocalPlayer, other);
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
            // A clicked rally keeps its own point (Task 29 Part C), so a unit trained later
            // walks to the spot the player picked rather than the last click anywhere.
            Vector3? rallyPoint = isPosition ? LastClickPosition : null;
            context.Runtime.Orders.SetRallyPoint(me, target, isPosition ? "there" : targetName,
                rallyPoint.HasValue, rallyPoint?.X ?? 0, rallyPoint?.Y ?? 0, rallyPoint?.Z ?? 0);
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
        /// A selected unit's own slot around a group destination (Task 23 Part C). A unit that is
        /// not selected, or is the whole selection, keeps the point unchanged.
        /// </summary>
        private static Vector3 FormationSlot(EventRuntime runtime, Actor me, Vector3 destination)
        {
            Selection selection = runtime?.Selection;
            if (selection == null || selection.Count <= 1)
                return destination;
            IReadOnlyList<object> selected = selection.Selected;
            int index = -1;
            for (int i = 0; i < selected.Count; i++)
            {
                if (ReferenceEquals(selected[i], me))
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
                return destination;
            Formations.Slot(Formations.KindFrom(runtime.Variables), index, selected.Count,
                Formations.SpacingFrom(runtime.Variables),
                destination.X - me.Position.X, destination.Z - me.Position.Z,
                out double offsetX, out double offsetZ);
            if (offsetX == 0 && offsetZ == 0)
                return destination;
            return new Vector3(destination.X + (float)offsetX, destination.Y,
                destination.Z + (float)offsetZ);
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

        /// <summary>Starts NavMesh movement for an order; a path the mesh refutes raises
        /// <c>when path blocked</c> and stops the unit instead of walking through walls, while a
        /// mesh that cannot answer at all (nothing baked, still building, a point off it) walks
        /// the straight line and raises nothing. <paramref name="stopDistance"/> shortens the walk
        /// to that distance from the target (attack range).</summary>
        private static void BeginOrderMove(EventRuntime runtime, Actor me, UnitOrder order, double speed,
            double stopDistance = 0)
        {
            if (runtime == null || me == null || me.Scene == null || order == null)
                return;
            Actor targetActor = order.Target as Actor;
            Vector3 destination;
            bool positionTarget = order.HasPositionTarget || order.TargetName == "there";
            bool groupOrder = positionTarget;
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
                // An order keeps the point it was given (Task 29 Part C), so a queued or
                // selection-wide order does not follow a later click. Only orders written
                // before this existed still read the last click.
                if (order.HasPoint)
                    destination = new Vector3((float)order.PointX, (float)order.PointY, (float)order.PointZ);
                else if (LastClickPosition.HasValue)
                    destination = LastClickPosition.Value;
                else
                    return;
            }
            else
                return;

            // A group's ground order spreads over a formation so the units don't stack on one
            // point (Task 23 Part C). Actor targets keep converging on the actor.
            if (groupOrder)
                destination = FormationSlot(runtime, me, destination);

            // A flyer ignores the NavMesh and the terrain map (Task 28 Part B): one waypoint, at
            // the height it holds — or the target's, when the target flies too.
            if (AirPath.IsFlying(runtime.Variables, me))
            {
                if (targetActor != null && targetActor.Scene != null)
                    ChaseAltitude(runtime, me, targetActor);
                double flyingAt = AltitudeOf(me);
                destination = new Vector3(destination.X,
                    (float)Altitude.GroundY(me.Position.Y, flyingAt), destination.Z);
                Remember(new OngoingMove
                {
                    Subject = me,
                    Target = snapToTarget ? targetActor : null,
                    Goto = true,
                    HasPositionTarget = positionTarget,
                    PositionTarget = destination,
                    Path = DirectRoute(destination),
                    PathIndex = 0,
                    Speed = speed,
                    Mode = PhysicsMoveMode.Transform,
                    CompletesOrder = true,
                    ArrivesAtThere = arrivesAtThere,
                });
                return;
            }

            // A destination this unit's type can never stand on is a blocked path, not a walk
            // that stalls at the shoreline (Task 22 Part B).
            if (TerrainBlocksStep(me, destination))
            {
                runtime.Orders.ReportPathBlocked(runtime, me);
                return;
            }

            NavRouteAnswer answer = QueryNavRoute(me.Position, destination, out Vector3[] route);
            if (NavRoute.Stops(answer))
            {
                // Both ends sit on a baked mesh that has no way between them: this is a real
                // block, so the unit stops and `when path blocked` fires. Walking the straight
                // line here is what used to put units through walls.
                runtime.Orders.ReportPathBlocked(runtime, me);
                WarnPathBlocked(me, positionTarget ? "the clicked spot" : "its target");
                return;
            }
            if (NavRoute.WalksStraight(answer))
            {
                // The mesh could not answer — nothing baked, still building, or a point off it —
                // so nothing is actually in the way: walk the straight line and raise no
                // trigger, because the unit never stopped.
                WarnNoNavRoute(me, positionTarget ? "the clicked spot" : "its target");
                route = DirectRoute(destination);
            }

            Remember(new OngoingMove
            {
                Subject = me,
                Target = snapToTarget ? targetActor : null,
                Goto = true,
                HasPositionTarget = positionTarget,
                PositionTarget = destination,
                Path = route,
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
            if (runtime.Orders.RallyHasPointOf(spawnAt))
            {
                runtime.Orders.RallyPointPositionOf(spawnAt,
                    out double rallyX, out double rallyY, out double rallyZ);
                order.SetPoint(rallyX, rallyY, rallyZ);
            }
            runtime.Orders.StartOrder(context.Variables, runtime, clone, order);
            BeginOrderMove(runtime, clone, order, OrderManager.MoveSpeedOf(context.Variables, clone));
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
            context.Runtime.MarkSpawnedRoot(instance, source, SaveSpawned.KindPrefab);
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

        private static bool SetControl<T>(string thing, Action<T> apply, bool reveal = true)
            where T : FlaxEngine.GUI.Control
        {
            var control = FindControl<T>(thing, out UIControl ui);
            if (control == null)
                return false;
            // A `hide GameText` earlier in the session must not brick `show text`. A bind and the
            // card's `set button … enabled` rules pass reveal: false — they write state, so a
            // button the sheet hid must stay hidden until the sheet shows it again.
            if (reveal && !ui.IsActive)
                ui.IsActive = true;
            apply(control);
            return true;
        }

        /// <summary>Finds the GUI label named by an event.</summary>
        private static FlaxEngine.GUI.Label FindLabel(string thing, out UIControl ui)
            => FindControl<FlaxEngine.GUI.Label>(thing, out ui);

        private static bool SetLabel(string thing, string text, bool reveal = true)
            => SetControl<FlaxEngine.GUI.Label>(thing, label =>
            {
                label.Text = text; // Label.Text is a LocalizedString; string converts implicitly
                label.Visible = true;
            }, reveal);

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
            // `play sound "shot" at Enemy`: a positional source parented to that actor, so it
            // follows it and attenuates with distance (Task 31 Part D). Flax only attenuates a
            // clip imported as a 3D sound, so a 2D clip plays the same at any distance.
            Actor at = null;
            if (!string.IsNullOrEmpty(spec.To))
            {
                at = ResolveThing(context, spec.To) as Actor;
                if (at == null)
                    WarnOnce($"I couldn't find \u201C{spec.To}\u201D to play \u201C{spec.Thing}\u201D at — playing it on \u201C{me?.Name}\u201D instead.");
            }

            var audio = new AudioSource
            {
                HideFlags = HideFlags.DontSave,
                Volume = (float)context.Runtime.Audio.EffectiveSoundVolume,
                Clip = clip,
                AllowSpatialization = at != null,
                MinDistance = SpatialMinDistance,
                Position = me != null ? me.Position : Vector3.Zero,
            };
            Level.SpawnActor(audio);
            if (at != null)
            {
                audio.Parent = at;
                audio.LocalPosition = Vector3.Zero;
            }
            audio.Play();
            ActiveSounds.Add((me, audio));
            FlaxEngine.Object.Destroy(audio, clip.Length);
            // The clip is a world event too, so `when me hears "shot" by 12` can answer it.
            EmitSound(AssetNames.Key(spec.Thing), audio.Position, me);
        }

        /// <summary>Distance (world units) before a positional `play sound ... at` starts to fade.</summary>
        private const float SpatialMinDistance = 200;

        /// <summary>
        /// The audio tick (Task 31): keeps the volume globals and <see cref="AudioMix"/> in step,
        /// advances fades and ducks, resumes the track after a scene change, and applies the three
        /// volume groups to every source this bridge owns.
        /// </summary>
        private static void StepAudio(float deltaTime)
        {
            var runtime = EventsManager.Instance?.Runtime;
            if (runtime == null)
                return;
            AudioMix mix = runtime.Audio;
            EventVariables variables = runtime.Variables;

            // `set master volume` / `set sound volume` write the globals, and a sheet or a
            // settings slider may write them directly, so the globals win each tick.
            if (variables.HasGlobal(AudioMix.MasterVariable))
                mix.SetMasterVolume(variables.GetGlobal(AudioMix.MasterVariable).AsNumber());
            if (variables.HasGlobal(AudioMix.SoundVariable))
                mix.SetSoundVolume(variables.GetGlobal(AudioMix.SoundVariable).AsNumber());
            if (variables.HasGlobal(AudioMix.MusicVariable))
            {
                double authored = variables.GetGlobal(AudioMix.MusicVariable).AsNumber();
                if (Math.Abs(authored - MusicGlobalSeen) > 0.0001)
                    mix.SetMusicVolume(authored);
            }

            mix.Update(deltaTime);
            // A fade writes its own level back out, so `bind` follows the ramp and the next tick
            // does not mistake our own write for an authored change.
            variables.SetGlobal(AudioMix.MusicVariable, ValueBox.OfNumber(mix.Music));
            MusicGlobalSeen = mix.Music;

            // A scene change destroys the music actor; the mix remembers the track and the
            // playhead, so the soundtrack carries on instead of stopping at the level load.
            if (mix.Playing && (MusicSource == null || MusicSource.Scene == null))
                ResumeMusic(mix);

            if (MusicSource != null && MusicSource.Scene != null)
            {
                if (MusicSource.State == AudioSource.States.Playing)
                {
                    MusicWasPlaying = true;
                }
                else if (MusicWasPlaying)
                {
                    // A non-looping track reached its end: `when music ends`.
                    MusicWasPlaying = false;
                    mix.Playing = false;
                    string track = mix.Track;
                    StopMusicSource();
                    runtime.RaisePushAll(new PushSignal { Kind = TriggerKind.MusicEnd, Name = track });
                    mix.Track = "";
                }
                else if (MusicSource != null)
                {
                    mix.Playhead = MusicSource.Time;
                }
            }

            if (MusicSource != null && MusicSource.Scene != null)
            {
                mix.Playhead = MusicSource.Time;
                MusicSource.Volume = (float)mix.EffectiveMusicVolume;
            }
            float soundVolume = (float)mix.EffectiveSoundVolume;
            foreach (var (_, source) in ActiveSounds)
                if (source != null && source.Scene != null)
                    source.Volume = soundVolume;
        }

        /// <summary>
        /// `play music "x"` / `loop music "x"` (one track at a time, Task 31 Part A), plus the
        /// list form `play music random from "a, b, c"` — one clip is rolled when the line runs,
        /// so a level can pick its own track without an if-block per track.
        /// </summary>
        private static void StartMusic(EventContext context, ActionSpec spec, bool loop)
        {
            string clipName = spec.Thing;
            if (spec.Choices.Count > 0)
                clipName = AudioMix.PickChoice(spec.Choices, Rng.NextDouble());
            if (string.IsNullOrEmpty(clipName))
            {
                WarnOnce("\u201Cplay music\u201D needs a clip name, like \u201Cplay music \"level1\"\u201D, or a list: \u201Cplay music random from \"level1, level2\"\u201D.");
                return;
            }
            AudioMix mix = context.Runtime.Audio;
            string key = AssetNames.Key(clipName);
            // The same track already running is left alone, so a `when scene starts` line that
            // re-runs after a hot reload never restarts the song from the top.
            if (mix.Playing && mix.TrackLooping == loop &&
                string.Equals(AssetNames.Key(mix.Track), key, StringComparison.OrdinalIgnoreCase) &&
                MusicSource != null && MusicSource.Scene != null)
                return;

            var clip = LoadAudioClip(clipName);
            if (clip == null)
            {
                // The rolled name is the one reported, so a typo in a list is easy to spot.
                WarnOnce($"Could not load music \u201C{clipName}\u201D. Imported clips: {AvailableSoundNames()}.");
                return;
            }

            SpawnMusicSource(clip, loop, 0);
            // A new track owns the player: this also fades it in when the previous music was
            // faded out, which is the menu -> level hand-off with no extra line.
            mix.StartMusic(clipName, loop);
            context.Runtime.Variables.SetGlobal(AudioMix.MusicVariable, ValueBox.OfNumber(mix.Music));
            MusicGlobalSeen = mix.Music;
        }

        /// <summary>Starts the one music source, replacing any previous track.</summary>
        private static void SpawnMusicSource(AudioClip clip, bool loop, double startAt)
        {
            StopMusicSource();
            var source = new AudioSource
            {
                HideFlags = HideFlags.DontSave,
                Clip = clip,
                IsLooping = loop,
                AllowSpatialization = false,
                Volume = 1,
            };
            Level.SpawnActor(source);
            if (startAt > 0.01)
                source.Time = (float)startAt;
            source.Play();
            MusicSource = source;
            MusicWasPlaying = true;
        }

        /// <summary>Re-creates the music source after a scene change, resuming the playhead.</summary>
        private static void ResumeMusic(AudioMix mix)
        {
            var clip = LoadAudioClip(mix.Track);
            if (clip == null)
            {
                WarnOnce($"Could not resume music \u201C{mix.Track}\u201D after a scene change. Imported clips: {AvailableSoundNames()}.");
                mix.Playing = false;
                return;
            }
            SpawnMusicSource(clip, mix.TrackLooping, mix.Playhead);
        }

        private static void StopMusicSource()
        {
            if (MusicSource == null)
                return;
            MusicSource.Stop();
            FlaxEngine.Object.Destroy(MusicSource);
            MusicSource = null;
            MusicWasPlaying = false;
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
                Volume = (float)context.Runtime.Audio.EffectiveSoundVolume,
                Clip = clip,
                AllowSpatialization = false,
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

        public static void SetText(string thing, string text, bool reveal = true)
        {
            if (string.IsNullOrEmpty(thing))
            {
                WarnOnce("\u201Cset text of\u201D needs the name of the thing whose text changes — like \u201Cset text of ScoreLabel to Score\u201D.");
                return;
            }
            if (SetLabel(thing, text, reveal))
                return;
            // A TextBox (input field) is not a Label; write its Text instead.
            var box = FindControl<FlaxEngine.GUI.TextBox>(thing, out UIControl boxUi);
            if (box != null)
            {
                if (reveal && boxUi != null && !boxUi.IsActive)
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
            if (!SetControl<FlaxEngine.GUI.Button>(thing, button => button.Enabled = enabled, reveal: false))
                WarnOnce($"\u201Cset button {thing} { (enabled ? "enabled" : "disabled") }\u201D needs an actor with a Button on a UIControl (I couldn't find one called \u201C{thing}\u201D). Buttons in this scene: {ControlActorNames<FlaxEngine.GUI.Button>()}.");
        }

        /// <summary><c>set button &lt;thing&gt; text to &lt;value&gt;</c>: a Button is a Label, so this reuses SetText.</summary>
        public static void SetButtonText(string thing, string text) => SetText(thing, text);

        /// <summary><c>set image of &lt;thing&gt; to &lt;sprite&gt;</c>: swaps the Image control's brush to the named sprite.</summary>
        public static void SetImageBrush(string thing, string sprite)
            => SwapImageBrush(thing, sprite, "set image of");

        /// <summary><c>set portrait of &lt;thing&gt; to &lt;sprite&gt;</c> (Task 35 Part B): the same
        /// Image swap and the same sprite resolver as <c>set image</c>, so one imported portrait
        /// works for either word. Only the art changes — the speaker label is its own control.</summary>
        public static void SetPortrait(string thing, string sprite)
            => SwapImageBrush(thing, sprite, "set portrait of");

        /// <summary>The one Image swap behind both words: the sprite is a loose name resolved by
        /// <see cref="LoadTexture"/> (the index shared with <see cref="CursorView"/>), and a miss
        /// warns once with the pictures the project actually has.</summary>
        private static void SwapImageBrush(string thing, string sprite, string word)
        {
            if (string.IsNullOrEmpty(thing) || string.IsNullOrEmpty(sprite))
            {
                WarnOnce($"\u201C{word}\u201D needs a thing and a sprite name — like \u201C{word} HeartIcon to \"heart_full\"\u201D.");
                return;
            }
            var image = FindControl<FlaxEngine.GUI.Image>(thing, out UIControl ui);
            if (image == null)
            {
                WarnOnce($"\u201C{word} {thing} to \"{sprite}\"\u201D needs an actor with an Image on a UIControl (I couldn't find one called \u201C{thing}\u201D). Images in this scene: {ControlActorNames<FlaxEngine.GUI.Image>()}.");
                return;
            }
            var texture = LoadTexture(sprite);
            if (texture == null)
            {
                WarnOnce($"I couldn't find a sprite texture named \u201C{sprite}\u201D for \u201C{word} {thing}\u201D. Imported textures: {AvailableTextureNames()}.");
                return;
            }
            image.Brush = new FlaxEngine.GUI.TextureBrush { Texture = texture };
        }

        /// <summary>The actor a scene authors as `BacklogList`, with row labels beneath it.</summary>
        public const string BacklogListActor = "BacklogList";

        /// <summary>The row actors <c>add to backlog</c> fills: `BacklogRow1`, `BacklogRow2`, …</summary>
        public const string BacklogRowPrefix = "BacklogRow";

        /// <summary>
        /// <c>add to backlog</c> (Task 35 Part D): shows the newest lines in the scene's authored
        /// row labels — `BacklogRow1`, `BacklogRow2`, … up to the first gap — with the newest line
        /// in the last row, and hides the rows the history does not fill. The rows are ordinary
        /// UIControl actors, so their font, width and scroll container are editor content; Task
        /// 34's list words can replace this renderer once they exist.
        /// </summary>
        public static void SetBacklog(IReadOnlyList<BacklogLine> lines)
        {
            var rows = new List<FlaxEngine.GUI.Label>();
            var rowActors = new List<Actor>();
            for (int index = 1; ; index++)
            {
                string name = BacklogRowPrefix + index;
                var label = FindLabel(name, out _);
                if (label == null)
                    break;
                rows.Add(label);
                rowActors.Add(Level.FindActor(name));
            }
            if (rows.Count == 0)
            {
                WarnOnce($"I couldn't find the backlog. Create one UIControl actor per visible line, named \u201C{BacklogRowPrefix}1\u201D, \u201C{BacklogRowPrefix}2\u201D, … (under a \u201C{BacklogListActor}\u201D panel or a scroll panel if the list is long).");
                return;
            }
            for (int i = 0; i < rows.Count; i++)
            {
                int lineIndex = lines.Count - rows.Count + i;
                bool has = lineIndex >= 0;
                rows[i].Text = has ? lines[lineIndex].Text : "";
                rows[i].Visible = has;
                if (rowActors[i] != null)
                    rowActors[i].IsActive = has;
            }
        }

        /// <summary>Show or hide a numbered choice button (Task 35 Part C). The button is an
        /// ordinary scene actor, so this is the same show/hide every sheet can write itself.</summary>
        public static void SetChoiceVisible(EventContext context, string thing, bool visible)
            => SetActive(context, thing, visible);

        /// <summary>The scene actor <c>set speaker to</c> writes (Task 35 Part B). The portrait is
        /// a different actor, so the name and the picture never share a control.</summary>
        public const string SpeakerLabelActor = "SpeakerLabel";

        /// <summary><c>set speaker to &lt;name&gt;</c> (Task 35 Part B): writes the scene's own
        /// <c>SpeakerLabel</c> and leaves the portrait art alone. The runtime already recorded the
        /// name in the <c>Speaker</c> global; an empty name clears the label.</summary>
        public static void SetSpeaker(string name)
        {
            if (SetLabel(SpeakerLabelActor, name))
                return;
            WarnOnce($"I couldn't find the speaker's label. Create a UIControl actor named \u201C{SpeakerLabelActor}\u201D with a Label (the portrait actor is separate). Labels in this scene: {ControlActorNames<FlaxEngine.GUI.Label>()}.");
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
        public static void SetProgress(string thing, double value, bool reveal = true)
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
            }, reveal))
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

        /// <summary>Loads a texture by name, ignoring folder and extension (mirrors <see cref="LoadAudioClip"/>).
        /// Shared with <see cref="CursorView"/>, which swaps its frames through the same index.</summary>
        internal static FlaxEngine.Texture LoadTexture(string name)
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
        /// The scene section of a save (Task 30 Part B): the level that is playing and the scenes
        /// added on top of it, in load order. Flax knows the loaded set; the runtime fills in the
        /// level name and number from the globals.
        /// </summary>
        public static SaveScene CaptureSaveScene()
        {
            var scene = new SaveScene();
            foreach (Scene loaded in Level.Scenes)
            {
                if (loaded == null)
                    continue;
                string name = SceneNameForSave(loaded);
                if (string.IsNullOrEmpty(name))
                    continue;
                if (scene.Name.Length == 0)
                    scene.Name = name;
                else
                    scene.Additive.Add(name);
            }
            return scene;
        }

        /// <summary>The name a later `load scene` can resolve: the asset's file name when the
        /// content database knows the scene, else the scene's own root name (a copied scene keeps
        /// the old root name).</summary>
        private static string SceneNameForSave(Scene scene)
        {
            if (scene == null)
                return "";
            if (Content.GetAssetInfo(scene.ID, out AssetInfo info) && !string.IsNullOrEmpty(info.Path))
                return System.IO.Path.GetFileNameWithoutExtension(info.Path);
            return scene.Name ?? "";
        }

        /// <summary>
        /// <c>load game</c> (Task 30 Part B): loads one saved overlay on top of the level with its
        /// sheets attached and `when scene starts` pushed, exactly like `add scene`. Returns false
        /// only when the scene isn't in the project; an overlay that is already loaded counts as
        /// restored.
        /// </summary>
        public static bool AddSavedScene(string name)
        {
            EventRuntime runtime = EventsManager.Instance?.Runtime;
            if (runtime == null)
                return false;
            if (!TryFindSceneId(name, out Guid id))
            {
                WarnOnce($"The save expects the scene \u201C{name}\u201D on top of its level, but I couldn't find it. Scenes: {SceneNames()}.");
                return false;
            }
            if (SceneLoadedAs(name, id) != null)
                return true;
            Scene loaded = LoadSceneAdditive(id);
            if (loaded == null)
                return false;
            var context = new EventContext { Runtime = runtime };
            FinishLoadedScene(context, loaded, name, sceneStarts: true);
            return true;
        }

        /// <summary>
        /// The engine half of one actor record (Task 30 Part C): world position, orientation and
        /// scale exactly as the transform serializes them. The runtime cannot read these, so the
        /// bridge passes this method to <see cref="SaveSnapshot.Capture"/>.
        /// </summary>
        public static SaveTransform CaptureSaveTransform(object actor)
        {
            var saved = new SaveTransform();
            if (actor is not Actor subject)
                return saved;
            Vector3 position = subject.Position;
            Quaternion orientation = subject.Orientation;
            Vector3 scale = subject.Scale;
            saved.Position = new SaveVector { X = position.X, Y = position.Y, Z = position.Z };
            saved.Orientation = new SaveQuaternion
            {
                X = orientation.X,
                Y = orientation.Y,
                Z = orientation.Z,
                W = orientation.W,
            };
            saved.Scale = new SaveVector { X = scale.X, Y = scale.Y, Z = scale.Z };
            return saved;
        }

        /// <summary>Whether the actor is active in the scene, for the save's `active` flag.</summary>
        public static bool CaptureSaveActive(object actor)
        {
            return actor is Actor subject ? subject.IsActive : true;
        }

        /// <summary>The live actor a saved name refers to, or null. Flax's lookup is case-sensitive,
        /// and a save writes the exact runtime name, so no normalizing is wanted here.</summary>
        public static object FindSaveActor(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            return Level.FindActor(name);
        }

        /// <summary>
        /// Puts a saved actor back (Task 30 Part C) through the same warp `teleport` uses: any
        /// move or push that could drag it away is cancelled and a dynamic body is stopped before
        /// the world transform is written, so a restored actor stays where the save put it.
        /// </summary>
        public static void ApplySaveTransform(object actor, SaveTransform saved)
        {
            if (actor is not Actor subject || saved == null)
                return;
            SaveVector position = saved.Position ?? new SaveVector();
            TeleportSubject(subject, new Vector3((float)position.X, (float)position.Y, (float)position.Z));
            SaveQuaternion orientation = saved.Orientation ?? new SaveQuaternion();
            subject.Orientation = new Quaternion(
                (float)orientation.X, (float)orientation.Y,
                (float)orientation.Z, (float)orientation.W);
            SaveVector scale = saved.Scale ?? SaveVector.One;
            subject.Scale = new Vector3((float)scale.X, (float)scale.Y, (float)scale.Z);
        }

        /// <summary>Restores a saved actor's active flag; a hidden actor stays hidden after a load.</summary>
        public static void ApplySaveActive(object actor, bool active)
        {
            if (actor is Actor subject)
                subject.IsActive = active;
        }

        /// <summary>
        /// The parent actor's name for a spawned root, or "" when it sits at the scene root
        /// (Task 30 Part D). Flax makes every scene an actor at the top of its tree, so a parent
        /// that is a scene means "no parent"; only a real actor parent is worth saving.
        /// </summary>
        public static string CaptureSaveParent(object actor)
        {
            if (actor is not Actor subject)
                return "";
            Actor parent = subject.Parent;
            if (parent == null || parent is Scene)
                return "";
            return parent.Name ?? "";
        }

        /// <summary>
        /// The engine half of one spawned record (Task 30 Part D): loads the prefab asset or
        /// clones the template actor and returns the new root, or null when the source is gone.
        /// The saved transform is not written here — <see cref="SaveSpawns.Restore"/> does that
        /// after every root exists and has its parent, so parenting cannot move it afterwards.
        /// </summary>
        public static object CreateSavedSpawned(SaveSpawned saved)
        {
            if (saved == null)
                return null;
            string kind = (saved.Kind ?? "").Trim().ToLowerInvariant();
            if (kind == SaveSpawned.KindPrefab)
            {
                Prefab prefab = LoadPrefabAsset(saved.Source);
                if (prefab == null)
                    return null;
                return PrefabManager.SpawnPrefab(prefab, Vector3.Zero, Quaternion.Identity);
            }
            if (kind == SaveSpawned.KindClone)
            {
                Actor template = Level.FindActor(saved.Source);
                if (template == null)
                    return null;
                var clone = template.Clone();
                clone.IsActive = true;
                Level.SpawnActor(clone);
                return clone;
            }
            return null;
        }

        /// <summary>
        /// Finishes a recreated spawned root (Task 30 Part D): keeps the saved name verbatim,
        /// because that is the key the actor records use, then gives it its sheet tree. The
        /// spawn naming already made the name unique in the run, so it is not re-run here.
        /// </summary>
        public static void FinishSavedSpawned(object actor, SaveSpawned saved)
        {
            if (actor is not Actor subject || saved == null)
                return;
            if (!string.IsNullOrEmpty(saved.Name))
                subject.Name = saved.Name;
            string kind = (saved.Kind ?? "").Trim();
            if (string.Equals(kind, SaveSpawned.KindPrefab, StringComparison.OrdinalIgnoreCase))
                EventsManager.Instance?.AttachSpawnedTree(subject);
            else
                EventsManager.Instance?.AttachSpawnedActor(subject);
        }

        /// <summary>Puts a restored spawned root under its saved parent (Task 30 Part D).</summary>
        public static void SetSaveParent(object actor, object parent)
        {
            if (actor is Actor child && parent is Actor parentActor)
                child.Parent = parentActor;
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

            // Task 33 Part A: the speed is the actor's, not the clip's, and Flax applies
            // UpdateSpeed to the running graph live — no rebuild, no restart, no pop.
            float speed = AnimSpeedOf(me);
            model.UpdateSpeed = speed;
            model.AnimationGraph = graph;
            if (state?.PreviousGraph != null)
                FlaxEngine.Object.Destroy(state.PreviousGraph); // keep only two graphs alive

            AnimStates[me] = new AnimState
            {
                Clip = clip,
                Name = spec.Thing.Trim(),
                Loop = loop,
                OneShot = !loop,
                EndTime = loop ? double.MaxValue : AnimPlayback.EndTime(Time.GameTime, anim.Length, speed),
                StartTime = Time.GameTime,
                Length = anim.Length,
                Play = ++AnimPlayCounter,
                Graph = graph,
                PreviousGraph = state?.Graph,
                PreviousClip = state?.Clip,
                PreviousName = state?.Name,
                PreviousLoop = state?.Loop ?? true,
            };
        }

        /// <summary>
        /// <c>set anim speed to &lt;n&gt;</c> (Task 33 Part A): the acting actor's playback speed. It lives for
        /// the actor, not the clip, so a new clip inherits it, and it is written to Flax's
        /// <see cref="AnimatedModel.UpdateSpeed"/> — a live delta-time scale, so the running clip
        /// changes speed instead of restarting.
        /// </summary>
        private static void SetAnimSpeed(EventContext context, ActionSpec spec)
        {
            var me = context.Me as Actor;
            if (me == null)
            {
                WarnOnce("\u201Cset anim speed\u201D needs to run on an actor's sheet — the global Game sheet has no model to animate.");
                return;
            }

            double asked = ResolveNumber(context, spec);
            float speed = AnimPlayback.ClampSpeed(asked);
            if (Math.Abs(asked - speed) > 1e-6)
                WarnOnce($"\u201Cset anim speed to {asked}\u201D is outside {AnimPlayback.MinSpeed}\u2013{AnimPlayback.MaxSpeed} — using {speed}.");

            float previous = AnimSpeedOf(me);
            AnimSpeeds[me] = speed;

            var model = ModelOf(me);
            if (model != null)
                model.UpdateSpeed = speed;

            // A running one-shot counted its end at the old speed, so `when anim ... ends` would
            // fire early or late: re-time what is left of the clip.
            if (AnimStates.TryGetValue(me, out AnimState state) && state.Length > 0)
            {
                if (state.OneShot)
                    state.EndTime = AnimPlayback.RetimedEnd(Time.GameTime, state.StartTime, state.Length, previous, speed);
                // `when anim ... reaches <n>%` reads progress from the same anchor, so move the
                // anchor too: the time already played at the old speed still counts and the percent
                // continues instead of jumping (or rewinding) at the speed change.
                state.StartTime = AnimPlayback.ReanchoredStart(Time.GameTime, state.StartTime, state.Length,
                    previous, speed);
            }
        }

        /// <summary>
        /// The clip an actor is playing right now, how far through it playback has got (0–1, or past
        /// 1 after a loop's first pass) and this play's token (Task 33 Part B). False when the actor
        /// is not playing that clip, so `when anim "Walk" reaches 50%` never fires from a stale clip.
        /// </summary>
        public static bool TryGetAnimProgress(object key, string clip, out double progress, out int play)
        {
            progress = 0;
            play = 0;
            if (!(key is Actor actor) || !AnimStates.TryGetValue(actor, out AnimState state))
                return false;
            if (state.Length <= 0 || state.Clip != AssetNames.Key(clip))
                return false;
            progress = AnimPlayback.Progress(Time.GameTime, state.StartTime, state.Length,
                AnimSpeedOf(actor), state.Loop);
            play = state.Play;
            return true;
        }

        /// <summary>The clip name an actor is playing, as it was written in the event (empty when none).</summary>
        public static string AnimNameOf(object key)
        {
            return key is Actor actor && AnimStates.TryGetValue(actor, out AnimState state)
                ? state.Name
                : "";
        }

        /// <summary>
        /// The sheet actor a model belongs to: an animation event hands us the
        /// <see cref="FlaxEngine.AnimatedModel"/>, which is often a child of the actor the sheet runs
        /// on. The tracked play's actor wins; otherwise the model itself.
        /// </summary>
        public static Actor OwnerActorOf(FlaxEngine.AnimatedModel model)
        {
            if (model == null)
                return null;
            for (Actor node = model; node != null && !(node is Scene); node = node.Parent)
                if (AnimStates.ContainsKey(node))
                    return node;
            return model;
        }

        /// <summary>The actor's animation speed, or <see cref="AnimPlayback.DefaultSpeed"/>.</summary>
        private static float AnimSpeedOf(Actor actor)
            => actor != null && AnimSpeeds.TryGetValue(actor, out float speed)
                ? speed
                : AnimPlayback.DefaultSpeed;

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
                    StartTime = Time.GameTime,
                    Length = 0,
                    Play = ++AnimPlayCounter,
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
            context.Runtime.Variables.SetGlobal(CameraRig.FreeVariable, ValueBox.OfNumber(0));
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
            context.Runtime.Variables.SetGlobal(CameraRig.FreeVariable, ValueBox.OfNumber(0));
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
            context.Runtime.Variables.SetGlobal(CameraRig.FreeVariable, ValueBox.OfNumber(0));
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
            ExitFreeCamera(context);
            CameraFollowTarget = null;
            CameraLookAtTarget = null;
            CameraAtTarget = null;
            CameraAtOffset = Vector3.Zero;
        }

        /// <summary>Leaves free-pan mode: a follow/snap/aim/switch owns the camera again.</summary>
        private static void ExitFreeCamera(EventContext context)
        {
            context.Runtime.Variables.SetGlobal(CameraRig.FreeVariable, ValueBox.OfNumber(0));
        }

        /// <summary>
        /// <c>camera free</c> (Task 26 Part D): release every camera target and enter free-pan
        /// mode, starting the pan focus where the camera is centred right now so the view never
        /// jumps. This is what turns the RTS controls on — a scene that never frees its camera
        /// keeps the old follow/snap behaviour exactly.
        /// </summary>
        private static void FreeCamera(EventContext context)
        {
            EnterFreeCamera(context.Runtime);
        }

        /// <summary>
        /// Enters free-pan mode without a sheet: the minimap's click-to-pan uses the same path
        /// (Task 24), so both agree on targets, defaults and placement.
        /// </summary>
        internal static void EnterFreeCamera(EventRuntime runtime)
        {
            if (runtime == null)
                return;
            StopCameraFollow();
            EventVariables variables = runtime.Variables;
            double height = variables.GetGlobal("CameraHeight").AsNumber();
            double depth = variables.GetGlobal("CameraDepth").AsNumber();

            double focusX = variables.GetGlobal(CameraRig.PanXVariable).AsNumber();
            double focusZ = variables.GetGlobal(CameraRig.PanZVariable).AsNumber();
            Actor camera = FlaxWorld.FindCamera();
            if (camera != null)
            {
                focusX = camera.Position.X;
                focusZ = camera.Position.Z - depth;
                camera.Orientation = Quaternion.Euler(
                    (float)CameraFraming.ClampPitch(variables.GetGlobal("CameraPitch").AsNumber()), 0.0f, 0.0f);
            }

            variables.SetGlobal(CameraRig.PanXVariable, ValueBox.OfNumber(focusX));
            variables.SetGlobal(CameraRig.PanZVariable, ValueBox.OfNumber(focusZ));
            variables.SetGlobal(CameraRig.FreeVariable, ValueBox.OfNumber(1));
            // Defaults only fill what the sheet left alone, so authored feel always wins.
            if (!variables.HasGlobal(CameraRig.ZoomVariable))
                variables.SetGlobal(CameraRig.ZoomVariable, ValueBox.OfNumber(CameraRig.DefaultZoom));
            if (!variables.HasGlobal(CameraRig.EdgePanVariable))
                variables.SetGlobal(CameraRig.EdgePanVariable, ValueBox.OfNumber(1));
            if (!variables.HasGlobal(CameraRig.EdgeThicknessVariable))
                variables.SetGlobal(CameraRig.EdgeThicknessVariable, ValueBox.OfNumber(CameraRig.DefaultEdgeThickness));
            if (!variables.HasGlobal(CameraRig.PanSpeedVariable))
                variables.SetGlobal(CameraRig.PanSpeedVariable, ValueBox.OfNumber(CameraRig.DefaultPanSpeed));
            ApplyFreeCamera(runtime);
        }

        /// <summary>
        /// <c>zoom camera by &lt;n&gt;</c> / <c>camera zoom in</c> / <c>camera zoom out</c>
        /// (Task 26 Part A): positive moves closer, negative further out, clamped so the camera
        /// cannot sink into the floor or drift off to infinity.
        /// </summary>
        private static void ZoomCamera(EventContext context, ActionSpec spec)
        {
            EventVariables variables = context.Runtime.Variables;
            double zoom = CameraRig.ZoomFrom(variables);
            if (spec.HasNumber)
                zoom = CameraRig.ZoomBy(zoom, spec.Number);
            else if (spec.AmountNode != null)
                zoom = CameraRig.ZoomBy(zoom, context.Evaluate(spec.AmountNode).AsNumber());
            else if (string.Equals(spec.Dir, "out", StringComparison.OrdinalIgnoreCase))
                zoom = CameraRig.ZoomedOut(zoom);
            else
                zoom = CameraRig.ZoomedIn(zoom);
            variables.SetGlobal(CameraRig.ZoomVariable, ValueBox.OfNumber(zoom));
            ApplyFreeCamera(context.Runtime);
        }

        /// <summary>
        /// <c>pan camera left by 100</c> (Task 26 Parts B/C): nudge the free camera's focus, arming
        /// free-pan mode first if the sheet never called <c>camera free</c>. A bare
        /// <c>pan camera left</c> moves for a tenth of a second's worth, which is what a held-key
        /// event wants.
        /// </summary>
        private static void PanCamera(EventContext context, ActionSpec spec)
        {
            if (!CameraRig.TryDirection(spec.Dir, out double directionX, out double directionZ))
                return;
            EventVariables variables = context.Runtime.Variables;
            if (variables.GetGlobal(CameraRig.FreeVariable).AsNumber() <= 0)
                FreeCamera(context);

            double distance;
            if (spec.HasNumber)
                distance = spec.Number;
            else if (spec.AmountNode != null)
                distance = context.Evaluate(spec.AmountNode).AsNumber();
            else
                distance = CameraRig.DefaultPanSpeed / 10.0;

            double focusX = variables.GetGlobal(CameraRig.PanXVariable).AsNumber();
            double focusZ = variables.GetGlobal(CameraRig.PanZVariable).AsNumber();
            CameraRig.Step(focusX, focusZ, directionX, directionZ, distance, out double x, out double z);
            variables.SetGlobal(CameraRig.PanXVariable, ValueBox.OfNumber(x));
            variables.SetGlobal(CameraRig.PanZVariable, ValueBox.OfNumber(z));
            ApplyFreeCamera(context.Runtime);
        }

        /// <summary>
        /// Places the free camera at its pan focus with the zoomed height/depth offsets. Shared
        /// with the per-tick edge/key panning in <c>EventsManager</c>, which owns the mouse and
        /// keyboard. Does nothing unless <c>camera free</c> armed free-pan mode and no follow /
        /// snap / aim target owns the camera.
        /// </summary>
        internal static void ApplyFreeCamera(EventRuntime runtime)
        {
            if (runtime == null)
                return;
            EventVariables variables = runtime.Variables;
            if (variables.GetGlobal(CameraRig.FreeVariable).AsNumber() <= 0)
                return;
            if (CameraFollowTarget != null || CameraLookAtTarget != null || CameraAtTarget != null)
                return;
            Actor camera = FlaxWorld.FindCamera();
            if (camera == null)
                return;
            double zoom = CameraRig.ZoomFrom(variables);
            double height = CameraRig.HeightFor(variables.GetGlobal("CameraHeight").AsNumber(), zoom);
            double depth = CameraRig.DepthFor(variables.GetGlobal("CameraDepth").AsNumber(), zoom);
            double x = variables.GetGlobal(CameraRig.PanXVariable).AsNumber();
            double z = variables.GetGlobal(CameraRig.PanZVariable).AsNumber();
            camera.Position = new Vector3((float)x, (float)height, (float)(z + depth));
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
            {
                CharacterFallSpeeds.Remove(body);
                JumpingCharacters.Remove(body);
                ScriptedJumps.Remove(body);
                Climbs.Remove(body);
                AnimSpeeds.Remove(body);
            }
            if (AnimStates.TryGetValue(actor, out AnimState state))
            {
                if (state.Graph != null)
                    FlaxEngine.Object.Destroy(state.Graph);
                if (state.PreviousGraph != null)
                    FlaxEngine.Object.Destroy(state.PreviousGraph);
                AnimStates.Remove(actor);
            }
            ParticleStates.Remove(actor);
            // A destroyed actor's committed chase side must not outlive it in the steering table.
            EventsManager.Instance?.Runtime?.Steering.Clear(actor);
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
