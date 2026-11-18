using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;
using UnityEngine.Networking;

namespace LivingPartner.Windows
{
    /// <summary>
    /// THE FILE IN THE PARTNER'S HANDS. "Bambi goes into the PC holding the PDF, and when I click it there the real PDF
    /// opens" (the user, 2026-11-18). When the partner has carried a document into this PC (show "file", stage "shown",
    /// or a DocItem that has just become delivered - then for a minute), it holds the file up while it roams the
    /// desktop: a paper sheet with a folded corner (a picture sheet for designs), about 96 pixels, bobbing in its hands,
    /// the file's name on a strip of paper under it and a small "Click me" that pulses beside it.
    /// It is drawn in the partner's own small window - which the mouse goes straight through, like every window of this
    /// client. So that the file can be CLICKED, a window of its own lies exactly over it: ClickPad, a layered window
    /// that is all but invisible (alpha 1 of 255) yet takes the mouse; it is topmost, never activated, never has the
    /// keyboard. A click on it sends DocCommand "reveal" to the host - which opens the real file - with a little pop.
    /// Parked (-lp-park) the pad is off the displays like every other window; lpctl debug docClick is the click then.
    /// ONE FILE, drawn as what it is ("it carries a PDF into the PC but inside it is a PNG", the user, 2026-11-18): the
    /// host names THE file it carries and its thumbnail (the show "file": name + icon "/docs/&lt;id&gt;/page-1-s.png" or
    /// ".../option-2-s.png"; without a show: DocItem.files[0]). The sheet is chosen by that NAME's ending (.pdf: the PDF
    /// sheet, .png: the picture sheet, anything else: a plain page), the strip shows exactly that name, and that
    /// thumbnail lies on the sheet.
    /// lpctl debug docFake [seconds] 0 0 file|filepic|off: a file made up here, to see it without the host.
    /// </summary>
    public sealed partial class PcApp
    {
        [Header("Documents: the file in the partner's hands (PcCharacter/DocFile; one unit = the side of the sheet)")]
        [SerializeField] Transform _dcRoot;
        [SerializeField] UiSprite _dcSheet;
        [SerializeField] FxQuad _dcGlow;
        [SerializeField] StoryLabel _dcName;
        [SerializeField] Transform _dcHint;
        [SerializeField] StoryLabel _dcHintLabel;
        [SerializeField] Texture2D _dcTexPdf, _dcTexPic;
        [Tooltip("The file's own thumbnail on the sheet (the first page, the picked picture), in a small paper frame; a plain page for a file that is neither PDF nor PNG.")]
        [SerializeField] UiSprite _dcThumb;
        [SerializeField] FxQuad _dcThumbFrame;
        [SerializeField] Texture2D _dcTexPage;

        const float DocIconPx = 124f, DocHoldSeconds = 60f;      // (the picture has a margin: the sheet in it is 0.82 of its side - about 100 pixels)
        string _dcId = "", _dcFile = "", _dcIcon = "", _dcNameShown = "\u0001"; Texture2D _dcThumbTex; bool _dcPic, _dcClicked, _dcStopPending, _dcKnown, _dcPlaced, _dcMouseWas;
        float _dcUntil, _dcA, _dcPunch, _dcPoseW; int _dcShowSeq = int.MinValue;
        readonly HashSet<string> _dcDelivered = new HashSet<string>();
        readonly LivingPartner.Shared.Pose _dcPose = new LivingPartner.Shared.Pose();
        ClickPad _dcPad; RectI _dcPadRect, _dcPadSaid;      // where the pad is; where it was when that was last written to the log

        bool DocHeld => _dcId.Length > 0 && Time.realtimeSinceStartup < _dcUntil;

        /// <summary>From now on the partner holds this file up (for so many seconds at least).</summary>
        void DocHold(string id, string file, string icon, float seconds, string why)
        {
            float until = Time.realtimeSinceStartup + seconds;
            file = string.IsNullOrEmpty(file) ? "document.pdf" : file; icon = icon ?? "";
            if (id == _dcId && DocHeld && !_dcClicked)
            {
                // (already in its hands - but it is always THE file the host names now, with its thumbnail)
                _dcUntil = Mathf.Max(_dcUntil, until);
                if (file != _dcFile || (icon.Length > 0 && icon != _dcIcon)) { _dcFile = file; if (icon.Length > 0) _dcIcon = icon; _dcNameShown = "\u0001"; Log($"docs: the file in its hands is '{_dcFile}' (thumbnail '{_dcIcon}') - {why}"); }
                return;
            }
            _dcId = id; _dcFile = file; _dcIcon = icon;
            _dcPic = file.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
            _dcUntil = until; _dcClicked = false; _dcStopPending = true; _dcNameShown = "\u0001";
            Log($"docs: the partner holds up '{_dcFile}' (document '{id}', {DocSheetName(file)}, thumbnail '{_dcIcon}') for {seconds:0} s - {why}{(_ch.owner != Env.Pc ? "; it is not inside this PC just now" : "")}");
        }

        static string DocSheetName(string file) => file.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? "a PDF sheet" : file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "a picture sheet" : "a plain page";

        /// <summary>The thumbnail of THE file of a document, as the host serves it: the picked picture when the file is that picture, else the first page.</summary>
        static string DocIconOf(DocItem d)
        {
            if (d == null || string.IsNullOrEmpty(d.id)) return "";
            var f = d.files != null && d.files.Count > 0 ? d.files[0] : null;
            if (f != null && f.kind == "png" && d.options != null)
            {
                int n = d.options.FindIndex(o => o != null && o.picked) + 1;
                if (n > 0) return $"/docs/{d.id}/option-{n}-s.png";
            }
            return $"/docs/{d.id}/page-1-s.png";
        }

        // ---- thumbnails from the host ("/docs/<id>/<file>.png"; a bare name is a picture under /world/)
        readonly Dictionary<string, Texture2D> _dcThumbs = new Dictionary<string, Texture2D>();
        readonly Dictionary<string, float> _dcThumbAskedAt = new Dictionary<string, float>();

        Texture2D DocThumb(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_dcThumbs.TryGetValue(path, out var have)) return have;
            if (_dcThumbAskedAt.TryGetValue(path, out float at) && Time.time - at < 8f) return null;
            _dcThumbAskedAt[path] = Time.time;
            StartCoroutine(FetchDocThumb(path));
            return null;
        }

        IEnumerator FetchDocThumb(string path)
        {
            string http = (_link.CurrentUrl ?? "").Replace("wss://", "https://").Replace("ws://", "http://");
            int cut = http.LastIndexOf("/ws", StringComparison.Ordinal);
            if (cut > 0) http = http.Substring(0, cut);
            if (http.Length == 0) yield break;
            string url = path.StartsWith("/") ? http + path : http + "/world/" + UnityWebRequest.EscapeURL(path).Replace("+", "%20");
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = 20;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { Log($"docs: the thumbnail {path} could not be fetched: {req.error}"); yield break; }
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "Thumb " + path, wrapMode = TextureWrapMode.Clamp };      // (picture data, not a scene object)
                if (tex.LoadImage(req.downloadHandler.data, true)) { _dcThumbs[path] = tex; Log($"docs: thumbnail {path} arrived ({tex.width}x{tex.height})"); }
                else Destroy(tex);
            }
        }

        /// <summary>Once a frame, whatever the partner is doing: what the host says about documents, the mouse, the clocks.</summary>
        void DocsStep(float dt)
        {
            // ---- a click: the left button went down with the cursor on the pad
            bool down = (Win32.GetAsyncKeyState(0x01) & 0x8000) != 0;
            if (down && !_dcMouseWas && _dcPad != null && _dcPad.Shown && !_dcClicked && Win32.GetCursorPos(out var cp) && Win32.WindowFromPoint(cp) == _dcPad.Hwnd) DocClicked("the mouse");
            _dcMouseWas = down;

            // ---- what was not placed in the last frame is not there (the partner is not roaming this PC)
            if (!_dcPlaced)
            {
                if (_dcRoot.gameObject.activeSelf) _dcRoot.gameObject.SetActive(false);
                if (_dcPad != null && _dcPad.Shown) { _dcPad.Hide(); Log("docs: the click pad is hidden"); }
                _dcA = 0f;
            }
            _dcPlaced = false;
            _dcPunch = Mathf.MoveTowards(_dcPunch, 0f, dt * 3.2f);

            // ---- the host: a document that has just been carried in, or the show that says it is here
            var snap = _link.Snapshot;
            if (!_link.Connected || snap == null) return;
            var docs = snap.docs; DocItem newest = null;
            if (docs != null && docs.items != null)
            {
                foreach (var d in docs.items)
                {
                    if (d == null) continue;
                    if (!d.delivered) { _dcDelivered.Remove(d.id); continue; }
                    newest = d;
                    if (_dcDelivered.Add(d.id) && _dcKnown) DocHold(d.id, d.files != null && d.files.Count > 0 ? d.files[0].name : d.title, DocIconOf(d), DocHoldSeconds, "it has just been carried into the PC");
                }
                _dcKnown = true;      // (what was delivered before this client connected is not held up again)
            }
            var s = snap.show;
            if (s != null && s.kind == "file" && s.stage == "shown" && s.seq != _dcShowSeq)
            {
                _dcShowSeq = s.seq;
                if (s.startedAt > 0 && _link.HostNow - s.startedAt > Mathf.Max(3f, s.seconds + 1f)) return;      // long over
                DocItem match = null;
                if (docs != null && docs.items != null)
                    foreach (var d in docs.items)
                    {
                        if (d == null || d.files == null) continue;
                        foreach (var f in d.files) if (f != null && f.name == s.name) match = d;
                    }
                match = match ?? newest;
                if (match == null) { Log($"docs: the host shows the file '{s.name}', but lists no document for it"); return; }
                // (the sheet follows the NAME of the file the host carries - not the kind of the document: a PDF is a PDF sheet)
                DocHold(match.id, string.IsNullOrEmpty(s.name) ? (match.files != null && match.files.Count > 0 ? match.files[0].name : match.title) : s.name,
                    string.IsNullOrEmpty(s.icon) ? DocIconOf(match) : s.icon, Mathf.Max(12f, s.seconds), "the host's show (file, shown)");
            }
        }

        void DocClicked(string how)
        {
            if (_dcId.Length == 0) { Log("docs: a click, but no file is held"); return; }
            _dcClicked = true; _dcPunch = 1f;
            _dcUntil = Time.realtimeSinceStartup + 3.2f;      // it has done its work: a moment more, then it is put away
            _link.Send(Msg.Doc, new DocCommand { cmd = "reveal", id = _dcId });
            GlassSfx("pop");
            if (_dcRoot.gameObject.activeSelf) _sparks.Burst(_dcSheet.transform.position + new Vector3(0, 0, -0.01f), Vector3.right, Vector3.up, _H * 0.36f, 12);
            Log($"docs: the file '{_dcFile}' was clicked ({how}) - doc.command reveal '{_dcId}' sent to the host");
        }

        /// <summary>After the motor has moved the partner (Roam): it holds the file out in front with both hands.</summary>
        void DocsPose(float dt)
        {
            bool want = DocHeld && !_stOn && _dvT < 0f && _xferTo == null && _ch.owner == Env.Pc;
            if (want)
            {
                _nextWander = Time.realtimeSinceStartupAsDouble + 6;
                if (_dcStopPending) { _dcStopPending = false; _motor.Stop(); _motor.Face(180f); }      // on its feet where it is, turned to the user
                if (_dcClicked) PoseLib.Happy(_dcPose, Time.time); else GlassPoses.Offer(_dcPose, Time.time, 1f);      // (held up high)
            }
            _dcPoseW = Mathf.MoveTowards(_dcPoseW, want ? 1f : 0f, dt / 0.25f);
            if (_dcPoseW > 0.001f) LivingPartner.Shared.Pose.Lerp(_pose, _dcPose, M.Smooth(_dcPoseW), _pose);
        }

        /// <summary>After the partner's pose has been applied (Roam): the sheet in its hands, its name, and the pad that takes the click.</summary>
        void DocsPlace(float dt)
        {
            bool want = DocHeld && !_stOn && _dvT < 0f && _xferTo == null && _ch.owner == Env.Pc;
            _dcA = Mathf.MoveTowards(_dcA, want ? 1f : 0f, dt / (want ? 0.35f : 0.2f));
            if (_dcA <= 0.001f) return;
            _dcPlaced = true;
            if (!_dcRoot.gameObject.activeSelf) _dcRoot.gameObject.SetActive(true);
            float t = Time.time;
            // held up: over its head, between its raised hands - its face stays free (in front of its chest the sheet hid the face)
            Vector3 hand = _rig.HandRWorld, chest = _rig.ChestWorld, top = _rig.HeadTopWorld;
            float z = Mathf.Min(hand.z, chest.z) - 0.03f;
            float d = -_eye.z, k = d > 0.05f ? d / (d + Mathf.Max(0f, z)) : 1f;      // how much smaller it is seen through the glass
            float side = DocIconPx * Mpp / Mathf.Max(0.2f, k);                    // metres: the picture's side (the sheet in it is about 96 pixels tall on this monitor)
            Vector3 at = new Vector3(chest.x, top.y + 0.44f * side + 0.035f * side * Mathf.Sin(t * 2.4f), z);
            float pop = Mathf.Max(0.001f, Ui.Pop(_dcA)) * (1f + 0.22f * Mathf.Sin(_dcPunch * Mathf.PI));
            _dcRoot.SetPositionAndRotation(at, Quaternion.Euler(0f, 0f, 2.5f * Mathf.Sin(t * 1.7f)));
            _dcRoot.localScale = Vector3.one * side * pop;
            if (_dcNameShown != _dcFile)
            {
                _dcNameShown = _dcFile;
                _dcSheet.SetTexture(_dcFile.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? _dcTexPdf : _dcFile.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? _dcTexPic : _dcTexPage);
                string fit = _dcFile;
                for (int n = _dcFile.Length - 1; n > 1 && _dcName.Measure(fit) > 1.52f; n--) fit = _dcFile.Substring(0, n).TrimEnd() + "…";
                _dcName.text = fit;
            }
            // the file's own thumbnail on the sheet (its first page, or the picked picture), once it has arrived
            var thumb = DocThumb(_dcIcon);
            bool hasThumb = thumb != null;
            if (_dcThumb.enabled != hasThumb) { _dcThumb.enabled = hasThumb; _dcThumbFrame.enabled = hasThumb; }
            if (hasThumb && _dcThumbTex != thumb)
            {
                _dcThumbTex = thumb; _dcThumb.SetTexture(thumb);
                float asp = thumb.width / (float)Mathf.Max(1, thumb.height), tw = Mathf.Min(0.44f, 0.38f * asp), th = tw / asp;
                _dcThumb.transform.localScale = new Vector3(tw, th, 1f);
                Ui.SizePanel(_dcThumbFrame, tw + 0.05f, th + 0.05f, 0.03f, 0.012f);
            }
            // "Click me": it pulses, the glow behind the sheet breathes with it; after the click it says what happens
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 4.2f);
            _dcHintLabel.text = _dcClicked ? "Opening ..." : "Click me";
            _dcHint.localScale = Vector3.one * (_dcClicked ? 1f : 1f + 0.09f * pulse);
            _dcGlow.SetColors(new Color(1f, 0.84f, 0.42f, (0.30f + 0.30f * pulse) * _dcA), new Color(1f, 0.96f, 0.84f, (0.30f + 0.30f * pulse) * _dcA));

            // the pad that takes the click: over the sheet and its name, where they are seen on the glass
            Vector2 on = new Vector2(_eye.x + (at.x - _eye.x) * k, _eye.y + (at.y - _eye.y) * k);
            LocalToPx(new Vector3(on.x, on.y, 0f), out float cx, out float cy);
            float px = side * k / Mpp;
            var rect = new RectI(Mathf.RoundToInt(cx - 0.84f * px), Mathf.RoundToInt(cy - 0.84f * px), Mathf.RoundToInt(1.68f * px), Mathf.RoundToInt(1.36f * px));
            rect = RectI.Intersect(rect, _mon.bounds);
            bool canClick = want && _dcA > 0.9f && !_dcClicked && !rect.IsEmpty && _suspended.Length == 0;
            if (canClick)
            {
                if (_dcPad == null)
                {
                    try { _dcPad = new ClickPad(256, 256); Log("docs: the click pad exists (a layered window that takes the mouse, never the focus)"); }
                    catch (Exception e) { Log("docs: the click pad could not be made: " + e.Message); return; }
                }
                bool was = _dcPad.Shown;
                _dcPad.Place(rect, LayeredOverlay.ParkX);
                _dcPadRect = rect;
                if (!was || Mathf.Abs(rect.x - _dcPadSaid.x) + Mathf.Abs(rect.y - _dcPadSaid.y) > 60) { _dcPadSaid = rect; Log($"docs: the click pad is over the file at {rect}{(LayeredOverlay.ParkX != 0 ? " (parked " + LayeredOverlay.ParkX + " px to the side)" : "")}"); }
            }
            else if (_dcPad != null && _dcPad.Shown) { _dcPad.Hide(); Log("docs: the click pad is hidden"); }
        }

        /// <summary>The file belongs in the partner's window: its place is added to what that window has to show.</summary>
        void DocsBounds(ref Bounds b)
        {
            if (!_dcRoot.gameObject.activeSelf) return;
            float side = _dcRoot.localScale.x;
            b.Encapsulate(new Bounds(_dcRoot.position + new Vector3(0.2f * side, 0.15f * side, 0f), new Vector3(2.9f * side, 1.9f * side, 0.01f)));
        }

        void DocsDispose() { _dcPad?.Dispose(); _dcPad = null; }

        void DocsDebug(DebugCommand d)
        {
            if (d.cmd == "docClick")
            {
                if (d.value > 0.5f)
                {
                    // lpctl debug docClick 1: through the hit test a real click goes through, without a mouse - is the pad
                    // the window Windows finds at the pad's own middle? (It is, parked or not, when the pad takes the mouse.)
                    if (_dcPad == null || !_dcPad.Shown) { Log("docs: docClick 1 - no click pad is shown"); return; }
                    var mid = new Win32.POINT(_dcPadRect.x + LayeredOverlay.ParkX + _dcPadRect.w / 2, _dcPadRect.y + _dcPadRect.h / 2);
                    IntPtr at = Win32.WindowFromPoint(mid);
                    Log($"docs: hit test at ({mid.x},{mid.y}): Windows finds {(at == _dcPad.Hwnd ? "the click pad" : at == IntPtr.Zero ? "no window" : "another window (" + Win32.WindowClass(at) + ")")}");
                    if (at != _dcPad.Hwnd) return;
                    DocClicked("the hit test of the debug command");
                    return;
                }
                DocClicked("the debug command");
                return;
            }
            if (d.cmd != "docFake") return;
            string what = (d.text ?? "").Trim().ToLowerInvariant();
            // file:/docs/doc-0123abcd/page-1-s.png - with that thumbnail of the host's; else a picture the host has stands in for it (a thing's sticker under /world/)
            string fakeIcon = ""; int colon = what.IndexOf(':');
            if (colon > 0) { fakeIcon = what.Substring(colon + 1); what = what.Substring(0, colon); }
            var fakeWorld = _link.Snapshot != null ? _link.Snapshot.world : null;
            var fakeItem = fakeWorld != null && fakeWorld.items != null ? fakeWorld.items.Find(i => i != null && !string.IsNullOrEmpty(i.icon)) : null;
            if (what == "file" || what == "filepic") DocHold("fake-file", what == "file" ? "Test report - mixed reality desks.pdf" : "New icon - option 2.png", fakeIcon.Length > 0 ? fakeIcon : fakeItem != null ? fakeItem.icon : "", d.value > 0.5f ? d.value : DocHoldSeconds, "made up here by the debug command");
            else if (what == "off" || what.Length == 0) { if (DocHeld) Log("docs: the file is put away (debug)"); _dcUntil = 0f; }
        }
    }

    /// <summary>
    /// A small window that takes mouse clicks where the partner holds something up. Layered like the overlays, but NOT
    /// click-through: every pixel has alpha 1 of 255 - nothing is seen of it, and the mouse lands on it. Topmost,
    /// WS_EX_NOACTIVATE (a click does not make it the foreground window, it never has the keyboard), not in the taskbar.
    /// Clicks are read by the owner (the left button going down while WindowFromPoint is this window).
    /// </summary>
    public sealed class ClickPad : IDisposable
    {
        public IntPtr Hwnd { get; private set; }
        public bool Shown { get; private set; }
        readonly int _w, _h;
        IntPtr _screenDc, _memDc, _dib, _oldBmp, _bits;

        public ClickPad(int w, int h)
        {
            _w = w; _h = h;
            const uint SS_NOTIFY = 0x100;      // (a plain "Static" answers hit tests with "transparent": with this style it is hit)
            uint ex = Win32.WS_EX_LAYERED | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOPMOST;
            Hwnd = Win32.CreateWindowEx(ex, "Static", "LivingPartnerFile", Win32.WS_POPUP | SS_NOTIFY, 0, 0, w, h, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (Hwnd == IntPtr.Zero) throw new Exception("CreateWindowEx failed: " + Marshal.GetLastWin32Error());
            _screenDc = Win32.GetDC(IntPtr.Zero);
            _memDc = Win32.CreateCompatibleDC(_screenDc);
            var bmi = new Win32.BITMAPINFO();
            bmi.bmiHeader.biSize = Marshal.SizeOf(typeof(Win32.BITMAPINFOHEADER));
            bmi.bmiHeader.biWidth = w; bmi.bmiHeader.biHeight = h;
            bmi.bmiHeader.biPlanes = 1; bmi.bmiHeader.biBitCount = 32; bmi.bmiHeader.biCompression = 0;
            _dib = Win32.CreateDIBSection(_memDc, ref bmi, 0, out _bits, IntPtr.Zero, 0);
            if (_dib == IntPtr.Zero) throw new Exception("CreateDIBSection failed");
            _oldBmp = Win32.SelectObject(_memDc, _dib);
            var fill = new int[w * h];
            for (int i = 0; i < fill.Length; i++) fill[i] = 0x01000000;      // BGRA, colour already multiplied by alpha: black at alpha 1
            Marshal.Copy(fill, 0, _bits, fill.Length);
        }

        /// <summary>Puts the pad over that part of the desktop (physical pixels; park = pixels to the side, off the displays) and shows it.</summary>
        public void Place(RectI r, int park)
        {
            if (Hwnd == IntPtr.Zero) return;
            var dst = new Win32.POINT(r.x + park, r.y);
            var size = new Win32.SIZE(Mathf.Clamp(r.w, 1, _w), Mathf.Clamp(r.h, 1, _h));
            var src = new Win32.POINT(0, 0);
            var blend = new Win32.BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            Win32.UpdateLayeredWindow(Hwnd, _screenDc, ref dst, ref size, _memDc, ref src, 0, ref blend, Win32.ULW_ALPHA);
            if (!Shown)
            {
                Win32.SetWindowPos(Hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW | Win32.SWP_NOOWNERZORDER);
                Shown = true;
            }
        }

        public void Hide()
        {
            if (Hwnd != IntPtr.Zero && Shown) { Win32.ShowWindow(Hwnd, Win32.SW_HIDE); Shown = false; }
        }

        public void Dispose()
        {
            if (Hwnd != IntPtr.Zero) { Win32.DestroyWindow(Hwnd); Hwnd = IntPtr.Zero; }
            if (_memDc != IntPtr.Zero) { Win32.SelectObject(_memDc, _oldBmp); Win32.DeleteDC(_memDc); _memDc = IntPtr.Zero; }
            if (_dib != IntPtr.Zero) { Win32.DeleteObject(_dib); _dib = IntPtr.Zero; }
            if (_screenDc != IntPtr.Zero) { Win32.ReleaseDC(IntPtr.Zero, _screenDc); _screenDc = IntPtr.Zero; }
            Shown = false;
        }
    }
}
