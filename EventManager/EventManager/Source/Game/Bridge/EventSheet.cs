using FlaxEngine;

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

        public override void OnStart()
        {
            SubscribeTree(Actor);
            EventsManager.Instance?.RegisterSheet(this);
        }

        public override void OnDestroy()
        {
            EventsManager.Instance?.UnregisterSheet(this);
            UnsubscribeTree(Actor);
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
            var above = collider.Parent;
            if (above != null && !(above is Scene) && !(above is PhysicsColliderActor))
                return above;
            return collider;
        }
    }
}
