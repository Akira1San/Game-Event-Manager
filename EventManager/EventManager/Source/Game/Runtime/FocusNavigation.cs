using System;
using System.Collections.Generic;

namespace Game
{
    public enum FocusDirection
    {
        Up,
        Down,
        Left,
        Right,
    }

    /// <summary>
    /// One focusable UI control as a screen-space box. Pure numbers only (no Flax types),
    /// so the "nearest control in a direction" rule can be unit tested without the engine.
    /// The bridge builds these from UIControl actors and applies the chosen focus.
    /// </summary>
    public class FocusCandidate
    {
        public object Subject;
        public string Name = "";
        public double CenterX;
        public double CenterY;
        public double HalfWidth;
        public double HalfHeight;

        public double Left => CenterX - HalfWidth;
        public double Right => CenterX + HalfWidth;
        public double Top => CenterY - HalfHeight;
        public double Bottom => CenterY + HalfHeight;
    }

    /// <summary>
    /// Arrow-key / D-pad focus movement: pick the nearest focusable control that lies in
    /// the requested direction. Straight rows/columns are preferred (the perpendicular gap
    /// is penalised), so a menu of stacked buttons navigates predictably.
    /// </summary>
    public static class FocusNavigation
    {
        public static FocusDirection? ParseDirection(string word)
        {
            if (string.IsNullOrEmpty(word))
                return null;
            switch (word.ToLowerInvariant())
            {
                case "up":
                    return FocusDirection.Up;
                case "down":
                    return FocusDirection.Down;
                case "left":
                    return FocusDirection.Left;
                case "right":
                    return FocusDirection.Right;
                default:
                    return null;
            }
        }

        public static string DirectionName(FocusDirection direction)
        {
            switch (direction)
            {
                case FocusDirection.Up:
                    return "up";
                case FocusDirection.Down:
                    return "down";
                case FocusDirection.Left:
                    return "left";
                default:
                    return "right";
            }
        }

        /// <summary>The first control to focus when nothing is focused: topmost, then leftmost.</summary>
        public static FocusCandidate First(IList<FocusCandidate> candidates)
        {
            if (candidates == null)
                return null;
            FocusCandidate best = null;
            foreach (FocusCandidate candidate in candidates)
            {
                if (candidate == null)
                    continue;
                if (best == null ||
                    candidate.CenterY < best.CenterY ||
                    (Math.Abs(candidate.CenterY - best.CenterY) < 0.001 && candidate.CenterX < best.CenterX) ||
                    (Math.Abs(candidate.CenterY - best.CenterY) < 0.001 &&
                     Math.Abs(candidate.CenterX - best.CenterX) < 0.001 &&
                     string.CompareOrdinal(candidate.Name, best.Name) < 0))
                {
                    best = candidate;
                }
            }
            return best;
        }

        /// <summary>
        /// The nearest candidate ahead of <paramref name="current"/> in
        /// <paramref name="direction"/>, or null when nothing lies that way.
        /// </summary>
        public static FocusCandidate Choose(
            IList<FocusCandidate> candidates,
            FocusCandidate current,
            FocusDirection direction)
        {
            if (candidates == null || candidates.Count == 0)
                return null;
            if (current == null)
                return First(candidates);

            FocusCandidate best = null;
            double bestScore = double.MaxValue;
            foreach (FocusCandidate candidate in candidates)
            {
                if (candidate == null || ReferenceEquals(candidate, current) ||
                    ReferenceEquals(candidate.Subject, current.Subject))
                {
                    continue;
                }

                double primary;
                double perpendicularGap;
                switch (direction)
                {
                    case FocusDirection.Up:
                        primary = current.CenterY - candidate.CenterY;
                        perpendicularGap = Gap(current.CenterX, current.HalfWidth, candidate.CenterX, candidate.HalfWidth);
                        break;
                    case FocusDirection.Down:
                        primary = candidate.CenterY - current.CenterY;
                        perpendicularGap = Gap(current.CenterX, current.HalfWidth, candidate.CenterX, candidate.HalfWidth);
                        break;
                    case FocusDirection.Left:
                        primary = current.CenterX - candidate.CenterX;
                        perpendicularGap = Gap(current.CenterY, current.HalfHeight, candidate.CenterY, candidate.HalfHeight);
                        break;
                    default:
                        primary = candidate.CenterX - current.CenterX;
                        perpendicularGap = Gap(current.CenterY, current.HalfHeight, candidate.CenterY, candidate.HalfHeight);
                        break;
                }

                // Must be genuinely ahead of the current control along the requested axis.
                if (primary <= 0.5)
                    continue;

                double score = primary + perpendicularGap * 2.0;
                if (score < bestScore - 0.001 ||
                    (Math.Abs(score - bestScore) < 0.001 && best != null &&
                     string.CompareOrdinal(candidate.Name, best.Name) < 0))
                {
                    best = candidate;
                    bestScore = score;
                }
            }
            return best;
        }

        /// <summary>Distance between two intervals on the perpendicular axis (0 when they overlap).</summary>
        private static double Gap(double aCenter, double aHalf, double bCenter, double bHalf)
        {
            double gap = Math.Abs(aCenter - bCenter) - (aHalf + bHalf);
            return gap > 0 ? gap : 0;
        }
    }
}
