using System;

namespace Game
{
    /// <summary>An engine-free point in camera space, so the camera rules stay unit-testable.
    /// The bridge converts it to <c>Vector3</c> when it places the real camera.</summary>
    public struct CameraPoint
    {
        public double X;
        public double Y;
        public double Z;

        public CameraPoint(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly CameraPoint Zero = new CameraPoint(0, 0, 0);

        public CameraPoint Scaled(double by) => new CameraPoint(X * by, Y * by, Z * by);

        public CameraPoint Plus(CameraPoint other)
            => new CameraPoint(X + other.X, Y + other.Y, Z + other.Z);
    }

    /// <summary>
    /// Everything the camera words remember, owned once per <see cref="EventRuntime"/> instead of
    /// by static fields on the bridge. Three position sources compete for the camera —
    /// <c>camera follows</c>, <c>camera at</c> and <c>camera free</c> — and an aim
    /// (<c>camera looks at</c>) can run alongside a position source. The verbs clear exactly the
    /// things they must not fight with, and this class holds those rules so they can be tested.
    ///
    /// No Flax types live here: targets are plain <see cref="object"/> handles (the same shape the
    /// runtime's other managers use) and the bridge decides liveness with
    /// <see cref="ForgetDead"/>. The authored feel — zoom, height, depth, pitch, pan speed — stays
    /// in globals, where sheets can read and write it.
    /// </summary>
    public sealed class CameraState
    {
        /// <summary>What places the camera this tick, in priority order.</summary>
        public enum PositionSource
        {
            /// <summary>No verb owns the camera; it stays where the scene authored it.</summary>
            None,
            /// <summary><c>camera follows &lt;thing&gt;</c>.</summary>
            Follow,
            /// <summary><c>camera at &lt;thing&gt;</c>.</summary>
            At,
            /// <summary><c>camera free</c>, steered by the panning input.</summary>
            Free,
        }

        /// <summary>Actor the camera keeps centred (<c>camera follows &lt;thing&gt;</c>).</summary>
        public object FollowTarget;
        /// <summary>Offset from the followed actor, captured when the follow started.</summary>
        public CameraPoint FollowOffset;

        /// <summary>Actor the camera snaps to each tick (<c>camera at &lt;thing&gt;</c>).</summary>
        public object AtTarget;
        /// <summary>Top-down offset applied to the snapped actor, captured when the snap started.</summary>
        public CameraPoint AtOffset;

        /// <summary>Actor the camera aims at (<c>camera looks at &lt;thing&gt;</c>).</summary>
        public object LookAtTarget;

        /// <summary>
        /// One running <c>shake camera</c>: its strength and length, how far in it is, the seed its
        /// wobble comes from, and the framing it started over — so the shake is always an offset on
        /// top of the rig, never a new saved position.
        /// </summary>
        public sealed class ShakeState
        {
            public double Strength;
            public double Duration;
            public double Elapsed;
            public int Seed;
            public long Tick;
            public CameraPoint BasePosition;
            /// <summary>The displacement this tick's maths produced. Free-pan mode rewrites the
            /// camera from the pan focus after the shake step, so it re-adds this offset itself.</summary>
            public CameraPoint Offset;
        }

        /// <summary>Null when nothing is shaking. There is one shake, refreshed rather than
        /// stacked, so two slams in a row cannot make the view drift.</summary>
        public ShakeState Shake;

        private int shakeSeed;

        public bool IsShaking => Shake != null;

        /// <summary>The offset to layer on the rig's framing this tick (zero when calm).</summary>
        public CameraPoint ShakeOffset => Shake != null ? Shake.Offset : CameraPoint.Zero;

        /// <summary>A fresh seed per shake, so two shakes never wobble identically.</summary>
        public int NextShakeSeed() => ++shakeSeed;

        public void ClearShake() => Shake = null;

        /// <summary>Which word places the camera right now. A follow outranks a snap (they clear
        /// each other, so this is a safety net), and free-pan only owns the camera when nothing
        /// else does.</summary>
        public PositionSource PositionFor(bool freeArmed)
        {
            if (FollowTarget != null)
                return PositionSource.Follow;
            if (AtTarget != null)
                return PositionSource.At;
            return freeArmed ? PositionSource.Free : PositionSource.None;
        }

        public bool OwnsPosition(bool freeArmed) => PositionFor(freeArmed) != PositionSource.None;

        /// <summary>True when free-pan mode is what places the camera: it is armed and no follow,
        /// snap or aim target has taken the camera back.</summary>
        public bool FreeOwns(bool freeArmed)
            => freeArmed && FollowTarget == null && AtTarget == null && LookAtTarget == null;

        /// <summary><c>camera follows &lt;thing&gt;</c>: takes the position and releases both the
        /// snap and the aim — following and aiming must not fight each tick.</summary>
        public void Follow(object target, CameraPoint offset)
        {
            FollowTarget = target;
            FollowOffset = offset;
            AtTarget = null;
            AtOffset = CameraPoint.Zero;
            LookAtTarget = null;
        }

        /// <summary><c>camera at &lt;thing&gt;</c>: takes the position and releases the follow, but
        /// keeps a live aim — position and aim are separate.</summary>
        public void SnapTo(object target, CameraPoint offset)
        {
            AtTarget = target;
            AtOffset = offset;
            FollowTarget = null;
        }

        /// <summary><c>camera looks at &lt;thing&gt;</c>: takes the aim and releases the follow so
        /// the aim is not overwritten each tick.</summary>
        public void LookAt(object target)
        {
            LookAtTarget = target;
            FollowTarget = null;
        }

        /// <summary>
        /// Drops every target the caller reports as gone. Each slot is forgotten on its own: losing
        /// the followed actor must not silently cancel an aim that still has a live target.
        /// </summary>
        public void ForgetDead(Func<object, bool> dead)
        {
            if (dead == null)
                return;
            if (FollowTarget != null && dead(FollowTarget))
                FollowTarget = null;
            if (AtTarget != null && dead(AtTarget))
                AtTarget = null;
            if (LookAtTarget != null && dead(LookAtTarget))
                LookAtTarget = null;
        }

        /// <summary><c>camera free</c> / <c>camera switch</c> / teardown: every target goes and the
        /// shake stops, so the next verb starts from a clean camera.</summary>
        public void Release()
        {
            FollowTarget = null;
            FollowOffset = CameraPoint.Zero;
            AtTarget = null;
            AtOffset = CameraPoint.Zero;
            LookAtTarget = null;
            Shake = null;
        }

        public void Reset() => Release();
    }
}
