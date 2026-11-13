using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>One noise made in the world: what it was, where it came from and how far it carries.</summary>
    public class HeardSound
    {
        public int Id;
        public string Name = "";
        /// <summary>The actor that made the noise (the shooter, the exploding barrel), or null for
        /// a sound with no body. A `when me hears ...` event runs with it as `other`, so
        /// `goto other` walks to where the noise came from.</summary>
        public object Source;
        public double X;
        public double Y;
        public double Z;
        /// <summary>How far this sound carries. A listener's <c>by &lt;dist&gt;</c> can only narrow it.</summary>
        public double Radius = SoundBus.DefaultRadius;
        public double Time;
    }

    /// <summary>
    /// The sound event bus (Task 16 Part B).
    ///
    /// <c>play sound</c>, <c>spawn Bullet</c> and explosions push a <see cref="HeardSound"/> here
    /// instead of waking listeners directly: the runtime polls the bus each tick, so
    /// <c>when me hears "gunshot" by 12</c> and <c>me hears "gunshot" by 12</c> read exactly the
    /// same list. A sound lives for <see cref="Memory"/> seconds — long enough to be noticed and
    /// re-read by a condition, short enough that "a shot was fired" is about now, not a minute ago.
    ///
    /// Occlusion reuses the sight ray (<see cref="IEventWorld.HasLineOfSightToPoint"/>), so a
    /// gunshot does not travel through a wall.
    /// </summary>
    public class SoundBus
    {
        /// <summary>How far a sound carries when nobody says otherwise (a gunshot across a room).</summary>
        public const double DefaultRadius = 40;
        /// <summary>Seconds a sound stays on the bus, i.e. how long a listener may notice it.</summary>
        public const double Memory = 0.5;

        private readonly List<HeardSound> recent = new List<HeardSound>();
        private int nextId = 1;

        public IReadOnlyList<HeardSound> Recent => recent;

        public HeardSound Emit(string name, object source, double x, double y, double z, double radius, double time)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            var sound = new HeardSound
            {
                Id = nextId++,
                Name = name.Trim(),
                Source = source,
                X = x,
                Y = y,
                Z = z,
                Radius = radius > 0 ? radius : DefaultRadius,
                Time = time,
            };
            recent.Add(sound);
            return sound;
        }

        /// <summary>Forget sounds older than <see cref="Memory"/> — called once per tick.</summary>
        public void Expire(double time)
        {
            recent.RemoveAll(sound => time - sound.Time > Memory);
        }

        public void Clear()
        {
            recent.Clear();
        }

        /// <summary>
        /// Every recent sound named <paramref name="name"/> that <paramref name="listener"/> can
        /// hear, nearest first. A sound is audible when it is not the listener's own, it is within
        /// the sound's own radius (and, when given, the listener's <paramref name="range"/>), and
        /// nothing solid sits between the two.
        /// </summary>
        public List<HeardSound> Audible(string name, object listener, IEventWorld world, double range)
        {
            var heard = new List<HeardSound>();
            if (world == null || listener == null || string.IsNullOrEmpty(name))
                return heard;
            foreach (HeardSound sound in recent)
            {
                if (!string.Equals(sound.Name, name, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (ReferenceEquals(sound.Source, listener))
                    continue; // you don't hear your own gunshot
                double reach = sound.Radius > 0 ? sound.Radius : DefaultRadius;
                if (range > 0 && range < reach)
                    reach = range;
                if (world.DistanceToPoint(listener, sound.X, sound.Z) > reach)
                    continue;
                if (!world.HasLineOfSightToPoint(listener, sound.X, sound.Z))
                    continue;
                heard.Add(sound);
            }
            heard.Sort((a, b) => world.DistanceToPoint(listener, a.X, a.Z)
                .CompareTo(world.DistanceToPoint(listener, b.X, b.Z)));
            return heard;
        }

        public bool IsAudible(string name, object listener, IEventWorld world, double range)
        {
            return Audible(name, listener, world, range).Count > 0;
        }
    }
}
