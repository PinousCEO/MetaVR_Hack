using UnityEngine;

namespace LivingPartner.Character
{
    /// <summary>
    /// The surroundings: a large sphere round the user's head drawn by LivingPartner/Ambience. Authored once in the
    /// prefab; the app sets what it shows and where the work area is (that part is never drawn over).
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class AmbienceView : MonoBehaviour
    {
        [SerializeField] MeshRenderer _renderer;
        [Tooltip("The small picture of noise the painted places are made of (Art/Textures/LPNoise2.asset, made by AmbienceNoise in the Editor).")]
        [SerializeField] Texture2D _noise;
        MaterialPropertyBlock _mpb;
        static readonly int C0 = Shader.PropertyToID("_C0"), C1 = Shader.PropertyToID("_C1"), C2 = Shader.PropertyToID("_C2"), Pattern = Shader.PropertyToID("_Pattern"),
            Dim = Shader.PropertyToID("_Dim"), Speed = Shader.PropertyToID("_Speed"), Density = Shader.PropertyToID("_Density"), Beat = Shader.PropertyToID("_Beat"),
            Amount = Shader.PropertyToID("_Amount"), WorkDir = Shader.PropertyToID("_WorkDir"), WorkSize = Shader.PropertyToID("_WorkSize"), Noise = Shader.PropertyToID("_Noise"),
            PlaceDir = Shader.PropertyToID("_PlaceDir"), PlaceAt = Shader.PropertyToID("_PlaceAt");

        public void Bind(MeshRenderer r) { _renderer = r; }
        public bool Visible { get => _renderer.enabled; set { if (_renderer.enabled != value) _renderer.enabled = value; } }

        /// <summary>
        /// placeDir: where a painted place (the mountain top, the seaside) is anchored in the WORLD - xyz the direction
        /// the work area lay in when the place came on, w its half width in degrees then. It must NOT change while the
        /// place is on: the sun, the sea and the peaks hang on it (the work area's window follows the head, they must not).
        /// placeAt: where the place's ground lies in the room - xyz the point of it straight under the user when the
        /// place came on, w how far below the eyes that was (metres). Fixed likewise: the beach, the sea and the
        /// summit underfoot stay where they are when the head moves.
        /// </summary>
        public void Set(Color c0, Color c1, Color c2, int pattern, float dim, float speed, float density, float beat, float amount, Vector3 workDir, Vector4 workSize, Vector4 placeDir, Vector4 placeAt)
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _mpb.SetColor(C0, c0); _mpb.SetColor(C1, c1); _mpb.SetColor(C2, c2);
            _mpb.SetFloat(Pattern, pattern); _mpb.SetFloat(Dim, dim); _mpb.SetFloat(Speed, speed); _mpb.SetFloat(Density, density);
            _mpb.SetFloat(Beat, beat); _mpb.SetFloat(Amount, amount);
            _mpb.SetVector(WorkDir, workDir); _mpb.SetVector(WorkSize, workSize); _mpb.SetVector(PlaceDir, placeDir); _mpb.SetVector(PlaceAt, placeAt);
            if (_noise != null) _mpb.SetTexture(Noise, _noise);
            _renderer.SetPropertyBlock(_mpb);
        }
    }
}
