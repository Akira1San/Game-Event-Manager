namespace Game
{
    /// <summary>
    /// A UIControl that only draws. It may cover the whole canvas, but it owns no part of it:
    /// <see cref="EventsManager"/>'s screen-space hit test skips these, so the selection boxes
    /// and the drag rectangle cannot swallow the click that clears a selection or starts a drag.
    ///
    /// This is needed because that hit test falls back to a rectangle built from the control's
    /// size when Flax's own <c>IsMouseOver</c> is false, and a full-canvas rectangle answers
    /// "yes" everywhere — a control's own <c>ContainsPoint</c> override cannot save it.
    /// </summary>
    public interface ICosmeticControl
    {
    }
}
