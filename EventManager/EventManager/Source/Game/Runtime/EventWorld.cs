namespace Game
{
    public interface IEventWorld
    {
        bool IsKeyHeld(string key);
        double Distance(object from, object to);
        bool HasTag(object target, string tag);
    }

    public class DefaultEventWorld : IEventWorld
    {
        public bool IsKeyHeld(string key)
        {
            return false;
        }

        public double Distance(object from, object to)
        {
            return double.MaxValue;
        }

        public bool HasTag(object target, string tag)
        {
            return false;
        }
    }
}
