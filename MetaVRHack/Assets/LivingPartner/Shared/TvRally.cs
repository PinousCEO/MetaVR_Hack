using System;

namespace LivingPartner.Shared
{
    public enum TvRallyPhase { Join, Align, Serve, Reach, Volley, Return, Miss, Won }

    public static class TvRallyWords
    {
        static readonly string[] Names = { "tv랠리", "티비랠리", "티브이랠리", "티비렐리", "tv렐리", "tvrally",
            "tv게임", "티비게임", "티브이게임", "가족게임", "familygame" };
        static readonly string[] Partners = { "아내랑", "와이프랑", "아내와", "와이프와", "부부가", "우리둘이", "우리", "둘이서", "같이" };
        static readonly string[] Starts = { "", "하자", "할래", "시작", "시작해", "시작해줘", "켜", "켜줘", "열어줘", "틀어줘", "해줘", "같이하자", "하자고" };
        static readonly string[] Stops = { "꺼", "꺼줘", "닫아", "닫아줘", "그만", "그만해", "그만하자", "멈춰", "멈춰줘", "종료", "종료해", "종료해줘", "끝내", "끝내줘", "끝내자" };
        public static string Command(string addressed)
        {
            if (string.IsNullOrEmpty(addressed) || addressed.Length > 90) return "";
            var clean = new System.Text.StringBuilder(addressed.Length);
            foreach (char c in addressed) if (char.IsLetterOrDigit(c)) clean.Append(char.ToLowerInvariant(c));
            string s = clean.ToString();
            foreach (var p in Partners) if (s.StartsWith(p, StringComparison.Ordinal)) s = s.Substring(p.Length);
            foreach (var name in Names)
            {
                if (s == "start" + name || s == "play" + name || s == "letsplay" + name) return "start";
                if (s == "stop" + name || s == "end" + name || s == "quit" + name) return "stop";
                if (!s.StartsWith(name, StringComparison.Ordinal)) continue;
                string verb = s.Substring(name.Length);
                if (verb.StartsWith("을") || verb.StartsWith("를")) verb = verb.Substring(1);
                if (Array.IndexOf(Starts, verb) >= 0) return "start";
                if (Array.IndexOf(Stops, verb) >= 0) return "stop";
            }
            return "";
        }
    }

    /// <summary>The TV player moves into a ring seen by the camera. The wearer strikes the returning ball with a tracked hand.</summary>
    public sealed class TvRally
    {
        public const int Goal = 6;
        public TvRallyPhase phase { get; private set; }
        public float elapsed { get; private set; }
        public int score { get; private set; }
        public int rally { get; private set; }
        public int side { get; private set; }
        int _serve;

        public void Join() { score = rally = _serve = 0; side = -1; Set(TvRallyPhase.Align); }
        public void Aligned() { if (phase == TvRallyPhase.Align) Serve(); }
        void Set(TvRallyPhase next) { phase = next; elapsed = 0f; }
        void Serve()
        {
            int pattern = _serve++ % 4;
            side = pattern == 0 || pattern == 3 ? -1 : 1;
            Set(TvRallyPhase.Serve);
        }

        public void Step(float dt, int reached, bool hit)
        {
            elapsed += Math.Min(0.15f, Math.Max(0f, dt));
            switch (phase)
            {
                case TvRallyPhase.Serve: if (elapsed >= 1f) Set(TvRallyPhase.Reach); break;
                case TvRallyPhase.Reach:
                    if (reached == side) Set(TvRallyPhase.Volley);
                    else if (elapsed >= 5f) Miss();
                    break;
                case TvRallyPhase.Volley:
                    if (hit && elapsed > 0.25f) { score++; rally++; Set(TvRallyPhase.Return); }
                    else if (elapsed > 2.65f) Miss();
                    break;
                case TvRallyPhase.Return:
                    if (elapsed > 0.85f) { if (score >= Goal) Set(TvRallyPhase.Won); else Serve(); }
                    break;
                case TvRallyPhase.Miss: if (elapsed > 1.35f) Serve(); break;
            }
        }
        void Miss() { rally = 0; Set(TvRallyPhase.Miss); }
    }

    /// <summary>Compares two world-anchored patches with their at-rest appearance. Global exposure shifts are removed using border samples.</summary>
    public sealed class TvMotion
    {
        public const int Grid = 9, Count = Grid * Grid;
        readonly float[] _reference = new float[Count * 2];
        readonly int[] _streak = new int[2];
        public bool calibrated { get; private set; }
        public bool IsNeutral { get; private set; }

        public void Calibrate(byte[] samples)
        {
            for (int i = 0; i < _reference.Length; i++) _reference[i] = samples[i];
            _streak[0] = _streak[1] = 0; calibrated = true;
        }
        public int Read(byte[] samples, int side)
        {
            if (!calibrated || (side != -1 && side != 1)) return 0;
            float exposure = 0f; int border = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                int x = i % Grid, y = i % Count / Grid;
                if (x != 0 && x != Grid - 1 && y != 0 && y != Grid - 1) continue;
                exposure += samples[i] - _reference[i]; border++;
            }
            exposure /= border;
            int index = side == -1 ? 0 : 1, offset = index * Count, changed = 0;
            for (int y = 1; y < Grid - 1; y++)
                for (int x = 1; x < Grid - 1; x++)
                {
                    int i = offset + y * Grid + x;
                    if (Math.Abs(samples[i] - _reference[i] - exposure) >= 19f) changed++;
                }
            IsNeutral = changed < 7;
            _streak[index] = changed >= 10 ? _streak[index] + 1 : 0;
            _streak[1 - index] = 0;
            return _streak[index] >= 2 ? side : 0;
        }
        public void Clear() { _streak[0] = _streak[1] = 0; calibrated = false; }
    }
}
