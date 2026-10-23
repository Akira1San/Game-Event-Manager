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
            Subscribe(Actor as Collider);
            foreach (Collider collider in Actor.GetChildren<Collider>())
                Subscribe(collider);
        }

        public override void OnDestroy()
        {
            Unsubscribe(Actor as Collider);
            foreach (Collider collider in Actor.GetChildren<Collider>())
                Unsubscribe(collider);
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
