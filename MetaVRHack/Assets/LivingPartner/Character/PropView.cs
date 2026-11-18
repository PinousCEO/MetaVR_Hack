using UnityEngine;

namespace LivingPartner.Character
{
    /// <summary>
    /// One thing the partner owns, as an object in the room. A fixed pool of these is authored in the prefab; the app
    /// gives one a shape (a built-in toy shape or a generated model - mesh and texture are data), a size and a place.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class PropView : MonoBehaviour
    {
        [SerializeField] MeshFilter _filter;
        [SerializeField] MeshRenderer _renderer;
        [Tooltip("The soft shadow under it on the desk.")]
        public FxQuad shadow;
        [Tooltip("The ring that fills while the thing is being made.")]
        public FxQuad progress;

        [System.NonSerialized] public string itemId = "";      // which item it shows ("" = free)
        [System.NonSerialized] public string shape = "";       // what its mesh was built from (to know when to rebuild)
        [System.NonSerialized] public float height = 1f;       // of the shape, with the longest side = 1
        [System.NonSerialized] public float appear;            // pop-in 0..1
        [System.NonSerialized] public Vector3 shown;           // where it is drawn (it follows where it should be)
        [System.NonSerialized] public Texture texture;         // the generated model's texture (null: the built-in shape's colours)
        MaterialPropertyBlock _mpb;
        static readonly int TexId = Shader.PropertyToID("_MainTex"), ColorId = Shader.PropertyToID("_Color"), GhostId = Shader.PropertyToID("_Ghost"), FillId = Shader.PropertyToID("_Fill");

        public void Bind(MeshFilter f, MeshRenderer r) { _filter = f; _renderer = r; }
        public bool Visible { get => _renderer.enabled; set { if (_renderer.enabled != value) _renderer.enabled = value; } }
        public Mesh Mesh { get => _filter.sharedMesh; set => _filter.sharedMesh = value; }
        /// <summary>Its draw order among what is see-through (0 as authored; a thing that stands in a game's field on the desk is drawn pale and after the field's pictures - MrGlass).</summary>
        public int Order { get => _renderer.sortingOrder; set { if (_renderer.sortingOrder != value) _renderer.sortingOrder = value; } }

        public void SetLook(Color tint, float ghost, float fill)
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _mpb.SetTexture(TexId, texture != null ? texture : Texture2D.whiteTexture);
            _mpb.SetColor(ColorId, tint); _mpb.SetFloat(GhostId, ghost); _mpb.SetFloat(FillId, fill);
            _renderer.SetPropertyBlock(_mpb);
        }
    }
}
