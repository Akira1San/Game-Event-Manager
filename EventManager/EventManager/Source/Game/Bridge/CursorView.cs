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
    /// hides the OS pointer (<c>Screen.CursorVisible = false</c>) and moves a scene-authored
    /// <c>Image</c> under the mouse, stepping its frames from <see cref="CursorState.FrameAt"/> on
    /// <c>Time.GameTime</c> so animation needs no coroutine and no tick loop of its own. When the
    /// cursor has no art — or the scene never authored the actor for it — the pointer comes back
    /// and the OS fallback (<see cref="CursorFallback"/>) is set instead.
    ///
    /// Nothing here decides *when* a cursor is right. Part A's <see cref="CursorState.For"/> makes
    /// that decision and this class only draws what it answers, so a sheet, an aiming mode or a
    /// hover rule can never disagree with the pointer on screen.
    ///
    /// Authoring (the actor half): one UIControl per cursor under the UI canvas, named
    /// <c>Cursor_&lt;Name&gt;</c> for the resolved name (<c>Cursor_Attack</c>, <c>Cursor_Hand</c>),
    /// whose control is an <c>Image</c>. Frames, fps and the hotspot come from the def in Part A,
    /// which Part C builds from the actor's own variables. Place the actor last under the canvas so
    /// it draws above the HUD, keep its pivot at the top-left and leave its anchors alone — the
    /// view writes <c>Position</c> every tick and a layout anchor would overwrite it. The frame's
    /// top-left corner is the cursor's reference point; the hotspot is subtracted from the mouse.
    /// </summary>
    public static class CursorView
    {
        /// <summary>Prefix of the scene actor that draws one cursor (<c>Cursor_Attack</c>).</summary>
        public const string ActorPrefix = "Cursor_";

        /// <summary>The answer last applied, for debug readouts and the play-check.</summary>
        public static CursorState Current { get; private set; }

        private static UIControl _actor;
        private static string _actorName;

        /// <summary>The frame currently on the Image, so the brush is replaced only on a change.</summary>
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
        public static void Apply(CursorState state)
        {
            Current = state;
            if (state == null)
            {
                Reset();
                return;
            }

            // The game viewport owns the pointer only while it is focused: in the editor the
            // player may be clicking another panel, and a drawn cursor chasing the editor mouse
            // would be wrong.
            bool focused = Engine.HasGameViewportFocus;
            Image image = focused && state.HasArt ? FindImage(state.Name) : null;

            if (image != null)
            {
                FlaxEngine.Texture texture = FrameTexture(state);
                if (texture != null)
                {
                    UIControl actor = _actor;
                    if (actor != null && !actor.IsActive)
                        actor.IsActive = true;

                    if (_brushOwner != image || !string.Equals(_frameName, FrameAt(state),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        image.Brush = new TextureBrush { Texture = texture };
                        _brushOwner = image;
                        _frameName = FrameAt(state);
                    }

                    Place(image, state);
                    SetPointerVisible(false);
                    return;
                }
                // The frame's texture is missing (already warned): fall through to the OS pointer
                // rather than hide it with nothing to draw.
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
        /// The Image that draws <paramref name="cursorName"/>, or null when the scene has no
        /// <c>Cursor_&lt;Name&gt;</c> UIControl with an Image. Everything that goes wrong here is
        /// said once: without the actor the OS pointer simply stays, which is the safe answer.
        /// </summary>
        private static Image FindImage(string cursorName)
        {
            string wanted = ActorPrefix + cursorName;
            if (_actor != null && _actor.Scene != null &&
                string.Equals(_actorName, wanted, StringComparison.OrdinalIgnoreCase))
                return _actor.Control as Image;

            _actor = Level.FindActor(wanted) as UIControl;
            _actorName = wanted;
            if (_actor == null)
            {
                FlaxActions.WarnOnce($"The cursor \u201C{cursorName}\u201D has art but the scene has no actor " +
                    $"named \u201C{wanted}\u201D. Add a UIControl with an Image control called " +
                    $"\u201C{wanted}\u201D under the UI canvas, or give the cursor no frames so the OS pointer shows. " +
                    $"Image actors in this scene: {ImageActorNames()}.");
                return null;
            }

            var image = _actor.Control as Image;
            if (image == null)
            {
                FlaxActions.WarnOnce($"\u201C{wanted}\u201D needs an Image control to draw the cursor. " +
                    $"Set the UI Control control type to Image, or give the cursor no frames. " +
                    $"Image actors in this scene: {ImageActorNames()}.");
            }
            return image;
        }

        /// <summary>
        /// The texture for the cursor's frame right now. The frame name comes from
        /// <see cref="CursorState.FrameAt"/> (already stepped by <c>Time.GameTime * Fps</c>), and
        /// the brush is only rebuilt when the frame changes, so a static cursor does no work.
        /// </summary>
        private static FlaxEngine.Texture FrameTexture(CursorState state)
        {
            string frame = FrameAt(state);
            if (string.IsNullOrEmpty(frame))
                return null;
            if (_brushOwner != null && string.Equals(_frameName, frame,
                    StringComparison.OrdinalIgnoreCase))
                return _brushOwner.Brush is TextureBrush brush ? brush.Texture : null;

            FlaxEngine.Texture texture = FlaxActions.LoadTexture(frame);
            if (texture == null)
            {
                FlaxActions.WarnOnce($"The cursor \u201C{state.Name}\u201D frame \u201C{frame}\u201D " +
                    "is not an imported texture. The OS pointer stays until it exists.");
            }
            return texture;
        }

        private static string FrameAt(CursorState state)
        {
            return state.FrameAt(Time.GameTime);
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
