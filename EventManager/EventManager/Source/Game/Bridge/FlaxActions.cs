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
        /// <summary>Move through the subject's RigidBody (the sheet's Use Physics Movement opt-in).</summary>
        public bool UsePhysics;
    }

    public static class FlaxActions
    {
        public static float DeltaTime;
        public static readonly List<OngoingMove> Moves = new List<OngoingMove>();
        private static readonly HashSet<string> WarnedOnce = new HashSet<string>();
        public static Action<string> Warn = FlaxActions.WarnOnce;
        /// <summary>Animation state per actor (current/previous clip + the virtual graphs that play them).</summary>
        private static readonly Dictionary<object, AnimState> AnimStates = new Dictionary<object, AnimState>();
        /// <summary>Sources spawned by <c>play sound</c>, with the actor whose sheet started them.</summary>
        private static readonly List<(Actor Owner, AudioSource Source)> ActiveSounds = new List<(Actor, AudioSource)>();
        /// <summary>Clip key (see <see cref="AssetNames"/>) to asset id, built once per play session.</summary>
        private static Dictionary<string, Guid> SoundIndexCache;
        /// <summary>Animation asset key/alias to asset id, built once per play session.</summary>
        private static Dictionary<string, Guid> AnimIndexCache;
        /// <summary>Full animation asset keys (no aliases), for the friendly "I don't know the anim" list.</summary>
        private static List<string> AnimFullNames;
        /// <summary>Actor the camera keeps centered (<c>camera follows &lt;thing&gt;</c>).</summary>
        private static Actor CameraFollowTarget;
        /// <summary>Camera position minus the target position, captured when the follow starts.</summary>
        private static Vector3 CameraFollowOffset;

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
            SoundIndexCache = null;
            AnimIndexCache = null;
            AnimFullNames = null;
            CameraFollowTarget = null;
            foreach (AnimState state in AnimStates.Values)
            {
                if (state.Graph != null)
                    FlaxEngine.Object.Destroy(state.Graph);
                if (state.PreviousGraph != null)
                    FlaxEngine.Object.Destroy(state.PreviousGraph);
            }
            AnimStates.Clear();
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
                if (move.Target == null || move.Target.Scene == null)
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
                        ArriveAt(move);
                        Moves.RemoveAt(i);
                        continue;
                    }
                    Vector3 waypoint = move.Path[move.PathIndex];
                    Vector3 delta = waypoint - move.Subject.Position;
                    float length = delta.Length;
                    float step = (float)move.Speed * DeltaTime;
                    if (length < 0.001f || length <= step)
                    {
                        // Physics moves are swept by the body; never teleport it onto a waypoint.
                        if (!move.UsePhysics)
                            move.Subject.Position = waypoint;
                        move.PathIndex++;
                        if (move.PathIndex >= move.Path.Length)
                        {
                            ArriveAt(move);
                            Moves.RemoveAt(i);
                        }
                    }
                    else
                    {
                        MoveSubject(move.Subject, delta / length, move.Speed, move.UsePhysics);
                        if (move.FaceForward)
                            move.Subject.LookAt(move.Path[move.PathIndex]);
                    }
                }
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

            // `camera follows <thing>`: translate the camera by the target's movement, keeping
            // the offset captured when the follow started (the authored framing never changes).
            if (CameraFollowTarget != null && CameraFollowTarget.Scene == null)
                CameraFollowTarget = null;
            if (CameraFollowTarget != null)
            {
                Actor camera = Level.FindActor("Camera");
                if (camera != null)
                    camera.Position = CameraFollowTarget.Position + CameraFollowOffset;
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
                    bool physics = context.Sheet != null && context.Sheet.UsePhysicsMovement;
                    if (!string.IsNullOrEmpty(spec.Thing))
                    {
                        var target = ResolveThing(context, spec.Thing) as Actor;
                        if (target != null)
                        {
                            Vector3 delta = target.Position - me.Position;
                            float length = delta.Length;
                            if (length > 0.001f)
                                MoveSubject(me, delta / length, ResolveNumber(context, spec), physics);
                        }
                        break;
                    }
                    MoveSubject(me, DirectionOf(me, spec.Dir), ResolveNumber(context, spec), physics);
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
                case ActionVerb.Turn:
                {
                    var me = context.Me as Actor;
                    if (me == null)
                        return;
                    double degrees = ResolveNumber(context, spec);
                    degrees = spec.Dir == "left" ? degrees : -degrees;
                    me.Orientation = Quaternion.Multiply(me.Orientation, Quaternion.Euler(0, (float)degrees, 0));
                    break;
                }
                case ActionVerb.LookAt:
                {
                    var target = ResolveThing(context, spec.Thing) as Actor;
                    var me = context.Me as Actor;
                    if (target != null && me != null)
                        me.LookAt(target.Position);
                    break;
                }
                case ActionVerb.Face:
                {
                    var target = ResolveThing(context, spec.Thing) as Actor;
                    var me = context.Me as Actor;
                    if (target != null && me != null)
                        Remember(new OngoingMove { Subject = me, Target = target, FaceTarget = true });
                    break;
                }
                case ActionVerb.Teleport:
                {
                    var me = context.Me as Actor;
                    var to = ResolveThing(context, spec.To) as Actor;
                    if (me != null && to != null)
                        me.Position = to.Position;
                    break;
                }
                case ActionVerb.Push:
                {
                    var target = ResolveThing(context, spec.Thing) as Actor;
                    if (target != null)
                    {
                        Vector3 direction = DirectionOf(target, spec.Dir);
                        double strength = ResolveNumber(context, spec);
                        RigidBody body = FindBody(target);
                        if (PhysicsMovement.PushModeFor(body != null, body != null && body.IsKinematic) == PhysicsPushMode.Impulse)
                        {
                            body.WakeUp();
                            body.AddForce(direction * (float)strength, ForceMode.Impulse);
                        }
                        else
                            target.Position = target.Position + direction * (float)strength * DeltaTime;
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
                    var target = ResolveThing(context, spec.Thing) as Actor;
                    if (me == null)
                    {
                        WarnOnce("\u201Cgoto\u201D needs to run on an actor's sheet — the global Game sheet has no body to move.");
                        break;
                    }
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
                        UsePhysics = context.Sheet != null && context.Sheet.UsePhysicsMovement,
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
                    double distance = Vector3.Distance(move.Subject.Position, move.Target.Position);
                    if (distance <= EventRuntime.ArrivalSlack)
                        EventRuntime.ClearArrivalContact(move.Subject, move.Target);
                }
            }
            else
            {
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
        /// Moves an actor for one tick. Transform by default; with the sheet's physics opt-in and
        /// a RigidBody present, drives the body instead (velocity when dynamic, a swept move when
        /// kinematic) so collision response and CCD apply.
        /// </summary>
        private static void MoveSubject(Actor subject, Vector3 direction, double speed, bool physics)
        {
            RigidBody body = physics ? FindBody(subject) : null;
            PhysicsMoveMode mode = PhysicsMovement.ModeFor(physics, body != null, body != null && body.IsKinematic);
            float step = (float)speed * DeltaTime;
            switch (mode)
            {
                case PhysicsMoveMode.Dynamic:
                    if (!IsAncestorOrSelf(body, subject))
                        WarnOnce($"\"{subject.Name}\" uses physics movement, but its RigidBody is a child — the mesh won't follow. Put the EventSheet on the RigidBody actor with the mesh under it.");
                    PhysicsMovement.DynamicVelocity(
                        direction.X, direction.Y, direction.Z, speed, body.LinearVelocity.Y,
                        out double vx, out double vy, out double vz);
                    body.LinearVelocity = new Vector3((float)vx, (float)vy, (float)vz);
                    break;
                case PhysicsMoveMode.Kinematic:
                    // A swept move: PhysX carries the body along and reports trigger/collision
                    // contacts on the way instead of teleporting through them.
                    if (IsAncestorOrSelf(body, subject))
                        body.AddMovement(direction * step);
                    else
                        subject.AddMovement(direction * step);
                    break;
                default:
                    subject.Position = subject.Position + direction * step;
                    break;
            }
        }

        /// <summary>Stop a physics-driven actor: brake a dynamic body's horizontal velocity
        /// (vertical speed is kept, so a fall keeps falling).</summary>
        private static void BrakeBody(Actor subject)
        {
            RigidBody body = FindBody(subject);
            if (body == null || body.IsKinematic)
                return;
            PhysicsMovement.BrakeVelocity(body.LinearVelocity.Y, out double vx, out double vy, out double vz);
            body.LinearVelocity = new Vector3((float)vx, (float)vy, (float)vz);
        }

        private static void ArriveAt(OngoingMove move)
        {
            if (move.UsePhysics)
                BrakeBody(move.Subject);
            else
                move.Subject.Position = move.Target.Position;
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
                WarnOnce($"I couldn't find the HUD text. Create a UIControl actor named \u201CGameText\u201D with a Label. Labels in this scene: {LabelActorNames()}.");
        }

        private static void HideHudText()
        {
            var label = FindLabel("GameText", out _);
            if (label != null)
                label.Visible = false;
            else
                WarnOnce($"I couldn't find the HUD text. Create a UIControl actor named \u201CGameText\u201D with a Label. Labels in this scene: {LabelActorNames()}.");
        }

        /// <summary>
        /// Finds the GUI label named by an event. The name is a UIControl actor, or a parent
        /// actor that owns exactly one label (a panel/canvas). <c>UIControl</c> is an Actor,
        /// so this uses <c>as</c>, never <c>GetScript&lt;UIControl&gt;</c>.
        /// </summary>
        private static FlaxEngine.GUI.Label FindLabel(string thing, out UIControl ui)
        {
            ui = null;
            if (string.IsNullOrEmpty(thing))
                return null;

            Actor actor = Level.FindActor(thing);
            if (actor == null)
                return null;

            ui = actor as UIControl;
            if (ui == null)
            {
                var found = new List<UIControl>();
                CollectLabels(actor, found);
                if (found.Count != 1)
                    return null;
                ui = found[0];
            }

            return ui.Control as FlaxEngine.GUI.Label;
        }

        private static void CollectLabels(Actor node, List<UIControl> found)
        {
            foreach (Actor child in node.Children)
            {
                if (child == null)
                    continue;
                if (child is UIControl ui && ui.Control is FlaxEngine.GUI.Label)
                    found.Add(ui);
                CollectLabels(child, found);
            }
        }

        /// <summary>Names of the label actors in the scene, for "I couldn't find it" warnings.</summary>
        private static string LabelActorNames()
        {
            var names = new List<string>();
            Scene[] scenes = Level.Scenes;
            for (int s = 0; s < scenes.Length; s++)
                if (scenes[s] != null)
                    CollectLabelNames(scenes[s], names);
            return names.Count == 0 ? "(none)" : string.Join(", ", names);
        }

        private static void CollectLabelNames(Actor node, List<string> names)
        {
            foreach (Actor child in node.Children)
            {
                if (child == null)
                    continue;
                if (child is UIControl ui && ui.Control is FlaxEngine.GUI.Label)
                    names.Add(child.Name);
                CollectLabelNames(child, names);
            }
        }

        private static bool SetLabel(string thing, string text)
        {
            var label = FindLabel(thing, out UIControl ui);
            if (label == null)
                return false;
            // A `hide GameText` earlier in the session must not brick `show text`.
            if (!ui.IsActive)
                ui.IsActive = true;
            label.Text = text; // Label.Text is a LocalizedString; string converts implicitly
            label.Visible = true;
            return true;
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
            if (!SetLabel(thing, text))
                WarnOnce($"\u201Cset text of\u201D needs an actor with a Label on a UIControl (I couldn't find one called \u201C{thing}\u201D). Labels in this scene: {LabelActorNames()}.");
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
            Actor camera = Level.FindActor("Camera");
            if (camera == null)
            {
                WarnOnce("\u201Ccamera follows\u201D needs an actor named \u201CCamera\u201D in the scene.");
                return;
            }

            CameraFollowTarget = target;
            CameraFollowOffset = camera.Position - target.Position;
            // Take over from the legacy Player follow, or the two would fight every tick.
            context.Runtime.Variables.SetGlobal("cameraFollow", ValueBox.OfNumber(0));
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

        public static void ClearActor(object actor)
        {
            if (actor == null) return;
            if (AnimStates.TryGetValue(actor, out AnimState state))
            {
                if (state.Graph != null)
                    FlaxEngine.Object.Destroy(state.Graph);
                if (state.PreviousGraph != null)
                    FlaxEngine.Object.Destroy(state.PreviousGraph);
                AnimStates.Remove(actor);
            }
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
