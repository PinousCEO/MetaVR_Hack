using UnityEngine;

namespace LivingPartner.Character
{
    /// <summary>
    /// Water for the virtual rain, drawn to look natural: where a drop lands a fine ripple spreads on that surface, a few droplets fly off and
    /// sometimes a glint twinkles; a hand that keeps catching drops gets a wobbling film of water on top of it that drips
    /// from its edge. Everything is drawn with the soft water modes of the LPFx shader (9-12).
    /// Lives under MrCore/Rain/Water: the crowns, beads, glints, the two hand anchors and their films are child objects
    /// referenced below; nothing is created at runtime.
    /// </summary>
    public sealed class WaterFx : MonoBehaviour
    {
        [Tooltip("Splash crowns (LPFx mode 10). They lie flat on the surface that was hit.")]
        [SerializeField] FxQuad[] _crowns;
        [Tooltip("Flying droplets (LPFx mode 12). Billboards.")]
        [SerializeField] FxQuad[] _beads;
        [Tooltip("Twinkles on a hit (LPFx mode 5). Billboards.")]
        [SerializeField] FxQuad[] _glints;
        [Tooltip("Top surface of each hand: 0 = right, 1 = left. Placed every frame by the MR app.")]
        [SerializeField] Transform[] _handAnchors;
        [Tooltip("Water film on each hand (LPFx mode 11), a child of its anchor.")]
        [SerializeField] FxQuad[] _handFilms;

        struct Crown { public float age, life, size, seed; public Transform anchor; public Vector3 pos; public Quaternion rot; }
        struct Bead { public Vector3 pos, vel; public float age, life, size; }
        struct Glint { public Vector3 pos; public float age, life, size; }

        Crown[] _c; Bead[] _b; Glint[] _g;
        int _nextC, _nextB, _nextG;
        readonly float[] _wet = new float[2];
        readonly float[] _dripIn = new float[2];
        readonly bool[] _handOn = new bool[2];
        const float Gravity = 3.2f;                 // slower than real water: readable at this size

        void Awake()
        {
            _c = new Crown[_crowns.Length]; _b = new Bead[_beads.Length]; _g = new Glint[_glints.Length];
            for (int i = 0; i < _c.Length; i++) { _c[i].age = 1; _c[i].life = 1; _crowns[i].enabled = false; }
            for (int i = 0; i < _b.Length; i++) { _b[i].age = 1; _b[i].life = 1; _beads[i].enabled = false; }
            for (int i = 0; i < _g.Length; i++) { _g[i].age = 1; _g[i].life = 1; _glints[i].enabled = false; }
            for (int i = 0; i < _handFilms.Length; i++) _handFilms[i].enabled = false;
        }

        /// <summary>How wet a hand currently is (0..1): rises while it catches drops, dries when it stops.</summary>
        public float Wetness(int hand) => _wet[hand];

        /// <summary>Places a hand's anchor on the surface of the hand that faces the rain. up = that surface's normal, forward = towards the fingers.</summary>
        public void SetHand(int hand, bool present, Vector3 top, Vector3 up, Vector3 forward)
        {
            _handOn[hand] = present;
            if (present) _handAnchors[hand].SetPositionAndRotation(top, Quaternion.LookRotation(-up, forward));
        }

        /// <summary>
        /// A drop landed. point / normal describe the surface (world). hand = 0 / 1 when it landed on that hand: the
        /// splash then stays on the hand as the hand moves, and the hand gets wetter.
        /// </summary>
        public void Splash(Vector3 point, Vector3 normal, float size, int beads, int hand = -1)
        {
            Vector3 tx = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 ty = Vector3.Cross(normal, tx);
            Quaternion rot = Quaternion.LookRotation(-normal, ty);

            int i = _nextC; _nextC = (_nextC + 1) % _c.Length;
            Transform anchor = hand >= 0 ? _handAnchors[hand] : null;
            _c[i] = new Crown
            {
                age = 0, life = Random.Range(0.34f, 0.46f), size = size * Random.Range(0.85f, 1.2f), seed = Random.value, anchor = anchor,
                pos = anchor != null ? anchor.InverseTransformPoint(point) : point,
                rot = anchor != null ? Quaternion.Inverse(anchor.rotation) * rot : rot,
            };

            for (int k = 0; k < beads; k++)
            {
                float a = Random.value * Mathf.PI * 2f;
                Vector3 dir = tx * Mathf.Cos(a) + ty * Mathf.Sin(a);
                int j = _nextB; _nextB = (_nextB + 1) % _b.Length;
                _b[j] = new Bead
                {
                    pos = point + dir * size * 0.25f + normal * 0.002f,
                    vel = dir * Random.Range(0.16f, 0.34f) + normal * Random.Range(0.32f, 0.55f),
                    age = 0, life = Random.Range(0.30f, 0.46f), size = size * Random.Range(0.20f, 0.30f),
                };
            }

            if (hand >= 0)
            {
                _wet[hand] = Mathf.Min(1f, _wet[hand] + 0.13f);
                if (Random.value < 0.10f)
                {
                    int g = _nextG; _nextG = (_nextG + 1) % _g.Length;
                    _g[g] = new Glint { pos = point + normal * 0.012f, age = 0, life = 0.32f, size = size * 0.9f };
                }
            }
        }

        /// <summary>Call once per frame. viewer = eye position (billboards face it); time drives the film's wobble.</summary>
        public void Step(float dt, Vector3 viewer, float time)
        {
            for (int i = 0; i < _c.Length; i++)
            {
                var q = _crowns[i];
                if (_c[i].age >= _c[i].life) { if (q.enabled) q.enabled = false; continue; }
                _c[i].age += dt;
                float k = Mathf.Clamp01(_c[i].age / _c[i].life);
                Transform t = q.transform;
                if (_c[i].anchor != null) t.SetPositionAndRotation(_c[i].anchor.TransformPoint(_c[i].pos), _c[i].anchor.rotation * _c[i].rot);
                else t.SetPositionAndRotation(_c[i].pos, _c[i].rot);
                t.localScale = Vector3.one * _c[i].size * 2f;
                if (!q.enabled) q.enabled = true;
                q.SetParam(new Vector4(7f, 0f, k, _c[i].seed));
            }

            for (int i = 0; i < _b.Length; i++)
            {
                var q = _beads[i];
                if (_b[i].age >= _b[i].life) { if (q.enabled) q.enabled = false; continue; }
                _b[i].age += dt;
                _b[i].vel += Vector3.down * (Gravity * dt);
                _b[i].pos += _b[i].vel * dt;
                float k = Mathf.Clamp01(_b[i].age / _b[i].life);
                Transform t = q.transform;
                t.SetPositionAndRotation(_b[i].pos, Face(_b[i].pos, viewer));
                t.localScale = Vector3.one * _b[i].size * (k < 0.8f ? 1f : Mathf.Max(0.05f, (1f - k) / 0.2f));      // pops out at the end
                if (!q.enabled) q.enabled = true;
            }

            for (int i = 0; i < _g.Length; i++)
            {
                var q = _glints[i];
                if (_g[i].age >= _g[i].life) { if (q.enabled) q.enabled = false; continue; }
                _g[i].age += dt;
                float k = Mathf.Clamp01(_g[i].age / _g[i].life);
                Transform t = q.transform;
                t.SetPositionAndRotation(_g[i].pos, Face(_g[i].pos, viewer) * Quaternion.Euler(0, 0, k * 90f));
                t.localScale = Vector3.one * _g[i].size * Mathf.Sin(k * Mathf.PI);
                if (!q.enabled) q.enabled = true;
            }

            for (int h = 0; h < _handFilms.Length; h++)
            {
                _wet[h] = Mathf.Max(0f, _wet[h] - dt * (_handOn[h] ? 0.45f : 2.5f));
                var film = _handFilms[h];
                bool on = false;      // no sheet of water on the hand and no drips from it: drops splash on a hand and that is all
                if (film.enabled != on) film.enabled = on;
                if (!on) continue;
                film.phase = time;
                film.SetParam(new Vector4(0f, 0f, _wet[h], 0f));

                // a wet hand drips from the lowest point of its rim
                _dripIn[h] -= dt;
                if (_wet[h] > 0.35f && _dripIn[h] <= 0f)
                {
                    _dripIn[h] = Random.Range(0.16f, 0.34f);
                    Transform a = _handAnchors[h];
                    Vector3 up = -a.forward;
                    Vector3 downhill = Vector3.down - up * Vector3.Dot(Vector3.down, up);
                    if (downhill.sqrMagnitude < 0.01f) { float ang = Random.value * Mathf.PI * 2f; downhill = a.right * Mathf.Cos(ang) + a.up * Mathf.Sin(ang); }
                    downhill.Normalize();
                    float reach = Mathf.Lerp(0.048f, 0.085f, Mathf.Abs(Vector3.Dot(downhill, a.up)));          // the hand is longer than it is wide
                    int j = _nextB; _nextB = (_nextB + 1) % _b.Length;
                    _b[j] = new Bead { pos = a.position + downhill * reach - up * 0.004f, vel = downhill * 0.05f + Vector3.down * 0.05f, age = 0, life = 0.55f, size = 0.0085f };
                }
            }
        }

        /// <summary>Hides everything at once (the rain was switched off while effects were still playing).</summary>
        public void Clear()
        {
            for (int i = 0; i < _c.Length; i++) _c[i].age = _c[i].life;
            for (int i = 0; i < _b.Length; i++) _b[i].age = _b[i].life;
            for (int i = 0; i < _g.Length; i++) _g[i].age = _g[i].life;
            _wet[0] = _wet[1] = 0f;
        }

        static Quaternion Face(Vector3 pos, Vector3 viewer)
        {
            Vector3 to = pos - viewer;
            return to.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(to, Vector3.up) : Quaternion.identity;
        }
    }
}
