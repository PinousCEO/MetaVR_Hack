using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// THE HEARD STRIP: whenever the partner has heard the user say something, a small strip of paper under its
    /// speech bubble shows the words for four seconds - greyed, with "say 밤비야 first", when it took them as not
    /// meant for it. So the user sees at once whether they were understood.
    /// (There was a Help page in the menu as well: "No Help window" - the user, on the headset, 2026-11-17. It is gone.)
    /// </summary>
    public sealed partial class MrApp
    {
        [Header("What it heard (MrCore/Captions)")]
        [SerializeField] FxQuad _heardBg;
        [SerializeField] StoryLabel _heard;

        string _dbgHeard = "", _dbgHeardNote = ""; float _dbgHeardAt = -100f;      // a line made up from the PC (lpctl debug heard): captures
        bool _heardInit, _heardNot; double _heardSeenAt; float _heardUntil = -1f, _heardPop = 1f, _captionLift;

        /// <summary>What the partner last heard the user say, what it made of it ("not for me": its name was not said), and when.</summary>
        void HeardNow(out string text, out string note, out double at)
        {
            if (_dbgHeardAt > -50f) { text = _dbgHeard; note = _dbgHeardNote; at = _dbgHeardAt; return; }
            text = _ch.heard ?? ""; note = _ch.heardNote ?? ""; at = _ch.heardAt;
        }

        /// <summary>s cut to n characters, with dots the font it will be drawn in has.</summary>
        static string Cut(string s, int n)
        {
            s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length <= n ? s : s.Substring(0, n - 1).TrimEnd() + (StoryGlyphs.CanDraw(s) ? "…" : "...");
        }

        /// <summary>Where the partner's speech bubble starts from: just above its head (above the light, while it is making something), or above the screen it is in.</summary>
        Vector3 CaptionBase()
        {
            if (_rig.Visible) return _rig.HeadTopWorld + Vector3.up * (0.026f + LightClear);
            var mv = Mon(_snap?.pc?.monitorId) ?? FirstMon();
            return mv != null ? mv.Origin + mv.Y * (mv.frame.heightM * 0.5f + 0.018f) - mv.Z * 0.02f : Input.HeadPos + Input.HeadRot * new Vector3(0, _calibrating ? -0.225f : -0.135f, 0.5f);
        }

        /// <summary>The heard strip: shown for four seconds whenever the partner has heard something new. The speech bubble moves up to make room for it.</summary>
        void UpdateHeard(float dt)
        {
            HeardNow(out string text, out string note, out double at);
            if (!_heardInit) { _heardInit = true; _heardSeenAt = at; }      // (what was heard before this app started is not news)
            if (at != _heardSeenAt)
            {
                _heardSeenAt = at;
                if (text.Length > 0)
                {
                    _heardNot = note == "not for me";
                    _heard.text = "“" + Cut(text, 38) + "”" + (_heardNot ? "\nsay “Bambi” first" : "");
                    _heard.color = _heardNot ? new Color(0.52f, 0.48f, 0.45f) : Ui.Pal.TextDim;
                    _heardUntil = Time.time + 4f; _heardPop = 0f;
                    Debug.Log($"[MR] heard: '{Cut(text, 60)}'{(_heardNot ? " (not taken as meant for the partner)" : "")}");
                }
            }
            bool on = Time.time < _heardUntil && _heard.text.Length > 0;
            float h = on ? _heard.Height + 0.012f : 0f;
            // (the bubble also stands clear of the light that gathers over the partner's head while it makes something: MrCraft)
            _captionLift = Mathf.MoveTowards(_captionLift, (on ? h + 0.012f : 0f) + (_rig.Visible ? LightClear : 0f), dt * 0.25f);
            if (!on)
            {
                if (_heard.text.Length > 0) _heard.text = "";
                if (_heardBg.enabled) _heardBg.enabled = false;
                return;
            }
            _heardPop = Mathf.Min(1f, _heardPop + dt / 0.28f);
            float fade = Mathf.Clamp01((_heardUntil - Time.time) / 0.4f);
            Vector3 at3 = CaptionBase() + Vector3.up * (h * 0.5f);
            Place(_heard, at3, null);
            _heard.transform.localScale = Vector3.one;
            Color c = _heard.color; c.a = fade; _heard.color = c;
            _heardBg.enabled = true;
            _heardBg.transform.SetPositionAndRotation(_heard.transform.position + _heard.transform.forward * 0.002f, _heard.transform.rotation);
            float k = Mathf.Max(0.001f, Ui.Pop(_heardPop));      // (the paper pops up; the words are there at once)
            Ui.SizePanel(_heardBg, (_heard.Width + 0.030f) * k, h * (0.6f + 0.4f * k), Mathf.Min(h * 0.5f, 0.011f), 0.0012f);
            Color top = _heardNot ? new Color(0.90f, 0.88f, 0.84f, 0.95f * fade) : new Color(0.99f, 0.97f, 0.92f, 0.96f * fade);
            Color bottom = _heardNot ? new Color(0.86f, 0.84f, 0.80f, 0.95f * fade) : new Color(0.96f, 0.93f, 0.86f, 0.96f * fade);
            _heardBg.SetColors(top, bottom);
        }
    }
}
