using UnityEngine;

namespace LivingPartner.Character
{
    /// <summary>
    /// The portal's look, shared by the Quest and Windows renderers: a gradient ring that drifts round, two counter-rotating
    /// dashed rings, ripples that travel outwards and a ring of twinkling sparkles. Everything lives on the display plane
    /// AROUND the opening - nothing is drawn over the body that is passing through.
    /// A prefab (Prefabs/PortalFx.prefab): every quad is a child object referenced below. Drive it with the shared host
    /// time so both sides animate in step.
    /// </summary>
    public sealed class PortalFx : MonoBehaviour
    {
        [SerializeField] FxQuad _ring, _dashOuter, _dots;
        [SerializeField] FxQuad[] _ripples;
        [SerializeField] FxQuad[] _sparks;

        float[] _rippleAge;
        float _alpha, _appear, _radius = 0.05f, _nextRipple;
        bool _want; float _wantIntensity; bool _crossing; bool _shown = true;
        public bool Visible => _alpha > 0.01f;
        public Vector3 Position => transform.position;
        public float Extent => _radius * 3.2f;

        void Awake()
        {
            _rippleAge = new float[_ripples.Length];
            for (int i = 0; i < _rippleAge.Length; i++) _rippleAge[i] = 2f;
            SetShown(false);
        }

        void SetShown(bool on)
        {
            if (_shown == on) return;
            _shown = on;
            _ring.enabled = _dashOuter.enabled = _dots.enabled = on;
            for (int i = 0; i < _ripples.Length; i++) _ripples[i].enabled = false;
            for (int i = 0; i < _sparks.Length; i++) _sparks[i].enabled = on;
        }

        /// <summary>Call once per frame before any Show().</summary>
        public void Begin() { _want = false; }

        /// <summary>Asks for the portal at this place this frame. intensity 0..1; crossing = the body is passing right now.</summary>
        public void Show(Vector3 pos, Quaternion planeRotation, float radius, float intensity, bool crossing)
        {
            _want = true; _wantIntensity = Mathf.Clamp01(intensity); _crossing = crossing; _radius = radius;
            transform.SetPositionAndRotation(pos, planeRotation);
        }

        /// <summary>Call once per frame after the Show() calls. time = shared (host) time in seconds.</summary>
        public void Step(float dt, double time)
        {
            float target = _want ? Mathf.Lerp(0.55f, 1f, _wantIntensity) : 0f;
            _alpha = Mathf.MoveTowards(_alpha, target, dt * (_want ? 3.5f : 4.5f));
            _appear = _want ? Mathf.Min(1f, _appear + dt / 0.45f) : Mathf.Max(0f, _appear - dt / 0.25f);
            if (_alpha <= 0.01f) { SetShown(false); for (int i = 0; i < _rippleAge.Length; i++) _rippleAge[i] = 2f; return; }
            SetShown(true);
            float t = (float)(time % 600.0);
            float size = _radius * 2f / 0.62f * Mathf.Max(0.001f, Ui.Pop(_appear));      // the main ring sits exactly on the opening's radius
            float a = _alpha;

            _ring.transform.localScale = Vector3.one * size;
            _ring.phase = t * 0.35f; _ring.param = new Vector4(0.62f, _crossing ? 0.055f : 0.038f, 0, 0);
            _ring.SetColors(WithA(Ui.Pal.Orange, a), WithA(Ui.Pal.Pink, a));

            _dashOuter.transform.localScale = Vector3.one * size;
            _dashOuter.phase = -t * 0.12f;
            _dashOuter.SetColors(WithA(Ui.Pal.Violet, a * 0.70f), WithA(Ui.Pal.Cyan, a * 0.70f));

            _dots.transform.localScale = Vector3.one * size;
            _dots.phase = t * 0.05f;
            _dots.SetColors(WithA(Ui.Pal.Orange, a * 0.60f), WithA(Ui.Pal.Pink, a * 0.60f));

            // ripples leave the ring and travel outwards; faster while the body is passing
            _nextRipple -= dt;
            if (_want && _nextRipple <= 0f)
            {
                _nextRipple = _crossing ? 0.28f : 0.85f;
                int oldest = 0; for (int i = 1; i < _rippleAge.Length; i++) if (_rippleAge[i] > _rippleAge[oldest]) oldest = i;
                if (_rippleAge[oldest] >= 1f) _rippleAge[oldest] = 0f;
            }
            for (int i = 0; i < _ripples.Length; i++)
            {
                _rippleAge[i] += dt / 0.95f;
                bool on = _rippleAge[i] < 1f;
                if (_ripples[i].enabled != on) _ripples[i].enabled = on;
                if (!on) continue;
                _ripples[i].transform.localScale = Vector3.one * size * 1.55f;
                _ripples[i].transform.localPosition = new Vector3(0, 0, 0.0002f);
                _ripples[i].param = new Vector4(0.62f / 1.55f, 0, _rippleAge[i], 0);
                _ripples[i].SetColors(WithA(Ui.Pal.Orange, a * 0.8f), WithA(Ui.Pal.Violet, a * 0.8f));
            }

            // sparkles orbit just outside the ring and twinkle
            float orbit = _radius * 1.28f;
            for (int i = 0; i < _sparks.Length; i++)
            {
                float ph = i / (float)_sparks.Length;
                float ang = (ph + t * (i % 2 == 0 ? 0.045f : -0.03f)) * Mathf.PI * 2f;
                float rr = orbit * (1f + 0.10f * Mathf.Sin(t * 1.7f + i * 2.1f)) * Mathf.Max(0.001f, Ui.Pop(_appear));
                float tw = 0.55f + 0.45f * Mathf.Sin(t * (3.1f + i * 0.37f) + i * 1.9f);
                var st = _sparks[i].transform;
                st.localPosition = new Vector3(Mathf.Cos(ang) * rr, Mathf.Sin(ang) * rr, -0.0003f);
                st.localScale = Vector3.one * _radius * (0.20f + 0.26f * tw) * (_crossing ? 1.25f : 1f);
                st.localRotation = Quaternion.Euler(0, 0, t * 40f * (i % 2 == 0 ? 1f : -1f));
                _sparks[i].SetColor(new Color(1, 1, 1, a * (0.35f + 0.65f * tw)));
            }
        }

        static Color WithA(Color c, float a) => new Color(c.r, c.g, c.b, a);
    }
}
