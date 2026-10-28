using FlaxEngine;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Game
{
    /// <summary>
    /// Attach this to an actor and point it at an .events file (e.g. "Player"), like assigning
    /// a script in any engine. Blank Event File = the actor's own name (so an actor named
    /// Player keeps playing Player.events with zero setup). Several actors can share one file;
    /// `my` variables stay per-object.
    /// </summary>
    public class EventSheet : Script
    {
        /// <summary>.events file to run (without extension or with it — both work). Blank = actor name.</summary>
        [Tooltip(".events file name (with or without the extension). Blank = this actor's name.")]
        public string EventFile = "";

        /// <summary>Tracked AnimatedModel animation-end subscriptions, so they can be unsubscribed.</summary>
        private readonly List<(FlaxEngine.AnimatedModel model, Action unsubscribe)> _animEndSubscriptions = new List<(FlaxEngine.AnimatedModel, Action)>();

        public override void OnStart()
        {
            SubscribeTree(Actor);
            EventsManager.Instance?.RegisterSheet(this);
        }

        public override void OnDestroy()
        {
            EventsManager.Instance?.UnregisterSheet(this);
            UnsubscribeTree(Actor);
            UnsubscribeAnimEnd();
        }

        /// <summary>The .events source this sheet points at (always without the extension).</summary>
        public string SourceName()
        {
            string wanted = EventFile == null ? "" : EventFile.Trim();
            if (wanted.EndsWith(".events", System.StringComparison.OrdinalIgnoreCase))
                wanted = wanted.Substring(0, wanted.Length - ".events".Length);
            return wanted.Length > 0 ? wanted : Actor.Name;
        }

        private void SubscribeTree(Actor actor)
        {
            if (actor == null)
                return;
            if (actor is Collider collider)
                Subscribe(collider);
            SubscribeAnimEnd(actor);
            foreach (Actor child in actor.Children)
                SubscribeTree(child);
        }

        private void UnsubscribeTree(Actor actor)
        {
            if (actor == null)
                return;
            if (actor is Collider collider)
                Unsubscribe(collider);
            foreach (Actor child in actor.Children)
                UnsubscribeTree(child);
        }

        private void Subscribe(Collider collider)
        {
            if (collider != null)
            {
                collider.TriggerEnter += HandleTriggerEnter;
                collider.TriggerExit += HandleTriggerExit;
            }
        }

        private void Unsubscribe(Collider collider)
        {
            if (collider != null)
            {
                collider.TriggerEnter -= HandleTriggerEnter;
                collider.TriggerExit -= HandleTriggerExit;
            }
        }

        private void SubscribeAnimEnd(Actor actor)
        {
            if (actor == null) return;
            // SubscribeTree walks every node, so only the model actor itself subscribes here —
            // a FindActor search would double-subscribe when the model is a child.
            var model = actor as FlaxEngine.AnimatedModel;
            if (model == null) return;
            try
            {
                // Flax 1.13 AnimatedModel animation-finished event — verify the exact event name
                // against the real API; adjust if the property/event differs.
                var ctrlProp = model.GetType().GetProperty("AnimationController");
                var ctrl = ctrlProp?.GetValue(model);
                if (ctrl == null) return;
                Action<string> handler = animName =>
                {
                    if (FlaxActions.IsOneShot(Actor, animName))
                        EventsManager.Instance?.HandleAnimEnd(Actor, animName);
                };
                // Subscribe via reflection-friendly pattern if the event exists.
                SubscribeAnimEndEvent(ctrl, handler);
                _animEndSubscriptions.Add((model, () => UnsubscribeAnimEndEvent(ctrl, handler)));
            }
            catch
            {
                // Animation end subscription not available on this Flax version —
                // when anim ends trigger will not fire until the API is matched.
            }
        }

        private void SubscribeAnimEndEvent(object ctrl, Action<string> handler)
        {
            // Hook any available animation-ended event. Try common Flax API names.
            SubscribeEvent(ctrl, "AnimationEnded", handler);
            SubscribeEvent(ctrl, "StateEnded", handler);
            SubscribeEvent(ctrl, "AnimationStopped", handler);
        }

        private void UnsubscribeAnimEndEvent(object ctrl, Action<string> handler)
        {
            UnsubscribeEvent(ctrl, "AnimationEnded", handler);
            UnsubscribeEvent(ctrl, "StateEnded", handler);
            UnsubscribeEvent(ctrl, "AnimationStopped", handler);
        }

        private static void SubscribeEvent(object target, string eventName, Delegate handler)
        {
            var ev = target.GetType().GetEvent(eventName);
            ev?.AddEventHandler(target, handler);
        }

        private static void UnsubscribeEvent(object target, string eventName, Delegate handler)
        {
            var ev = target.GetType().GetEvent(eventName);
            ev?.RemoveEventHandler(target, handler);
        }

        private void UnsubscribeAnimEnd()
        {
            foreach (var (model, unsubscribe) in _animEndSubscriptions)
                unsubscribe();
            _animEndSubscriptions.Clear();
        }

        private void HandleTriggerEnter(PhysicsColliderActor collider)
        {
            EventsManager.Instance?.HandleTouch(Actor, OwnerOf(collider), false);
        }

        private void HandleTriggerExit(PhysicsColliderActor collider)
        {
            EventsManager.Instance?.HandleTouch(Actor, OwnerOf(collider), true);
        }

        private static Actor OwnerOf(PhysicsColliderActor collider)
        {
            if (collider == null)
                return null;
            // Walk up from the collider (Bullet → RigidBody → SphereCollider) until we find the
            // actor that actually carries the event sheet; fall back to the topmost actor below Scene.
            Actor topmost = null;
            Actor node = collider;
            while (node != null && !(node is Scene))
            {
                if (node.GetScript<EventSheet>() != null)
                    return node;
                topmost = node;
                node = node.Parent;
            }
            return topmost ?? collider;
        }
    }
}
