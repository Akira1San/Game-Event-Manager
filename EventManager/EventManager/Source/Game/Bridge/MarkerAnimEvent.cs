using FlaxEngine;

namespace Game
{
    /// <summary>
    /// An authored marker on an animation clip (Task 33 Part B). Put this event on one of the clip's
    /// Animation Event tracks, type the marker's name, and `when anim "Attack" hits "impact"` fires
    /// the tick playback crosses it. Flax invokes <see cref="OnEvent"/> once per pass, so the trigger
    /// is an edge by construction — no polling and no guessed `wait`.
    ///
    /// Flax exposes an Animation asset's event list to the editor only, so this callback is the one
    /// place a marker becomes visible to gameplay: a clip with no Marker Anim Event has no markers to
    /// hit, and `when anim "..." reaches &lt;n&gt;%` is the fallback.
    /// </summary>
    public class MarkerAnimEvent : AnimEvent
    {
        [Tooltip("The marker name `when anim \"<clip>\" hits \"<marker>\"` listens for.")]
        public string Marker = "";

        /// <inheritdoc />
        public override void OnEvent(AnimatedModel actor, Animation anim, float time, float deltaTime)
        {
            if (string.IsNullOrEmpty(Marker) || actor == null)
                return;
            EventsManager.Instance?.HandleAnimMarker(FlaxActions.OwnerActorOf(actor), Marker);
        }
    }
}
