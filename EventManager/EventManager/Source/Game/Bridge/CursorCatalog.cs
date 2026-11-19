using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;

namespace Game
{
    /// <summary>
    /// Every cursor the scene authors, and the art each one draws (Task 48 Part C).
    ///
    /// A cursor is content, not code: one <c>UIControl</c> actor named <c>Cursor_&lt;Name&gt;</c>
    /// whose control is an <c>Image</c>. That Image is the frame when the cursor is static; each
    /// child <c>UIControl</c> with an Image is one frame in child order when it animates. The
    /// catalog reads the actor's own variables through <see cref="CursorVariables"/> and builds
    /// the <see cref="CursorLibrary"/> every part reads, so the view and the runtime look names up
    /// in one place instead of each doing its own <c>Level.FindActor</c>.
    ///
    /// The frame art stays the scene's brushes: the child Images are copied by reference, which
    /// keeps sprite atlases, texture brushes and their margins working without an asset-name
    /// lookup. Keep the frame children inactive in the editor — this scan deliberately includes
    /// inactive actors (the view deactivates the drawer when the OS pointer is showing), so a
    /// hidden frame remains a frame.
    ///
    /// A rescan runs a few times a second rather than every tick: a cursor's fps and hotspot are
    /// authored values, and a sheet that changes one this frame is seen by the next scan.
    /// </summary>
    public class CursorCatalog
    {
        private const double RescanSeconds = 0.2;

        private CursorLibrary _library = new CursorLibrary();
        private readonly Dictionary<string, CursorArt> _art =
            new Dictionary<string, CursorArt>(StringComparer.OrdinalIgnoreCase);
        private double _nextScan;

        /// <summary>One cursor actor's art: the drawer and its frames, in play order.</summary>
        public sealed class CursorArt
        {
            /// <summary>The <c>Cursor_&lt;Name&gt;</c> actor the view shows, moves and hides.</summary>
            public UIControl Actor;

            /// <summary>The Image on that actor — the one control whose brush changes per frame.</summary>
            public Image Drawer;

            /// <summary>One brush per frame, copied from the frame Images by reference.</summary>
            public List<IBrush> Frames = new List<IBrush>();

            /// <summary>The frame actor's name per frame, for warnings and debug readouts.</summary>
            public List<string> FrameNames = new List<string>();
        }

        /// <summary>The table every cursor answer resolves through. Never null.</summary>
        public CursorLibrary Library => _library;

        /// <summary>How many cursors the scene authors (including <c>default</c>).</summary>
        public int Count => _library.Count;

        /// <summary>
        /// Rescans the loaded scenes when the scan interval has passed. Safe to call every tick;
        /// the scan cost is paid a few times a second, not per frame.
        /// </summary>
        public void Update(EventVariables variables, double now)
        {
            if (now < _nextScan)
                return;
            _nextScan = now + RescanSeconds;
            Rescan(variables);
        }

        /// <summary>
        /// Rebuilds the library and the art table from every loaded scene. A later scene whose
        /// cursor has the same name replaces the earlier one, which is the same "last wins" rule
        /// additive scene loads already follow.
        /// </summary>
        public void Rescan(EventVariables variables)
        {
            var fresh = new CursorLibrary();
            var art = new Dictionary<string, CursorArt>(StringComparer.OrdinalIgnoreCase);

            Scene[] scenes = Level.Scenes;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i] != null)
                    Collect(scenes[i], variables, fresh, art);
            }

            _library = fresh;
            _art.Clear();
            foreach (KeyValuePair<string, CursorArt> pair in art)
                _art[pair.Key] = pair.Value;
        }

        /// <summary>Forgets every cursor, for stopping play or starting a new session.</summary>
        public void Reset()
        {
            _library = new CursorLibrary();
            _art.Clear();
            _nextScan = 0;
        }

        /// <summary>
        /// The art for a cursor name, case-insensitively. False when the scene does not author
        /// that cursor at all, which is the answer the view turns into the OS pointer.
        /// </summary>
        public bool TryGetArt(string cursorName, out CursorArt art)
        {
            art = null;
            if (string.IsNullOrWhiteSpace(cursorName))
                return false;
            return _art.TryGetValue(cursorName.Trim(), out art);
        }

        private static void Collect(Actor node, EventVariables variables, CursorLibrary library,
            Dictionary<string, CursorArt> art)
        {
            foreach (Actor child in node.Children)
            {
                if (child == null)
                    continue;
                TryAdd(child, variables, library, art);
                Collect(child, variables, library, art);
            }
        }

        private static void TryAdd(Actor actor, EventVariables variables, CursorLibrary library,
            Dictionary<string, CursorArt> art)
        {
            UIControl control = actor as UIControl;
            if (control == null)
                return;
            string name = actor.Name;
            if (string.IsNullOrEmpty(name) ||
                !name.StartsWith(CursorView.ActorPrefix, StringComparison.OrdinalIgnoreCase))
                return;

            string cursorName = name.Substring(CursorView.ActorPrefix.Length).Trim();
            if (cursorName.Length == 0)
                return;

            var image = control.Control as Image;
            if (image == null)
            {
                FlaxActions.WarnOnce($"The cursor actor \u201C{name}\u201D needs an Image control to " +
                    "draw. Set its UI Control type to Image, or rename the actor so it is not a cursor.");
                return;
            }

            var entry = new CursorArt { Actor = control, Drawer = image };

            // Child Image actors are the frames, in order. A static cursor has none and draws the
            // root's own Image.
            foreach (Actor child in actor.Children)
            {
                UIControl frameControl = child as UIControl;
                Image frameImage = frameControl == null ? null : frameControl.Control as Image;
                if (frameImage == null || frameImage.Brush == null)
                    continue;
                entry.Frames.Add(frameImage.Brush);
                entry.FrameNames.Add(child.Name);
            }
            if (entry.Frames.Count == 0 && image.Brush != null)
            {
                entry.Frames.Add(image.Brush);
                entry.FrameNames.Add(name);
            }

            if (entry.Frames.Count == 0)
            {
                FlaxActions.WarnOnce($"The cursor actor \u201C{name}\u201D has no art: give its " +
                    "Image control a brush, or add child Image actors as frames. It falls back to " +
                    "the OS pointer until it has some.");
            }

            WarnAboutValues(cursorName, name, actor, variables);

            library.Add(CursorVariables.Build(cursorName, entry.FrameNames, variables, actor));
            art[cursorName] = entry;
        }

        /// <summary>
        /// One friendly line per misspelled value. The declared text is echoed back so the author
        /// can see exactly what was read, and the accepted fallback words are listed.
        /// </summary>
        private static void WarnAboutValues(string cursorName, string actorName, Actor actor,
            EventVariables variables)
        {
            if (!CursorVariables.TryReadFps(variables, actor, out _, out string fpsText) &&
                fpsText != null)
            {
                FlaxActions.WarnOnce($"The cursor \u201C{cursorName}\u201D has \u201C" +
                    $"{CursorVariables.FpsVariable}\u201D set to \u201C{fpsText}\u201D, which is not a " +
                    "number. Its frames hold still.");
            }

            if (!CursorVariables.TryReadHotspot(variables, actor, out _, out _, out string hotspotText) &&
                hotspotText != null)
            {
                FlaxActions.WarnOnce($"The cursor \u201C{cursorName}\u201D has \u201C" +
                    $"{CursorVariables.HotspotVariable}\u201D set to \u201C{hotspotText}\u201D, which is " +
                    "not a hotspot. Write \u201Cx, y\u201D in pixels, like \u201C4, 4\u201D.");
            }

            if (!CursorVariables.TryReadFallback(variables, actor, out _,
                    out string fallbackText) && fallbackText != null)
            {
                FlaxActions.WarnOnce($"The cursor \u201C{cursorName}\u201D has \u201C" +
                    $"{CursorVariables.FallbackVariable}\u201D set to \u201C{fallbackText}\u201D. Use " +
                    "\u201Cdefault\u201D, \u201Chand\u201D, \u201Ccross\u201D, \u201Cno\u201D, " +
                    "\u201Chidden\u201D or \u201Cimage\u201D.");
            }
        }
    }
}
