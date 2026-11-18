using LivingPartner.Character;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// What a glass game sends out of the screen at the user, as REAL BALLS in the room (MrCore/GlassOutBalls, authored
    /// by LPSceneBuilder.AuthorU1Ball - Editor/LPSceneU1Ball.cs; flown by MrGlassOut): for each of the 8 things that can
    /// be in the air a toon sphere (Resources/Shaders/LPOutBall.shader: a toy football, a ball with the game's picture
    /// on it, or a plain ball with a band), a soft blob shadow on the desk under it, four soft puffs behind it (its
    /// trail - seen from any side, also when it comes straight at the eye) and a ring that bursts where it was blocked
    /// or where it got through.
    /// </summary>
    public sealed class GlassOutBallView : MonoBehaviour
    {
        public const int Count = 8, Puffs = 4;
        /// <summary>The looks of the shader.</summary>
        public const int LookFootball = 0, LookPicture = 1, LookBand = 2;

        [Tooltip("The balls (8): sphere meshes of diameter 1 - the scale is the diameter in metres.")]
        public MeshRenderer[] balls;
        [Tooltip("The blob shadow of each ball, lying on the desk.")]
        public FxQuad[] shadows;
        [Tooltip("The trail: four soft puffs behind each ball (8 x 4, ball by ball).")]
        public FxQuad[] puffs;
        [Tooltip("The ring that bursts where a ball was blocked by a palm / where it got through (one per ball).")]
        public FxQuad[] bursts;

        static readonly int MainTexId = Shader.PropertyToID("_MainTex"), ColorId = Shader.PropertyToID("_Color"), PatchId = Shader.PropertyToID("_PatchColor"),
                            LookId = Shader.PropertyToID("_Look"), FlashId = Shader.PropertyToID("_Flash"), LightId = Shader.PropertyToID("_LightDirWS");
        MaterialPropertyBlock _mpb;
        readonly Color[] _color = new Color[Count], _patch = new Color[Count];
        readonly Texture[] _tex = new Texture[Count];
        readonly int[] _look = new int[Count];
        readonly float[] _flash = new float[Count];
        Vector4 _light = new Vector4(-0.35f, 0.8f, -0.5f, 0f);

        /// <summary>The light all balls are shaded by: the direction TO it, in the room.</summary>
        public void SetLight(Vector3 toLight) { _light = toLight.normalized; }

        /// <summary>What ball i looks like from now on (set when it is launched).</summary>
        public void SetLook(int i, int look, Color color, Color patch, Texture picture)
        {
            _look[i] = look; _color[i] = color; _patch[i] = patch; _tex[i] = picture; _flash[i] = 0f;
            Apply(i);
        }

        /// <summary>0..1: the ball flashes towards white (the moment it is blocked).</summary>
        public void SetFlash(int i, float flash)
        {
            flash = Mathf.Clamp01(flash);
            if (Mathf.Abs(_flash[i] - flash) < 0.004f) return;
            _flash[i] = flash; Apply(i);
        }

        void Apply(int i)
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _mpb.Clear();
            _mpb.SetTexture(MainTexId, _tex[i] != null ? _tex[i] : Texture2D.whiteTexture);
            _mpb.SetColor(ColorId, _color[i]); _mpb.SetColor(PatchId, _patch[i]);
            _mpb.SetFloat(LookId, _look[i]); _mpb.SetFloat(FlashId, _flash[i]); _mpb.SetVector(LightId, _light);
            balls[i].SetPropertyBlock(_mpb);
        }
    }
}
