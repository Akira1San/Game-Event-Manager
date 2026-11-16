using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>One nearby actor the separation rule keeps its distance from.</summary>
    public struct SeparationNeighbour
    {
        /// <summary>Ground position (X/Z) of the neighbour's origin.</summary>
        public double X;
        public double Z;
        /// <summary>Half-width of the neighbour's collider (0 for a point actor).</summary>
        public double Radius;
    }

    /// <summary>
    /// Character-to-character spacing for a direct chase (Task 7 G2 follow-up).
    ///
    /// Two chasers used to wedge because every movement probe treated the other enemy as a wall:
    /// the front one blocks the back one, both push, neither moves. Movement now ignores character
    /// colliders and only world geometry stops a body, so this rule is what keeps units from
    /// stacking on one spot. It is deliberately small and predictable:
    /// - a neighbour inside <c>radius + neighbourRadius + Gap</c> produces a push that grows as it
    ///   gets closer (full strength once the two bodies touch);
    /// - the push is the part of "away from the neighbour" that is **perpendicular** to the chase
    ///   heading, so the actor sidesteps and keeps walking toward the target instead of backing up;
    /// - a neighbour that is already behind the actor produces nothing at all;
    /// - two bodies on the exact same spot still separate deterministically (the actor's right).
    ///
    /// The caller blends the result into the heading it was going to walk anyway, so a chaser that
    /// is alone is byte-for-byte unchanged.
    /// </summary>
    public static class Separation
    {
        /// <summary>Breathing room beyond both collider radii, in engine units (cm).</summary>
        public const double Gap = 6.0;

        /// <summary>How much of the sideways push bends the final heading (0..1).</summary>
        public const double BlendStrength = 0.8;

        /// <summary>
        /// The sideways escape for one actor, given the neighbours around it and the heading it
        /// wants to walk. Returns false when nothing is close enough (or everything nearby is
        /// already behind it). <paramref name="weight"/> is 0..1 and scales with how deep the
        /// overlap is, so a grazing neighbour bends the path less than a touching one.
        /// </summary>
        public static bool Push(
            double x, double z, double radius,
            IReadOnlyList<SeparationNeighbour> neighbours,
            double headingX, double headingZ,
            out double pushX, out double pushZ, out double weight)
        {
            pushX = 0;
            pushZ = 0;
            weight = 0;

            if (neighbours == null || neighbours.Count == 0)
                return false;

            double headingLength = Math.Sqrt(headingX * headingX + headingZ * headingZ);
            if (headingLength < 1e-9)
                return false;
            headingX /= headingLength;
            headingZ /= headingLength;

            double sumX = 0;
            double sumZ = 0;
            double strongest = 0;
            for (int i = 0; i < neighbours.Count; i++)
            {
                SeparationNeighbour neighbour = neighbours[i];
                double minDistance = Math.Max(0.001, radius + Math.Max(0.0, neighbour.Radius) + Gap);
                double dx = x - neighbour.X;
                double dz = z - neighbour.Z;
                double distance = Math.Sqrt(dx * dx + dz * dz);
                if (distance >= minDistance)
                    continue; // far enough apart: no reaction

                double overlap = 1.0 - distance / minDistance;
                if (distance < 1e-6)
                {
                    // Exactly on top of each other: the heading's right is a stable escape.
                    dx = headingZ;
                    dz = -headingX;
                    distance = 1.0;
                }
                sumX += dx / distance * overlap;
                sumZ += dz / distance * overlap;
                if (overlap > strongest)
                    strongest = overlap;
            }

            if (strongest <= 0.0)
                return false;

            double sumLength = Math.Sqrt(sumX * sumX + sumZ * sumZ);
            if (sumLength < 1e-9)
                return false;
            double awayX = sumX / sumLength;
            double awayZ = sumZ / sumLength;

            // Already walking away from that neighbour: nothing to do. A body exactly abreast
            // still counts as an overlap (nothing behind the actor is reacted to), so the
            // tolerance sits just past zero.
            double forward = awayX * headingX + awayZ * headingZ;
            if (forward > 0.05)
                return false;

            // Keep the sideways part of "away" so the chase sidesteps instead of reversing.
            double sideX = awayX - headingX * forward;
            double sideZ = awayZ - headingZ * forward;
            double sideLength = Math.Sqrt(sideX * sideX + sideZ * sideZ);
            if (sideLength < 0.15)
            {
                // The neighbour is dead ahead: pick the actor's right for a stable choice.
                sideX = headingZ;
                sideZ = -headingX;
                sideLength = 1.0;
            }

            pushX = sideX / sideLength;
            pushZ = sideZ / sideLength;
            weight = Math.Min(1.0, strongest);
            return true;
        }

        /// <summary>
        /// Bends a walk heading toward <paramref name="pushX"/>/<paramref name="pushZ"/> by
        /// <paramref name="weight"/> × <see cref="BlendStrength"/>, then normalizes. A zero push
        /// leaves the heading exactly as it was.
        /// </summary>
        public static void BlendHeading(
            double headingX, double headingZ,
            double pushX, double pushZ, double weight,
            out double x, out double z)
        {
            double bend = Math.Min(1.0, Math.Max(0.0, weight)) * BlendStrength;
            x = headingX + pushX * bend;
            z = headingZ + pushZ * bend;
            double length = Math.Sqrt(x * x + z * z);
            if (length < 1e-9)
            {
                x = headingX;
                z = headingZ;
                return;
            }
            x /= length;
            z /= length;
        }
    }
}
