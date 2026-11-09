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
    }

    public static class FlaxActions
    {
    public static float DeltaTime;
    public static readonly List<OngoingMove> Moves = new List<OngoingMove>();
    /// <summary>Actors a <c>move</c> word ran for this tick (transient: cleared at the start of
    /// each <c>Step</c>, so <c>me is moving</c> is true only while motion is actually happening).</summary>
    public static readonly HashSet<Actor> MovedThisTick = new HashSet<Actor>();
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

        public static void ClearSession()
        {
            Moves.Clear();
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
                case ActionVerb.Tween:
                    StartTween(context, spec);
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
                    Vector3 direction = DirectionOf(target, spec.Dir);
                    double strength = ResolveNumber(context, spec);
                    float step = (float)strength * DeltaTime;
                    switch (ResolvePushMode(target))
                    {
                        case PhysicsPushMode.Impulse:
                        {
                            RigidBody body = FindBody(target);
                            if (body != null)
                            {
                                body.WakeUp();
                                body.AddForce(direction * (float)strength, ForceMode.Impulse);
                            }
                            else
                                target.Position = target.Position + direction * step;
                            break;
                        }
                        case PhysicsPushMode.Sweep:
                        {
                            RigidBody body = FindBody(target);
                            if (body != null && IsAncestorOrSelf(body, target))
                                body.AddMovement(direction * step);
                            else
                                target.AddMovement(direction * step);
                            break;
                        }
                        case PhysicsPushMode.Character:
                        {
                            CharacterController character = EventSheet.FindCharacterController(target);
                            if (character != null)
                                character.Move(direction * step);
                            else
                                target.Position = target.Position + direction * step;
                            break;
                        }
                        default:
                            target.Position = target.Position + direction * step;
                            break;
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
                        for (int i = Moves.Count - 1; i >= 0; i--)
                            if (Moves[i].Subject == actor || Moves[i].Target == actor)
                                Moves.RemoveAt(i);
                        ClearActor(actor);
                        context.Runtime.Selection.Forget(actor);
                        FlaxEngine.Object.Destroy(actor, 0f);
                    }
                    break;
                }
                case ActionVerb.Spawn:
                    Spawn(context, spec);
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
                {
                    if (!LoadSceneByName(spec.Thing))
                        WarnOnce($"I couldn't find a scene named \u201C{spec.Thing}\u201D. Scenes: {SceneNames()}.");
                    break;
                }
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
        private static void MoveSubject(Actor subject, Vector3 direction, double speed, PhysicsMoveMode mode)
        {
            float step = (float)speed * DeltaTime;
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
            CharacterFallSpeeds.Remove(subject);

            RigidBody body = FindBody(subject);
            if (body != null && !body.IsKinematic)
            {
                body.LinearVelocity = Vector3.Zero;
                body.AngularVelocity = Vector3.Zero;
            }
            subject.Position = position;
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
            if (move.HasPositionTarget)
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
            Actor template = Level.FindActor(spec.Thing);
            if (template == null)
            {
                WarnOnce($"To spawn \"{spec.Thing}\" first place an actor with that name in the scene (hidden if you like — it stages the clones).");
                return;
            }
            Actor spawnAt = ResolveThing(context, spec.To) as Actor;
            var clone = template.Clone();
            clone.IsActive = true;
            if (spawnAt != null)
            {
                clone.Position = spawnAt.Position;
                clone.Orientation = spawnAt.Orientation;
            }
            Level.SpawnActor(clone);
            EventsManager.Instance?.AttachSpawnedActor(clone);
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

        /// <summary>Loads a scene asset by its name (folder, extension and case ignored), so
        /// <c>load scene "Level1"</c> can switch to any scene in the project.</summary>
        private static void ReplaceScene(Guid sceneId)
        {
            // Unload all currently loaded scenes so the new one replaces them
            foreach (var scene in Level.Scenes)
                if (scene != null)
                    Level.UnloadScene(scene.ID);
            Level.LoadScene(sceneId);
        }

        private static bool LoadSceneByName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            // 1. Check currently loaded scenes (in memory)
            foreach (var scene in Level.Scenes)
                if (scene != null && scene.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    ReplaceScene(scene.ID);
                    return true;
                }

            // 2. Check imported content database
            var scenePaths = new List<string>();
            foreach (var id in Content.GetAllAssetsByType(typeof(Scene)))
            {
                if (!Content.GetAssetInfo(id, out AssetInfo info))
                    continue;
                scenePaths.Add(info.Path);
                if (System.IO.Path.GetFileNameWithoutExtension(info.Path).Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    ReplaceScene(id);
                    return true;
                }
            }

            // 3. Fallback: scan raw .scene files on disk (Content folder)
            string contentDir = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Content");
            if (System.IO.Directory.Exists(contentDir))
            {
                foreach (var file in System.IO.Directory.GetFiles(contentDir, "*.scene", System.IO.SearchOption.AllDirectories))
                {
                    if (System.IO.Path.GetFileNameWithoutExtension(file).Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        scenePaths.Add(file);
                        try
                        {
                            string contentPath = "Content/" + file.Substring(contentDir.Length + 1).Replace('\\', '/');
                            var sceneAsset = Content.Load<SceneAsset>(contentPath, 0);
                            if (sceneAsset != null)
                            {
                                ReplaceScene(sceneAsset.ID);
                                return true;
                            }
                        }
                        catch
                        {
                            // Content.Load<SceneAsset>(string) or SceneAsset.ID may not exist in this Flax version
                        }
                    }
                }
            }

            Debug.LogWarning($"I couldn't find a scene named \u201C{name}\u201D. Scenes: {SceneNames()}. Scene asset paths: {string.Join(", ", scenePaths)}.");
            return false;
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
            // Use the default top-down framing (cameraHeight/cameraDepth globals) so
            // "camera follows Player" always returns to the normal player camera framing,
            // regardless of where the camera was snapped to before (e.g. by camera at).
            float camHeight = (float)context.Runtime.Variables.GetGlobal("cameraHeight").AsNumber();
            float camDepth = (float)context.Runtime.Variables.GetGlobal("cameraDepth").AsNumber();
            CameraFollowOffset = new Vector3(0f, camHeight, camDepth);
            CameraLookAtTarget = null; // following and aiming don't fight each tick
            CameraAtTarget = null; // following and snapping don't fight each tick
            // Reset orientation to default top-down so the camera isn't left rotated
            // from a previous "camera looks at".
            Actor followCamera = FlaxWorld.FindCamera();
            if (followCamera != null)
                followCamera.Orientation = Quaternion.Euler(45.0f, 0.0f, 0.0f);
            context.Runtime.Variables.SetGlobal("cameraFollow", ValueBox.OfNumber(0));
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
            float camHeight = (float)context.Runtime.Variables.GetGlobal("cameraHeight").AsNumber();
            float camDepth = (float)context.Runtime.Variables.GetGlobal("cameraDepth").AsNumber();
            CameraAtOffset = new Vector3(0f, camHeight, camDepth);
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
