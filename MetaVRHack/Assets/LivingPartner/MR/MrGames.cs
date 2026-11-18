using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// Small things round the menu and the games: the shooting star a press sends to the partner (and the smaller one
    /// that carries a new thing into the bag), a little show from the partner, breaking a text into lines.
    /// (The menu itself is MrMenuBoard; the games are MrPlay, MrTennis and MrMarket.)
    /// </summary>
    public sealed partial class MrApp
    {
        [Tooltip("The shooting star a pressed tile sends to the partner.")]
        [SerializeField] UiSprite _comet;
        float _cometT = -1f; Vector3 _cometFrom, _cometTo; int _cometFrame;
        [Tooltip("The small shooting star that carries something the partner made from its card into the bag.")]
        [SerializeField] UiSprite _bagComet;
        float _bagCometT = -1f; Vector3 _bagCometFrom, _bagCometTo;
        /// <summary>A badge was pressed: a shooting star carries it to the partner - across the room, or to the screen it is in.</summary>
        void MenuComet(Vector3 from)
        {
            Vector3 to;
            if (_ch.owner == Env.Mr && _rig.Visible) to = _rig.HeadTopWorld;
            else { var mv = Mon(_snap?.pc?.monitorId) ?? FirstMon(); if (mv == null) return; to = mv.Origin; }
            _cometFrom = from; _cometTo = to; _cometT = 0f;
            Sfx("whoosh", from, 0.25f, 1.2f);
        }

        /// <summary>A thing goes into the bag: a smaller shooting star from where it was shown to the left palm (when that hand is seen).</summary>
        void BagComet(Vector3 from)
        {
            if (!Input.Left.tracked) return;
            _bagCometFrom = from; _bagCometTo = Input.Left.palmPos; _bagCometT = 0f;
        }

        void UpdateBagComet(float dt)
        {
            if (_bagCometT < 0f) { if (_bagComet.enabled) _bagComet.enabled = false; return; }
            _bagCometT += dt / 0.5f;
            if (Input.Left.tracked) _bagCometTo = Input.Left.palmPos;      // the palm may move while it is on its way
            float u = Mathf.Clamp01(_bagCometT), e = u * u * (3f - 2f * u);
            Vector3 mid = (_bagCometFrom + _bagCometTo) * 0.5f + Vector3.up * (0.05f + 0.12f * Vector3.Distance(_bagCometFrom, _bagCometTo));
            Vector3 pos = Vector3.Lerp(Vector3.Lerp(_bagCometFrom, mid, e), Vector3.Lerp(mid, _bagCometTo, e), e);
            Vector3 right = Input.HeadRot * Vector3.right;
            if (!_bagComet.enabled) _bagComet.enabled = true;
            _bagComet.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - Input.HeadPos, Vector3.up));
            _bagComet.transform.localScale = Vector3.one * (0.036f - 0.014f * u);
            _bagComet.SetColor(new Color(1f, 0.86f, 0.45f, 1f));
            if ((Time.frameCount & 3) == 0) _sparks.Burst(pos, right, Vector3.up, 0.010f, 1);       // the tail
            if (_bagCometT >= 1f) { _bagCometT = -1f; _sparks.Burst(_bagCometTo, right, Vector3.up, 0.04f, 8); }
        }

        void UpdateComet(float dt)
        {
            UpdateBagComet(dt);
            if (_cometT < 0f) { if (_comet.enabled) _comet.enabled = false; return; }
            _cometT += dt / 0.55f;
            float u = Mathf.Clamp01(_cometT), e = u * u * (3f - 2f * u);
            Vector3 mid = (_cometFrom + _cometTo) * 0.5f + Vector3.up * (0.08f + 0.18f * Vector3.Distance(_cometFrom, _cometTo));
            Vector3 pos = Vector3.Lerp(Vector3.Lerp(_cometFrom, mid, e), Vector3.Lerp(mid, _cometTo, e), e);
            Vector3 right = Input.HeadRot * Vector3.right;
            if (!_comet.enabled) _comet.enabled = true;
            _comet.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - Input.HeadPos, Vector3.up));
            _comet.transform.localScale = Vector3.one * (0.060f - 0.025f * u);
            _comet.SetColor(new Color(1f, 0.86f, 0.45f, 1f));
            if ((_cometFrame++ & 1) == 0) _sparks.Burst(pos, right, Vector3.up, 0.016f, 1);       // the tail
            if (_cometT >= 1f)
            {
                _cometT = -1f;
                _sparks.Burst(_cometTo, right, Vector3.up, 0.06f, 14);
                if (_ch.owner == Env.Mr) _petT = Mathf.Max(_petT, 0.9f);       // it felt that
            }
        }

        static string Shorten(string s, int n) => string.IsNullOrEmpty(s) ? "" : s.Length <= n ? s : s.Substring(0, n - 1) + "…";

        /// <summary>A little show from the partner (it has to be out here to be seen doing it).</summary>
        void PlayEmote(Emote e, float seconds)
        {
            if (_ch.owner != Env.Mr) { Caption("I'm inside the screen - call me out first.", 3f); Sfx2D("error", 0.5f); return; }
            _emoteForced = e; _emoteT = seconds;
            if (_rig.Visible) _sparks.Burst(_rig.HeadTopWorld + Vector3.up * 0.02f, Input.HeadRot * Vector3.right, Vector3.up, 0.035f, 7);
        }
        Emote _emoteForced; float _emoteT;

        /// <summary>Breaks a text into lines no wider than maxWidth (at most maxLines, the last one ending in an ellipsis), each starting with indent.</summary>
        static void WrapInto(StoryLabel l, System.Text.StringBuilder sb, string s, float maxWidth, int maxLines, string indent)
        {
            s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            if (s.Length == 0) return;
            string[] words = s.Split(' ');
            string line = indent; int lines = 0; bool any = false;
            foreach (string w in words)
            {
                if (w.Length == 0) continue;
                string trial = any ? line + " " + w : line + w;
                if (any && l.Measure(trial) > maxWidth)
                {
                    if (++lines >= maxLines) { sb.Append(line).Append(" \u2026\n"); return; }
                    sb.Append(line).Append('\n');
                    line = indent + w;
                }
                else line = trial;
                any = true;
            }
            if (any) sb.Append(line).Append('\n');
        }
    }
}
