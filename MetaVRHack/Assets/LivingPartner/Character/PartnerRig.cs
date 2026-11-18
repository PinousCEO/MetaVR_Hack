using LivingPartner.Shared;
using UnityEngine;
using Pose = LivingPartner.Shared.Pose;
using SN = System.Numerics;

namespace LivingPartner.Character
{
    public static class Conv
    {
        public static Vector3 U(SN.Vector3 v) => new Vector3(v.X, v.Y, v.Z);
        public static Vector3 U(V3 v) => new Vector3(v.x, v.y, v.z);
        public static SN.Vector3 N(Vector3 v) => new SN.Vector3(v.x, v.y, v.z);
        public static V3 V(Vector3 v) => new V3(v.x, v.y, v.z);
        public static Quaternion U(SN.Quaternion q) => new Quaternion(q.X, q.Y, q.Z, q.W);
        public static SN.Quaternion N(Quaternion q) => new SN.Quaternion(q.x, q.y, q.z, q.w);
    }

    /// <summary>
    /// The one partner character ("Nilo"): the owner's own model (Art/Characters/Tripo) - a small figure in a yellow
    /// bonnet and romper with nothing on the face but two eyes - skinned to the shared Skeleton, with its own texture
    /// under flat two-tone shading and an ink outline.
    /// It is a PREFAB (Assets/LivingPartner/Prefabs/Partner.prefab): bones, skinned body, eyes and contact shadow are real
    /// objects in the hierarchy and every reference below is serialized. The Quest scene and the Windows scene use the
    /// same prefab, so both show exactly the same model, proportions and rig. Per-instance values (clip plane, status
    /// light, light direction) go through MaterialPropertyBlocks: the material assets are never modified at runtime.
    /// </summary>
    public sealed class PartnerRig : MonoBehaviour
    {
        [SerializeField] float _height = 0.10f;
        [SerializeField] Transform _visualRoot;
        [SerializeField] Transform[] _bones;
        [SerializeField] SkinnedMeshRenderer _body;
        [SerializeField] MeshRenderer[] _face;
        [SerializeField] Transform _eyeL, _eyeR;
        [SerializeField] Vector3 _eyeBaseScale, _eyeLBase, _eyeRBase;
        [SerializeField] FxQuad _shadow;
        [Tooltip("Microphone recording and the partner's voice (on the head, with its AudioSource).")]
        [SerializeField] VoiceIo _voice;
        [Tooltip("The flat face that closes the body where the screen cuts it (a quad laid on the display plane).")]
        [SerializeField] MeshRenderer _cutFace;
        [Tooltip("The looks this partner can take (Art/Characters). All share this rig.")]
        [SerializeField] PartnerDesign[] _designs;
        [Tooltip("Index into Designs of the look shown now.")]
        [SerializeField] int _design;

        public Transform[] Bones => _bones;
        public PartnerDesign[] Designs => _designs;
        public string DesignId => _designs != null && _design >= 0 && _design < _designs.Length ? _designs[_design].id : "";

        /// <summary>Shows the design with this id (no-op when it is already shown or unknown).</summary>
        public void SetDesign(string id)
        {
            if (string.IsNullOrEmpty(id) || id == DesignId) return;
            for (int i = 0; i < _designs.Length; i++) if (_designs[i].id == id) { ApplyDesign(i); return; }
        }

        /// <summary>Swaps the body mesh and the eye placement. The bones, materials and everything else stay as they are.</summary>
        public void ApplyDesign(int index)
        {
            if (_designs == null || index < 0 || index >= _designs.Length) return;
            var d = _designs[index];
            _design = index;
            _body.sharedMesh = d.bodyMesh;
            _eyeLBase = d.eyeL; _eyeRBase = d.eyeR; _eyeBaseScale = d.eyeScale;
            _eyeL.localPosition = _eyeLBase; _eyeR.localPosition = _eyeRBase;
            _eyeL.localScale = _eyeBaseScale; _eyeR.localScale = _eyeBaseScale;
        }
        public float Height => _height;

        MaterialPropertyBlock _bodyBlock, _faceBlock;
        Color _glow = new Color(0.3f, 0.9f, 1f); float _glowAmount;
        bool _clipped, _drawCutFace;
        public MeshRenderer CutFace => _cutFace;
        public VoiceIo Voice => _voice;
        Quaternion[] _restRot;
        static readonly int ClipPlaneId = Shader.PropertyToID("_ClipPlane"), ClipSignId = Shader.PropertyToID("_ClipSign"),
            LightDirId = Shader.PropertyToID("_LightDirWS"), GlowId = Shader.PropertyToID("_GlowColor"), GlowAmountId = Shader.PropertyToID("_GlowAmount"),
            CapId = Shader.PropertyToID("_CapColor"), OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        static readonly Color ShadowColor = new Color(0.10f, 0.08f, 0.25f, 0.30f);

        public static readonly Color InkColor = new Color(0.20f, 0.13f, 0.16f);      // outline: the dark plum of the model's eyes
        public static readonly Color ShadeTint = new Color(0.84f, 0.68f, 0.66f);     // the darker of the two tones, as a multiplier
        public static readonly Color CapColor = new Color(1.00f, 0.86f, 0.60f);      // the cut face while passing through the screen: warm cream
        public const float OutlineWidthPerMetre = 0.0085f;      // skinned vertices reach the shader at world scale

        void Awake()
        {
            // On the headset the cut always faces the screen, away from the viewer, and whether the eye buffers have the
            // stencil bits the cut face needs has not been checked on a device: there the inside of the body is only tinted.
            _drawCutFace = Application.platform != RuntimePlatform.Android;
            _cutFace.enabled = false;
            _restRot = new Quaternion[Skeleton.N];
            for (int i = 0; i < Skeleton.N; i++) { var r = Skeleton.RestEuler[i]; _restRot[i] = Quaternion.Euler(r.X, r.Y, r.Z); }
            _bodyBlock = new MaterialPropertyBlock(); _faceBlock = new MaterialPropertyBlock();
            _bodyBlock.SetFloat(OutlineWidthId, OutlineWidthPerMetre * _height);
            _bodyBlock.SetVector(LightDirId, new Vector3(-0.35f, 0.8f, -0.5f).normalized);
            _bodyBlock.SetColor(GlowId, _glow.linear); _bodyBlock.SetFloat(GlowAmountId, 0f);
            _bodyBlock.SetColor(CapId, CapColor); _faceBlock.SetColor(CapId, CapColor);
            _bodyBlock.SetFloat(ClipSignId, 0f); _faceBlock.SetFloat(ClipSignId, 0f);
            Flush();
        }

        void Flush()
        {
            _body.SetPropertyBlock(_bodyBlock);
            for (int i = 0; i < _face.Length; i++) _face[i].SetPropertyBlock(_faceBlock);
        }

        /// <summary>Applies a shared pose. The same call with the same pose gives the same body on both platforms.</summary>
        public void Apply(Pose pose)
        {
            for (int i = 0; i < Skeleton.N; i++)
            {
                var e = pose.Euler[i];
                _bones[i].localRotation = _restRot[i] * Quaternion.Euler(e.X, e.Y, e.Z);
            }
            _bones[(int)Bone.Hips].localPosition = Conv.U(Skeleton.RestLocal[(int)Bone.Hips]) + Conv.U(pose.HipsOffset);
            float sq = pose.Squash;
            _bones[(int)Bone.Root].localScale = new Vector3(1f + 0.12f * sq, 1f - 0.16f * sq, 1f + 0.12f * sq);
            // expression lives in the eyes only: blink / widen, a happy squint when the pose asks for a "smile", and gaze
            float open = Mathf.Clamp(1f - pose.Blink, 0.10f, 1.5f);
            float widen = 1f + 0.10f * Mathf.Clamp01(pose.Mouth);
            var es = new Vector3(_eyeBaseScale.x * widen, _eyeBaseScale.y * open, _eyeBaseScale.z);
            var look = new Vector3(pose.EyeX * 0.010f, pose.EyeY * 0.008f, 0);
            _eyeL.localScale = es; _eyeR.localScale = es;
            _eyeL.localPosition = _eyeLBase + look; _eyeR.localPosition = _eyeRBase + look;
        }

        public void SetVisible(bool v)
        {
            if (_body.enabled != v) _body.enabled = v;
            for (int i = 0; i < _face.Length; i++) if (_face[i].enabled != v) _face[i].enabled = v;
            if (_shadow.enabled != v) _shadow.enabled = v;
            _cutFace.enabled = v && _clipped && _drawCutFace;
        }
        public bool Visible => _body.enabled;

        /// <summary>Clip to one side of a world-space plane. sign 0 disables clipping; +1 keeps the side the normal points to.</summary>
        public void SetClip(Vector3 planePoint, Vector3 planeNormal, float sign)
        {
            var v = new Vector4(planeNormal.x, planeNormal.y, planeNormal.z, -Vector3.Dot(planeNormal, planePoint));
            _bodyBlock.SetVector(ClipPlaneId, v); _bodyBlock.SetFloat(ClipSignId, sign);
            _faceBlock.SetVector(ClipPlaneId, v); _faceBlock.SetFloat(ClipSignId, sign);
            Flush();
            _shadow.SetClip(v, sign);
            _clipped = sign != 0;
            _cutFace.enabled = _clipped && _drawCutFace && _body.enabled;
            if (_clipped) PlaceCutFace(_cutFace.transform, ChestWorld, planePoint, planeNormal, _height);
        }

        /// <summary>Lays the cut-face quad on the plane, centred under the body and large enough for any pose.</summary>
        public static void PlaceCutFace(Transform quad, Vector3 bodyPoint, Vector3 planePoint, Vector3 planeNormal, float height)
        {
            Vector3 onPlane = bodyPoint - planeNormal * Vector3.Dot(bodyPoint - planePoint, planeNormal);
            quad.SetPositionAndRotation(onPlane, Quaternion.LookRotation(planeNormal));
            quad.localScale = new Vector3(height * 3f, height * 3f, 1f);
        }
        public void ClearClip()
        {
            _bodyBlock.SetFloat(ClipSignId, 0); _faceBlock.SetFloat(ClipSignId, 0);
            Flush();
            _shadow.SetClip(Vector4.zero, 0);
            _clipped = false; _cutFace.enabled = false;
        }

        public void SetLightDir(Vector3 toLightWorld) { _bodyBlock.SetVector(LightDirId, toLightWorld.normalized); _body.SetPropertyBlock(_bodyBlock); }
        /// <summary>
        /// The status light: the clasp at the neck takes this colour while the partner is working (amount 1) and is
        /// its own honey colour otherwise (amount 0).
        /// </summary>
        public void SetGlow(Color c, float amount)
        {
            if (c == _glow && amount == _glowAmount) return;
            _glow = c; _glowAmount = amount;
            _bodyBlock.SetColor(GlowId, c.linear); _bodyBlock.SetFloat(GlowAmountId, amount); _body.SetPropertyBlock(_bodyBlock);
        }
        public Color Glow => _glow;

        /// <summary>Places the contact shadow on a ground plane below the body (world space). Hidden when too high above it.</summary>
        public void SetShadow(bool show, Vector3 groundPoint, Vector3 groundNormal, float heightAbove)
        {
            if (!show || !Visible) { if (_shadow.enabled) _shadow.enabled = false; return; }
            _shadow.enabled = true;
            var t = _shadow.transform;
            Vector3 fwd = Vector3.Cross(groundNormal, transform.right);
            t.SetPositionAndRotation(groundPoint + groundNormal * 0.0008f, Quaternion.LookRotation(-groundNormal, fwd.sqrMagnitude > 1e-4f ? fwd : Vector3.forward));
            float k = Mathf.Clamp01(1f - heightAbove / (_height * 2.5f));
            t.localScale = Vector3.one * _height * Mathf.Lerp(0.3f, 0.62f, k);
            _shadow.SetColor(new Color(ShadowColor.r, ShadowColor.g, ShadowColor.b, 0.32f * k));
        }

        /// <summary>The right hand: its middle, and the way it points (from the wrist to the fingertips). For something drawn in that hand.</summary>
        public Vector3 HandRWorld => _bones[(int)Bone.ArmLR].TransformPoint(new Vector3(0, -Skeleton.ArmLower * 0.6f, 0));
        public Vector3 HandRDir => _bones[(int)Bone.ArmLR].TransformDirection(Vector3.down);
        public Vector3 HeadWorld => _bones[(int)Bone.Head].TransformPoint(new Vector3(0, Skeleton.HeadCenterUp, 0));
        public Vector3 HeadTopWorld => _bones[(int)Bone.Head].TransformPoint(new Vector3(0, Skeleton.HeadCenterUp + Skeleton.HeadRadius + 0.03f, 0));
        public Vector3 ChestWorld => _bones[(int)Bone.Chest].position;

        // the head is far wider than its bones: its six outermost points (the bonnet), for the bounds below
        static readonly Vector3[] HeadExtents =
        {
            new Vector3(-Skeleton.HeadHalfWidth, Skeleton.HeadCenterUp, 0), new Vector3(Skeleton.HeadHalfWidth, Skeleton.HeadCenterUp, 0),
            new Vector3(0, Skeleton.HeadCenterUp, -Skeleton.HeadRadius), new Vector3(0, Skeleton.HeadCenterUp, Skeleton.HeadRadius),
            new Vector3(0, 0, 0), new Vector3(0, Skeleton.HeadCenterUp + Skeleton.HeadRadius, 0),
        };

        /// <summary>Bounds of only the bones on the kept side of a plane (the part this renderer actually shows). any = false when none.</summary>
        public Bounds BoneBoundsOnSide(Vector3 planePoint, Vector3 planeNormal, float margin, out bool any)
        {
            bool found = false; var b = new Bounds();
            void Add(Vector3 p) { if (Vector3.Dot(p - planePoint, planeNormal) < -margin) return; if (!found) { b = new Bounds(p, Vector3.zero); found = true; } else b.Encapsulate(p); }
            for (int i = 0; i < _bones.Length; i++) Add(_bones[i].position);
            Add(HeadTopWorld); Add(HeadWorld);
            var head = _bones[(int)Bone.Head];
            for (int i = 0; i < HeadExtents.Length; i++) Add(head.TransformPoint(HeadExtents[i]));
            if (found) b.Expand(_height * 0.42f);
            any = found;
            return b;
        }

        /// <summary>World-space bounds of the body from the bone positions (cheap; used to size the PC overlay window).</summary>
        public Bounds BoneBounds()
        {
            var b = new Bounds(_bones[(int)Bone.Hips].position, Vector3.zero);
            for (int i = 0; i < _bones.Length; i++) b.Encapsulate(_bones[i].position);
            b.Encapsulate(HeadTopWorld);
            var head = _bones[(int)Bone.Head];
            for (int i = 0; i < HeadExtents.Length; i++) b.Encapsulate(head.TransformPoint(HeadExtents[i]));
            b.Expand(_height * 0.42f);
            return b;
        }
    }
}
