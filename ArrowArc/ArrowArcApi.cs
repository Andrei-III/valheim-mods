using UnityEngine;

namespace ArrowArc
{
    // Public entry point for other mods (DamagePreview looks this up by reflection, so keep the signature stable).
    // The prediction is computed at most once per frame and shared with the arc renderer.
    public static class ArrowArcApi
    {
        private static readonly Flight Cached = new Flight();
        private static int _frame = -1;
        private static bool _active;

        // True when an arc is being shown this frame. `collider` is what the arrow will hit first,
        // or null when it lands on nothing (mid-air, water) - callers should then show no target.
        public static bool TryGetPredictedHit(out Collider collider)
        {
            var flight = GetFlight();
            collider = flight != null && flight.Hit ? flight.HitCollider : null;
            return flight != null;
        }

        internal static Flight GetFlight()
        {
            if (_frame != Time.frameCount)
            {
                _frame = Time.frameCount;
                var player = Player.m_localPlayer;
                _active = ArrowArcPlugin.Enabled.Value && player != null && !player.IsDead() &&
                          !player.InPlaceMode() && Trajectory.TryPredict(player, Cached);
            }
            return _active ? Cached : null;
        }
    }
}
