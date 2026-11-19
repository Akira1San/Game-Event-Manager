using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;

namespace Game
{
    /// <summary>
    /// Draws the cursor the runtime resolved (Task 48 Part B).
    ///
    /// An operating system has one static pointer and no frame stepping, so an animated cursor has
    /// to be drawn by the game. This view owns that half: while the resolved cursor has frames it
    /// hides the OS pointer (<c>Screen.CursorVisible = false</c>) and moves the scene's
    /// <c>Cursor_&lt;Name&gt;</c> actor under the mouse, stepping its frames from
    /// <see cref="CursorState.FrameIndexAt"/> on <c>Time.GameTime</c> so animation needs no
    /// coroutine and no tick loop of its own. When the cursor has no art — or the scene never
    /// authored the actor for it — the pointer comes back and the OS fallback
    /// (<see cref="CursorFallback"/>) is set instead.
    ///
    /// The art itself comes from <see cref="CursorCatalog"/> (Part C), which scans the scene and
    /// owns both the library and each cursor's frame brushes. This class never searches the scene
    /// on its own, so the name the runtime resolved and the art on screen cannot disagree.
    ///
    /// Nothing here decides *when* a cursor is right. Part A's <see cref="CursorState.For"/> makes
    /// that decision and this class only draws what it answers, so a sheet, an aiming mode or a
    /// hover rule can never disagree with the pointer on screen.
    ///
    /// Authoring (the actor half): one UIControl per cursor under the UI canvas, named
    /// <c>Cursor_&lt;Name&gt;</c> for the resolved name (<c>Cursor_Attack</c>, <c>Cursor_Hand</c>),
    /// whose control is an <c>Image</c>. A single Image is a static cursor; give it child Image
    /// actors to animate, one frame each in child order (leave those children inactive). Frames,
    /// fps and the hotspot come from the catalog def, built from the actor's own variables —
    /// <c>my Cursor fps</c>, <c>my Cursor hotspot</c>, <c>my Cursor fallback</c>,
    /// <c>my Cursor image</c>. Place the actor last under the canvas so it draws above the HUD,
    /// keep its pivot at the top-left and leave its anchors alone — the view writes
    /// <c>Location</c> every tick and a layout anchor would overwrite it. The frame's top-left
    /// corner is the cursor's reference point; the hotspot is subtracted from the mouse.
    /// </summary>
    public static class CursorView
    {
        /// <summary>Prefix of the scene actor that draws one cursor (<c>Cursor_Attack</c>).</summary>
        public const string ActorPrefix = "Cursor_";

        /// <summary>The answer last applied, for debug readouts and the play-check.</summary>
        public static CursorState Current { get; private set; }

        private static UIControl _actor;
        private static string _actorName;

        /// <summary>The brush currently on the drawer, so it is replaced only on a frame change.</summary>
        private static IBrush _brush;
        private static string _frameName;
        private static Image _brushOwner;

        private static Window _window;
        private static bool _osApplied;
        private static CursorFallback? _appliedFallback;
        private static string _appliedImage;

        /// <summary>OS cursor images loaded by path. Destroyed on <see cref="Reset"/>.</summary>
        private static readonly Dictionary<string, IntPtr> OsImages =
            new Dictionary<string, IntPtr>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Draws one tick's resolved cursor. Safe to call every frame; it only touches the OS
        /// pointer when the answer actually asks for a pointer, and it never hides the pointer
        /// without something to draw in its place.
        /// </summary>
        public static void Apply(CursorState state, CursorCatalog catalog)
        {
            Current = state;
            if (state == null)
            {
                Reset();
                return;
            }

            // `hide cursor`: no art and no OS pointer at all, for a cutscene or a scripted beat.
            // `_osApplied` is cleared so the next visible answer sets its type again instead of
            // being skipped as "already applied".
            if (state.Hidden)
            {
                HideArt();
                SetPointerVisible(false);
                _osApplied = false;
                _appliedFallback = null;
                _appliedImage = null;
                return;
            }

            // The game viewport owns the pointer only while it is focused: in the editor the
            // player may be clicking another panel, and a drawn cursor chasing the editor mouse
            // would be wrong.
            bool focused = Engine.HasGameViewportFocus;
            CursorCatalog.CursorArt art = null;
            if (focused && state.HasArt && catalog != null)
                catalog.TryGetArt(state.Name, out art);

            if (art != null && art.Drawer != null && art.Frames.Count > 0)
            {
                int index = state.FrameIndexAt(Time.GameTime);
                if (index < 0 || index >= art.Frames.Count)
                    index = 0;
                string frameName = index < art.FrameNames.Count ? art.FrameNames[index] : null;

                _actor = art.Actor;
                _actorName = art.Actor != null ? art.Actor.Name : null;
                if (art.Actor != null && !art.Actor.IsActive)
                    art.Actor.IsActive = true;

                // Same brush reference means the frame did not change: the catalog rebuilds its
                // table a few times a second but copies the scene's brushes by reference, so a
                // static cursor does no GUI work at all.
                if (_brushOwner != art.Drawer || !ReferenceEquals(_brush, art.Frames[index]))
                {
                    art.Drawer.Brush = art.Frames[index];
                    _brushOwner = art.Drawer;
                    _brush = art.Frames[index];
                    _frameName = frameName;
                }
                else if (!string.Equals(_frameName, frameName, StringComparison.OrdinalIgnoreCase))
                {
                    _frameName = frameName;
                }

                Place(art.Drawer, state);
                SetPointerVisible(false);
                return;
            }

            if (focused && state.HasArt && catalog != null)
            {
                if (art == null)
                {
                    FlaxActions.WarnOnce($"The cursor \u201C{state.Name}\u201D has art but the scene has no " +
                        $"actor named \u201C{ActorPrefix + state.Name}\u201D. Add a UIControl with an Image " +
                        $"control called \u201C{ActorPrefix + state.Name}\u201D under the UI canvas, or give the " +
                        $"cursor no frames so the OS pointer shows. Image actors in this scene: {ImageActorNames()}.");
                }
                else if (art.Frames.Count == 0)
                {
                    FlaxActions.WarnOnce($"\u201C{art.Actor.Name}\u201D needs its cursor art on its own Image " +
                        "control or on child Image actors. The OS pointer stays until it has some.");
                }
            }

            ShowOsPointer(state);
        }

        /// <summary>
        /// Puts the pointer back the way the platform had it: art hidden, OS pointer visible and
        /// the cursor type reset. Called when play ends (<c>EventsManager.OnDestroy</c>), because
        /// <see cref="Screen.CursorVisible"/> is engine-wide state that outlives the session.
        /// </summary>
        public static void Reset()
        {
            Current = null;
            HideArt();
            _actor = null;
            _actorName = null;
            _brush = null;
            _frameName = null;
            _brushOwner = null;

            SetPointerVisible(true);
            _osApplied = false;
            _appliedFallback = null;
            _appliedImage = null;

            Window window = GameWindow();
            if (window != null)
                window.Cursor = CursorType.Default;

            foreach (IntPtr handle in OsImages.Values)
            {
                if (handle != IntPtr.Zero)
                    Window.DestroyCursorImage(handle);
            }
            OsImages.Clear();
            _window = null;
        }

        /// <summary>
        /// The OS pointer for a cursor with no art. Showing the pointer before setting its type is
        /// deliberate: Flax restores the cursor it cached when it was hidden, so setting the type
        /// first would be overwritten by the restore.
        /// </summary>
        private static void ShowOsPointer(CursorState state)
        {
            HideArt();
            SetPointerVisible(true);

            Window window = GameWindow();
            if (window == null)
                return;

            CursorFallback fallback = state.OsFallback;
            if (_osApplied && _appliedFallback == fallback &&
                (fallback != CursorFallback.Image || string.Equals(_appliedImage, state.OsImage,
                    StringComparison.OrdinalIgnoreCase)))
                return;

            if (fallback == CursorFallback.Image)
            {
                IntPtr handle = OsImageHandle(state);
                if (handle != IntPtr.Zero)
                {
                    window.CursorImage = handle;
                    window.Cursor = CursorType.Image;
                    _appliedImage = state.OsImage;
                }
                else
                {
                    fallback = CursorFallback.Default;
                }
            }

            if (fallback != CursorFallback.Image)
            {
                window.Cursor = ToCursorType(fallback);
                _appliedImage = null;
            }
            _appliedFallback = fallback;
            _osApplied = true;
        }

        private static CursorType ToCursorType(CursorFallback fallback)
        {
            switch (fallback)
            {
                case CursorFallback.Hand: return CursorType.Hand;
                case CursorFallback.Cross: return CursorType.Cross;
                case CursorFallback.No: return CursorType.No;
                case CursorFallback.Hidden: return CursorType.Hidden;
                default: return CursorType.Default;
            }
        }

        private static void SetPointerVisible(bool visible)
        {
            if (Screen.CursorVisible != visible)
                Screen.CursorVisible = visible;
        }

        /// <summary>
        /// Moves the Image so its top-left corner sits at the pointer minus the hotspot. The
        /// conversion goes through the control's parent, so a scaled or offset UI canvas still
        /// lands the art under the mouse instead of at raw pixel coordinates.
        /// </summary>
        private static void Place(Image image, CursorState state)
        {
            Float2 mouse = Input.MousePosition;
            ContainerControl parent = image.Parent;
            Float2 local = parent != null ? parent.PointFromScreen(mouse) : mouse;
            image.Location = new Float2(
                (float)(local.X - state.HotspotX),
                (float)(local.Y - state.HotspotY));
        }

        private static void HideArt()
        {
            if (_actor != null && _actor.Scene != null && _actor.IsActive)
                _actor.IsActive = false;
        }

        /// <summary>
        /// The game's window, whether the game runs in the editor viewport or as a cooked build.
        /// The engine's own docs use <c>RootControl.GameRoot.RootWindow.Window</c> for exactly
        /// this, because <c>Screen.MainWindow</c> is the editor's window in a play session.
        /// </summary>
        private static Window GameWindow()
        {
            if (_window != null && !_window.IsClosed)
                return _window;
            ContainerControl root = RootControl.GameRoot;
            _window = root != null && root.RootWindow != null
                ? root.RootWindow.Window
                : Screen.MainWindow;
            return _window;
        }

        /// <summary>
        /// The native handle for <c>CursorFallback.Image</c>, loaded once per path and freed in
        /// <see cref="Reset"/>. Platform support for cursor files varies (Windows reads .cur/.ani);
        /// a platform that refuses the file just keeps the default arrow.
        /// </summary>
        private static IntPtr OsImageHandle(CursorState state)
        {
            string path = state.OsImage;
            if (string.IsNullOrWhiteSpace(path))
            {
                FlaxActions.WarnOnce($"The cursor \u201C{state.Name}\u201D asks for an OS image " +
                    "but names no file. Set its OS image path, or use a built-in fallback " +
                    "(\u201Chand\u201D, \u201Ccross\u201D, \u201Cno\u201D, \u201Chidden\u201D).");
                return IntPtr.Zero;
            }
            if (OsImages.TryGetValue(path, out IntPtr loaded))
                return loaded;

            IntPtr handle = Window.LoadCursorImage(path);
            if (handle == IntPtr.Zero)
            {
                FlaxActions.WarnOnce($"I couldn't load the OS cursor image \u201C{path}\u201D " +
                    $"for the cursor \u201C{state.Name}\u201D. The default arrow stays.");
                return IntPtr.Zero;
            }
            OsImages[path] = handle;
            return handle;
        }

        /// <summary>Names of this scene's Image actors, for "I couldn't find it" warnings.</summary>
        private static string ImageActorNames()
        {
            var names = new List<string>();
            Scene[] scenes = Level.Scenes;
            for (int s = 0; s < scenes.Length; s++)
            {
                if (scenes[s] != null)
                    CollectImageNames(scenes[s], names);
            }
            return names.Count == 0 ? "(none)" : string.Join(", ", names);
        }

        private static void CollectImageNames(Actor node, List<string> names)
        {
            foreach (Actor child in node.Children)
            {
                if (child == null)
                    continue;
                if (child is UIControl ui && ui.Control is Image)
                    names.Add(child.Name);
                CollectImageNames(child, names);
            }
        }
    }
}
