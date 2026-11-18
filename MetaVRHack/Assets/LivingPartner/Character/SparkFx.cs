using UnityEngine;

namespace LivingPartner.Character
{
    /// <summary>
    /// Small flying sparkles and expanding rings for take-off, landing and the airborne trail. Billboards that always face
    /// the viewer; short-lived, small, and emitted around the body rather than over it.
    /// A prefab (Prefabs/SparkFx.prefab) with a fixed pool of child quads referenced below - nothing is created at runtime.
    /// </summary>
    public sealed class SparkFx : MonoBehaviour
    {
        struct P { public Vector3 pos, vel; public float age, life, size; public bool ring; }
        [SerializeField] FxQuad[] _quads;
        P[] _p;
        int _next;

        void Awake()
        {
            _p = new P[_quads.Length];
            for (int i = 0; i < _p.Length; i++) { _p[i].age = 1; _p[i].life = 1; _quads[i].enabled = false; }
        }

        void Emit(Vector3 pos, Vector3 vel, float life, float size, bool ring)
        {
            int i = _next; _next = (_next + 1) % _p.Length;
            _p[i] = new P { pos = pos, vel = vel, age = 0, life = life, size = size, ring = ring };
            _quads[i].mode = ring ? 6 : 5;
        }

        /// <summary>A burst for take-off / landing: one expanding ring and a fan of sparkles. right/up span the plane the viewer sees.</summary>
        public void Burst(Vector3 pos, Vector3 right, Vector3 up, float scale, int sparks = 9)
        {
            Emit(pos, Vector3.zero, 0.55f, scale * 3.4f, true);
            for (int i = 0; i < sparks; i++)
            {
                // an upward fan; one spark alone goes up the middle (i / (sparks - 1) was 0 / 0 for it: a position and a
                // velocity that are not numbers, on every frame of the comet's tail)
                float a = Mathf.Lerp(0.12f, 0.88f, sparks > 1 ? i / (float)(sparks - 1) : 0.5f) * Mathf.PI;
                Vector3 dir = right * Mathf.Cos(a) + up * Mathf.Sin(a);
                Emit(pos + dir * scale * 0.25f, dir * scale * Random.Range(2.2f, 4.2f), Random.Range(0.35f, 0.6f), scale * Random.Range(0.28f, 0.5f), false);
            }
        }

        /// <summary>One small trailing sparkle (call a few times per second while airborne).</summary>
        public void Trail(Vector3 pos, float scale)
        {
            Emit(pos + Random.insideUnitSphere * scale * 0.25f, Random.insideUnitSphere * scale * 0.4f, Random.Range(0.3f, 0.5f), scale * Random.Range(0.22f, 0.4f), false);
        }

        /// <summary>Call once per frame. faceViewer = rotation that turns a quad towards the viewer.</summary>
        public void Step(float dt, Quaternion faceViewer)
        {
            for (int i = 0; i < _p.Length; i++)
            {
                var q = _quads[i];
                if (_p[i].age >= _p[i].life) { if (q.enabled) q.enabled = false; continue; }
                _p[i].age += dt;
                float k = Mathf.Clamp01(_p[i].age / _p[i].life);
                _p[i].pos += _p[i].vel * dt;
                _p[i].vel *= Mathf.Exp(-dt * 4.5f);
                var t = q.transform;
                if (!q.enabled) q.enabled = true;
                t.SetPositionAndRotation(_p[i].pos, faceViewer * Quaternion.Euler(0, 0, _p[i].ring ? 0 : k * 160f + i * 37f));
                if (_p[i].ring)
                {
                    t.localScale = Vector3.one * _p[i].size;
                    q.param = new Vector4(0.3f, 0, k, 0);
                    q.SetColors(Ui.Pal.Orange, Ui.Pal.Pink);
                }
                else
                {
                    t.localScale = Vector3.one * _p[i].size * (1f - 0.6f * k);
                    q.param = new Vector4(1.3f, 0, 0, 0);
                    q.color = new Color(1, 1, 1, 1f - k * k);
                    q.Apply();
                }
            }
        }
    }
}
