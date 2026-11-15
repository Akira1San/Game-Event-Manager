using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;

namespace Game
{
    /// <summary>
    /// The minimap panel itself (Task 24): draws one <see cref="MinimapSnapshot"/> — fogged
    /// terrain, unit dots and the main camera's footprint — and reports clicks and drags back to
    /// <see cref="MinimapView"/>.
    ///
    /// To use it, add a UIControl actor named <c>Minimap</c> and set its control type to
    /// <c>MinimapPanel</c> in the editor; the panel takes its size from that control.
    /// </summary>
    public class MinimapPanel : ContainerControl
    {
        /// <summary>The frame to draw. Set by <see cref="MinimapView"/> every tick.</summary>
        public MinimapSnapshot Snapshot;

        /// <summary>The runtime whose camera and selection the input drives.</summary>
        public EventRuntime Runtime;

        /// <summary>How far the mouse must move before a press counts as a drag.</summary>
        public const float DragThreshold = 4f;

        private bool _pressed;
        private bool _dragging;
        private Float2 _pressStart;
        private Float2 _dragEnd;

        /// <inheritdoc />
        public override void Draw()
        {
            base.Draw();
            var bounds = new Rectangle(Float2.Zero, Size);
            Render2D.FillRectangle(bounds, new Color(0.06f, 0.07f, 0.08f));

            MinimapSnapshot snapshot = Snapshot;
            if (snapshot == null || snapshot.Columns <= 0 || Width <= 0 || Height <= 0)
            {
                Render2D.DrawRectangle(bounds, new Color(0.3f, 0.32f, 0.35f));
                return;
            }

            float cellWidth = Width / snapshot.Columns;
            float cellHeight = Height / snapshot.Rows;
            for (int row = 0; row < snapshot.Rows; row++)
            {
                for (int column = 0; column < snapshot.Columns; column++)
                {
                    MinimapCell cell = snapshot.CellAt(column, row);
                    Render2D.FillRectangle(
                        new Rectangle(column * cellWidth, row * cellHeight, cellWidth + 0.5f, cellHeight + 0.5f),
                        ColourOf(cell));
                }
            }

            float scale = snapshot.PanelSize > 0 ? Width / (float)snapshot.PanelSize : 1f;

            // Where the main camera is looking, so the panel doubles as a scrollbar.
            if (snapshot.ViewWidth > 0 && snapshot.ViewHeight > 0)
            {
                Render2D.DrawRectangle(new Rectangle(
                    (float)snapshot.ViewX * scale, (float)snapshot.ViewY * scale,
                    (float)snapshot.ViewWidth * scale, (float)snapshot.ViewHeight * scale),
                    new Color(0.95f, 0.95f, 0.95f, 0.75f));
            }

            foreach (MinimapUnit unit in snapshot.Units)
            {
                Minimap.WorldToPanel(unit.X, unit.Z, snapshot.CenterX, snapshot.CenterZ,
                    snapshot.PanelSize, snapshot.Extent, out double px, out double py);
                var dot = new Rectangle((float)px * scale - 2f, (float)py * scale - 2f, 4f, 4f);
                Color colour = unit.Player == 0
                    ? new Color(0.35f, 0.75f, 1f)
                    : new Color(1f, 0.4f, 0.35f);
                Render2D.FillRectangle(dot, colour);
                if (unit.Selected)
                    Render2D.DrawRectangle(new Rectangle(dot.X - 1f, dot.Y - 1f, dot.Width + 2f, dot.Height + 2f),
                        Color.White);
            }

            if (_dragging)
            {
                Render2D.DrawRectangle(new Rectangle(
                    Math.Min(_pressStart.X, _dragEnd.X), Math.Min(_pressStart.Y, _dragEnd.Y),
                    Math.Abs(_dragEnd.X - _pressStart.X), Math.Abs(_dragEnd.Y - _pressStart.Y)),
                    new Color(1f, 0.9f, 0.3f));
            }

            Render2D.DrawRectangle(bounds, new Color(0.3f, 0.32f, 0.35f));
        }

        /// <inheritdoc />
        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button != MouseButton.Left)
                return base.OnMouseDown(location, button);
            _pressed = true;
            _dragging = false;
            _pressStart = location;
            _dragEnd = location;
            StartMouseCapture();
            return true;
        }

        /// <inheritdoc />
        public override void OnMouseMove(Float2 location)
        {
            if (_pressed)
            {
                _dragEnd = location;
                double dx = location.X - _pressStart.X;
                double dz = location.Y - _pressStart.Y;
                if (Math.Sqrt(dx * dx + dz * dz) > DragThreshold)
                    _dragging = true;
            }
            base.OnMouseMove(location);
        }

        /// <inheritdoc />
        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            if (button != MouseButton.Left || !_pressed)
                return base.OnMouseUp(location, button);
            _pressed = false;
            EndMouseCapture();
            _dragEnd = location;
            if (Runtime != null && Snapshot != null)
            {
                Float2 from = ToPanelSpace(_pressStart);
                Float2 to = ToPanelSpace(location);
                if (_dragging)
                    MinimapView.SelectRect(Runtime, Snapshot, from, to);
                else
                    MinimapView.PanTo(Runtime, Snapshot, to);
            }
            _dragging = false;
            return true;
        }

        /// <inheritdoc />
        public override void OnMouseLeave()
        {
            if (!_pressed)
                _dragging = false;
            base.OnMouseLeave();
        }

        /// <summary>Control pixels → the panel units the snapshot is expressed in.</summary>
        private Float2 ToPanelSpace(Float2 location)
        {
            if (Snapshot == null || Snapshot.PanelSize <= 0 || Width <= 0)
                return location;
            float scale = (float)Snapshot.PanelSize / Width;
            return new Float2(location.X * scale, location.Y * scale);
        }

        private static Color ColourOf(MinimapCell cell)
        {
            if (cell.Fog == FogState.Unexplored)
                return new Color(0.03f, 0.03f, 0.04f); // never seen: solid fog
            if (cell.Water)
                return cell.Fog == FogState.Visible
                    ? new Color(0.12f, 0.3f, 0.55f)
                    : new Color(0.09f, 0.19f, 0.32f);
            return cell.Fog == FogState.Visible
                ? new Color(0.28f, 0.45f, 0.29f)
                : new Color(0.2f, 0.3f, 0.21f); // remembered terrain, dimmed
        }
    }

    /// <summary>
    /// Feeds the minimap panel (Task 24): one snapshot per tick from the terrain, fog and
    /// selection state, and the two interactions — click to move the camera there, drag to
    /// select the units inside a rectangle.
    ///
    /// Authoring: a UIControl actor named <c>Minimap</c> whose control type is
    /// <see cref="MinimapPanel"/>. The global <c>Minimap</c> set to 0 hides the panel; without
    /// the actor nothing happens. Clicking reuses the free camera from Task 26
    /// (<c>camera free</c> semantics), so a click releases a follow and pans the main camera.
    /// </summary>
    public static class MinimapView
    {
        public const string ActorName = "Minimap";

        private static bool _warnedAboutControl;

        /// <summary>Refreshes the panel from the running game. Called once per tick.</summary>
        public static void Update(EventRuntime runtime)
        {
            if (runtime == null)
                return;
            var ui = Level.FindActor(ActorName) as UIControl;
            if (ui == null)
                return;

            bool show = Minimap.Enabled(runtime.Variables);
            ui.IsActive = show;
            if (!show)
                return;

            var panel = ui.Control as MinimapPanel;
            if (panel == null)
            {
                if (!_warnedAboutControl)
                {
                    _warnedAboutControl = true;
                    Debug.LogWarning("Minimap: set the UI Control \"" + ActorName +
                        "\" control type to MinimapPanel to show the map.");
                }
                return;
            }

            panel.Runtime = runtime;
            panel.Snapshot = BuildSnapshot(runtime);
        }

        /// <summary>Pan the main camera so its centre lands on this panel point.</summary>
        public static void PanTo(EventRuntime runtime, MinimapSnapshot snapshot, Float2 panelPoint)
        {
            if (runtime == null || snapshot == null)
                return;
            Minimap.PanelToWorld(panelPoint.X, panelPoint.Y, snapshot.CenterX, snapshot.CenterZ,
                snapshot.PanelSize, snapshot.Extent, out double worldX, out double worldZ);

            // Same hop as `camera free`, then the focus lands where the player clicked.
            FlaxActions.EnterFreeCamera(runtime);
            runtime.Variables.SetGlobal(CameraRig.PanXVariable, ValueBox.OfNumber(worldX));
            runtime.Variables.SetGlobal(CameraRig.PanZVariable, ValueBox.OfNumber(worldZ));
            FlaxActions.ApplyFreeCamera(runtime);
        }

        /// <summary>Select the local player's units inside the dragged rectangle.</summary>
        public static void SelectRect(EventRuntime runtime, MinimapSnapshot snapshot, Float2 from, Float2 to)
        {
            if (runtime == null || snapshot == null)
                return;
            Minimap.DragRect(from.X, from.Y, to.X, to.Y, snapshot.CenterX, snapshot.CenterZ,
                snapshot.PanelSize, snapshot.Extent,
                out double minX, out double minZ, out double maxX, out double maxZ);

            int localPlayer = runtime.Fog.LocalPlayer;
            var picked = new List<object>();
            foreach (object item in runtime.World.Actors())
            {
                var actor = item as Actor;
                if (actor == null || !actor || !actor.IsActive)
                    continue;
                if (runtime.Fog.Enabled && runtime.Fog.PlayerOf(actor) != localPlayer)
                    continue;
                if (runtime.Fog.Enabled && !runtime.Fog.IsVisibleTo(localPlayer, actor))
                    continue;
                if (!Minimap.InRect(minX, minZ, maxX, maxZ, actor.Position.X, actor.Position.Z))
                    continue;
                picked.Add(actor);
            }
            runtime.Selection.ReplaceSelection(picked);
        }

        private static MinimapSnapshot BuildSnapshot(EventRuntime runtime)
        {
            EventVariables variables = runtime.Variables;
            double height = variables.GetGlobal("CameraHeight").AsNumber();

            double focusX;
            double focusZ;
            if (variables.GetGlobal(CameraRig.FreeVariable).AsNumber() > 0)
            {
                focusX = variables.GetGlobal(CameraRig.PanXVariable).AsNumber();
                focusZ = variables.GetGlobal(CameraRig.PanZVariable).AsNumber();
            }
            else
            {
                Actor camera = FlaxWorld.FindCamera();
                focusX = camera != null ? camera.Position.X : 0;
                focusZ = camera != null ? camera.Position.Z : 0;
            }

            int localPlayer = runtime.Fog.LocalPlayer;
            var units = new List<MinimapUnit>();
            foreach (object item in runtime.World.Actors())
            {
                var actor = item as Actor;
                if (actor == null || !actor || !actor.IsActive)
                    continue;
                if (runtime.Fog.Enabled && !runtime.Fog.IsVisibleTo(localPlayer, actor))
                    continue;
                units.Add(new MinimapUnit(actor.Position.X, actor.Position.Z,
                    runtime.Fog.PlayerOf(actor), runtime.Selection.IsSelected(actor)));
            }

            // Rough footprint of the top-down camera: a screen's worth of ground around the focus.
            double viewWidth = height > 0 ? height * 1.6 : 800;
            double viewDepth = height > 0 ? height * 1.2 : 600;
            return Minimap.Build(variables, runtime.Terrain, runtime.Fog, localPlayer,
                focusX, focusZ, viewWidth, viewDepth, units);
        }
    }
}
