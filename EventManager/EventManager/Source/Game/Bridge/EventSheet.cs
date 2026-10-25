using FlaxEngine;

namespace Game
{
    public class EventSheet : Script
    {
        public EventsManager Manager;

        public void Setup(EventsManager manager)
        {
            Manager = manager;
        }

        public override void OnStart()
        {
            SubscribeTree(Actor);
        }

        public override void OnDestroy()
        {
            UnsubscribeTree(Actor);
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
            Manager?.HandleTouch(Actor, OwnerOf(collider), false);
        }

        private void HandleTriggerExit(PhysicsColliderActor collider)
        {
            Manager?.HandleTouch(Actor, OwnerOf(collider), true);
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
