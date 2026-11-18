using UnityEngine;

namespace LivingPartner.Character
{
    /// <summary>
    /// The ball of the tennis game as an object in the hierarchy (a sphere mesh with Art/Materials/LPBall.mat). The apps
    /// only place it, show or hide it, and give it the plane of the glass: each side draws its part of the ball.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class BallView : MonoBehaviour
    {
        [SerializeField] MeshRenderer _renderer;
        MaterialPropertyBlock _mpb;
        static readonly int FootballId = Shader.PropertyToID("_Football");
        static readonly int ClipPlaneId = Shader.PropertyToID("_ClipPlane"), ClipSignId = Shader.PropertyToID("_ClipSign"), LightDirId = Shader.PropertyToID("_LightDirWS");
        bool _football;
        public void Football(bool on) { if (_football != on) { _football = on; Apply(); } }
        Vector4 _plane; float _sign; Vector4 _light;

        public void PlaceFootball(Vector3 local, float diameter, float spin, bool visible)
        {
            transform.localPosition = local;
            transform.localRotation = Quaternion.Euler(spin, 0.6f * spin, 18f);
            transform.localScale = Vector3.one * diameter;
            if (!_football) { Football(true); SetLightDir(new Vector3(-0.38f, 0.76f, -0.52f)); }
            Visible = visible;
        }

        public void Bind(MeshRenderer r) { _renderer = r; }

        public bool Visible
        {
            get => _renderer.enabled;
            set { if (_renderer.enabled != value) _renderer.enabled = value; }
        }

        /// <summary>Where the ball is, how it has turned, and its diameter in metres.</summary>
        public void Place(Vector3 position, Quaternion rotation, float diameter)
        {
            transform.SetPositionAndRotation(position, rotation);
            transform.localScale = Vector3.one * diameter;
        }

        /// <summary>The glass: only the part of the ball on one side of it is drawn (sign +1 = the side the normal points to).</summary>
        public void SetClip(Vector3 planePoint, Vector3 planeNormal, float sign)
        {
            Vector3 n = planeNormal.normalized;
            _plane = new Vector4(n.x, n.y, n.z, -Vector3.Dot(n, planePoint)); _sign = sign;
            Apply();
        }

        public void SetLightDir(Vector3 toLightWorld) { Vector3 l = toLightWorld.normalized; _light = new Vector4(l.x, l.y, l.z, 0); Apply(); }

        void Apply()
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _mpb.SetFloat(FootballId, _football ? 1f : 0f);
            _mpb.SetVector(ClipPlaneId, _plane); _mpb.SetFloat(ClipSignId, _sign); _mpb.SetVector(LightDirId, _light);
            _renderer.SetPropertyBlock(_mpb);
        }
    }
}
