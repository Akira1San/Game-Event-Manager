using System.Collections.Generic;

namespace Game
{
    /// <summary>An axis-aligned rectangle in screen pixels (upper-left origin), used by the UI
    /// hit test. Engine-free so the clipping rules are testable without Flax.</summary>
    public struct UiRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;

        public UiRect(double x, double y, double width, double height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        /// <summary>True when the point is inside the rectangle. A collapsed rectangle (zero or
        /// negative size) holds nothing, so a closed or rolled-up panel cannot be clicked.</summary>
        public bool Contains(double x, double y)
        {
            return Width > 0 && Height > 0 &&
                   x >= X && x <= X + Width &&
                   y >= Y && y <= Y + Height;
        }
    }

    /// <summary>
    /// Decides whether a mouse point is really over a UI control, honouring the clipping its
    /// parents apply (Task 34 Part C follow-up). The engine hit-tests a click against every
    /// `UIControl` under the cursor, so without this a list row scrolled behind the edge of its
    /// panel — or outside a container that clips its children — still fired a click for the part
    /// that is not drawn. A point counts only when it is inside the control and inside every
    /// clipping parent: what is clipped is not clickable.
    /// </summary>
    public static class UiHitTest
    {
        /// <summary>
        /// True when the point is over <paramref name="control"/> and inside every rectangle in
        /// <paramref name="clips"/> (nearest clip first; order does not matter).
        /// </summary>
        public static bool IsClickablePoint(UiRect control, IReadOnlyList<UiRect> clips,
            double x, double y)
        {
            if (!control.Contains(x, y))
                return false;
            if (clips != null)
            {
                for (int i = 0; i < clips.Count; i++)
                {
                    if (!clips[i].Contains(x, y))
                        return false;
                }
            }
            return true;
        }
    }
}
