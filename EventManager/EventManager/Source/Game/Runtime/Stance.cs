using System;

namespace Game
{
    /// <summary>How a walking actor is postured (Task_FPS Phase 2).</summary>
    public enum BodyStance
    {
        /// <summary>Full height, full speed — the default.</summary>
        Stand,
        /// <summary>Short capsule, half pace, eyes lowered.</summary>
        Crouch,
    }

    /// <summary>
    /// Pure rules behind <c>crouch</c> / <c>stand</c> / <c>toggle crouch</c> and the
    /// <c>my Stance</c> variable. No Flax types live here, so the numbers are unit tested; the
    /// bridge resizes the real capsule, sinks the actor and clamps move speed from these answers.
    /// </summary>
    public static class Stance
    {
        /// <summary>The per-actor variable the stance is mirrored into, read by
        /// <c>my Stance is "crouch"</c>.</summary>
        public const string Variable = "Stance";

        /// <summary>The two author-facing names. A condition compares against these in quotes.</summary>
        public const string StandName = "stand";
        public const string CrouchName = "crouch";

        /// <summary>How much of the authored move speed a crouched actor may use.</summary>
        public const double CrouchSpeedFactor = 0.5;

        /// <summary>Share of the standing capsule height a crouch keeps. 0.55 turns a 150 cm
        /// capsule into ~82 cm — below the eye line of a doorway, above a crawl.</summary>
        public const double CrouchHeightFactor = 0.55;

        /// <summary>Shortest capsule a crouch may produce, so a tiny authored body cannot
        /// collapse into the floor.</summary>
        public const double MinCrouchHeight = 20.0;

        /// <summary>
        /// Reads a stance name (<c>"stand"</c>, <c>"crouch"</c>), case and padding insensitive.
        /// False when the text is not a stance, so a typo is reported instead of silently standing.
        /// </summary>
        public static bool TryParse(string text, out BodyStance stance)
        {
            stance = BodyStance.Stand;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            switch (text.Trim().ToLowerInvariant())
            {
                case StandName:
                    return true;
                case CrouchName:
                    stance = BodyStance.Crouch;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The name to write into <c>my Stance</c>.</summary>
        public static string NameOf(BodyStance stance)
            => stance == BodyStance.Crouch ? CrouchName : StandName;

        /// <summary><c>toggle crouch</c>: crouch from standing, stand from crouched.</summary>
        public static BodyStance Toggle(BodyStance current)
            => current == BodyStance.Crouch ? BodyStance.Stand : BodyStance.Crouch;

        /// <summary>Speed multiplier for a stance: a crouched actor moves at half pace no matter
        /// what speed the sheet asked for.</summary>
        public static double SpeedFactor(BodyStance stance)
            => stance == BodyStance.Crouch ? CrouchSpeedFactor : 1.0;

        /// <summary>
        /// The capsule a stance asks for: the authored standing body with a shortened height while
        /// crouched (radius, step offset and slope limit unchanged, all re-clamped so the step
        /// offset can never exceed the new height).
        /// </summary>
        public static CharacterBodySettings Capsule(CharacterBodySettings stand, BodyStance stance)
        {
            CharacterBodySettings settings = (stand ?? new CharacterBodySettings()).Sanitized();
            if (stance == BodyStance.Crouch)
                settings.Height = (float)Math.Max(MinCrouchHeight, settings.Height * CrouchHeightFactor);
            return settings.Sanitized();
        }

        /// <summary>
        /// How far the actor drops when the capsule shortens. Flax keeps the actor origin at the
        /// capsule centre, so losing <c>standHeight - crouchHeight</c> of height lifts the feet by
        /// half of it; sinking the actor by this much puts them back on the floor and lowers the
        /// eyes (a camera child rides the actor) by the same amount.
        /// </summary>
        public static double Sink(double standHeight, double crouchHeight)
            => Math.Max(0.0, (Math.Max(0.0, standHeight) - Math.Max(0.0, crouchHeight)) * 0.5);
    }
}
