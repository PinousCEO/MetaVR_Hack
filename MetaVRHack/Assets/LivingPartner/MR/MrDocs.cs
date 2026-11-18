using System;
using System.Collections.Generic;
using System.IO;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// DOCUMENTS in the room - what the partner made for the user (Shared/Docs.cs; the PC makes them and keeps the files).
    /// "Look into X and make a PDF: it can be shown in MR, and when I ask it is put into my computer" (the user, 2026-11-18).
    ///  - While one is being made, a small strip of paper floats beside the partner: its title, what is being done, a bar.
    ///  - A REPORT that is open (docs.openId) stands as an upright sheet at a reading place: 55 cm from the eyes, a little
    ///    below them, 25 degrees to the right of where the user looked when it came - beside the work, not over it. It
    ///    stays where it appeared and is moved by a pinch on its top strip. It shows one page as a picture (the small
    ///    one first, then the full one; with mip maps and anisotropic filtering, or the letters would crawl), the page
    ///    number, earlier / later (also: a pinch on the page drawn sideways), "Send to PC" and a button that puts it away.
    ///  - DESIGNS that are open: a wider sheet with what there is now and the four options as cards; touching a card
    ///    picks it (a honey frame and a tick); cards that are still being drawn shimmer.
    ///  - While the partner carries the file to the screen (show "file", stage "going") it holds a small sheet over its head.
    /// Everything goes to the PC as a DocCommand (open | close | page | pick | deliver); what is shown is what the PC says.
    /// lpctl debug docFake [distance] 0 0 report|designs|drawing|making|sheet|off[:folder] - a document made up here from
    /// pictures in a folder of this PC (artifacts/tmp/d1), docPage n, docPick n, docSend, docClose.
    /// </summary>
    public sealed partial class MrApp
    {
        [Header("Documents (MrCore/Docs and MrCore/Panels/Doc...)")]
        [SerializeField] DocBoardView _docs;

        const float DocReach = 0.55f, DocBelow = 0.09f, DocSide = 25f, DocSideWide = 31f;
        static readonly DocsView NoDocs = new DocsView();
        DocsView _docFake; string _docFakeDir = ""; float _docDbgDist, _docSheetFakeUntil; string _docSheetFakeName = "";
        DocsView DocsNow => _docFake ?? (_snap != null && _snap.docs != null ? _snap.docs : NoDocs);

        // the board: which document, where it stands
        string _docOpenId = "", _docShown = "", _docHideId = ""; float _docHideUntil;
        int _docPage = 1, _docHostPage = 1;
        Vector3 _docPos; Quaternion _docRot = Quaternion.identity;
        // its pictures (of the open document only): by file name
        readonly Dictionary<string, Texture2D> _docTex = new Dictionary<string, Texture2D>();
        readonly HashSet<string> _docAsked = new HashSet<string>();
        readonly Dictionary<string, float> _docFailedAt = new Dictionary<string, float>();
        readonly List<string> _docDrop = new List<string>();
        // moving it by its top strip, turning a page by a pinch drawn across it
        bool _docDrag, _docDragRay; int _docDragHand; float _docDragReach, _docDragLost; Vector3 _docDragOffset;
        bool _docSwipe, _docSwipeRay; int _docSwipeHand; float _docSwipeX0, _docSwipeDx;
        // the strip, the sheet
        string _docStripFor = ""; Vector3 _docStripPos; string _docStripShown = "";
        bool _docSheetWas;
        // what the report board / the designs board show now (they are written anew only when one of these changes)
        int _drNamesFor = -1, _drPage, _drPages; string _drSmall = "", _drFull = "", _drNext = "", _drId = "", _drTitle = ""; Texture2D _drTex; bool _drDelivered, _drSending, _drFailed;
        string _ddId = "", _ddTitle = "", _ddStatus = "", _ddStage = ""; int _ddRev, _ddNames, _ddHave, _ddPicked; bool _ddOrig, _ddDelivered, _ddSending;

        void InitDocs()
        {
            var v = _docs;
            v.prev.press = () => DocTurn(-1, "the button");
            v.next.press = () => DocTurn(+1, "the button");
            v.send.press = () => DocSend(v.send);
            v.close.press = DocClose;
            v.designsSend.press = () => DocSend(v.designsSend);
            v.designsClose.press = DocClose;
            for (int i = 0; i < v.cards.Length; i++) { int k = i; v.cards[i].press = () => DocPick(k); }
            v.report.gameObject.SetActive(false); v.designs.gameObject.SetActive(false); v.strip.gameObject.SetActive(false);
            v.sheet.enabled = false;
        }

        DocItem DocOpen()
        {
            var view = DocsNow;
            if (string.IsNullOrEmpty(view.openId) || view.items == null) return null;
            foreach (var d in view.items) if (d != null && d.id == view.openId) return d;
            return null;
        }

        void SendDoc(string cmd, string id, int n = 0)
        {
            Debug.Log($"[MR] docs: {cmd} '{id}'{(n != 0 ? " " + n : "")}{(_docFake != null ? " (a made-up document: nothing is sent)" : " -> the PC")}");
            if (_docFake == null) { _link.Send(Msg.Doc, new DocCommand { cmd = cmd, id = id, n = n }); return; }
            // a made-up document answers as the PC would
            var d = _docFake.items.Count > 0 ? _docFake.items[0] : null;
            if (cmd == "close") _docFake.openId = "";
            else if (cmd == "page") _docFake.page = n;
            else if (cmd == "pick" && d != null) for (int i = 0; i < d.options.Count; i++) d.options[i].picked = i == n - 1;
            else if (cmd == "deliver" && d != null) { d.delivered = true; _docSheetFakeUntil = Time.time + 8f; _docSheetFakeName = d.files.Count > 0 ? d.files[0].name : "file.pdf"; }
            _docFake.revision++;
        }

        void DocTurn(int step, string how)
        {
            var d = DocOpen();
            if (d == null || d.kind != "report" || d.pages <= 0) return;
            int to = Mathf.Clamp(_docPage + step, 1, d.pages);
            if (to == _docPage) return;
            _docPage = to; _docShown = "";
            Sfx2D("tap", 0.35f, step > 0 ? 1.15f : 0.9f);
            Debug.Log($"[MR] docs: page {to} of {d.pages} ({how})");
            SendDoc("page", d.id, to);
        }

        void DocSend(UiButton from)
        {
            var d = DocOpen();
            if (d == null) return;
            SendDoc("deliver", d.id);
            _sparks.Burst(from.transform.position, from.transform.right, from.transform.up, 0.05f, 10);
            Sfx2D("sparkle", 0.5f);
            _docShown = "";
        }

        void DocClose()
        {
            var d = DocOpen();
            if (d == null) return;
            _docHideId = d.id; _docHideUntil = Time.time + 2.5f;      // (it goes at once; the PC's answer keeps it away)
            SendDoc("close", d.id);
        }

        /// <summary>The option of that card is picked (the PC is told the option's number, counted from 1).</summary>
        void DocPick(int card)
        {
            var d = DocOpen();
            if (d == null || d.kind != "designs" || card < 0 || card >= d.options.Count) return;
            SendDoc("pick", d.id, card + 1);
            var b = _docs.cards[card];
            _sparks.Burst(b.transform.position, b.transform.right, b.transform.up, 0.045f, 8);
            _docShown = "";
        }

        // ------------------------------------------------------------------ pictures of the open document
        string DocUrl(string id, string file)
        {
            if (_docFake != null) return new Uri(Path.Combine(_docFakeDir, file)).AbsoluteUri;
            return HostUrl("/docs/" + Uri.EscapeDataString(id) + "/" + Uri.EscapeDataString(file));
        }

        bool DocFailed(string file) => _docFailedAt.TryGetValue(file, out float at) && Time.time - at < 10f;

        /// <summary>A picture of the open document (null until it has arrived). crisp: a page of writing - it is read from close by.</summary>
        Texture2D DocPicture(string file, bool crisp)
        {
            if (string.IsNullOrEmpty(file) || _docOpenId.Length == 0) return null;
            if (_docTex.TryGetValue(file, out var have)) return have;
            if (DocFailed(file)) return null;
            if (!_docAsked.Add(file)) return null;
            string id = _docOpenId;
            // (decoded away from the main thread - MrMarket.FetchTexture: a page of 12 MB decoded by LoadImage was a hitch at every turn)
            StartCoroutine(FetchTexture(DocUrl(id, file), true, (tex, kb) =>
            {
                if (id != _docOpenId) { if (tex != null) Destroy(tex); return; }      // (another document is open by now)
                _docAsked.Remove(file);
                if (tex == null) { _docFailedAt[file] = Time.time; Debug.Log($"[MR] docs: {file} could not be fetched, or is not a picture"); return; }
                // picture data, not a scene object. Mip maps and anisotropic filtering: the board is seen at an angle and from
                // further than one texel a pixel - without them the letters of a page shimmer and break up.
                tex.name = "Doc " + file;
                tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Trilinear; tex.anisoLevel = crisp ? 8 : 4;
                if (crisp) tex.mipMapBias = -0.4f;      // (a touch sharper than the mip level the distance asks for)
                _docFailedAt.Remove(file);
                if (_docTex.TryGetValue(file, out var was) && was != null && was != tex) Destroy(was);
                _docTex[file] = tex; _docShown = "";
                Debug.Log($"[MR] docs: {file} arrived ({tex.width}x{tex.height}, {kb} kB, {tex.mipmapCount} mip levels, aniso {tex.anisoLevel})");
            }));
            return null;
        }

        void DocFreePictures(string keepFull = null)
        {
            if (keepFull == null)
            {
                foreach (var t in _docTex.Values) if (t != null) Destroy(t);
                _docTex.Clear(); _docAsked.Clear(); _docFailedAt.Clear(); _docFullKept.Clear();
                return;
            }
            // The full pictures of the THREE pages looked at last stay (about 12 MB each): leafing to and fro between them
            // fetches and decodes nothing. The other full pictures go; the small ones all stay.
            _docFullKept.Remove(keepFull); _docFullKept.Add(keepFull);
            while (_docFullKept.Count > DocFullPages) _docFullKept.RemoveAt(0);
            _docDrop.Clear();
            foreach (var k in _docTex.Keys) if (k.StartsWith("page-", StringComparison.Ordinal) && !k.EndsWith("-s.png", StringComparison.Ordinal) && !_docFullKept.Contains(k)) _docDrop.Add(k);
            foreach (var k in _docDrop) { Destroy(_docTex[k]); _docTex.Remove(k); }
        }
        const int DocFullPages = 3;
        readonly List<string> _docFullKept = new List<string>();      // the full pictures that are kept, the one looked at last at the end

        // ------------------------------------------------------------------ per frame
        /// <summary>The PC is gone: nothing of a document stays up (nothing is behind its buttons).</summary>
        void DocsHostLost()
        {
            var v = _docs;
            v.report.Hide(); v.designs.Hide(); v.strip.Hide();
            if (v.sheet.enabled) v.sheet.enabled = false;
            if (v.sheetPic.enabled) { v.sheetPic.enabled = false; v.sheetPaper.enabled = false; }
            if (v.sheetNameBg.enabled) { v.sheetNameBg.enabled = false; v.sheetName.text = ""; _docSheetNameShown = ""; }
            _docSheetWas = false;
            _docDrag = _docSwipe = false;
        }

        /// <summary>Once a frame while the PC is there (MrUi.UpdateUi, among the boards).</summary>
        void UpdateDocs(float dt)
        {
            var v = _docs; var view = DocsNow;
            var open = _calibrating ? null : DocOpen();
            if (open != null && open.id == _docHideId && Time.time < _docHideUntil) open = null;      // just closed here
            if (view.openId != _docHideId) _docHideId = "";
            // a report has nothing to show until it has pages; designs are shown while they are being drawn
            if (open != null && open.kind == "report" && (open.status != "ready" || open.pages <= 0)) open = null;
            if (open != null && open.status == "failed") open = null;

            string openId = open != null ? open.id : "";
            if (openId != _docOpenId)
            {
                DocFreePictures();
                _docOpenId = openId; _docShown = ""; _docDrag = _docSwipe = false;
                _docPage = _docHostPage = Mathf.Max(1, view.page);
                v.report.Hide(); v.designs.Hide();
                if (open != null)
                {
                    DocPlace(open.kind == "designs" && !SinglePng(open));
                    Sfx("whoosh", _docPos, 0.4f);
                    Debug.Log($"[MR] docs: '{open.title}' ({open.kind}, {(open.kind == "report" ? open.pages + " pages" : open.options.Count + " options")}) is open on its board at {_docPos.ToString("0.00")}, {Vector3.Distance(_docPos, Input.HeadPos):0.00} m from the eyes");
                }
                else Debug.Log("[MR] docs: the board is put away");
            }
            if (view.page != _docHostPage) { _docHostPage = view.page; if (open != null && open.kind == "report") { _docPage = Mathf.Max(1, view.page); _docShown = ""; } }

            if (open != null && (open.kind == "report" || SinglePng(open))) UpdateDocReport(dt, open);
            else if (open != null) UpdateDocDesigns(dt, open, view);
            UpdateDocStrip(dt, view, open);
            UpdateDocSheet(dt);
        }

        /// <summary>Where the board comes up: at a reading distance, a little below the eyes, to the right of the work - and there it stays.</summary>
        void DocPlace(bool wide)
        {
            Vector3 head = Input.HeadPos;
            if (_docDbgDist > 0.05f)
            {
                // (a capture from close by: straight in front of the eyes, at that distance)
                _docPos = head + Input.HeadRot * Vector3.forward * _docDbgDist;
                _docRot = Quaternion.LookRotation(_docPos - head, Input.HeadRot * Vector3.up);
                return;
            }
            Vector3 fwd = Input.HeadRot * Vector3.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            Vector3 dir = Quaternion.AngleAxis(wide ? DocSideWide : DocSide, Vector3.up) * fwd;
            float flat = Mathf.Sqrt(DocReach * DocReach - DocBelow * DocBelow);
            _docPos = head + dir * flat + Vector3.down * DocBelow;
            _docRot = Quaternion.LookRotation(_docPos - head, Vector3.up);      // square to the eyes: below them, so it leans back a little
        }

        static bool SinglePng(DocItem d) => d.kind == "designs" && d.options.Count == 1 && d.files.Count == 1 && d.files[0].kind == "png";

        void UpdateDocReport(float dt, DocItem d)
        {
            var v = _docs; var p = v.report;
            _docPage = Mathf.Clamp(_docPage, 1, Mathf.Max(1, d.pages));
            // the page: its small picture first, then the full one
            // (the names of its pictures are put together when the page changes, not in every frame)
            if (_drNamesFor != _docPage) { _drNamesFor = _docPage; _drSmall = "page-" + _docPage + "-s.png"; _drFull = "page-" + _docPage + ".png"; _drNext = "page-" + (_docPage + 1) + "-s.png"; }
            bool picture = SinglePng(d);
            string small = picture ? "option-1-s.png" : _drSmall, full = picture ? "option-1.png" : _drFull;
            Texture2D ts = DocPicture(small, true);
            Texture2D tf = ts != null || DocFailed(small) ? DocPicture(full, true) : null;
            Texture2D tex = tf != null ? tf : ts;
            if (ts != null && _docPage < d.pages) DocPicture(_drNext, true);      // the next one is fetched ahead: leafing does not wait
            bool sending = DocCarrying(d), failedFull = DocFailed(full);
            // (written anew only when something it shows has changed; _docShown "" = asked for from elsewhere)
            if (_docShown.Length == 0 || _drId != d.id || _drPage != _docPage || _drPages != d.pages || _drTitle != d.title || !ReferenceEquals(_drTex, tex) || _drDelivered != d.delivered || _drSending != sending || _drFailed != failedFull)
            {
                _docShown = "report"; _drId = d.id; _drPage = _docPage; _drPages = d.pages; _drTitle = d.title; _drTex = tex; _drDelivered = d.delivered; _drSending = sending; _drFailed = failedFull;
                FitInto(v.reportTitle, string.IsNullOrEmpty(d.title) ? "Report" : d.title, p.width - 0.118f);
                v.reportPage.text = picture ? "PNG" : _docPage + " / " + d.pages;
                bool more = d.pages > 1;
                if (v.prev.gameObject.activeSelf != more) { v.prev.gameObject.SetActive(more); v.next.gameObject.SetActive(more); }
                DocTint(v.prev, _docPage > 1 ? Ui.Pal.Violet : GreyPaper); DocTint(v.next, _docPage < d.pages ? Ui.Pal.Violet : GreyPaper);
                DocSendLook(v.send, d, sending);
                v.page.SetTexture(tex != null ? tex : v.texBlankPage);
                // the picture keeps its own proportion inside the page's place
                float w = v.pageW, h = v.pageH;
                if (tex != null && tex.height > 0) { float a = tex.width / (float)tex.height; if (a > w / h) h = w / a; else w = h * a; }
                v.page.transform.localScale = new Vector3(w, h, 1f);
                v.pageEdge.transform.localScale = new Vector3(w + 0.004f, h + 0.004f, 1f);
                v.reportNote.text = tex != null ? "" : DocFailed(full) && DocFailed(small) ? "This page could not be fetched." : "Page " + _docPage + " ...";
                if (tf != null) DocFreePictures(full);
            }
            p.Show();
            DocDrag(dt, p, v.reportBar);
            DocSwipe(dt, d);
            v.page.transform.localPosition = new Vector3(Mathf.Clamp(_docSwipeDx * 0.35f, -0.03f, 0.03f), v.pageY, -0.003f);
            p.transform.SetPositionAndRotation(_docPos, _docRot);
            UpdateButtons(p, dt);
        }

        void UpdateDocDesigns(float dt, DocItem d, DocsView view)
        {
            var v = _docs; var p = v.designs;
            bool making = d.status == "making", sending = DocCarrying(d);
            Texture2D orig = DocPicture("original.png", false);
            int have = 0, picked = -1, names = 17;
            for (int i = 0; i < v.cards.Length; i++)
            {
                var o = i < d.options.Count ? d.options[i] : null;
                var tex = o != null && !string.IsNullOrEmpty(o.picture) ? DocPicture(o.picture, false) : null;
                if (tex != null) have |= 1 << i;
                if (o != null && o.picked) picked = i;
                unchecked { names = names * 31 + (o != null && o.name != null ? o.name.GetHashCode() : 0); }
            }
            // (written anew only when something it shows has changed - nothing is put together in a frame in which nothing has; _docShown "" = asked for from elsewhere)
            if (_docShown.Length == 0 || _ddId != d.id || _ddRev != view.revision || _ddTitle != d.title || _ddStatus != d.status || _ddStage != d.stage || _ddNames != names || _ddHave != have || _ddPicked != picked || _ddOrig != (orig != null) || _ddDelivered != d.delivered || _ddSending != sending)
            {
                _docShown = "designs"; _ddId = d.id; _ddRev = view.revision; _ddTitle = d.title; _ddStatus = d.status; _ddStage = d.stage; _ddNames = names; _ddHave = have; _ddPicked = picked; _ddOrig = orig != null; _ddDelivered = d.delivered; _ddSending = sending;
                FitInto(v.designsTitle, string.IsNullOrEmpty(d.title) ? "New designs" : d.title, p.width - 0.118f);
                v.original.SetTexture(orig != null ? orig : v.texBlankCard);
                for (int i = 0; i < v.cards.Length; i++)
                {
                    var c = v.cards[i]; var o = i < d.options.Count ? d.options[i] : null;
                    bool on = o != null || making;      // (a card that is still to be drawn has its place already)
                    if (c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
                    if (!on) continue;
                    var tex = o != null && !string.IsNullOrEmpty(o.picture) ? DocPicture(o.picture, false) : null;
                    c.art.SetTexture(tex != null ? tex : v.texBlankCard);
                    if (tex != null) c.art.SetLook(0f, -1f);
                    string name = o != null && !string.IsNullOrEmpty(o.name) ? o.name : "Option " + (i + 1);
                    c.label.text = name;
                    var lines = new System.Text.StringBuilder();
                    WrapInto(c.label, lines, name, 0.084f, 3, "");
                    c.label.text = lines.ToString().TrimEnd('\n');
                    bool isPicked = o != null && o.picked;
                    if (v.cardPicked[i].activeSelf != isPicked) v.cardPicked[i].SetActive(isPicked);
                    c.color = isPicked ? HoneyPaper : Paper; c.color2 = isPicked ? new Color(1f, 0.84f, 0.50f, 0.98f) : PaperDeep;
                }
                v.designsNote.text = making ? (string.IsNullOrEmpty(d.stage) ? "Drawing ..." : d.stage) : picked >= 0 ? "Option " + (picked + 1) + " is picked." : "Touch the one you like.";
                FitInto(v.designsNote, v.designsNote.text, p.width - 0.20f);
                DocSendLook(v.designsSend, d, sending);
            }
            // a card whose picture is not there yet: a soft band of light goes over it, again and again
            float t = Time.time;
            for (int i = 0; i < v.cards.Length; i++)
            {
                var c = v.cards[i];
                if (!c.gameObject.activeSelf || (have & (1 << i)) != 0) continue;
                c.art.SetLook(0.25f + 0.25f * Mathf.Sin(t * 3f + i), Mathf.Repeat(t * 0.8f + i * 0.21f, 1.25f));
            }
            p.Show();
            DocDrag(dt, p, v.designsBar);
            p.transform.SetPositionAndRotation(_docPos, _docRot);
            UpdateButtons(p, dt);
        }

        static void DocTint(UiButton b, Color hue)
        {
            b.color = Color.Lerp(hue, Color.white, 0.18f); b.color2 = hue * 0.96f; b.color.a = b.color2.a = 0.98f;
        }

        /// <summary>Is the partner on its way into the PC with this document's file just now?</summary>
        bool DocCarrying(DocItem d)
        {
            var s = _snap != null ? _snap.show : null;
            return _docFake == null ? s != null && s.kind == "file" && s.stage == "going" && !d.delivered : Time.time < _docSheetFakeUntil;
        }

        void DocSendLook(UiButton b, DocItem d, bool sending)
        {
            b.label.text = sending ? "On its way ..." : d.delivered ? "On your PC  ✓" : "Send to PC";
            DocTint(b, sending ? Ui.Pal.Orange : Ui.Pal.Mint);
            bool can = d.status == "ready";
            if (b.gameObject.activeSelf != can) b.gameObject.SetActive(can);
        }

        // ------------------------------------------------------------------ moving the board, leafing by hand
        /// <summary>Where a hand's pointing ray meets the sheet of a board, in the board's own measure (false: it does not).</summary>
        static bool DocRayOn(UiPanel p, in HandState hs, out Vector3 local)
        {
            local = default;
            Transform pt = p.transform;
            float den = Vector3.Dot(hs.rayDir, pt.forward);
            if (den < 0.1f) return false;
            float along = Vector3.Dot(pt.position - hs.rayOrigin, pt.forward) / den;
            if (along <= 0.02f) return false;
            local = OnBoard(p, hs.rayOrigin + hs.rayDir * along);
            return true;
        }

        /// <summary>
        /// Thumb and finger closed on the top strip (right on it, or with the ray on it) take hold of the board; it goes
        /// with the hand until they open, and keeps facing the user.
        /// </summary>
        void DocDrag(float dt, UiPanel p, FxQuad bar)
        {
            if (!_docDrag)
            {
                if (!p.Shown || p.appear < 1f) return;
                float barY = bar.transform.localPosition.y, halfW = p.width * 0.5f - 0.050f, halfH = _docs.barH * 0.5f + 0.012f;      // (not the corner with the button that puts it away)
                for (int h = 0; h < 2; h++)
                {
                    var hs = UiHand(h);
                    if (!hs.tracked || !hs.pinchDown || HandBusy(h, hs)) continue;
                    Vector3 q = (hs.indexTip + hs.thumbTip) * 0.5f, l = OnBoard(p, q);
                    bool direct = Mathf.Abs(l.z) < 0.04f && l.x > -p.width * 0.5f && l.x < halfW && Mathf.Abs(l.y - barY) < halfH;
                    Vector3 rl = default;
                    bool byRay = !direct && _rayPanel[h] == p && DocRayOn(p, hs, out rl) && rl.x > -p.width * 0.5f && rl.x < halfW && Mathf.Abs(rl.y - barY) < halfH;
                    if (!direct && !byRay) continue;
                    _docDrag = true; _docDragHand = h; _docDragRay = byRay; _docDragLost = 0f; _uiConsumedPinch = true; _docSwipe = false;
                    _docDragReach = byRay ? Vector3.Distance(hs.rayOrigin, _rayAt[h]) : 0f;
                    _docDragOffset = _docPos - (byRay ? _rayAt[h] : q);
                    Sfx("tap", _docPos, 0.4f);
                    Debug.Log($"[MR] docs: the board is taken by its top strip ({(byRay ? "the ray" : "the fingers")})");
                    break;
                }
                return;
            }
            var hd = UiHand(_docDragHand);
            _docDragLost = hd.tracked ? 0f : _docDragLost + dt;
            if (hd.tracked ? !hd.pinch : _docDragLost > 0.3f) { _docDrag = false; Sfx("land", _docPos, 0.3f); Debug.Log($"[MR] docs: the board is let go at {_docPos.ToString("0.00")}"); return; }
            if (hd.tracked)
            {
                Vector3 hold = _docDragRay ? hd.rayOrigin + hd.rayDir * _docDragReach : (hd.indexTip + hd.thumbTip) * 0.5f;
                _docPos = Vector3.Lerp(_docPos, hold + _docDragOffset, 1f - Mathf.Exp(-dt * 18f));
                Vector3 to = _docPos - Input.HeadPos;
                if (to.sqrMagnitude > 0.01f) _docRot = Quaternion.Slerp(_docRot, Quaternion.LookRotation(to.normalized, Vector3.up), 1f - Mathf.Exp(-dt * 12f));
            }
            foreach (var b in p.buttons) b.cool = Mathf.Max(b.cool, 0.3f);      // nothing is pressed while the board is being moved
        }

        /// <summary>A pinch on the page drawn sideways by 7 cm turns it: to the left the next page, to the right the one before.</summary>
        void DocSwipe(float dt, DocItem d)
        {
            var v = _docs; var p = v.report;
            if (_docDrag || d.pages <= 1 || !p.Shown || p.appear < 1f) { _docSwipe = false; _docSwipeDx = Mathf.MoveTowards(_docSwipeDx, 0f, dt * 0.6f); return; }
            if (!_docSwipe)
            {
                _docSwipeDx = Mathf.MoveTowards(_docSwipeDx, 0f, dt * 0.6f);
                for (int h = 0; h < 2; h++)
                {
                    var hs = UiHand(h);
                    if (!hs.tracked || !hs.pinchDown || HandBusy(h, hs)) continue;
                    Vector3 l = OnBoard(p, (hs.indexTip + hs.thumbTip) * 0.5f);
                    bool direct = Mathf.Abs(l.z) < 0.04f;
                    if (!direct && !(_rayPanel[h] == p && DocRayOn(p, hs, out l))) continue;
                    if (Mathf.Abs(l.x) > v.pageW * 0.5f || Mathf.Abs(l.y - v.pageY) > v.pageH * 0.5f) continue;
                    _docSwipe = true; _docSwipeHand = h; _docSwipeRay = !direct; _docSwipeX0 = l.x; _docSwipeDx = 0f; _uiConsumedPinch = true;
                    break;
                }
                return;
            }
            var hd = UiHand(_docSwipeHand);
            if (!hd.tracked || !hd.pinch) { _docSwipe = false; return; }
            Vector3 now;
            if (_docSwipeRay) { if (!DocRayOn(p, hd, out now)) return; }
            else now = OnBoard(p, (hd.indexTip + hd.thumbTip) * 0.5f);
            _docSwipeDx = now.x - _docSwipeX0;
            if (Mathf.Abs(_docSwipeDx) > 0.07f)
            {
                DocTurn(_docSwipeDx < 0f ? +1 : -1, "a pinch drawn across the page");
                _docSwipe = false;      // (one page a pinch)
            }
        }

        // ------------------------------------------------------------------ the strip while it works
        void UpdateDocStrip(float dt, DocsView view, DocItem open)
        {
            var v = _docs; var p = v.strip;
            DocItem making = null;
            if (!_calibrating && view.items != null)
                for (int i = view.items.Count - 1; i >= 0; i--) { var d = view.items[i]; if (d != null && d.status == "making") { making = d; break; } }
            if (making != null && making == open) making = null;      // (its board says how far it is)
            // (the work queue says it: ONE strip for everything that is being worked on, and that one can be taken and put
            //  anywhere - MrWorkStrip. This strip is only still shown when the PC lists no job for the document.)
            if (making != null) { var jobs = WorkJobs; for (int i = 0; i < jobs.Count; i++) if (WorkOpen(jobs[i])) { making = null; break; } }
            if (making == null) { p.Hide(); _docStripFor = ""; return; }

            // beside the partner (to the user's right of it) while it is out in the room, over its spot while it is in the PC
            Vector3 head = Input.HeadPos, fwd = Input.HeadRot * Vector3.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            Vector3 at;
            if (_ch.owner == Env.Mr && _rig.Visible)
            {
                Vector3 away = _rig.HeadWorld - head; away.y = 0f; away = away.sqrMagnitude > 1e-4f ? away.normalized : fwd;
                at = _rig.HeadWorld + Vector3.Cross(Vector3.up, away) * (p.width * 0.5f + 0.062f) + Vector3.up * 0.012f;
            }
            else if (_homeSet) at = new Vector3(_home.x, _deskY + 0.15f, _home.z);
            else at = head + fwd * 0.45f + Vector3.down * 0.16f;
            if (_docStripFor != making.id)
            {
                _docStripFor = making.id; _docStripPos = at; _docStripShown = "";
                p.Hide();
                Debug.Log($"[MR] docs: '{making.title}' ({making.kind}) is being made - the strip is up at {at.ToString("0.00")}");
            }
            _docStripPos = Vector3.Lerp(_docStripPos, at, 1f - Mathf.Exp(-dt * 5f));
            string key = making.title + "/" + making.stage + "/" + making.kind;
            if (key != _docStripShown)
            {
                _docStripShown = key;
                FitInto(v.stripTitle, string.IsNullOrEmpty(making.title) ? "Working on it" : making.title, v.stripBarW);
                FitInto(v.stripStage, making.stage ?? "", v.stripBarW);
                v.stripIcon.SetTexture(making.kind == "designs" ? v.texPic : v.texPdf);
            }
            // the bar: as far as the PC says - or, while it says nothing, a piece of it going to and fro
            float full = v.stripBarW, w, x0 = v.stripBarBg.transform.localPosition.x - full * 0.5f, x;
            if (making.progress > 0.005f) { w = full * Mathf.Clamp(making.progress, 0.04f, 1f); x = x0 + w * 0.5f; }
            else { w = full * 0.28f; x = x0 + w * 0.5f + (full - w) * (0.5f + 0.5f * Mathf.Sin(Time.time * 2.2f)); }
            Ui.SizePanel(v.stripBar, w, 0.0070f, 0.0035f);
            var bp = v.stripBar.transform.localPosition; v.stripBar.transform.localPosition = new Vector3(x, bp.y, bp.z);
            p.Show();
            FaceUser(p.transform, _docStripPos + Vector3.up * (Mathf.Sin(Time.time * 1.3f) * 0.002f));
            p.Animate(dt, Time.time);
        }

        // ------------------------------------------------------------------ the sheet over its head on the way into the screen
        void UpdateDocSheet(float dt)
        {
            var v = _docs;
            if (v.sheetPic == null) return;      // (a scene authored before the sheet had its picture: LPSceneBuilder.BuildAll has not run)
            var s = _snap != null ? _snap.show : null;
            bool fake = Time.time < _docSheetFakeUntil;
            bool on = (fake || (s != null && s.kind == "file" && s.stage == "going")) && _ch.owner == Env.Mr && _rig.Visible;
            Vector3 at = default;
            if (on)
            {
                at = _rig.HeadTopWorld + Vector3.up * (0.030f + 0.0035f * Mathf.Sin(Time.time * 5.5f));
                // gone as soon as it is through the glass: the PC draws it from there
                foreach (var m in _mons.Values)
                {
                    Vector3 rel = at - m.Origin;
                    if (Vector3.Dot(rel, m.Z) > -0.004f && Mathf.Abs(Vector3.Dot(rel, m.X)) < m.frame.widthM * 0.5f + 0.03f && Mathf.Abs(Vector3.Dot(rel, m.Y)) < m.frame.heightM * 0.5f + 0.03f) { on = false; break; }
                }
            }
            // What it carries is ONE file, and the sheet shows exactly that file: the picture the PC names for it
            // (ShowInfo.icon - the first page of the PDF, or the picture itself), its name under it, and the badge of its
            // kind (PDF / picture) at the corner. ("It carries a PDF into the PC but inside it is a PNG" - 2026-11-18: the
            // sheet used to be a drawn PDF icon whatever was carried.) Until the picture has arrived: the badge alone, large.
            string name = fake ? _docSheetFakeName : s != null ? s.name ?? "" : "";
            string icon = !on ? "" : fake ? _docSheetFakeIcon : s.icon ?? "";
            if (on != _docSheetWas)
            {
                _docSheetWas = on;
                if (on) Debug.Log($"[MR] docs: the partner carries '{name}' (picture '{icon}') to the screen - the sheet is over its head{(fake ? " (made up here)" : "")}");
                else Debug.Log("[MR] docs: the sheet over its head is gone");
            }
            if (on) DocSheetPicture(icon, fake);
            bool pic = on && _docSheetTex != null;
            if (v.sheet.enabled != on) v.sheet.enabled = on;
            if (v.sheetPic.enabled != pic) { v.sheetPic.enabled = pic; v.sheetPaper.enabled = pic; }
            bool named = on && name.Length > 0;
            if (v.sheetNameBg.enabled != named) v.sheetNameBg.enabled = named;
            if (!named) { if (v.sheetName.text.Length > 0) { v.sheetName.text = ""; _docSheetNameShown = ""; } }
            if (!on) return;
            bool png = name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase);
            v.sheet.SetTexture(png ? v.texPic : name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? v.texPdf : v.texBlankPage);
            if (named && name != _docSheetNameShown)
            {
                _docSheetNameShown = name;
                FitInto(v.sheetName, name, 0.120f);
                Ui.SizePanel(v.sheetNameBg, v.sheetName.Width + 0.012f, 0.0135f, 0.0060f, 0.0009f);
            }
            Quaternion rot = Quaternion.LookRotation(at - Input.HeadPos, Vector3.up) * Quaternion.Euler(0f, 0f, 5f * Mathf.Sin(Time.time * 2.7f));
            Vector3 up = rot * Vector3.up, right = rot * Vector3.right, fwd = rot * Vector3.forward;
            const float SheetH = 0.074f, SheetW = 0.062f;
            if (pic)
            {
                // the page as it is: its own proportions, 7.4 cm tall (a wide picture: 7.4 cm wide)
                float aspect = Mathf.Clamp(_docSheetTex.width / (float)Mathf.Max(1, _docSheetTex.height), 0.5f, 2f);
                float h = aspect <= 1f ? SheetH : SheetH / aspect, w = h * aspect;
                Vector3 mid = at + up * (h * 0.5f - 0.020f);
                v.sheetPic.transform.SetPositionAndRotation(mid - fwd * 0.0008f, rot);
                v.sheetPic.transform.localScale = new Vector3(w, h, 1f);
                v.sheetPaper.transform.SetPositionAndRotation(mid, rot);
                Ui.SizePanel(v.sheetPaper, w + 0.006f, h + 0.006f, 0.003f, 0.0010f);
                // the badge of its kind: small, at the lower right corner
                v.sheet.transform.SetPositionAndRotation(mid + right * (w * 0.5f - 0.004f) - up * (h * 0.5f - 0.006f) - fwd * 0.0016f, rot);
                v.sheet.transform.localScale = new Vector3(0.026f, 0.026f, 1f);
                v.sheetName.transform.SetPositionAndRotation(mid - up * (h * 0.5f + 0.0125f) - fwd * 0.0016f, rot);
                v.sheetNameBg.transform.SetPositionAndRotation(mid - up * (h * 0.5f + 0.0125f) - fwd * 0.0008f, rot);
            }
            else
            {
                v.sheet.transform.SetPositionAndRotation(at, rot);
                v.sheet.transform.localScale = new Vector3(SheetW, SheetW, 1f);
                v.sheetName.transform.SetPositionAndRotation(at - up * (SheetW * 0.5f + 0.0100f) - fwd * 0.0016f, rot);
                v.sheetNameBg.transform.SetPositionAndRotation(at - up * (SheetW * 0.5f + 0.0100f) - fwd * 0.0008f, rot);
            }
        }

        Texture2D _docSheetTex; string _docSheetIcon = "", _docSheetNameShown = "", _docSheetFakeIcon = "";

        /// <summary>The picture of the file that is being carried (ShowInfo.icon, a path under the PC's address - or, made up, a file here).</summary>
        void DocSheetPicture(string icon, bool fake)
        {
            if (icon == _docSheetIcon) return;
            _docSheetIcon = icon;
            if (_docSheetTex != null) { Destroy(_docSheetTex); _docSheetTex = null; }
            if (icon.Length == 0) return;
            string url = fake ? new Uri(icon).AbsoluteUri : HostUrl(icon);
            StartCoroutine(FetchTexture(url, true, (tex, kb) =>
            {
                if (icon != _docSheetIcon) { if (tex != null) Destroy(tex); return; }      // (something else is carried by now)
                if (tex == null) { Debug.Log($"[MR] docs: the picture of the carried file ({icon}) could not be fetched - the sheet shows its kind only"); return; }
                tex.name = "Doc sheet"; tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Trilinear;
                _docSheetTex = tex;
                _docs.sheetPic.SetTexture(tex);
                Debug.Log($"[MR] docs: the sheet shows the carried file's own picture ({tex.width}x{tex.height}, {kb} kB)");
            }));
        }

        // ------------------------------------------------------------------ checks from the PC (lpctl debug ...)
        bool OnDebugDocs(DebugCommand d)
        {
            switch (d.cmd)
            {
                case "docFake":
                {
                    string text = (d.text ?? "").Trim(), what = text, dir = "";
                    int colon = text.IndexOf(':');
                    if (colon > 0) { what = text.Substring(0, colon); dir = text.Substring(colon + 1).Trim(); }
                    what = what.Trim().ToLowerInvariant();
                    if (what == "file" || what == "filepic") return true;      // (the PC client's part: Windows/PcDocs.cs)
                    if (what == "sheet")
                    {
                        // (docFake <seconds> 0 0 "sheet:<a picture file>": the sheet with that picture, named after it as a PDF; no file: the kind's badge only)
                        _docSheetFakeUntil = Time.time + (d.value > 0.5f ? d.value : 8f);
                        _docSheetFakeIcon = dir.Length > 0 && File.Exists(dir) ? dir : "";
                        _docSheetFakeName = dir.Length > 0 ? Path.GetFileNameWithoutExtension(dir) + ".pdf" : "Test report.pdf";
                        return true;
                    }
                    if (what != "report" && what != "designs" && what != "drawing" && what != "making")
                    {
                        if (_docFake != null) Debug.Log("[MR] docs: the made-up document is gone");
                        _docFake = null; _docSheetFakeUntil = 0f; _docDbgDist = 0f;
                        return true;
                    }
                    _docFakeDir = dir.Length > 0 ? dir : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "artifacts", "tmp", "d1"));
                    _docDbgDist = d.value;
                    var item = new DocItem { id = "fake-" + what + "-" + Time.frameCount, kind = what == "report" || what == "making" ? "report" : "designs", status = what == "making" || what == "drawing" ? "making" : "ready", madeUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
                    if (what == "report") { item.title = "Test report - mixed reality desks"; item.pages = 2; item.files.Add(new DocFile { name = "Test report.pdf", kind = "pdf" }); }
                    else if (what == "making") { item.title = "Mixed reality desks"; item.stage = "Reading about it ..."; item.progress = 0.4f; }
                    else
                    {
                        item.title = "New icon - four options"; item.asked = "make me a new icon";
                        string[] names = { "Option 1 - round and warm", "Option 2 - leafy", "Option 3 - calm and square", "Option 4 - a star" };
                        int drawn = what == "drawing" ? 2 : 4;
                        for (int i = 0; i < drawn; i++) item.options.Add(new DocOption { name = names[i], picture = "option-" + (i + 1) + ".png" });
                        if (what == "drawing") { item.stage = "Drawing option 3 of 4 ..."; item.progress = 0.5f; }
                        else item.files.Add(new DocFile { name = "New icon - options.pdf", kind = "pdf" });
                    }
                    _docFake = new DocsView { revision = 1, openId = what == "making" ? "" : item.id, page = 1 };
                    _docFake.items.Add(item);
                    Debug.Log($"[MR] docs: a made-up document ({what}) with pictures from {_docFakeDir}{(_docDbgDist > 0.05f ? $", {_docDbgDist:0.00} m straight in front of the eyes" : "")}");
                    return true;
                }
                case "docPage": { var o = DocOpen(); if (o != null) DocTurn(Mathf.RoundToInt(d.value) - _docPage, "the debug command"); return true; }
                case "docPick": DocPick(Mathf.RoundToInt(d.value) - 1); return true;
                case "docSend": { var o = DocOpen(); if (o != null) DocSend(o.kind == "report" ? _docs.send : _docs.designsSend); return true; }
                case "docClose": DocClose(); return true;
            }
            return false;
        }
    }
}
