using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game
{
    /// <summary>
    /// The variables a <c>Cursor_&lt;Name&gt;</c> actor carries (Task 48 Part C), read as plain
    /// values so the rules stay testable without Flax.
    ///
    /// A cursor's art is scene content: the actor's own Image is the frame, or each child Image
    /// actor is one frame in order. Everything else is declared on that actor the same way
    /// <c>my LiftHeight</c> is — <c>set my Cursor fps to 12</c>,
    /// <c>set my Cursor hotspot to "4, 4"</c>, <c>set my Cursor fallback to "hand"</c> and, for a
    /// static OS image, <c>set my Cursor image to "Content/Cursors/arrow.png"</c>.
    ///
    /// Nothing here warns. <see cref="Game.CursorCatalog"/> owns the warnings because it also owns
    /// the actor scan, and the tests can read every answer here without an engine.
    /// </summary>
    public static class CursorVariables
    {
        /// <summary>Frames per second for a cursor with more than one frame and no declared speed.</summary>
        public const string FpsVariable = "Cursor fps";

        /// <summary>Where the art points, relative to its top-left corner (<c>"4, 4"</c>).</summary>
        public const string HotspotVariable = "Cursor hotspot";

        /// <summary>The OS pointer to use when the cursor has no art (<c>hand</c>, <c>cross</c>, ...).</summary>
        public const string FallbackVariable = "Cursor fallback";

        /// <summary>The file for <see cref="CursorFallback.Image"/>, when that fallback is chosen.</summary>
        public const string ImageVariable = "Cursor image";

        /// <summary>
        /// The speed a frame strip plays at when the actor does not declare one. A striped cursor
        /// that stays frozen on its first frame is almost never what an author meant, so two or
        /// more frames animate at a readable default instead; a declared <c>0</c> still holds still.
        /// </summary>
        public const double DefaultFps = 12;

        /// <summary>The hotspot an actor gets when it declares none.</summary>
        public const double DefaultHotspotX = 0;

        /// <summary>The hotspot an actor gets when it declares none.</summary>
        public const double DefaultHotspotY = 0;

        private static readonly char[] HotspotSeparators = { ',', ';' };
        private static readonly char[] HotspotSpaces = { ' ', '\t', '\r', '\n' };

        /// <summary>
        /// The declared speed, or <c>0</c> when undeclared (<paramref name="written"/> is null
        /// then). Returns false when the value is declared but is not a number; the caller warns
        /// and the frames hold still.
        /// </summary>
        public static bool TryReadFps(EventVariables variables, object actor, out double fps,
            out string written)
        {
            fps = 0;
            written = null;
            ValueBox value;
            if (!TryRead(variables, actor, FpsVariable, out value))
                return true;

            written = value.AsText();
            if (value.Kind == ValueKind.Text && !string.IsNullOrWhiteSpace(value.Text) &&
                !TryNumber(value.Text, out _))
                return false;

            fps = Sanitise(value.AsNumber());
            return true;
        }

        /// <summary>
        /// The declared hotspot in pixels. One number shifts the art right only (y stays 0); two
        /// numbers are <c>x, y</c>. <paramref name="written"/> is null when nothing was declared,
        /// so a caller can tell "unset" from "unreadable".
        /// </summary>
        public static bool TryReadHotspot(EventVariables variables, object actor, out double x,
            out double y, out string written)
        {
            x = DefaultHotspotX;
            y = DefaultHotspotY;
            written = null;
            ValueBox value;
            if (!TryRead(variables, actor, HotspotVariable, out value))
                return true;

            written = value.AsText();
            return TryParseHotspot(written, out x, out y);
        }

        /// <summary>
        /// The declared OS fallback. Returns false when the word is not one of the known fallbacks;
        /// <paramref name="written"/> is null when nothing was declared, so a caller can tell
        /// "unset" (Default) from "misspelled".
        /// </summary>
        public static bool TryReadFallback(EventVariables variables, object actor,
            out CursorFallback fallback, out string written)
        {
            fallback = CursorFallback.Default;
            written = null;
            ValueBox value;
            if (!TryRead(variables, actor, FallbackVariable, out value))
                return true;

            written = value.AsText();
            if (string.IsNullOrWhiteSpace(written))
                return true;
            return CursorDef.TryParseFallback(written, out fallback);
        }

        /// <summary>The OS image path for <see cref="CursorFallback.Image"/>, or null.</summary>
        public static string ReadImage(EventVariables variables, object actor)
        {
            ValueBox value;
            if (!TryRead(variables, actor, ImageVariable, out value))
                return null;
            string text = value.AsText();
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        /// <summary>
        /// One cursor actor's def: the frames the catalog found on the actor, the values it
        /// declares, and the default fps for a multi-frame strip that declares none. A single
        /// frame or no frames never animates.
        /// </summary>
        public static CursorDef Build(string name, IEnumerable<string> frames,
            EventVariables variables, object actor)
        {
            var kept = new List<string>();
            if (frames != null)
            {
                foreach (string frame in frames)
                {
                    if (!string.IsNullOrWhiteSpace(frame))
                        kept.Add(frame.Trim());
                }
            }

            double fps = DefaultFps;
            TryReadFps(variables, actor, out double declaredFps, out string fpsText);
            if (fpsText != null)
                fps = declaredFps;
            if (kept.Count <= 1)
                fps = 0;

            TryReadHotspot(variables, actor, out double hotspotX, out double hotspotY, out _);
            TryReadFallback(variables, actor, out CursorFallback fallback, out _);

            return new CursorDef(name, kept, fps, hotspotX, hotspotY, fallback,
                ReadImage(variables, actor));
        }

        /// <summary>
        /// Reads <c>"4, 4"</c>, <c>"4 4"</c> or a bare number. Commas are optional because the
        /// lexer only splits on spaces; an author may write either. False for anything else,
        /// including three or more numbers, so a typo cannot silently shift the art.
        /// </summary>
        public static bool TryParseHotspot(string written, out double x, out double y)
        {
            x = DefaultHotspotX;
            y = DefaultHotspotY;
            if (string.IsNullOrWhiteSpace(written))
                return false;

            var parts = new List<string>();
            foreach (string piece in written.Split(HotspotSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                foreach (string bit in piece.Split(HotspotSpaces, StringSplitOptions.RemoveEmptyEntries))
                    parts.Add(bit);
            }

            if (parts.Count == 0 || parts.Count > 2)
                return false;
            if (!TryNumber(parts[0], out x))
                return false;
            if (parts.Count == 2 && !TryNumber(parts[1], out y))
                return false;
            return true;
        }

        private static bool TryRead(EventVariables variables, object actor, string name,
            out ValueBox value)
        {
            value = null;
            if (variables == null || actor == null || !variables.HasLocal(actor, name))
                return false;
            value = variables.GetLocal(actor, name);
            return value != null;
        }

        private static bool TryNumber(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture,
                out value);
        }

        private static double Sanitise(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) || value < 0 ? 0 : value;
        }
    }
}
