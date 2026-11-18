using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.RegularExpressions;

namespace LivingPartner.Shared
{
    /// <summary>
    /// A user-created runtime module. It is a stored artefact (id, version, config, state, callable functions),
    /// composed from the allowed runtime kinds below - not arbitrary generated code.
    /// </summary>
    [Serializable]
    public sealed class ModuleDef
    {
        public string moduleId = "";
        public string kind = "timer";
        public string name = "";
        public int version = 1;
        public long createdUnixMs, updatedUnixMs;
        public string display = "ring";     // ring | digits
        public string alert = "wave";       // wave | chime | silent
        public long durationMs = 25 * 60 * 1000;
        public string status = "idle";      // idle | running | paused | done
        public long endUnixMs;              // absolute UTC end time while running (survives restarts and frame drops)
        public long remainingMs;            // remaining time while idle/paused
        public bool placed;
        public V3 position;                 // MR placement (tracking space)
        public string spaceEpoch = "";
        public List<string> functions = new List<string> { "start", "pause", "resume", "reset", "setDuration", "setDisplay", "setAlert" };
        public string origin = "composed-from-runtime-kind:timer"; // honest provenance: pre-built runtime kind + declarative config
    }

    /// <summary>Timer logic on absolute UTC milliseconds. No per-frame accumulation, so restarts and frame drops cannot skew it.</summary>
    public static class TimerLogic
    {
        public static ModuleDef Create(string id, string name, long durationMs, long now)
        {
            return new ModuleDef { moduleId = id, name = name, durationMs = Math.Max(1000, durationMs), remainingMs = Math.Max(1000, durationMs), status = "idle", createdUnixMs = now, updatedUnixMs = now };
        }

        public static long Remaining(ModuleDef m, long now)
        {
            if (m.status == "running") return Math.Max(0, m.endUnixMs - now);
            if (m.status == "done") return 0;
            return m.remainingMs;
        }

        public static bool Start(ModuleDef m, long now, long durationMs = 0)
        {
            if (durationMs > 0) { m.durationMs = durationMs; }
            m.remainingMs = m.durationMs; m.endUnixMs = now + m.durationMs; m.status = "running"; Touch(m, now); return true;
        }
        public static bool Pause(ModuleDef m, long now)
        {
            if (m.status != "running") return false;
            m.remainingMs = Math.Max(0, m.endUnixMs - now); m.status = "paused"; Touch(m, now); return true;
        }
        public static bool Resume(ModuleDef m, long now)
        {
            if (m.status != "paused") return false;
            m.endUnixMs = now + m.remainingMs; m.status = "running"; Touch(m, now); return true;
        }
        public static bool Reset(ModuleDef m, long now)
        {
            m.status = "idle"; m.remainingMs = m.durationMs; m.endUnixMs = 0; Touch(m, now); return true;
        }
        public static bool SetDuration(ModuleDef m, long durationMs, long now)
        {
            if (durationMs < 1000) return false;
            long elapsed = m.status == "running" ? m.durationMs - Math.Max(0, m.endUnixMs - now) : m.durationMs - m.remainingMs;
            m.durationMs = durationMs;
            long remaining = Math.Max(0, durationMs - Math.Max(0, elapsed));
            if (m.status == "running") m.endUnixMs = now + remaining; else if (m.status != "done") m.remainingMs = m.status == "idle" ? durationMs : remaining;
            Touch(m, now); return true;
        }
        /// <summary>Returns true exactly once, when a running timer reaches zero.</summary>
        public static bool Tick(ModuleDef m, long now)
        {
            if (m.status == "running" && now >= m.endUnixMs) { m.status = "done"; m.remainingMs = 0; Touch(m, now); return true; }
            return false;
        }
        static void Touch(ModuleDef m, long now) { m.updatedUnixMs = now; m.version++; }

        public static string Format(long ms)
        {
            long s = (ms + 999) / 1000;
            return s >= 3600 ? $"{s / 3600}:{(s % 3600) / 60:00}:{s % 60:00}" : $"{s / 60:00}:{s % 60:00}";
        }
    }

    public sealed class Intent
    {
        public string kind = "unknown"; // readFile | playTennis | stopTennis | enterPortal | returnToMr | createTimer | timerStart | timerPause | timerResume | timerReset | timerSetDuration | analyze | quietOn | quietOff | rainOn | rainOff | cancel | stop | openMenu | closeMenu | applyChange | publish | unknown
        public long durationMs;
        public bool alsoStart;
        public bool deictic;            // "여기", "이쪽", "this" - needs the pointing history
        public string text = "";
        // playGame: the words that name the game, when the language model gave any (a pack's title); stop: "game" when the sentence said that it is a game that should stop
        public string name = "";
    }

    /// <summary>Small deterministic Korean/English intent parser for the supported commands. Unknown text stays "unknown".</summary>
    public static class IntentParser
    {
        static readonly Regex Hours = new Regex(@"(\d+(?:\.\d+)?)\s*(시간|hours?|hrs?|h\b)", RegexOptions.IgnoreCase);
        static readonly Regex Minutes = new Regex(@"(\d+(?:\.\d+)?)\s*(분|minutes?|mins?|m\b)", RegexOptions.IgnoreCase);
        static readonly Regex Seconds = new Regex(@"(\d+(?:\.\d+)?)\s*(초|seconds?|secs?|s\b)", RegexOptions.IgnoreCase);

        public static long ParseDurationMs(string text)
        {
            double ms = 0;
            var h = Hours.Match(text); if (h.Success) ms += double.Parse(h.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 3600000.0;
            var m = Minutes.Match(text); if (m.Success) ms += double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 60000.0;
            var s = Seconds.Match(text); if (s.Success) ms += double.Parse(s.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 1000.0;
            return (long)ms;
        }

        static bool Has(string t, params string[] keys) { foreach (var k in keys) if (t.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true; return false; }

        // RAIN is named: "비" as a word of its own ("비 그쳐", "비가 와", "비를 멈춰 줘", "비내려") or "rain". Not the 비 inside
        // another word - above all not the one in the partner's own name: "밤비야, 멈춰" was taken for "stop the rain"
        // until 2026-11-17, and "밤비 내려와" for "let it rain".
        static readonly Regex RainKo = new Regex(@"(?<![가-힣])비(?=$|[\s,.!?~]|가|를|는|도|좀|그쳐|그치|멈춰|멈추|꺼|내려|내리|와|오)|빗방울|빗소리|소나기");
        static readonly Regex RainEn = new Regex(@"\brain(s|y|ing|fall)?\b", RegexOptions.IgnoreCase);      // (not "rainbow")
        /// <summary>Does the sentence name rain?</summary>
        public static bool NamesRain(string text) { string t = text ?? ""; return RainKo.IsMatch(t) || RainEn.IsMatch(t); }

        // "가방 열어 줘", "메뉴 보여 줘", "가방 좀 꺼내 봐", "open the menu" / "가방 닫아 줘", "메뉴 치워", "close the bag". The bag or the
        // menu has to be what is opened: "가방에서 의자 꺼내 줘" takes a chair out, and is none of these.
        static readonly Regex MenuOpen = new Regex(@"(가방|메뉴)\s*(판|창)?\s*(을|를)?\s*(좀\s*|다시\s*|한번\s*)?(열어|열래|열자|보여|켜|띄워|꺼내)|\b(open|show)\s+(me\s+)?(up\s+)?(the\s+|my\s+|your\s+)?(bag|menu)\b|\b(bag|menu)\s+(open|please)\b", RegexOptions.IgnoreCase);
        static readonly Regex MenuClose = new Regex(@"(가방|메뉴)\s*(판|창)?\s*(을|를)?\s*(좀\s*|다시\s*|이제\s*)?(닫아|닫어|닫자|치워|숨겨|넣어|꺼(?!내))|\b(close|hide|put\s+away)\s+(the\s+|my\s+|your\s+)?(bag|menu)\b|\b(bag|menu)\s+(close|closed|away)\b", RegexOptions.IgnoreCase);

        /// <summary>
        /// A plain STOP - "멈춰", "그만", "그만해", "스톱", "stop", with or without "게임" - and nothing else that is asked
        /// for in the same breath ("그만하고 책 읽어" is two wishes: that one is for the language model).
        /// </summary>
        static bool PlainStop(string t)
        {
            if (!Has(t, "멈춰", "멈추", "그만", "스톱", "stop")) return false;
            if (Has(t, "하고 ", "말고", "다음", " and ", " then ", "don't stop", "dont stop", "멈추지", "그만두지")) return false;
            int letters = 0;
            foreach (char ch in t) if (char.IsLetterOrDigit(ch)) letters++;
            return letters <= 22;
        }

        public static Intent Parse(string text)
        {
            var it = new Intent { text = text ?? "" };
            string t = (text ?? "").Trim();
            if (t.Length == 0) return it;
            it.deictic = Has(t, "여기", "이쪽", "이 창", "저기", "이거", "here", "this ", "over there");
            it.durationMs = ParseDurationMs(t);
            bool timer = Has(t, "타이머", "timer");
            if (timer)
            {
                bool create = Has(t, "만들", "생성", "create", "make", "build", "new timer");
                bool start = Has(t, "시작", "start", "run", "켜");
                if (Has(t, "일시정지", "일시 정지", "멈춰", "멈춤", "그만", "pause", "stop")) { it.kind = "timerPause"; return it; }
                if (Has(t, "재개", "다시 시작", "계속", "resume", "continue")) { it.kind = "timerResume"; return it; }
                if (Has(t, "초기화", "리셋", "reset")) { it.kind = "timerReset"; return it; }
                if (create) { it.kind = "createTimer"; it.alsoStart = start; return it; }
                if (start) { it.kind = "timerStart"; return it; }
                if (it.durationMs > 0 && Has(t, "바꿔", "변경", "으로", "change", "set")) { it.kind = "timerSetDuration"; return it; }
                it.kind = "timerStart"; return it;
            }
            if (Has(t, "보정", "캘리브", "calibrat", "화면 찾", "화면 다시 찾", "화면을 찾", "화면을 다시 찾", "모니터 찾", "모니터 다시 찾", "모니터를 찾", "모니터를 다시 찾", "find my screen", "find the screen", "find my monitor")) { it.kind = "calibrate"; return it; }
            // the bag / the menu board of the headset, opened and closed by voice
            if (MenuClose.IsMatch(t)) { it.kind = "closeMenu"; return it; }
            if (MenuOpen.IsMatch(t)) { it.kind = "openMenu"; return it; }
            if (Has(t, "오목", "gomoku") && !Has(t, "만들", "만드", "제작", "make", "create", "build", "설명", "규칙", "뭐야", "what"))
            {
                if (Has(t, "그만", "끝내", "멈춰", "종료", "꺼", "stop", "quit", "end")) { it.kind = "stopTennis"; return it; }
                if (t.Trim(' ', '.', '!', '?') == "오목" || Has(t, "하자", "할래", "시작", "켜", "열어", "두자", "한 판", "한판", "play", "start"))
                { it.kind = "playGame"; it.name = StarterGames.GomokuId; return it; }
            }
            if (SwarmRules.Asked(t) || Has(t, "골키퍼", "승부차기", "goalkeeper", "penalty shoot"))
            {
                if (Has(t, "그만", "끝", "멈춰", "stop", "quit", "end")) it.kind = "stopTennis";
                else { it.kind = "playGame"; it.name = SwarmRules.Asked(t) ? SwarmRules.PackId : StarterGames.KeeperId; }
                return it;
            }
            if (Has(t, "테니스", "tennis")) { it.kind = Has(t, "그만", "끝", "멈춰", "stop", "quit", "end") ? "stopTennis" : "playTennis"; return it; }
            if (Has(t, "조용", "quiet")) { it.kind = Has(t, "해제", "끝", "그만", "꺼", "off") ? "quietOff" : "quietOn"; return it; }
            // the rain stops only when rain is named ("비 그쳐", "비 멈춰", "stop the rain") ...
            bool rain = NamesRain(t);
            if (rain && Has(t, "그쳐", "그치", "멈춰", "멈추", "꺼", "그만", "stop", "off")) { it.kind = "rainOff"; return it; }
            if (Has(t, "취소", "cancel", "abort")) { it.kind = "cancel"; return it; }
            // ... and "멈춰" / "그만" / "stop" by itself, with or without "게임", is the general STOP: whatever is on ends (the host knows what: a game first)
            if (PlainStop(t)) { it.kind = "stop"; it.name = Has(t, "게임", "game", "놀이") ? "game" : ""; return it; }
            if (rain && (RainEn.IsMatch(t) || Has(t, "내", "오게", "와", "켜", "뿌려"))) { it.kind = "rainOn"; return it; }
            // reading a file (a PDF, a picture) is its own thing: the file itself is read, not only what the window shows
            if (Has(t, "읽어", "읽고", "read ") && Has(t, "파일", "pdf", "문서", "file", "document", "png", "jpg", "그림", "사진", "이미지", "picture", "image")) { it.kind = "readFile"; return it; }
            bool analyze = Has(t, "디자인", "봐줘", "봐 줘", "어때", "분석", "확인해", "검토", "피드백", "수정본", "feedback", "review", "analy", "look at", "check");
            bool enter = Has(t, "들어가", "들어와", "go in", "enter", "jump in", "get in");
            if (enter) { it.kind = "enterPortal"; it.alsoStart = analyze; return it; }
            if (Has(t, "이리 와", "이리와", "일로 와", "일로와", "이쪽으로 와", "여기로 와", "come here", "come over")) { it.kind = "comeHere"; return it; }
            if (Has(t, "나와", "돌아와", "나오", "come out", "come back", "return")) { it.kind = "returnToMr"; return it; }
            if (analyze) { it.kind = "analyze"; return it; }
            if (Has(t, "올려", "푸시", "반영", "push", "publish")) { it.kind = "publish"; return it; }
            if (Has(t, "적용", "수정해", "고쳐", "apply", "fix it")) { it.kind = "applyChange"; return it; }
            return it;
        }
    }

    /// <summary>
    /// Geometric rain-shelter test. The hand shelters the head only if it actually blocks the path rain takes to the head:
    /// a hand that is merely near the character does not count.
    /// </summary>
    public static class RainShelter
    {
        /// <param name="rainDir">Unit direction the rain travels (e.g. (0,-1,0) or slanted by wind).</param>
        /// <param name="palmCenter">Centre of the palm.</param>
        /// <param name="palmNormal">Palm normal (either sign).</param>
        public static bool IsSheltered(Vector3 headPos, Vector3 rainDir, Vector3 palmCenter, Vector3 palmNormal, float palmRadius, float maxGap, out float gap, out float lateral)
        {
            gap = 0; lateral = float.MaxValue;
            rainDir = M.SafeNormalize(rainDir, -Vector3.UnitY);
            palmNormal = M.SafeNormalize(palmNormal, Vector3.UnitY);
            Vector3 upwind = -rainDir;
            float facing = Math.Abs(Vector3.Dot(palmNormal, rainDir));
            if (facing < 0.35f) return false;                       // hand is edge-on to the rain: drops slip past
            float denom = Vector3.Dot(upwind, palmNormal);
            if (Math.Abs(denom) < 1e-5f) return false;
            float tHit = Vector3.Dot(palmCenter - headPos, palmNormal) / denom;
            gap = tHit;
            if (tHit < 0.005f || tHit > maxGap) return false;       // hand is below/behind the head or too far to matter
            Vector3 hit = headPos + upwind * tHit;
            lateral = Vector3.Distance(hit, palmCenter);
            return lateral <= palmRadius;
        }
    }
}
