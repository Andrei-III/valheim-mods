using UnityEngine;
using UnityEngine.Rendering;

namespace ArrowArc
{
    // Draws the predicted flight as a world-space line, plus a ring where it lands.
    internal class ArcRenderer : MonoBehaviour
    {
        private const int RingSegments = 40;
        private const float FadeInMeters = 2.5f; // keep the first stretch clear so it doesn't cover the crosshair
        private const float MinRingRadius = 0.15f;

        private Flight _flight;
        private LineRenderer _line;
        private LineRenderer _ring;
        private Material _material;
        private readonly Vector3[] _ringPoints = new Vector3[RingSegments];
        private Vector3[] _linePoints = new Vector3[256];

        private void LateUpdate()
        {
            _flight = ArrowArcApi.GetFlight();
            if (_flight == null)
            {
                SetVisible(false);
                return;
            }

            EnsureCreated();
            SetVisible(true);

            Color color = !_flight.Hit
                ? ArrowArcPlugin.NoHitColor.Value // vanishes in mid-air (out of range): firing now hits nothing
                : _flight.HitCharacter != null && _flight.HitCharacter != Player.m_localPlayer
                    ? ArrowArcPlugin.CreatureHitColor.Value
                    : ArrowArcPlugin.LineColor.Value;

            UpdateLine(color);
            UpdateRing(color);
        }

        private void UpdateLine(Color color)
        {
            var pts = _flight.Points;
            if (_linePoints.Length < pts.Count) _linePoints = new Vector3[pts.Count * 2];
            float length = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                _linePoints[i] = pts[i];
                if (i > 0) length += Vector3.Distance(pts[i - 1], pts[i]);
            }
            _line.positionCount = pts.Count;
            _line.SetPositions(_linePoints);

            float w = ArrowArcPlugin.LineWidth.Value;
            _line.startWidth = w;
            _line.endWidth = w;

            float fade = length > 0f ? Mathf.Clamp(FadeInMeters / length, 0.01f, 0.5f) : 0.5f;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(color.a, fade), new GradientAlphaKey(color.a, 1f) });
            _line.colorGradient = g;
        }

        private void UpdateRing(Color color)
        {
            if (!ArrowArcPlugin.ShowImpactMarker.Value || !_flight.Hit)
            {
                _ring.enabled = false;
                return;
            }
            _ring.enabled = true;

            float radius = MinRingRadius;
            if (ArrowArcPlugin.MarkerShowsSpread.Value)
            {
                float dist = Vector3.Distance(_flight.Points[0], _flight.HitPoint);
                radius = Mathf.Max(MinRingRadius, dist * Mathf.Tan(_flight.SpreadDegrees * Mathf.Deg2Rad));
            }

            Vector3 n = _flight.HitNormal.sqrMagnitude > 0.001f ? _flight.HitNormal.normalized : Vector3.up;
            Vector3 a = Vector3.Cross(n, Mathf.Abs(n.y) < 0.99f ? Vector3.up : Vector3.forward).normalized;
            Vector3 b = Vector3.Cross(n, a);
            Vector3 c = _flight.HitPoint + n * 0.03f; // lift off the surface to avoid z-fighting
            for (int i = 0; i < RingSegments; i++)
            {
                float t = i * Mathf.PI * 2f / RingSegments;
                _ringPoints[i] = c + (a * Mathf.Cos(t) + b * Mathf.Sin(t)) * radius;
            }
            _ring.positionCount = RingSegments;
            _ring.SetPositions(_ringPoints);
            float w = ArrowArcPlugin.LineWidth.Value;
            _ring.startWidth = w;
            _ring.endWidth = w;
            _ring.startColor = color;
            _ring.endColor = color;
        }

        private void EnsureCreated()
        {
            if (_line != null) return;

            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
            if (shader == null) ArrowArcPlugin.Log.LogWarning("No suitable shader found; the arc may not render.");
            _material = new Material(shader);

            _line = CreateLine("ArrowArc_line", false);
            _ring = CreateLine("ArrowArc_ring", true);
        }

        private LineRenderer CreateLine(string name, bool loop)
        {
            var go = new GameObject(name);
            DontDestroyOnLoad(go);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = loop;
            lr.sharedMaterial = _material;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 2;
            lr.alignment = LineAlignment.View;
            lr.enabled = false;
            return lr;
        }

        private void SetVisible(bool visible)
        {
            if (_line == null) return;
            if (_line.enabled != visible) _line.enabled = visible;
            if (!visible && _ring.enabled) _ring.enabled = false;
        }

        private void OnDestroy()
        {
            if (_line != null) Destroy(_line.gameObject);
            if (_ring != null) Destroy(_ring.gameObject);
            if (_material != null) Destroy(_material);
        }
    }
}
