using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;

namespace Game
{
    /// <summary>
    /// Draws a thin box around every selected actor (Task 32 Part B), in the same custom-control
    /// style as the marquee and the minimap: one control draws all of them, so changing the
    /// selection can never leave an orphan control behind, and nothing here creates or destroys a
    /// scene actor.
    ///
    /// Border only, no fill: the unit under the box has to stay readable.
    /// </summary>
    public class SelectionBoxPanel : Control
    {
        /// <summary>True while at least one box is being drawn.</summary>
        public bool Showing;

        /// <summary>The boxes, already converted into this control's own space.</summary>
        public readonly List<BoxScreenRect> Boxes = new List<BoxScreenRect>();

        /// <summary>The outline. Bright enough to read over grass, fog or a model's own colour.</summary>
        public Color Border = new Color(0.45f, 0.85f, 1f, 0.9f);

        /// <summary>How thick the outline is, in pixels.</summary>
        public float BorderThickness = 1.5f;

        /// <summary>Replaces every box, given screen pixels. A control may sit inside a scaled or
        /// offset canvas, so each box is converted into the control's own space rather than
        /// assuming screen pixels are local ones.</summary>
        public void SetBoxes(IList<BoxScreenRect> boxes)
        {
            Boxes.Clear();
            if (boxes == null)
            {
                Showing = false;
                return;
            }
            foreach (BoxScreenRect box in boxes)
            {
                var from = new Float2((float)box.MinX, (float)box.MinY);
                var to = new Float2((float)box.MaxX, (float)box.MaxY);
                from = PointFromParent(ref from);
                to = PointFromParent(ref to);
                Boxes.Add(BoxScreenRect.FromCorners(from.X, from.Y, to.X, to.Y));
            }
            Showing = Boxes.Count > 0;
        }

        /// <summary>Puts every box away — on an empty selection, a lost camera, or teardown.</summary>
        public void Clear()
        {
            Boxes.Clear();
            Showing = false;
        }

        /// <inheritdoc />
        public override void Draw()
        {
            base.Draw();
            if (!Showing)
                return;
            float thickness = BorderThickness > 0f ? BorderThickness : 1f;
            foreach (BoxScreenRect box in Boxes)
            {
                float x = (float)box.MinX;
                float y = (float)box.MinY;
                float width = (float)box.Width;
                float height = (float)box.Height;
                if (width <= 0f || height <= 0f)
                    continue;
                // Four edges rather than one outline call: FillRectangle is what the marquee panel
                // already proved works in this project, so the box needs no new drawing API.
                float edge = Math.Min(thickness, Math.Min(width, height));
                Render2D.FillRectangle(new Rectangle(x, y, width, edge), Border);
                Render2D.FillRectangle(new Rectangle(x, y + height - edge, width, edge), Border);
                Render2D.FillRectangle(new Rectangle(x, y, edge, height), Border);
                Render2D.FillRectangle(new Rectangle(x + width - edge, y, edge, height), Border);
            }
        }

        /// <summary>A cosmetic overlay never owns the mouse: the panel covers the whole canvas, so
        /// answering "not over me" keeps clicks and hover going to the world and to the card.</summary>
        public override bool ContainsPoint(ref Float2 location, bool precise = false)
        {
            return false;
        }
    }

    /// <summary>
    /// Keeps the selection boxes in step with the live selection (Task 32 Part B), by projecting
    /// only the actors that are selected — never the whole scene — through the marquee's own
    /// projection, asking <see cref="SelectionBoxes"/> for the rectangles and handing them to the
    /// panel.
    ///
    /// Because it reads the selection every frame, a marquee release, a control-group recall and
    /// <c>select all …</c> need no wiring of their own, and deselecting clears the boxes in the
    /// same frame.
    ///
    /// Authoring: a UIControl actor named <c>SelectionBox</c> under a UI Canvas, inactive, whose
    /// control type is <see cref="SelectionBoxPanel"/>. Without it the selection still works — it
    /// just draws no boxes, and the console says once what is missing.
    /// </summary>
    public static class SelectionBoxesView
    {
        public const string ActorName = "SelectionBox";

        private static UIControl _panelActor;
        private static bool _warnedAboutActor;
        private static bool _warnedAboutControl;
        private static readonly List<BoxSelectCandidate> Candidates = new List<BoxSelectCandidate>();
        private static readonly List<BoxScreenRect> Boxes = new List<BoxScreenRect>();

        /// <summary>Refreshes the boxes from the current selection. Called once per frame, after
        /// the tick and the camera, so the boxes land on where the units are drawn this frame.</summary>
        public static void Update(EventRuntime runtime)
        {
            if (runtime == null)
                return;
            IReadOnlyList<object> selected = runtime.Selection == null ? null : runtime.Selection.Selected;
            if (selected == null || selected.Count == 0)
            {
                Hide();
                return;
            }

            UIControl actor = FindBox();
            if (actor == null)
            {
                WarnMissingActor();
                return;
            }
            SelectionBoxPanel panel = actor.Control as SelectionBoxPanel;
            if (panel == null)
            {
                WarnMissingControl();
                return;
            }

            FlaxEngine.Camera camera = FlaxWorld.FindCamera();
            if (camera == null)
            {
                Hide();
                return;
            }

            bool fog = runtime.Fog.Enabled;
            int localPlayer = runtime.Fog.LocalPlayer;
            Candidates.Clear();
            foreach (object item in selected)
            {
                var target = item as Actor;
                if (!MarqueeView.TryProject(runtime, camera, target, fog, localPlayer,
                        out BoxSelectCandidate candidate))
                    continue;
                Candidates.Add(candidate);
            }

            Float2 viewport = Screen.Size;
            Boxes.Clear();
            Boxes.AddRange(SelectionBoxes.Boxes(Candidates, viewport.X, viewport.Y));
            if (Boxes.Count == 0)
            {
                Hide();
                return;
            }

            if (!actor.IsActive)
                actor.IsActive = true;
            panel.SetBoxes(Boxes);
        }

        /// <summary>Puts the boxes away without warning: nothing selected, nothing in view, or a
        /// scene without a camera.</summary>
        public static void Hide()
        {
            UIControl actor = FindBox();
            if (actor == null)
                return;
            SelectionBoxPanel panel = actor.Control as SelectionBoxPanel;
            if (panel != null)
                panel.Clear();
            actor.IsActive = false;
        }

        private static UIControl FindBox()
        {
            if (_panelActor != null && _panelActor.Scene != null)
                return _panelActor;
            _panelActor = Level.FindActor(ActorName) as UIControl;
            return _panelActor;
        }

        /// <summary>Says once that the scene has no <c>SelectionBox</c> actor, the same treatment
        /// the marquee gets, so a play-check that sees no boxes is told why.</summary>
        private static void WarnMissingActor()
        {
            if (_warnedAboutActor)
                return;
            _warnedAboutActor = true;
            Debug.LogWarning("SelectionBox: add a UIControl actor named \"" + ActorName +
                "\" under a UI Canvas (control type SelectionBoxPanel) to see the boxes around " +
                "selected units.");
        }

        private static void WarnMissingControl()
        {
            if (_warnedAboutControl)
                return;
            _warnedAboutControl = true;
            Debug.LogWarning("SelectionBox: set the UI Control \"" + ActorName +
                "\" control type to SelectionBoxPanel to see the boxes around selected units.");
        }
    }
}
