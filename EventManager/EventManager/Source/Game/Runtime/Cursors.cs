using System;
using System.Collections.Generic;

namespace Game
{
    /// <summary>
    /// The OS pointer a cursor asks for when it has no drawn art (Task 48 Part A). This is the
    /// pure mirror of Flax's <c>CursorType</c>: the bridge in Part B translates one enum to the
    /// other, so no Flax type leaks into the rules and the tests can read every answer.
    /// </summary>
    public enum CursorFallback
    {
        /// <summary>The platform arrow — the right answer for a game that never touched the pointer.</summary>
        Default,
        /// <summary>The pointing finger, for a hotspot or a button.</summary>
        Hand,
        /// <summary>The crosshair, for targeting and dragging.</summary>
        Cross,
        /// <summary>The "not allowed" pointer.</summary>
        No,
        /// <summary>No pointer at all — for a cursor the game draws itself but has no art for.</summary>
        Hidden,
        /// <summary>A static OS image loaded by the bridge (<c>Window.LoadCursorImage</c>).</summary>
        Image,
    }

    /// <summary>Which source decided the cursor this tick (Task 48 Part A), for tests and warnings.</summary>
    public enum CursorSource
    {
        /// <summary>Nothing asked for anything; the library's <c>default</c> is in effect.</summary>
        Default,
        /// <summary>A pushed name (the <c>push cursor</c> stack) is in effect.</summary>
        Stack,
        /// <summary>An aiming mode asked for a cursor and nothing was pushed over it.</summary>
        Aim,
        /// <summary>A hover rule asked for a cursor (Part D's <c>CursorRules</c>).</summary>
        Hover,
    }

    /// <summary>
    /// One named cursor (Task 48 Part A): its frames, how fast they play, where its hotspot sits
    /// and what the OS pointer should be when the game has no art to draw.
    ///
    /// A def is data only. It never touches Flax, never animates itself and never decides when it
    /// applies — <see cref="CursorState.For"/> picks the def and
    /// <see cref="FrameAt"/> answers the frame for a clock value, so the bridge in Part B can draw
    /// at <c>Time.GameTime * Fps</c> with no coroutine and no tick loop of its own.
    ///
    /// Frames are asset names, in play order. An empty list is a cursor with no art: the OS
    /// fallback is used instead, which is what keeps a scene from hiding the pointer and drawing
    /// nothing. One frame is a static game cursor; two or more loop at <see cref="Fps"/>.
    /// </summary>
    public class CursorDef
    {
        /// <summary>The name the language and the actor use (<c>Attack</c>, <c>Hand</c>).</summary>
        public string Name;

        /// <summary>Frame asset names in play order; empty means "no art, use the OS fallback".</summary>
        public string[] Frames;

        /// <summary>Frames per second. <c>0</c> (or less) holds the first frame still.</summary>
        public double Fps;

        /// <summary>Pixels from the pointer to the frame's left edge (where the art "points").</summary>
        public double HotspotX;

        /// <summary>Pixels from the pointer to the frame's top edge.</summary>
        public double HotspotY;

        /// <summary>The OS pointer to restore when <see cref="HasArt"/> is false.</summary>
        public CursorFallback OsFallback;

        /// <summary>The static image for <see cref="CursorFallback.Image"/>, or null.</summary>
        public string OsImage;

        /// <summary>Every frame a def carries before it was told otherwise.</summary>
        public static readonly string[] NoFrames = Array.Empty<string>();

        /// <summary>
        /// A def with its values normalised: a blank name becomes <c>default</c>, blank frames
        /// drop out, a negative or NaN fps becomes <c>0</c>, and the frame list is copied so a
        /// later edit to the caller's array cannot change a cursor mid-play.
        /// </summary>
        public CursorDef(string name, IEnumerable<string> frames = null, double fps = 0,
            double hotspotX = 0, double hotspotY = 0,
            CursorFallback osFallback = CursorFallback.Default, string osImage = null)
        {
            Name = string.IsNullOrWhiteSpace(name) ? CursorLibrary.DefaultName : name.Trim();
            Frames = CopyFrames(frames);
            Fps = SanitiseFps(fps);
            HotspotX = SanitiseNumber(hotspotX);
            HotspotY = SanitiseNumber(hotspotY);
            OsFallback = osFallback;
            OsImage = string.IsNullOrWhiteSpace(osImage) ? null : osImage.Trim();
        }

        /// <summary>True when the game can draw this cursor itself instead of using the OS pointer.</summary>
        public bool HasArt => Frames != null && Frames.Length > 0;

        /// <summary>How many frames the cursor loops through (0 for a cursor with no art).</summary>
        public int FrameCount => Frames == null ? 0 : Frames.Length;

        /// <summary>True when the cursor plays more than one frame (a one-frame cursor is static).</summary>
        public bool IsAnimated => FrameCount > 1 && Fps > 0;

        /// <summary>
        /// The frame index for a clock value. A cursor with no art answers -1; a single frame or
        /// <c>Fps</c> of 0 always answers 0; otherwise the frames loop at <c>Fps</c> from zero.
        /// Negative, NaN and infinite times read as the first frame instead of throwing.
        /// </summary>
        public int FrameIndexAt(double seconds)
        {
            int count = FrameCount;
            if (count == 0)
                return -1;
            if (count == 1 || Fps <= 0)
                return 0;
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
                return 0;

            double raw = Math.Floor(seconds * Fps);
            if (double.IsInfinity(raw))
                return 0;
            // Wrap before casting so a very large clock cannot overflow an int.
            double wrapped = raw % count;
            int index = (int)wrapped;
            if (index < 0)
                index += count;
            return index;
        }

        /// <summary>The frame asset name for a clock value, or null when the cursor has no art.</summary>
        public string FrameAt(double seconds)
        {
            int index = FrameIndexAt(seconds);
            return index < 0 ? null : Frames[index];
        }

        /// <summary>The space a drawn cursor's art must be shifted left of the pointer.</summary>
        public double DrawX(double mouseX)
        {
            return mouseX - HotspotX;
        }

        /// <summary>The space a drawn cursor's art must be shifted above the pointer.</summary>
        public double DrawY(double mouseY)
        {
            return mouseY - HotspotY;
        }

        /// <summary>The canonical spelling of a written fallback (<c>hand</c>, <c>no</c>, ...).</summary>
        public static bool TryParseFallback(string written, out CursorFallback fallback)
        {
            fallback = CursorFallback.Default;
            if (string.IsNullOrWhiteSpace(written))
                return false;
            switch (written.Trim().ToLowerInvariant())
            {
                case "default":
                case "arrow":
                    fallback = CursorFallback.Default;
                    return true;
                case "hand":
                case "link":
                    fallback = CursorFallback.Hand;
                    return true;
                case "cross":
                case "crosshair":
                    fallback = CursorFallback.Cross;
                    return true;
                case "no":
                case "forbidden":
                    fallback = CursorFallback.No;
                    return true;
                case "hidden":
                case "none":
                    fallback = CursorFallback.Hidden;
                    return true;
                case "image":
                    fallback = CursorFallback.Image;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>The lowercase spelling used in warnings and docs.</summary>
        public static string FallbackName(CursorFallback fallback)
        {
            switch (fallback)
            {
                case CursorFallback.Hand: return "hand";
                case CursorFallback.Cross: return "cross";
                case CursorFallback.No: return "no";
                case CursorFallback.Hidden: return "hidden";
                case CursorFallback.Image: return "image";
                default: return "default";
            }
        }

        private static string[] CopyFrames(IEnumerable<string> frames)
        {
            if (frames == null)
                return NoFrames;
            var kept = new List<string>();
            foreach (string frame in frames)
            {
                if (!string.IsNullOrWhiteSpace(frame))
                    kept.Add(frame.Trim());
            }
            return kept.Count == 0 ? NoFrames : kept.ToArray();
        }

        private static double SanitiseFps(double fps)
        {
            return double.IsNaN(fps) || double.IsInfinity(fps) || fps < 0 ? 0 : fps;
        }

        private static double SanitiseNumber(double number)
        {
            return double.IsNaN(number) || double.IsInfinity(number) ? 0 : number;
        }
    }

    /// <summary>
    /// Every cursor a project has, by name (Task 48 Part A).
    ///
    /// <c>default</c> is always present — a no-art def with the platform arrow — so a lookup can
    /// never come back empty and a scene cannot end up with no pointer. Lookups are
    /// case-insensitive, so <c>set cursor "attack"</c> finds the <c>Attack</c> actor's def, which
    /// is the same rule asset names already follow. Adding a name twice replaces it, because the
    /// bridge rebuilds a def whenever its actor's values change.
    ///
    /// Part C fills this from <c>Cursor_&lt;Name&gt;</c> actors; Part A only owns the table.
    /// </summary>
    public class CursorLibrary
    {
        /// <summary>The reserved cursor that is always present and always the last resort.</summary>
        public const string DefaultName = "default";

        private readonly Dictionary<string, CursorDef> defs =
            new Dictionary<string, CursorDef>(StringComparer.OrdinalIgnoreCase);

        public CursorLibrary()
        {
            defs[DefaultName] = new CursorDef(DefaultName);
        }

        /// <summary>How many cursors the library holds (including <c>default</c>).</summary>
        public int Count => defs.Count;

        /// <summary>The fallback def. Reading it never returns null.</summary>
        public CursorDef Default => defs[DefaultName];

        /// <summary>Adds or replaces one cursor. A null def is ignored.</summary>
        public void Add(CursorDef def)
        {
            if (def == null)
                return;
            string name = string.IsNullOrWhiteSpace(def.Name) ? DefaultName : def.Name;
            defs[name] = def;
        }

        /// <summary>
        /// Adds a def from plain values, the shape the bridge builds from a cursor actor's own
        /// variables.
        /// </summary>
        public CursorDef Define(string name, IEnumerable<string> frames, double fps,
            double hotspotX, double hotspotY,
            CursorFallback osFallback = CursorFallback.Default, string osImage = null)
        {
            var def = new CursorDef(name, frames, fps, hotspotX, hotspotY, osFallback, osImage);
            Add(def);
            return def;
        }

        /// <summary>True when the library holds a cursor with this name (case-insensitive).</summary>
        public bool Has(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && defs.ContainsKey(name.Trim());
        }

        /// <summary>The def for a name, or null when the name is unknown.</summary>
        public CursorDef FindOrNull(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            CursorDef def;
            return defs.TryGetValue(name.Trim(), out def) ? def : null;
        }

        /// <summary>
        /// The def for a name, falling back to <see cref="Default"/> when it is unknown. Callers
        /// that want to warn about a typo should ask <see cref="TryFind"/> first, so the warning
        /// can name the miss and list what does exist.
        /// </summary>
        public CursorDef Find(string name)
        {
            return FindOrNull(name) ?? Default;
        }

        public bool TryFind(string name, out CursorDef def)
        {
            def = FindOrNull(name);
            return def != null;
        }

        /// <summary>Drops one cursor. <c>default</c> cannot be removed; true when one went away.</summary>
        public bool Remove(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
            string trimmed = name.Trim();
            if (string.Equals(trimmed, DefaultName, StringComparison.OrdinalIgnoreCase))
                return false;
            return defs.Remove(trimmed);
        }

        /// <summary>Forgets every cursor except <c>default</c>, for a scene change or new session.</summary>
        public void Clear()
        {
            var oldDefault = defs.TryGetValue(DefaultName, out CursorDef def) ? def : null;
            defs.Clear();
            defs[DefaultName] = oldDefault ?? new CursorDef(DefaultName);
        }

        /// <summary>
        /// Every cursor name, sorted, with <c>default</c> first — the list a friendly "unknown
        /// cursor" message prints.
        /// </summary>
        public List<string> Names
        {
            get
            {
                var names = new List<string>(defs.Keys);
                names.Sort(StringComparer.OrdinalIgnoreCase);
                names.Remove(DefaultName);
                names.Insert(0, DefaultName);
                return names;
            }
        }

        /// <summary>Every name on one line, for warnings.</summary>
        public string KnownNames()
        {
            return string.Join(", ", Names);
        }
    }

    /// <summary>
    /// The cursor modes currently pushed, last in first out (Task 48 Part A).
    ///
    /// A mode that takes the pointer — an RTS attack cursor, a placement cursor — pushes a name
    /// and pops it when it ends. The stack never empties: popping past the bottom leaves
    /// <c>default</c> in effect, so a sheet that forgets a pop cannot leave the game on the combat
    /// cursor. Part E's rule is that leaving the mode is enough; the stack is the backup.
    /// </summary>
    public class CursorStack
    {
        private readonly List<string> names = new List<string>();

        /// <summary>How many names are pushed (not counting the implicit <c>default</c>).</summary>
        public int Depth => names.Count;

        /// <summary>The name in effect, or <c>default</c> when nothing is pushed.</summary>
        public string Peek()
        {
            return names.Count == 0 ? CursorLibrary.DefaultName : names[names.Count - 1];
        }

        /// <summary>
        /// Pushes a name. A blank name is ignored, so a missing value cannot hide the cursor.
        /// Returns false when nothing was pushed.
        /// </summary>
        public bool Push(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;
            names.Add(name.Trim());
            return true;
        }

        /// <summary>
        /// Pops the top name and returns it, or null when the stack was already at its floor. The
        /// floor is <c>default</c>, never an empty answer.
        /// </summary>
        public string Pop()
        {
            if (names.Count == 0)
                return null;
            string name = names[names.Count - 1];
            names.RemoveAt(names.Count - 1);
            return name;
        }

        /// <summary>Drops every pushed name, returning to <c>default</c>.</summary>
        public void Clear()
        {
            names.Clear();
        }

        /// <summary>Every pushed name, bottom first — for debug readouts and the tests.</summary>
        public List<string> Names
        {
            get { return new List<string>(names); }
        }
    }

    /// <summary>
    /// One resolved cursor for one tick (Task 48 Part A): the answer every other part reads.
    ///
    /// <see cref="For"/> is the whole rule. The push stack wins whenever anything is pushed —
    /// including a pushed <c>default</c>, because an explicit mode must never be overridden
    /// silently — then an aiming mode, then a hover rule, then <c>default</c>. Every answer is
    /// resolved through the <see cref="CursorLibrary"/>, so an unknown name lands on a real
    /// cursor rather than hiding the pointer with nothing to draw; <see cref="Missing"/> and
    /// <see cref="Requested"/> exist so the bridge can warn once and name what it looked for.
    ///
    /// The state is a value for this tick only; Part B draws it and Part D decides hover/aim
    /// names. Nothing here touches Flax, so the tests read it directly.
    /// </summary>
    public class CursorState
    {
        /// <summary>The canonical name in effect; always resolvable through the library.</summary>
        public string Name;

        /// <summary>The name the winning source asked for (may differ in case, or be unknown).</summary>
        public string Requested;

        /// <summary>True when the winning source named a cursor the library does not have.</summary>
        public bool Missing;

        /// <summary>Which source won: stack, aim, hover or the default.</summary>
        public CursorSource Source;

        /// <summary>The def in effect. Never null after <see cref="For"/>.</summary>
        public CursorDef Def;

        /// <summary>True when the game has art to draw instead of showing the OS pointer.</summary>
        public bool HasArt => Def != null && Def.HasArt;

        /// <summary>How many frames the cursor has (0 = no art).</summary>
        public int FrameCount => Def == null ? 0 : Def.FrameCount;

        /// <summary>True when the cursor has more than one frame and a speed.</summary>
        public bool IsAnimated => Def != null && Def.IsAnimated;

        /// <summary>Pixels from the pointer to the drawn frame's left edge.</summary>
        public double HotspotX => Def == null ? 0 : Def.HotspotX;

        /// <summary>Pixels from the pointer to the drawn frame's top edge.</summary>
        public double HotspotY => Def == null ? 0 : Def.HotspotY;

        /// <summary>The OS pointer to restore when there is no art.</summary>
        public CursorFallback OsFallback =>
            Def == null ? CursorFallback.Default : Def.OsFallback;

        /// <summary>The static image for <see cref="CursorFallback.Image"/>, or null.</summary>
        public string OsImage => Def == null ? null : Def.OsImage;

        /// <summary>The frame asset for a clock value, or null when there is no art to draw.</summary>
        public string FrameAt(double seconds)
        {
            return Def == null ? null : Def.FrameAt(seconds);
        }

        /// <summary>The frame index for a clock value, or -1 when there is no art.</summary>
        public int FrameIndexAt(double seconds)
        {
            return Def == null ? -1 : Def.FrameIndexAt(seconds);
        }

        /// <summary>True when the OS pointer must stay visible for this answer.</summary>
        public bool UsesOsPointer => !HasArt;

        /// <summary>Where the drawn frame's left edge goes for a pointer at <paramref name="mouseX"/>.</summary>
        public double DrawX(double mouseX)
        {
            return mouseX - HotspotX;
        }

        /// <summary>Where the drawn frame's top edge goes for a pointer at <paramref name="mouseY"/>.</summary>
        public double DrawY(double mouseY)
        {
            return mouseY - HotspotY;
        }

        /// <summary>
        /// The one resolved answer for this tick. <paramref name="hover"/> and
        /// <paramref name="aiming"/> are cursor names (Part D supplies them); blank means "this
        /// source asks for nothing". A null library or stack is tolerated and answers the
        /// built-in default.
        /// </summary>
        public static CursorState For(CursorLibrary library, CursorStack stack, string hover,
            string aiming)
        {
            if (library == null)
                library = new CursorLibrary();

            string requested;
            CursorSource source;
            if (stack != null && stack.Depth > 0)
            {
                requested = stack.Peek();
                source = CursorSource.Stack;
            }
            else if (!string.IsNullOrWhiteSpace(aiming))
            {
                requested = aiming;
                source = CursorSource.Aim;
            }
            else if (!string.IsNullOrWhiteSpace(hover))
            {
                requested = hover;
                source = CursorSource.Hover;
            }
            else
            {
                requested = CursorLibrary.DefaultName;
                source = CursorSource.Default;
            }

            CursorDef def;
            bool found = library.TryFind(requested, out def);
            if (!found)
                def = library.Default;

            return new CursorState
            {
                Name = def != null ? def.Name : CursorLibrary.DefaultName,
                Requested = requested == null ? "" : requested.Trim(),
                Missing = !found,
                Source = source,
                Def = def,
            };
        }
    }

    /// <summary>
    /// What the pointer is over this tick, as far as the cursor rules care (Task 48 Part D).
    /// A flags set rather than a single value because an enemy unit is also selectable, and an
    /// authored hotspot can also be a selectable prop: the rules decide which flag wins, so the
    /// bridge never has to guess an order.
    /// </summary>
    [Flags]
    public enum CursorHoverKind
    {
        /// <summary>Nothing under the pointer (or the pointer is over UI).</summary>
        None = 0,
        /// <summary>A pickable actor (`my Selectable`, or a sheet on a normal actor).</summary>
        Selectable = 1,
        /// <summary>An enemy (`my Hostile`, or `enemy type`) the player can actually see.</summary>
        Hostile = 2,
        /// <summary>A prop with a sheet that cannot be picked — a point-and-click hotspot / bag item.</summary>
        Interactive = 4,
    }

    /// <summary>
    /// The automatic cursor sources (Task 48 Part D): which cursor names the game asks for on its
    /// own, without a sheet writing anything.
    ///
    /// The table exists so no genre's branch is hardcoded: an RTS wants aiming → `Attack` and a
    /// drag → `Cross`, an adventure wants a hotspot → `Hand`, and both get it without touching C#.
    /// The defaults follow the task's words (selectable/hotspot → `Hand`, hostile → `Attack`,
    /// marquee → `Cross`, aiming → `Attack`) and a sheet overrides any of them with one global —
    /// <c>set Cursor hand to "Point"</c> — or switches the whole thing off with
    /// <c>set Cursor auto to 0</c>.
    ///
    /// A rule asks for a name; it never decides whether that cursor exists. The bridge only asks
    /// for names the <see cref="CursorLibrary"/> actually has, so a scene that authors no
    /// <c>Cursor_&lt;Name&gt;</c> actors keeps the plain OS pointer and pays no cost — which is what
    /// makes this part optional by construction.
    ///
    /// Everything here is pure: no Flax types, no raycasts, no clock. The bridge answers
    /// "what is hovered" and "is a marquee in flight" and calls these.
    /// </summary>
    public class CursorRules
    {
        /// <summary><c>set Cursor auto to 0</c> turns every automatic source off (default: on).</summary>
        public const string AutoVariable = "Cursor auto";
        /// <summary>Global override for <see cref="Aim"/>.</summary>
        public const string AimVariable = "Cursor aim";
        /// <summary>Global override for <see cref="Hand"/>.</summary>
        public const string HandVariable = "Cursor hand";
        /// <summary>Global override for <see cref="Attack"/>.</summary>
        public const string AttackVariable = "Cursor attack";
        /// <summary>Global override for <see cref="Drag"/>.</summary>
        public const string DragVariable = "Cursor drag";

        /// <summary>What an aiming mode asks for when the scene says nothing.</summary>
        public const string DefaultAimName = "Attack";
        /// <summary>What a hovered selectable actor or hotspot asks for by default.</summary>
        public const string DefaultHandName = "Hand";
        /// <summary>What a hovered hostile actor asks for by default.</summary>
        public const string DefaultAttackName = "Attack";
        /// <summary>What a marquee drag asks for by default.</summary>
        public const string DefaultDragName = "Cross";

        /// <summary>False when a sheet switched the automatic sources off.</summary>
        public bool Enabled = true;

        /// <summary>The cursor an armed <c>aim</c> shows.</summary>
        public string Aim = DefaultAimName;

        /// <summary>The cursor a selectable actor or an interactive hotspot shows.</summary>
        public string Hand = DefaultHandName;

        /// <summary>The cursor a visible hostile actor shows.</summary>
        public string Attack = DefaultAttackName;

        /// <summary>The cursor a marquee drag shows.</summary>
        public string Drag = DefaultDragName;

        /// <summary>The shipped table: every name as the task words it, all sources on.</summary>
        public static CursorRules Defaults()
        {
            return new CursorRules();
        }

        /// <summary>
        /// The table a scene asked for: the defaults, with each global that exists replacing one
        /// name and <see cref="AutoVariable"/> switching the sources off. A blank override is
        /// ignored, so <c>set Cursor hand to ""</c> cannot silently disable a rule.
        /// </summary>
        public static CursorRules Read(EventVariables variables)
        {
            var rules = new CursorRules();
            if (variables == null)
                return rules;

            if (variables.HasGlobal(AutoVariable))
                rules.Enabled = variables.GetGlobal(AutoVariable).AsNumber() != 0;
            rules.Aim = ReadName(variables, AimVariable, rules.Aim);
            rules.Hand = ReadName(variables, HandVariable, rules.Hand);
            rules.Attack = ReadName(variables, AttackVariable, rules.Attack);
            rules.Drag = ReadName(variables, DragVariable, rules.Drag);
            return rules;
        }

        /// <summary>
        /// The cursor for what the pointer is over, or null when this source asks for nothing.
        /// A marquee in flight beats whatever is under the pointer (the pointer is dragging, not
        /// inspecting), then a visible enemy, then an interactive hotspot, then a selectable
        /// actor. Two flags on one actor are normal, so the order matters and lives here.
        /// </summary>
        public string Hover(CursorHoverKind kind, bool dragging)
        {
            if (!Enabled)
                return null;
            if (dragging)
                return Clean(Drag);
            if ((kind & CursorHoverKind.Hostile) != 0)
                return Clean(Attack);
            if ((kind & CursorHoverKind.Interactive) != 0)
                return Clean(Hand);
            if ((kind & CursorHoverKind.Selectable) != 0)
                return Clean(Hand);
            return null;
        }

        /// <summary>The cursor an armed aim asks for, or null when this source asks for nothing.</summary>
        public string Aiming()
        {
            return Enabled ? Clean(Aim) : null;
        }

        /// <summary>
        /// Classifies one hovered actor. <paramref name="hostileVisible"/> is the fog rule the
        /// bridge already applies to picks and marquees: a hostile actor behind the shroud
        /// contributes no flag, so unexplored ground never advertises an attack.
        /// </summary>
        public static CursorHoverKind KindOf(bool selectable, bool interactive, bool hostile,
            bool hostileVisible)
        {
            CursorHoverKind kind = CursorHoverKind.None;
            if (selectable)
                kind |= CursorHoverKind.Selectable;
            if (interactive)
                kind |= CursorHoverKind.Interactive;
            if (hostile && hostileVisible)
                kind |= CursorHoverKind.Hostile;
            return kind;
        }

        private static string ReadName(EventVariables variables, string variable, string fallback)
        {
            if (!variables.HasGlobal(variable))
                return fallback;
            string written = variables.GetGlobal(variable).AsText();
            return string.IsNullOrWhiteSpace(written) ? fallback : written.Trim();
        }

        private static string Clean(string name)
        {
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
    }
}
