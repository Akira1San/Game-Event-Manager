using System;
using System.Collections.Generic;
using FlaxEngine;

namespace Game
{
    public class GlobalHost
    {
        public override string ToString()
        {
            return "Game";
        }
    }

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
            object fromSheets = context.Runtime.FindNamedActor(thing);
            if (fromSheets != null)
                return fromSheets;
            return Level.FindActor(thing);
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
                    WarnOnce($"Playing the animation \u201C{spec.Thing}\u201D needs the animation wiring — arriving in a later update.");
                    break;
                case ActionVerb.StopAnim:
                    WarnOnce("Stopping animations arrives with the animation wiring.");
                    break;
                case ActionVerb.Hide:
                    SetActive(context, spec.Thing, false);
                    break;
                case ActionVerb.Show:
                    SetActive(context, spec.Thing, true);
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
                actor.IsActive = on;
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
            if (spawnAt != null)
                clone.Position = spawnAt.Position;
            Level.SpawnActor(clone);
        }

        private static void PlaySound(EventContext context, ActionSpec spec)
        {
            var me = context.Me as Actor;
            var audio = me != null ? me.GetScript<AudioSource>() : null;
            if (audio != null)
            {
                audio.Play();
                return;
            }
            WarnOnce($"To \u201Cplay sound\u201D attach an AudioSource (with the clip set) to this actor — \u201Cplay sound \"{spec.Thing}\"\u201D is waiting for one.");
        }

        public static void SetText(string thing, string text)
        {
            var actor = string.IsNullOrEmpty(thing) ? null : Level.FindActor(thing);
            var uiControl = actor != null ? actor.GetScript<UIControl>() : null;
            var label = uiControl != null && uiControl.Control is FlaxEngine.GUI.Label textLabel ? textLabel : null;
            if (label != null)
            {
                label.Text = text;
                return;
            }
            WarnOnce($"\u201Cset text of\u201D needs an actor with a Label on a UIControl (I couldn't find one called \u201C{thing}\u201D).");
        }
    }
}
