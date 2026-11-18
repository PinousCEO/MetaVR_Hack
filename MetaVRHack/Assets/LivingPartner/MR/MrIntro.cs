using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// The first seconds, made into a little show round the word mark (MrWorld.UpdateIntro places the mark, its ring and
    /// the line under it; this is everything that moves round them):
    ///  - a warm glow blooms behind the mark and a ring of light spreads from it now and then;
    ///  - stars burst out of the middle and keep drifting round it, twinkling;
    ///  - sticker badges - what the partner can do - fly out one after another and circle the mark on a tilted path,
    ///    the near ones larger;
    ///  - when everything is ready it all falls back into the middle and ONE shooting star leaves for the partner.
    /// All of it is authored under MrCore/Intro; nothing is created here.
    /// </summary>
    public sealed partial class MrApp
    {
        [Header("Intro show")]
        [SerializeField] FxQuad _introGlow;
        [SerializeField] FxQuad _introWave;
        [Tooltip("Sticker badges that circle the word mark.")]
        [SerializeField] UiSprite[] _introOrbit;
        [Tooltip("Stars and sparks that burst out and drift round it.")]
        [SerializeField] UiSprite[] _introStars;
        bool _introLeft; int _introSound;

        void IntroFx(Vector3 at, Quaternion face, float show)
        {
            float t = _introT, gone = Mathf.Clamp01(_introGone / 0.7f), stay = 1f - gone * gone;
            if (t < 0.1f) { _introLeft = false; _introSound = 0; }
            // what is heard of it: a chime as the word mark pops up, bells as the badges fly out (and the rush of the shooting star at the end)
            if (_introSound == 0 && t >= 0.25f) { _introSound = 1; Sfx("open", at, 0.6f); }
            if (_introSound == 1 && t >= 0.75f) { _introSound = 2; Sfx("sparkle", at, 0.6f); }
            Vector3 right = face * Vector3.right, up = face * Vector3.up, fwd = face * Vector3.forward;

            // the glow behind everything
            if (!_introGlow.enabled) _introGlow.enabled = true;
            float bloom = Ui.Pop(Mathf.Clamp01(t / 0.55f)) * stay;
            _introGlow.transform.SetPositionAndRotation(at + fwd * 0.035f, face);
            _introGlow.transform.localScale = new Vector3(0.64f, 0.40f, 1f) * Mathf.Max(0.001f, bloom * (1f + 0.045f * Mathf.Sin(t * 2.1f)));
            _introGlow.SetColors(new Color(1f, 0.90f, 0.55f, 0.50f * show), new Color(1f, 0.98f, 0.88f, 0.50f * show));

            // a ring of light that spreads from the middle and fades, again and again
            float w = Mathf.Repeat(t, 2.2f) / 1.0f;
            bool wave = w < 1f && gone <= 0f;
            if (_introWave.enabled != wave) _introWave.enabled = wave;
            if (wave)
            {
                float e = 1f - (1f - w) * (1f - w);
                _introWave.transform.SetPositionAndRotation(at + fwd * 0.025f, face);
                _introWave.transform.localScale = Vector3.one * Mathf.Lerp(0.08f, 1.05f, e);
                _introWave.SetColors(new Color(1f, 0.84f, 0.42f, (1f - w) * 0.85f * show), new Color(1f, 0.97f, 0.86f, (1f - w) * 0.85f * show));
            }

            // stars: out of the middle along the golden angle, then a slow drift each its own way
            for (int i = 0; i < _introStars.Length; i++)
            {
                var s = _introStars[i];
                float u = Mathf.Clamp01((t - 0.05f - i * 0.022f) / 0.75f), e = 1f - (1f - u) * (1f - u) * (1f - u);
                if (s.enabled != u > 0f) s.enabled = u > 0f;
                if (u <= 0f) continue;
                float reach = 0.16f + 0.23f * Mathf.Repeat(i * 0.618f, 1f);
                float ang = i * 2.399f + t * 0.22f * (i % 2 == 0 ? 1f : -1f);
                float r = reach * e * stay;
                Vector3 pos = at + right * (Mathf.Cos(ang) * r * 1.28f) + up * (Mathf.Sin(ang) * r * 0.64f) + fwd * (0.012f + 0.03f * Mathf.Sin(ang * 1.7f + i));
                float twinkle = 0.55f + 0.45f * Mathf.Sin(t * 3.1f + i * 1.7f);
                s.transform.SetPositionAndRotation(pos, face * Quaternion.Euler(0, 0, t * 38f + i * 31f));
                s.transform.localScale = Vector3.one * ((0.018f + 0.016f * Mathf.Repeat(i * 0.37f, 1f)) * twinkle * Mathf.Min(1f, e * 3f) * Mathf.Max(0.05f, stay));
                s.SetColor(new Color(1f, 1f, 1f, show));
            }

            // the badges: one after another out of the middle, then round and round on a tilted path
            int n = _introOrbit.Length;
            for (int k = 0; k < n; k++)
            {
                var b = _introOrbit[k];
                float u = Mathf.Clamp01((t - 0.75f - k * 0.10f) / 0.65f);
                if (b.enabled != u > 0f) b.enabled = u > 0f;
                if (u <= 0f) continue;
                float e = Ui.Pop(u) * stay;
                float ang = k * (Mathf.PI * 2f / n) + t * 0.42f;
                float near = -Mathf.Sin(ang);                                   // +1: the lowest point of the path, nearest to the user
                Vector3 pos = at + right * (Mathf.Cos(ang) * 0.41f * e) + up * (Mathf.Sin(ang) * 0.19f * e + 0.007f * Mathf.Sin(t * 2.1f + k * 1.3f)) - fwd * (near * 0.075f * e);
                b.transform.SetPositionAndRotation(pos, face * Quaternion.Euler(0, 0, Mathf.Sin(t * 1.5f + k) * 8f));
                b.transform.localScale = Vector3.one * Mathf.Max(0.001f, 0.062f * e * (0.82f + 0.18f * near));
                b.SetColor(new Color(1f, 1f, 1f, show));
                float cycle = Mathf.Repeat(t * 0.5f - k * 0.31f, 2.4f);
                b.SetLook(0f, cycle < 1f ? cycle : -1f);
            }

            // ready: one burst, and a shooting star carries it all to the partner
            if (_introGone > 0f && !_introLeft)
            {
                _introLeft = true;
                _sparks.Burst(at, right, up, 0.18f, 26);
                Sfx("whoosh", at, 0.7f);
                MenuComet(at);
            }
        }

        void IntroFxHide()
        {
            if (_introGlow.enabled) _introGlow.enabled = false;
            if (_introWave.enabled) _introWave.enabled = false;
            foreach (var s in _introStars) if (s.enabled) s.enabled = false;
            foreach (var b in _introOrbit) if (b.enabled) b.enabled = false;
        }
    }
}
