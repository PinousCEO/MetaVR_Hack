using UnityEngine;

namespace LivingPartner.Character
{
    /// <summary>
    /// One picture of the interface art on a quad (a tile of the hand menu, its panel, its tab bar ...). The pictures
    /// are drawn as a web page and cut into textures by tools/ui-art/build_ui_art.py; here one of them is shown, and can
    /// be swapped (Rain / Rain off), brightened (a finger near it) and given a passing band of light.
    /// All of them share ONE material asset; the per-quad values go through a MaterialPropertyBlock.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class UiSprite : MonoBehaviour
    {
        public Texture2D texture;
        public Color color = Color.white;
        [Range(0, 1)] public float boost;
        [Tooltip("0..1: a band of light crossing the picture. Negative: none.")]
        public float shine = -1f;
        public int order;
        [Tooltip("w > 0.5: no picture - a filled rounded rectangle x by y (the quad's own size, any unit) with the corner radius z, in `color` (GlassArt.Paper).")]
        public Vector4 round;
        [SerializeField] MeshRenderer _renderer;

        MaterialPropertyBlock _mpb;
        static readonly int TexId = Shader.PropertyToID("_MainTex"), ColorId = Shader.PropertyToID("_Color"), BoostId = Shader.PropertyToID("_Boost"), ShineId = Shader.PropertyToID("_Shine"), RoundId = Shader.PropertyToID("_Round");

        public MeshRenderer Renderer => _renderer;
        public void Bind(MeshRenderer r) { _renderer = r; Apply(); }

#if UNITY_EDITOR
        void Reset() { _renderer = GetComponent<MeshRenderer>(); }      // authoring only
#endif
        void OnEnable() { if (_renderer == null) return; _renderer.enabled = true; Apply(); }
        void OnDisable() { if (_renderer != null) _renderer.enabled = false; }
        void OnValidate() { if (_renderer != null && isActiveAndEnabled) Apply(); }

        public void SetTexture(Texture2D t) { if (texture == t || t == null) return; texture = t; Apply(); }
        public void SetColor(Color c) { color = c; Apply(); }
        public void SetLook(float boostNow, float shineNow) { boost = boostNow; shine = shineNow; Apply(); }

        public void Apply()
        {
            if (_renderer == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            if (texture != null) _mpb.SetTexture(TexId, texture);
            _mpb.SetColor(ColorId, color); _mpb.SetFloat(BoostId, boost); _mpb.SetFloat(ShineId, shine); _mpb.SetVector(RoundId, round);
            _renderer.SetPropertyBlock(_mpb);
            if (_renderer.sortingOrder != order) _renderer.sortingOrder = order;
        }
    }
}
