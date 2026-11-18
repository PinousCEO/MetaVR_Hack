using UnityEngine;

namespace LivingPartner.Character
{
    /// <summary>
    /// One motion-graphics quad in the hierarchy (ring, panel, sparkle, shadow ...). It lives in the scene / prefab as a
    /// real object: its look (mode, colours, parameters, draw order) is serialized here and visible in the Inspector, and
    /// all quads share ONE material asset - per-quad values go through a MaterialPropertyBlock.
    /// Enabling / disabling this component shows / hides the quad.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class FxQuad : MonoBehaviour
    {
        [Tooltip("0 soft disc, 1 ring, 2 gradient panel, 3 frame, 4 radial progress, 5 sparkle, 6 ripple, 7 dashed ring, 8 glow; water: 9 rain streak, 10 splash ripple, 11 sheet of water, 12 droplet, 13 spell flare")]
        public int mode;
        public Color color = Color.white;
        public Color color2 = Color.white;
        public Vector4 param = new Vector4(0.8f, 0.1f, 0, 0);
        public float phase;
        public int order;
        [SerializeField] MeshRenderer _renderer;

        MaterialPropertyBlock _mpb;
        Vector4 _clipPlane; float _clipSign;
        static readonly int ColorId = Shader.PropertyToID("_Color"), Color2Id = Shader.PropertyToID("_Color2"), ModeId = Shader.PropertyToID("_Mode"),
            ParamId = Shader.PropertyToID("_Param"), PhaseId = Shader.PropertyToID("_Phase"), ClipPlaneId = Shader.PropertyToID("_ClipPlane"), ClipSignId = Shader.PropertyToID("_ClipSign");

        public MeshRenderer Renderer => _renderer;

        public void Bind(MeshRenderer r) { _renderer = r; Apply(); }

#if UNITY_EDITOR
        void Reset() { _renderer = GetComponent<MeshRenderer>(); }      // authoring only: adding the component in the Editor
#endif
        void OnEnable() { if (_renderer == null) return; _renderer.enabled = true; Apply(); }
        void OnDisable() { if (_renderer != null) _renderer.enabled = false; }
        void OnValidate() { if (_renderer != null && isActiveAndEnabled) Apply(); }

        public void SetColor(Color c) { color = c; Apply(); }
        public void SetColors(Color c, Color c2) { color = c; color2 = c2; Apply(); }
        public void SetParam(Vector4 p) { param = p; Apply(); }
        public void SetPhase(float p) { phase = p; Apply(); }
        public void SetMode(int m) { mode = m; Apply(); }
        public void SetClip(Vector4 plane, float sign) { _clipPlane = plane; _clipSign = sign; Apply(); }

        public void Apply()
        {
            if (_renderer == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _mpb.SetColor(ColorId, color); _mpb.SetColor(Color2Id, color2);
            _mpb.SetFloat(ModeId, mode); _mpb.SetVector(ParamId, param); _mpb.SetFloat(PhaseId, phase);
            _mpb.SetVector(ClipPlaneId, _clipPlane); _mpb.SetFloat(ClipSignId, _clipSign);
            _renderer.SetPropertyBlock(_mpb);
            if (_renderer.sortingOrder != order) _renderer.sortingOrder = order;
        }
    }
}
