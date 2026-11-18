using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// A screen-space rectangle dragged over the world (Task 29 Part B). Plain numbers: pixels
    /// with the top-left corner as the origin and Y growing downward, which is what
    /// <see cref="FlaxEngine.Camera.ProjectPoint"/> and <c>Input.MousePosition</c> already use,
    /// so the bridge can hand projected points straight in.
    /// </summary>
    public struct BoxScreenRect
    {
        public double MinX;
        public double MinY;
        public double MaxX;
        public double MaxY;

        /// <summary>A rectangle from two opposite corners, in either order — a drag can go up
        /// and to the left as easily as down and to the right.</summary>
        public static BoxScreenRect FromCorners(double ax, double ay, double bx, double by)
        {
            var rect = new BoxScreenRect
            {
                MinX = Math.Min(ax, bx),
                MinY = Math.Min(ay, by),
                MaxX = Math.Max(ax, bx),
                MaxY = Math.Max(ay, by),
            };
            return rect;
        }

        /// <summary>A zero-size rectangle, used as the seed when growing bounds from corners.</summary>
        public static BoxScreenRect At(double x, double y)
        {
            return new BoxScreenRect { MinX = x, MinY = y, MaxX = x, MaxY = y };
        }

        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;

        /// <summary>True when the point is inside the rectangle; the edges count as inside, so
        /// a unit exactly on the marquee's line is selected (the same rule the minimap uses).</summary>
        public bool Contains(double x, double y)
        {
            return x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;
        }

        /// <summary>True when <paramref name="inner"/> fits entirely inside this rectangle —
        /// StarCraft's rule for buildings, which you must box whole rather than clip.</summary>
        public bool Covers(BoxScreenRect inner)
        {
            return Contains(inner.MinX, inner.MinY) && Contains(inner.MaxX, inner.MaxY);
        }

        /// <summary>Grows to include one more point (the corners of a projected bounding box).</summary>
        public BoxScreenRect Grown(double x, double y)
        {
            return new BoxScreenRect
            {
                MinX = Math.Min(MinX, x),
                MinY = Math.Min(MinY, y),
                MaxX = Math.Max(MaxX, x),
                MaxY = Math.Max(MaxY, y),
            };
        }
    }

    /// <summary>
    /// One actor as the marquee sees it (Task 29 Part B): where its centre projects, the screen
    /// rectangle of its bounds (buildings must be covered whole), and the three filters the
    /// selection applies — selectable, on screen, friendly. The bridge fills this from the camera,
    /// the tests fill it by hand.
    /// </summary>
    public class BoxSelectCandidate
    {
        /// <summary>The actor this describes.</summary>
        public object Actor;

        /// <summary>Projected screen X of the actor's centre.</summary>
        public double X;

        /// <summary>Projected screen Y of the actor's centre (downward).</summary>
        public double Y;

        /// <summary>Screen rectangle of the actor's bounds; only used for buildings.</summary>
        public BoxScreenRect Bounds;

        /// <summary>True when <see cref="Bounds"/> was measured. A building with no measurable
        /// bounds stays out of a drag rather than sneaking in as a point.</summary>
        public bool HasBounds;

        /// <summary>A building (or a lifted one) — it only joins when the marquee covers it whole.</summary>
        public bool Building;

        /// <summary>The actor may be selected (<see cref="ClickSelect.IsSelectable"/>).</summary>
        public bool Selectable;

        /// <summary>The actor is inside the viewport.</summary>
        public bool OnScreen;

        /// <summary>The actor is the local player's, or fog is off so everyone counts.</summary>
        public bool Friendly;

        /// <summary>Grows <see cref="Bounds"/> to include one projected corner of the box.</summary>
        public void AddCorner(double x, double y)
        {
            Bounds = HasBounds ? Bounds.Grown(x, y) : BoxScreenRect.At(x, y);
            HasBounds = true;
        }
    }

    /// <summary>
    /// StarCraft-style drag selection (Task 29 Part B): the decision half, kept free of Flax
    /// types so the tests drive it directly. The bridge draws the marquee and projects the
    /// actors; this decides whether a drag counts as one and who is inside it.
    ///
    /// The rectangle is screen-space over projected positions — no physics volume, no colliders,
    /// no per-unit table — so a unit standing behind a rock (projecting inside the box) is still
    /// selected, which is what the minimap's rectangle select already does.
    /// </summary>
    public static class BoxSelect
    {
        /// <summary>How far the mouse must travel before a press becomes a marquee. A drag under
        /// this stays a plain click, so a shaky click never clears the selection by accident.</summary>
        public const double DragThreshold = 4.0;

        /// <summary>True when the mouse moved far enough from where it went down to be a drag.</summary>
        public static bool IsDrag(double startX, double startY, double currentX, double currentY)
        {
            return IsDrag(startX, startY, currentX, currentY, DragThreshold);
        }

        /// <summary>The same test with an explicit threshold (the tests vary it).</summary>
        public static bool IsDrag(double startX, double startY, double currentX, double currentY,
            double threshold)
        {
            double dx = currentX - startX;
            double dy = currentY - startY;
            return dx * dx + dy * dy > threshold * threshold;
        }

        /// <summary>
        /// Whether one actor is inside the marquee. A unit joins when its centre projects inside
        /// the rectangle; a building joins only when the rectangle covers its whole projected box
        /// (StarCraft's rule, so a half-boxed barracks stays out). An actor that may not be
        /// selected, is off screen, or is not the local player's is never included.
        /// </summary>
        public static bool Includes(BoxScreenRect marquee, BoxSelectCandidate candidate)
        {
            if (candidate == null || candidate.Actor == null)
                return false;
            if (!candidate.Selectable || !candidate.OnScreen || !candidate.Friendly)
                return false;
            if (candidate.Building)
                return candidate.HasBounds && marquee.Covers(candidate.Bounds);
            return marquee.Contains(candidate.X, candidate.Y);
        }

        /// <summary>
        /// The selection a released marquee produces: everything inside it, plus the actors
        /// already selected when Shift is held (<paramref name="keep"/>). Order is kept-then-new,
        /// duplicates drop, and an empty result is a legitimate answer — a plain drag over empty
        /// ground clears the selection, the way <see cref="Selection.ReplaceSelection"/> already does.
        /// </summary>
        public static List<object> Resolve(BoxScreenRect marquee,
            IEnumerable<BoxSelectCandidate> candidates, IEnumerable<object> keep)
        {
            var wanted = new List<object>();
            if (keep != null)
            {
                foreach (object actor in keep)
                {
                    if (actor != null && !wanted.Contains(actor))
                        wanted.Add(actor);
                }
            }
            if (candidates != null)
            {
                foreach (BoxSelectCandidate candidate in candidates)
                {
                    if (!Includes(marquee, candidate) || wanted.Contains(candidate.Actor))
                        continue;
                    wanted.Add(candidate.Actor);
                }
            }
            return wanted;
        }
    }
}
