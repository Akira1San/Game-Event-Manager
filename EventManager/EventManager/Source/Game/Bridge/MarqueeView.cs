using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;

namespace Game
{
    /// <summary>
    /// The drag rectangle itself (Task 29 Part B): a translucent fill with a border, in the same
    /// custom-control style as the minimap panel, so the scene owns the look and the runtime only
    /// says where the corners are.
    ///
    /// To use it, add a UIControl actor named <c>Marquee</c> (inside a UICanvas, usually inactive)
    /// and set its control type to <c>MarqueePanel</c> in the editor.
    /// </summary>
    public class MarqueePanel : ContainerControl, ICosmeticControl
    {
        /// <summary>True while a drag is in flight; the rectangle is only drawn then.</summary>
        public bool Showing;

        /// <summary>The drag's corners, in this control's own space (top-left origin, Y down).</summary>
        public Float2 From;
        public Float2 To;

        /// <summary>Body of the rectangle — barely there, so units underneath stay readable.</summary>
        public Color Fill = new Color(0.35f, 0.78f, 1f, 0.12f);

        /// <summary>The outline, bright enough to follow against grass or fog.</summary>
        public Color Border = new Color(0.45f, 0.85f, 1f, 0.85f);

        /// <summary>Places the drag, given both corners in screen pixels. A control may sit
        /// inside a scaled or offset canvas, so this converts into the control's own space
        /// rather than assuming screen pixels are local ones.</summary>
        public void SetCorners(Float2 fromScreen, Float2 toScreen)
        {
            Showing = true;
            From = PointFromParent(ref fromScreen);
            To = PointFromParent(ref toScreen);
        }

        /// <inheritdoc />
        public override void Draw()
        {
            base.Draw();
            if (!Showing)
                return;
            var bounds = new Rectangle(
                (float)Math.Min(From.X, To.X), (float)Math.Min(From.Y, To.Y),
                (float)Math.Abs(To.X - From.X), (float)Math.Abs(To.Y - From.Y));
            if (bounds.Width <= 0f || bounds.Height <= 0f)
                return;
            Render2D.FillRectangle(bounds, Fill);
            Render2D.DrawRectangle(bounds, Border);
        }
    }

    /// <summary>
    /// Feeds and shows the marquee (Task 29 Part B): the bridge asks for <see cref="Candidates"/>
    /// once on release, and calls <see cref="Show"/> while the mouse is moving with the button
    /// down. The selection itself is <see cref="EventRuntime.SelectInBox"/>.
    ///
    /// Authoring: a UIControl actor named <c>Marquee</c> whose control type is
    /// <see cref="MarqueePanel"/>. Without the actor the drag still selects — it just draws no
    /// rectangle, the same "author it and it appears" rule the minimap follows.
    /// </summary>
    public static class MarqueeView
    {
        public const string ActorName = "Marquee";

        private static UIControl _marquee;
        private static bool _warnedAboutControl;
        private static bool _warnedAboutActor;

        /// <summary>Draws the rectangle for the drag from <paramref name="from"/> to
        /// <paramref name="to"/> (screen pixels).</summary>
        public static void Show(EventRuntime runtime, Float2 from, Float2 to)
        {
            UIControl actor = FindMarquee();
            if (actor == null)
            {
                WarnMissingActor();
                return;
            }
            MarqueePanel panel = PanelOf(actor);
            if (panel == null)
                return;
            if (!actor.IsActive)
                actor.IsActive = true;
            panel.SetCorners(from, to);
        }

        /// <summary>Puts the rectangle away — on release, on Escape, and on teardown.</summary>
        public static void Hide()
        {
            UIControl actor = FindMarquee();
            if (actor == null)
                return;
            MarqueePanel panel = actor.Control as MarqueePanel;
            if (panel != null)
            {
                panel.Showing = false;
                // Cleared as well as hidden: a rectangle left here would be drawn again by the
                // next Show, before that drag's own corners arrive.
                panel.From = Float2.Zero;
                panel.To = Float2.Zero;
            }
            actor.IsActive = false;
        }

        /// <summary>
        /// Every actor a released marquee may pick, projected into screen space: the actor's
        /// centre, and for buildings the whole projected box (they must be covered to join).
        /// UIControls, inactive actors and actors without a sheet or with
        /// <c>my Selectable</c> set to 0 are listed as not selectable, so
        /// <see cref="BoxSelect.Includes"/> keeps them out with the same rule a click uses.
        ///
        /// With fog enabled only the local player's visible units are friendly, the way
        /// <see cref="MinimapView.SelectRect"/> picks blips; with fog off everything is.
        /// </summary>
        public static List<BoxSelectCandidate> Candidates(EventRuntime runtime)
        {
            var found = new List<BoxSelectCandidate>();
            if (runtime == null)
                return found;
            FlaxEngine.Camera camera = FlaxWorld.FindCamera();
            bool fog = runtime.Fog.Enabled;
            int localPlayer = runtime.Fog.LocalPlayer;
            foreach (object item in runtime.World.Actors())
            {
                if (TryProject(runtime, camera, item as Actor, fog, localPlayer, out BoxSelectCandidate candidate))
                    found.Add(candidate);
            }
            return found;
        }

        /// <summary>
        /// One actor as the marquee sees it, or false for an actor that is not a candidate at all
        /// (null, dead, inactive, or a UIControl). This is the projection in one place so the
        /// selection boxes (Task 32 Part B) can project just the selected actors each frame
        /// instead of walking the whole scene, and so both draw the same rectangle for the same
        /// actor.
        /// </summary>
        public static bool TryProject(EventRuntime runtime, FlaxEngine.Camera camera, Actor actor,
            bool fog, int localPlayer, out BoxSelectCandidate candidate)
        {
            candidate = null;
            if (runtime == null || actor == null || !actor || !actor.IsActive || actor is UIControl)
                return false;

            bool hasSheet = runtime.FindSheetFor(actor) != null;
            candidate = new BoxSelectCandidate
            {
                Actor = actor,
                Selectable = ClickSelect.IsSelectable(runtime.Variables, actor, hasSheet),
                Building = runtime.Construction.IsBuilding(actor),
                Friendly = !fog || (runtime.Fog.PlayerOf(actor) == localPlayer &&
                                    runtime.Fog.IsVisibleTo(localPlayer, actor)),
            };

            if (camera != null)
            {
                camera.ProjectPoint(actor.Position, out Float2 centre);
                candidate.X = centre.X;
                candidate.Y = centre.Y;
                candidate.OnScreen = camera.IsPointOnView(actor.Position);
                if (candidate.Building)
                    ProjectBounds(camera, actor, candidate);
            }
            else
            {
                // No camera (a tool scene, a test): fall back to the ground plane, so the
                // rectangle still means "these actors" instead of selecting nothing.
                candidate.X = actor.Position.X;
                candidate.Y = -actor.Position.Z;
                candidate.OnScreen = true;
                if (candidate.Building)
                {
                    double radius = actor.BoundingSphere.Radius;
                    candidate.AddCorner(candidate.X - radius, candidate.Y - radius);
                    candidate.AddCorner(candidate.X + radius, candidate.Y + radius);
                }
            }
            return true;
        }

        /// <summary>Projects the eight corners of the actor's bounds into the candidate's screen
        /// rectangle. A building with no measurable bounds keeps <c>HasBounds == false</c> and so
        /// never joins a drag — there is nothing to cover whole.</summary>
        private static void ProjectBounds(FlaxEngine.Camera camera, Actor actor, BoxSelectCandidate candidate)
        {
            BoundingBox box = actor.BoundingBox;
            Vector3 size = box.Size;
            if (size.X <= 0f && size.Y <= 0f && size.Z <= 0f)
                return;
            foreach (Vector3 corner in box.GetCorners())
            {
                camera.ProjectPoint(corner, out Float2 screen);
                candidate.AddCorner(screen.X, screen.Y);
            }
        }

        private static UIControl FindMarquee()
        {
            if (_marquee != null && _marquee.Scene != null)
                return _marquee;
            _marquee = Level.FindActor(ActorName) as UIControl;
            return _marquee;
        }

        /// <summary>
        /// Says once that the scene has no <c>Marquee</c> actor. The drag keeps selecting — it
        /// just draws no rectangle — so without this line a play-check sees a silent nothing
        /// and has to guess why.
        /// </summary>
        private static void WarnMissingActor()
        {
            if (_warnedAboutActor)
                return;
            _warnedAboutActor = true;
            Debug.LogWarning("Marquee: add a UIControl actor named \"" + ActorName +
                "\" under a UI Canvas (control type MarqueePanel) to see the drag rectangle.");
        }

        private static MarqueePanel PanelOf(UIControl actor)
        {
            MarqueePanel panel = actor.Control as MarqueePanel;
            if (panel == null && !_warnedAboutControl)
            {
                _warnedAboutControl = true;
                Debug.LogWarning("Marquee: set the UI Control \"" + ActorName +
                    "\" control type to MarqueePanel to see the drag rectangle.");
            }
            return panel;
        }
    }
}
