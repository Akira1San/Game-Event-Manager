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
        public bool Goto;
    }

    public static class FlaxActions
    {
        public static float DeltaTime;
        public static readonly List<OngoingMove> Moves = new List<OngoingMove>();
        private static readonly HashSet<string> WarnedOnce = new HashSet<string>();
        public static Action<string> Warn = FlaxActions.WarnOnce;
        /// <summary>Last played animation per actor, so <c>stop anim</c> can restore it.</summary>
        private static readonly Dictionary<object, string> PreviousAnim = new Dictionary<object, string>();
        /// <summary>One-shot animations currently playing per actor (play anim, not loop anim).</summary>
        private static readonly Dictionary<object, HashSet<string>> OneShotAnims = new Dictionary<object, HashSet<string>>();

        public static void WarnOnce(string message)
        {
            if (WarnedOnce.Add(message))
                Debug.LogWarning(message);
        }

        public static void ClearSession()
        {
            Moves.Clear();
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
                    Vector3 delta = move.Target.Position - move.Subject.Position;
                    float length = delta.Length;
                    if (length < 0.001f || length <= DeltaTime * (float)move.Speed)
                    {
                        move.Subject.Position = move.Target.Position;
                        Moves.RemoveAt(i);
                    }
                    else
                        move.Subject.Position = move.Subject.Position + delta / length * (float)move.Speed * DeltaTime;
                }
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
                    if (!string.IsNullOrEmpty(spec.Thing))
                    {
                        var target = ResolveThing(context, spec.Thing) as Actor;
                        if (target != null)
                        {
                            Vector3 delta = target.Position - me.Position;
                            float length = delta.Length;
                            if (length > 0.001f)
                                me.Position = me.Position + delta / length * (float)ResolveNumber(context, spec) * DeltaTime;
                        }
                        break;
                    }
                    me.Position = me.Position + DirectionOf(me, spec.Dir) * (float)ResolveNumber(context, spec) * DeltaTime;
                    break;
                }
                case ActionVerb.StopMoving:
                    for (int i = Moves.Count - 1; i >= 0; i--)
                        if (Moves[i].Subject == context.Me && !Moves[i].FaceTarget)
                            Moves.RemoveAt(i);
                    break;
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
                        target.Position = target.Position + DirectionOf(target, spec.Dir) * (float)ResolveNumber(context, spec) * DeltaTime;
                    break;
                }
                case ActionVerb.Goto:
                {
                    var target = ResolveThing(context, spec.Thing) as Actor;
                    var me = context.Me as Actor;
                    if (target != null && me != null)
                        Remember(new OngoingMove { Subject = me, Target = target, Goto = true });
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
            var audio = me != null ? me.GetScript<AudioSource>() : null;
                    audio?.Stop();
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
                        Level.LoadScene(scenes[0].ID);
                    break;
                }
                case ActionVerb.LoadScene:
                    WarnOnce("\u201Cload scene\u201D arrives with the menu work — scenes are not wired for names yet.");
                    break;
                case ActionVerb.QuitGame:
                    Engine.RequestExit(0, FatalErrorType.None);
                    break;
            }
        }

        private static void Remember(OngoingMove move)
        {
            foreach (OngoingMove existing in Moves)
            {
                if (existing.Subject == move.Subject && existing.Target == move.Target &&
                    existing.FaceTarget == move.FaceTarget && existing.Goto == move.Goto)
                    return;
            }
            Moves.Add(move);
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
                WarnOnce("I couldn't find the HUD text (an actor called \u201CGameText\u201D with a Label on a UIControl) to show text on.");
        }

        private static void HideHudText()
        {
            var actor = Level.FindActor("GameText");
            var label = actor?.GetScript<UIControl>()?.Control as FlaxEngine.GUI.Label;
            if (label != null)
                label.Visible = false;
            else
                WarnOnce("I couldn't find the HUD text (an actor called \u201CGameText\u201D with a Label on a UIControl) to hide.");
        }

        private static bool SetLabel(string thing, string text)
        {
            var actor = string.IsNullOrEmpty(thing) ? null : Level.FindActor(thing);
            var uiControl = actor != null ? actor.GetScript<UIControl>() : null;
            var label = uiControl != null && uiControl.Control is FlaxEngine.GUI.Label textLabel ? textLabel : null;
            if (label == null)
                return false;
            label.Text = text;
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
                WarnOnce($"Could not load sound clip \u201C{spec.Thing}\u201D. Make sure the audio file is imported in the Flax Content browser.");
                return;
            }

            var audio = new AudioSource
            {
                HideFlags = HideFlags.DontSave,
                Volume = 1,
                Clip = clip,
            };
            Level.SpawnActor(audio);
            audio.Play();
            FlaxEngine.Object.Destroy(audio, clip.Length);
        }

        private static AudioClip LoadAudioClip(string name)
        {
            var candidates = new[]
            {
                "Content/" + name + ".flax",
                "Content/" + name,
                name,
            };
            foreach (var path in candidates)
            {
                if (string.IsNullOrEmpty(path))
                    continue;
                var clip = Content.Load<AudioClip>(path);
                if (clip != null)
                    return clip;
            }
            return null;
        }

        public static void SetText(string thing, string text)
        {
            if (string.IsNullOrEmpty(thing))
            {
                WarnOnce("\u201Cset text of\u201D needs the name of the thing whose text changes — like \u201Cset text of ScoreLabel to Score\u201D.");
                return;
            }
            if (!SetLabel(thing, text))
                WarnOnce($"\u201Cset text of\u201D needs an actor with a Label on a UIControl (I couldn't find one called \u201C{thing}\u201D).");
        }

        /// <summary>Play an animation by name on the actor's <see cref="FlaxEngine.AnimatedModel"/>.</summary>
        private static void PlayAnim(EventContext context, ActionSpec spec)
        {
            var me = context.Me as Actor;
            if (me == null)
            {
                WarnOnce($"\u201Cplay anim \u201C{spec.Thing}\u201D\u201D needs to run on an actor's sheet — the global Game sheet has no model to animate.");
                return;
            }
            var model = me.GetScript<FlaxEngine.AnimatedModel>();
            if (model == null)
            {
                WarnOnce($"\u201Cplay anim \u201C{spec.Thing}\u201D\u201D needs an AnimatedModel on \u201C{me.Name}\u201D.");
                return;
            }
            if (!HasAnim(model, spec.Thing))
            {
                WarnOnce(UnknownAnimMessage(model, spec.Thing));
                return;
            }
            PreviousAnim[me] = CurrentAnim(model);
            CallPlayState(model, spec.Thing);
            MarkOneShot(me, spec.Thing);
        }

        /// <summary>Loop an animation by name on the actor's <see cref="FlaxEngine.AnimatedModel"/>.</summary>
        private static void LoopAnim(EventContext context, ActionSpec spec)
        {
            var me = context.Me as Actor;
            if (me == null)
            {
                WarnOnce($"\u201Cloop anim \u201C{spec.Thing}\u201D\u201D needs to run on an actor's sheet — the global Game sheet has no model to animate.");
                return;
            }
            var model = me.GetScript<FlaxEngine.AnimatedModel>();
            if (model == null)
            {
                WarnOnce($"\u201Cloop anim \u201C{spec.Thing}\u201D\u201D needs an AnimatedModel on \u201C{me.Name}\u201D.");
                return;
            }
            if (!HasAnim(model, spec.Thing))
            {
                WarnOnce(UnknownAnimMessage(model, spec.Thing));
                return;
            }
            PreviousAnim[me] = CurrentAnim(model);
            CallPlayState(model, spec.Thing);
            SetLooping(model, true);
            ClearOneShot(me, spec.Thing);
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
            var model = me.GetScript<FlaxEngine.AnimatedModel>();
            if (model == null)
                return;
            SetLooping(model, false);
            ClearOneShot(me, null);
            if (PreviousAnim.TryGetValue(me, out string previous) && previous.Length > 0)
            {
                CallPlayState(model, previous);
                PreviousAnim.Remove(me);
            }
        }

        private static void MarkOneShot(object actor, string animName)
        {
            if (actor == null) return;
            if (!OneShotAnims.TryGetValue(actor, out var set))
            {
                set = new HashSet<string>();
                OneShotAnims[actor] = set;
            }
            set.Add(animName);
        }

        private static void ClearOneShot(object actor, string animName)
        {
            if (actor == null) return;
            if (!OneShotAnims.TryGetValue(actor, out var set)) return;
            if (animName == null)
                set.Clear();
            else
                set.Remove(animName);
        }

        public static bool IsOneShot(object actor, string animName)
        {
            if (actor == null || animName == null) return false;
            return OneShotAnims.TryGetValue(actor, out var set) && set.Contains(animName);
        }

        public static void ClearActor(object actor)
        {
            if (actor == null) return;
            OneShotAnims.Remove(actor);
            PreviousAnim.Remove(actor);
        }

        private static bool HasAnim(FlaxEngine.AnimatedModel model, string name)
        {
            try
            {
                var ctrl = GetAnimationController(model);
                if (ctrl == null) return true;
                var statesProp = ctrl.GetType().GetProperty("States");
                if (statesProp == null) return true;
                foreach (var state in (System.Collections.IEnumerable)statesProp.GetValue(ctrl))
                {
                    var nameProp = state.GetType().GetProperty("Name");
                    if (nameProp != null && (string)nameProp.GetValue(state) == name)
                        return true;
                }
            }
            catch
            {
                // If the API surface differs, fall back to attempting the play —
                // Flax will surface its own error if the name is invalid.
            }
            return true;
        }

        private static string CurrentAnim(FlaxEngine.AnimatedModel model)
        {
            try
            {
                var ctrl = GetAnimationController(model);
                if (ctrl == null) return "";
                var currentStateProp = ctrl.GetType().GetProperty("CurrentState");
                var currentState = currentStateProp?.GetValue(ctrl);
                if (currentState == null) return "";
                var nameProp = currentState.GetType().GetProperty("Name");
                return nameProp?.GetValue(currentState) as string ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static void SetLooping(FlaxEngine.AnimatedModel model, bool looping)
        {
            try
            {
                var ctrl = GetAnimationController(model);
                if (ctrl == null) return;
                var loopProp = ctrl.GetType().GetProperty("Loop");
                loopProp?.SetValue(ctrl, looping);
            }
            catch
            {
                // Looping property may not exist on this Flax version; ignore.
            }
        }

        private static object GetAnimationController(FlaxEngine.AnimatedModel model)
        {
            try
            {
                var ctrlProp = model.GetType().GetProperty("AnimationController");
                return ctrlProp?.GetValue(model);
            }
            catch
            {
                return null;
            }
        }

        private static void CallPlayState(FlaxEngine.AnimatedModel model, string stateName)
        {
            try
            {
                var playState = model.GetType().GetMethod("PlayState");
                playState?.Invoke(model, new object[] { stateName });
            }
            catch
            {
                // PlayState may not exist on this Flax version; ignore.
            }
        }

        private static string UnknownAnimMessage(FlaxEngine.AnimatedModel model, string name)
        {
            var available = new List<string>();
            try
            {
                var ctrl = GetAnimationController(model);
                if (ctrl != null)
                {
                    var statesProp = ctrl.GetType().GetProperty("States");
                    if (statesProp != null)
                    {
                        foreach (var state in (System.Collections.IEnumerable)statesProp.GetValue(ctrl))
                        {
                            var nameProp = state.GetType().GetProperty("Name");
                            if (nameProp != null)
                                available.Add((string)nameProp.GetValue(state));
                        }
                    }
                }
            }
            catch
            {
                // ignore
            }
            if (available.Count == 0)
                return $"I don't know the anim \u201C{name}\u201D — this character's AnimatedModel has no states exposed.";
            var list = string.Join(", ", available);
            return $"I don't know the anim \u201C{name}\u201D. This character has: {list}.";
        }
    }
}
