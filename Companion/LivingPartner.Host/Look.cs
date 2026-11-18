using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using LivingPartner.Shared;

namespace LivingPartner.Host;

/// <summary>
/// "밤비야, 여기 어떤 폴더가 있어?" - the partner looks at what the user points at and answers. Nothing is saved,
/// nothing is drawn into a file, it does not go anywhere.
/// The HOST decides where, for every kind of client (LookRules.Resolve: the pointing hand's dwell round the utterance,
/// else the gaze's, else the newest sample of the last seconds, else the display the partner is on), and what is
/// captured is the whole DISPLAY that was pointed at - never one window: the desktop's icons are no window, and the
/// window the user last worked in may be on another display altogether. The model gets the whole display with a ring
/// on the spot and a close-up round the spot at the display's own resolution (LookRules.Question).
/// While it reads, the snapshot's `show` (kind look) names the display and the close-up's rectangle, so that the PC
/// client frames on the real screen what is being read.
/// Only when the user asks, only that one display, kept in memory for the one request and sent to the OpenAI API with
/// the user's own key (LP_LOOK_KEEP=1 keeps the two pictures of the last look in the captures folder, for checks).
/// </summary>
public sealed partial class TaskManager
{
    bool _looking;
    double _lookAskedAt = -100;     // when the host last started a look for an utterance (a client's own request for the same look is dropped)

    /// <summary>The model that looks: the quick one of the conversation unless LP_OPENAI_LOOK_MODEL names another.</summary>
    string LookModel => Environment.GetEnvironmentVariable("LP_OPENAI_LOOK_MODEL") is { Length: > 0 } m ? m : Ai.ChatModel;

    /// <summary>The display taken when nothing was pointed or looked at: the one the partner is on inside the PC, otherwise the main one.</summary>
    string FallbackMonitor()
    {
        var mons = _core.Desktop.Monitors;
        if (_core.State.character.owner == Env.Pc && _core.Pc != null && ScreenMath.FindMonitor(mons, _core.Pc.monitorId) != null) return _core.Pc.monitorId;
        return (mons.FirstOrDefault(m => m.primary) ?? mons.FirstOrDefault())?.id ?? "";
    }

    /// <summary>Where the user means for something said from t0 to t1 (host time) through client c. Core thread.</summary>
    public LookSpot ResolveLook(Client c, double t0, double t1)
    {
        var spot = LookRules.Resolve(_core.PointerHistoryFor(c), t0, t1, HostClock.Now, FallbackMonitor());
        // a display that is not there (any more): the fallback, said as such
        if (ScreenMath.FindMonitor(_core.Desktop.Monitors, spot.monitorId) == null) spot = new LookSpot { source = LookRules.Fallback, monitorId = FallbackMonitor() };
        return spot;
    }

    /// <summary>
    /// The intent "lookScreen", from any client: the host resolves the spot and looks. Returns the spot (for the
    /// headset, which turns the partner towards it) and "" - or the line that says why it cannot look.
    /// </summary>
    public (LookSpot spot, string refused) LookAsked(Client c, Utterance u)
    {
        _lookAskedAt = HostClock.Now;
        var spot = ResolveLook(c, u.t0, u.t1);
        return (spot, StartLook(spot, u.text ?? "", "asked through " + (c?.Role ?? "nobody")));
    }

    /// <summary>
    /// task.command "look" (a button of the headset's menu, lpctl, an older headset build that still sends it after the
    /// intent's result): the pixel it names is the spot; with none the host resolves it as for an utterance that ends
    /// now. When the host has just started this very look for the utterance, the command is dropped - one look, one answer.
    /// </summary>
    void LookCommand(TaskCommand c)
    {
        double now = HostClock.Now;
        if (now - _lookAskedAt < 6) { Log.Info("look: the client's own request for the look that was just started is dropped (the host looks by itself now)"); return; }
        var mons = _core.Desktop.Monitors;
        LookSpot spot = null;
        float px = c.pxX, py = c.pxY;
        if (px == 0 && py == 0 && c.hwnd != 0 && Win32.FrameBounds(new IntPtr(c.hwnd), out RectI wr)) { px = wr.x + wr.w / 2f; py = wr.y + wr.h / 2f; }
        if ((px != 0 || py != 0) && ScreenMath.FindMonitor(mons, px, py) is { } m)
        {
            ScreenMath.PixelToUV(m, px, py, out float u, out float v);
            spot = new LookSpot { source = LookRules.Command, monitorId = m.id, u = Math.Clamp(u, 0f, 1f), v = Math.Clamp(v, 0f, 1f) };
        }
        spot ??= ResolveLook(null, now - 1.5, now);
        string refused = StartLook(spot, c.prompt ?? "", "task.command");
        if (refused.Length > 0) _core.Say(refused, "worried", 5, "look");
    }

    /// <summary>Captures the display of the spot, asks the model and says the answer. Returns "" when it has started (or one is running), else the line that says why not.</summary>
    string StartLook(LookSpot spot, string words, string how)
    {
        bool ko = ReplyLanguage(words) == "Korean";
        if (!ReviewEnabled) return ko ? "화면을 읽으려면 PC에 화면 인식용 API 키가 필요해." : "Reading the screen needs an AI key on the PC.";
        if (_looking) return ko ? "지금 화면을 읽고 있어. 다 읽으면 말해 줄게." : "I’m still reading the screen.";
        var mons = _core.Desktop.Monitors;
        var m = ScreenMath.FindMonitor(mons, spot.monitorId) ?? mons.FirstOrDefault(x => x.primary) ?? mons.FirstOrDefault();
        if (m == null) return ko ? "화면을 못 찾겠어." : "I can't find a screen to look at.";
        var b = m.bounds;
        var crop = spot.Pointed ? LookRules.Crop(b.w, b.h, spot.u, spot.v) : new RectI(0, 0, b.w, b.h);
        string label = "display " + (m.index + 1) + (m.primary ? " (the main one)" : "");
        // one line that says what was decided: which source, which display, where
        Log.Info($"look: source={spot.source} monitor={m.id} (u, v)=({spot.u:0.000}, {spot.v:0.000}) close-up=[{crop.x},{crop.y} {crop.w}x{crop.h}] of {b.w}x{b.h}, {how}; asked: '{Shorten(words, 120)}'");
        _looking = true;
        string model = LookModel;
        Task.Run(async () =>
        {
            int show = 0;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                byte[] overview, close = null;
                using (var full = LookShots.Grab(b))
                {
                    // the frame on the real screen is drawn only now that the picture is taken: it is not in it
                    _core.Post(() => show = _core.Shows.LookBegin(m.id, crop.x / (float)b.w, crop.y / (float)b.h, crop.w / (float)b.w, crop.h / (float)b.h));
                    overview = LookShots.Overview(full, spot);
                    if (spot.Pointed) close = LookShots.CloseUp(full, spot, crop);
                }
                LookShots.Keep(overview, close);
                var pictures = new List<(string mime, byte[] data, string name)> { ("image/jpeg", overview, "display.jpg") };
                if (close != null) pictures.Add(("image/png", close, "close-up.png"));
                var schema = Obj(("kind", Str), ("at", Str), ("summary", Str), ("answer", Str));
                string user = LookRules.Question(spot, words, ko, label, b.w, b.h, crop);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var json = await Ai.AskJsonAsync(LookRules.System, user, pictures, schema, 1500, cts.Token, model);
                string kind = (string)json["kind"] ?? "", at = (string)json["at"] ?? "", summary = (string)json["summary"] ?? "", answer = (string)json["answer"] ?? "";
                Log.Info($"look: answered in {sw.ElapsedMilliseconds} ms by {model} ({overview.Length / 1024} + {(close?.Length ?? 0) / 1024} kB sent), kind='{Shorten(kind, 60)}' at='{Shorten(at, 80)}'");
                _core.Post(() =>
                {
                    _core.Talk.Remember($"You looked at the user's {label}{(spot.Pointed ? $", at the spot they were {(spot.source == LookRules.Gaze ? "looking" : "pointing")} at: {at}" : " as a whole (no spot was pointed at)")} ({kind}). {summary} " +
                                        "(This is only about that one spot at that moment: when they point somewhere else or ask about \"here\" again, look again.)");
                    _core.Say(LookRules.Spoken(spot, answer, ko), "curious", 9, "look");
                });
            }
            catch (Exception e)
            {
                Log.Warn("look: " + e.Message);
                _core.Post(() => _core.Say(ko ? "지금은 잘 안 보여. 다시 물어봐 줄래?" : "I couldn't get a good look. Ask me again?", "worried", 4, "look"));
            }
            finally { _core.Post(() => { _looking = false; _core.Shows.LookEnd(show); }); }
        });
        return "";
    }
}

/// <summary>The pictures of a look: the display as it is on the screen, the overview with the ring, the close-up with the small ring.</summary>
static class LookShots
{
    static readonly Color Pink = Color.FromArgb(255, 45, 135);

    /// <summary>The display as it is on the screen right now (physical pixels).</summary>
    /// <summary>A check runs without anybody's screen (LP_LOOK_TEST_IMAGE is set): nothing is captured, and no real input may be sent either (PcActions).</summary>
    public static bool StandInSet => Environment.GetEnvironmentVariable("LP_LOOK_TEST_IMAGE") is { Length: > 0 };

    /// <summary>
    /// The picture that stands in for the display in a check: LP_LOOK_TEST_IMAGE. With "{n}" in its name it is a row of
    /// pictures - the frame-th look of one run of operating the computer gets "...1.png", "...2.png" ... (the last one
    /// that exists stays); null: none is set or it does not exist.
    /// </summary>
    static string StandIn(int frame)
    {
        if (Environment.GetEnvironmentVariable("LP_LOOK_TEST_IMAGE") is not { Length: > 0 } path) return null;
        if (!path.Contains("{n}")) return File.Exists(path) ? path : null;
        for (int n = Math.Max(1, frame); n >= 1; n--) if (File.Exists(path.Replace("{n}", n.ToString()))) return path.Replace("{n}", n.ToString());
        return null;
    }

    public static Bitmap Grab(RectI b, int frame = 1)
    {
        var full = new Bitmap(b.w, b.h, PixelFormat.Format32bppRgb);
        // A CHECK without anybody's screen: LP_LOOK_TEST_IMAGE names a picture that stands in for the display (stretched
        // to its size). Nothing is captured then - the log says so with every look.
        if (StandIn(frame) is { } standIn)
        {
            try
            {
                using var src = Image.FromFile(standIn); using var g = Graphics.FromImage(full);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, new Rectangle(0, 0, b.w, b.h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel);
            }
            catch { full.Dispose(); throw; }
            Log.Warn("look: TEST - the picture " + Path.GetFileName(standIn) + " stands in for the display (LP_LOOK_TEST_IMAGE); the screen was NOT captured");
            return full;
        }
        // (a check that names a picture which is not there must never fall through to the real screen)
        if (StandInSet) { full.Dispose(); throw new FileNotFoundException("LP_LOOK_TEST_IMAGE is set but the picture is not there: nothing is captured"); }
        try { using var g = Graphics.FromImage(full); g.CopyFromScreen(b.x, b.y, 0, 0, new Size(b.w, b.h), CopyPixelOperation.SourceCopy); }
        catch { full.Dispose(); throw; }
        return full;
    }

    /// <summary>The whole display at most LookRules.OverviewWidth wide, with a clearly visible ring on the spot. JPEG.</summary>
    public static byte[] Overview(Bitmap full, LookSpot spot)
    {
        var (w, h) = LookRules.Overview(full.Width, full.Height);
        using var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(full, new Rectangle(0, 0, w, h), 0, 0, full.Width, full.Height, GraphicsUnit.Pixel);
            if (spot.Pointed) Ring(g, spot.u * w, spot.v * h, Math.Max(26f, w * 0.03f), Math.Max(5f, w * 0.0045f));
        }
        var codec = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
        using var quality = new EncoderParameters(1);
        quality.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L);
        using var ms = new MemoryStream();
        bmp.Save(ms, codec, quality);
        return ms.ToArray();
    }

    /// <summary>The close-up round the spot at the display's own resolution (enlarged when it is small), with a small ring on the spot. PNG: text stays sharp.</summary>
    public static byte[] CloseUp(Bitmap full, LookSpot spot, RectI crop)
    {
        float zoom = LookRules.CropZoom(crop.w);
        int w = (int)Math.Round(crop.w * zoom), h = (int)Math.Round(crop.h * zoom);
        using var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(full, new Rectangle(0, 0, w, h), crop.x, crop.y, crop.w, crop.h, GraphicsUnit.Pixel);
            var (x, y) = LookRules.SpotInCrop(crop, full.Width, full.Height, spot.u, spot.v, zoom);
            // small, thin and hollow: what is under the finger must stay readable
            Ring(g, x, y, 36f, 2.5f, false);
        }
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>A hollow pink ring with a white edge and a thin dark line round that: seen on any background.</summary>
    static void Ring(Graphics g, float x, float y, float r, float thick, bool bold = true)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (bold) using (var dark = new Pen(Color.FromArgb(170, 40, 20, 30), thick * 2.6f)) g.DrawEllipse(dark, x - r, y - r, r * 2, r * 2);
        using (var white = new Pen(Color.FromArgb(bold ? 255 : 200, 255, 255, 255), thick * (bold ? 2.1f : 1.9f))) g.DrawEllipse(white, x - r, y - r, r * 2, r * 2);
        using (var pink = new Pen(Pink, thick)) g.DrawEllipse(pink, x - r, y - r, r * 2, r * 2);
    }

    /// <summary>LP_LOOK_KEEP=1 (checks only): the two pictures of the last look are kept in the captures folder, each look over the one before.</summary>
    public static void Keep(byte[] overview, byte[] close)
    {
        if (Environment.GetEnvironmentVariable("LP_LOOK_KEEP") != "1") return;
        try
        {
            File.WriteAllBytes(Path.Combine(HostPaths.Captures, "look-overview.jpg"), overview);
            string c = Path.Combine(HostPaths.Captures, "look-close-up.png");
            if (close != null) File.WriteAllBytes(c, close); else if (File.Exists(c)) File.Delete(c);
        }
        catch (Exception e) { Log.Warn("look: the pictures could not be kept: " + e.Message); }
    }
}
