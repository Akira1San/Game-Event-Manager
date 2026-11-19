using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// The screen-space box drawn around each selected actor (Task 32 Part B). Plain numbers in
    /// the same space the marquee uses — pixels, top-left origin, Y growing downward — so the
    /// bridge can reuse Part A's projection and the tests can drive this directly.
    ///
    /// One candidate, one box: a unit is its projected centre with a minimum size (nothing to
    /// measure at RTS distance, and a 1 px box on a far-away Marine reads as nothing), a building
    /// is its whole projected bounds, because a building is a shape you box rather than a point.
    /// Every box is then clamped into the viewport, so a box at the screen edge slides in instead
    /// of being cut in half.
    /// </summary>
    public static class SelectionBoxes
    {
        /// <summary>Smallest a box may be, in pixels. A distant or tiny unit still gets a box
        /// you can see and count.</summary>
        public const double MinimumSize = 18.0;

        /// <summary>Breathing room between a building's projected bounds and its box, in pixels,
        /// so the outline does not sit on the model's own silhouette.</summary>
        public const double Margin = 6.0;

        /// <summary>One box per selectable, friendly, on-screen candidate, in candidate order.</summary>
        public static List<BoxScreenRect> Boxes(IEnumerable<BoxSelectCandidate> candidates,
            double viewportWidth, double viewportHeight)
        {
            return Boxes(candidates, viewportWidth, viewportHeight, MinimumSize);
        }

        /// <summary>The same list with an explicit minimum size (the tests vary it).</summary>
        public static List<BoxScreenRect> Boxes(IEnumerable<BoxSelectCandidate> candidates,
            double viewportWidth, double viewportHeight, double minimumSize)
        {
            var boxes = new List<BoxScreenRect>();
            if (candidates == null)
                return boxes;
            foreach (BoxSelectCandidate candidate in candidates)
            {
                if (TryBox(candidate, viewportWidth, viewportHeight, minimumSize, out BoxScreenRect box))
                    boxes.Add(box);
            }
            return boxes;
        }

        /// <summary>
        /// The box for one candidate, or false when it deserves none: a null or dead candidate, one
        /// that may not be selected, one that is not the local player's, one that is off screen, or
        /// a viewport with no size.
        /// </summary>
        public static bool TryBox(BoxSelectCandidate candidate, double viewportWidth,
            double viewportHeight, double minimumSize, out BoxScreenRect box)
        {
            box = default;
            if (candidate == null || candidate.Actor == null)
                return false;
            if (!candidate.Selectable || !candidate.Friendly || !candidate.OnScreen)
                return false;
            if (viewportWidth <= 0.0 || viewportHeight <= 0.0)
                return false;

            double minX;
            double minY;
            double maxX;
            double maxY;
            if (candidate.Building && candidate.HasBounds)
            {
                // A building is boxed by what it projects as, which is what makes a tall barracks
                // read as tall rather than as a point at its feet.
                minX = candidate.Bounds.MinX - Margin;
                minY = candidate.Bounds.MinY - Margin;
                maxX = candidate.Bounds.MaxX + Margin;
                maxY = candidate.Bounds.MaxY + Margin;
            }
            else
            {
                minX = maxX = candidate.X;
                minY = maxY = candidate.Y;
            }

            // Grow to the minimum around the box's own centre, so a unit does not shift when it
            // gets its box and a thin building stays centred on itself.
            double size = Math.Max(minimumSize, 0.0);
            double centreX = (minX + maxX) * 0.5;
            double centreY = (minY + maxY) * 0.5;
            if (maxX - minX < size)
            {
                minX = centreX - size * 0.5;
                maxX = centreX + size * 0.5;
            }
            if (maxY - minY < size)
            {
                minY = centreY - size * 0.5;
                maxY = centreY + size * 0.5;
            }

            box = Clamp(new BoxScreenRect { MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY },
                viewportWidth, viewportHeight);
            return true;
        }

        /// <summary>
        /// Slides a box inside the viewport without resizing it, and shrinks it to the viewport when
        /// it is larger than the screen — a box bigger than the window cannot be slid anywhere, and
        /// letting it hang outside would draw half a border.
        /// </summary>
        public static BoxScreenRect Clamp(BoxScreenRect rect, double viewportWidth, double viewportHeight)
        {
            double width = Math.Min(rect.Width, viewportWidth);
            double height = Math.Min(rect.Height, viewportHeight);
            double minX = Math.Min(Math.Max(rect.MinX, 0.0), viewportWidth - width);
            double minY = Math.Min(Math.Max(rect.MinY, 0.0), viewportHeight - height);
            return new BoxScreenRect
            {
                MinX = minX,
                MinY = minY,
                MaxX = minX + width,
                MaxY = minY + height,
            };
        }
    }
}
