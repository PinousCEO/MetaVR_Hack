using System;
using System.Collections.Generic;
using System.IO;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;
using V = LivingPartner.MR.MenuBoardView;

namespace LivingPartner.MR
{
    /// <summary>
    /// PLAYING WITHOUT THE PC (the stand-in for the host is LocalWorld; this is what the app itself does about it).
    ///  - GETTING IN. On the headset the PC is looked for as always, for ProbeSeconds. A PC that is found and paired:
    ///    everything as it was. None found (or it asks for its code): a paper card with two buttons instead of the bare
    ///    pairing numpad - "Play now" (the local world) and "Connect my PC" (the pairing as it was, with a small card
    ///    beside it that still offers Play now). Local play starts when the user chooses it.
    ///  - THE DESK. With no screens to go by, the partner's spot is the surface in front of the seated user: the level
    ///    the depth sensor finds there (MrRoom.UpdateDesk, as before). When that finds nothing within a few seconds
    ///    (no access to spatial data), the user is asked once to put a hand flat on the desk: the palm's height is the
    ///    desk, the spot is 35 cm in front of the user. "Find my desk" in Tools asks again.
    ///  - THE FIRST STEPS. No wall of text: one tiny paper hint beside the partner, for the three first actions, one at
    ///    a time - "Look at your open left palm" (the fan), "Tap a sticker to bring it out" (a thing on the desk), "Now tap the
    ///    thing itself" (the partner uses it). Each goes when it is done, the partner cheers, and none ever comes back.
    ///  - A paper tag over the Tools page says "Playing without the PC"; what needs the PC says so in the menu's line.
    ///  - FOR CHECKS WITHOUT A HOST: a script file of debug commands (LocalScript: lines "seconds cmd value px py
    ///    text", fed to the app's own debug handler at those times, the commands lpctl debug sends), and the debug
    ///    commands localPick 1|2 (the card's buttons), localMode 1|0, localSteps n, localForget, localDesk, localInfo, localSave,
    ///    localHands (the line shown while no hand has been seen).
    /// </summary>
    public sealed partial class MrApp
    {
        [Header("Playing without the PC (MrCore/Panels/Welcome, MrCore/Local)")]
        [SerializeField] UiPanel _welcome;
        [SerializeField] UiButton _welcomePlay, _welcomePc;
        [Tooltip("The one tiny hint of the first steps, beside the partner.")]
        [SerializeField] FxQuad _hintBg;
        [SerializeField] StoryLabel _hint;
        [Tooltip("The tag over the Tools page of the fan.")]
        [SerializeField] FxQuad _localTagBg;
        [SerializeField] StoryLabel _localTag;
        [Tooltip("The stickers of the things and games of the local world (Art/Ui/Local): named as LocalWorld names them.")]
        [SerializeField] Texture2D[] _localIcons;

        /// <summary>Set by the platform before Start. OfferLocal: when no paired PC is found, offer to play without one (the headset). LocalAtStart: no PC is looked for at all.</summary>
        [NonSerialized] public bool OfferLocal, LocalAtStart;
        /// <summary>A file of debug commands to feed to the app itself ("" = none), and where the local world is kept ("" = the app's own folder).</summary>
        [NonSerialized] public string LocalScript = "", LocalFile = "";
        /// <summary>The app is served by the stand-in inside it: there is no PC.</summary>
        public bool LocalMode => _link != null && _link.Local;

        const float ProbeSeconds = 6f;
        const string PalmHint = "Look at your open left palm";
        const string HandsLine = "Put the controllers down and hold your hands in front of you.";      // (MrApp.Update: every 20 s while no hand has been seen)
        enum LcState { Off, Probing, Ask, Pc, Local }
        LcState _lc = LcState.Off;
        float _lcT, _lcSince; string _lcStatus; bool _lcHello;
        LocalWorld _local;
        Vector3 _wlPos; bool _wlSet; string _wlShown = "";

        // the first steps: 0 open the hand, 1 tap a sticker, 2 tap the thing, 3 done (kept: they never come back)
        const int FsDone = 3;
        int _fsStep; float _fsAt, _fsQuietUntil;
        string _hintShown = ""; float _hintPop;
        // the desk by hand
        bool _ldAsking, _ldAsked, _ldByMenu; float _ldUntil; readonly float[] _ldHeld = new float[2]; readonly Vector3[] _ldWas = new Vector3[2];

        void InitLocal()
        {
            foreach (var t in _localIcons) if (t != null) _icons[LocalWorld.IconPrefix + t.name + ".png"] = t;
            _welcomePlay.press = () => EnterLocal("Play now");
            _welcomePc.press = () => { _lc = LcState.Pc; _wlShown = ""; Debug.Log("[MR] local: 'Connect my PC' - the pairing as it was"); };
            _welcome.gameObject.SetActive(false);
            _hint.text = ""; _hintBg.enabled = false;
            _localTag.text = ""; _localTagBg.enabled = false;
            _fsStep = Mathf.Clamp(PlayerPrefs.GetInt("lp.firstSteps", 0), 0, FsDone);
            LoadLocalScript();
            if (LocalAtStart) EnterLocal("asked for at the start");
            else if (OfferLocal) _lc = LcState.Probing;
        }

        void EnterLocal(string why)
        {
            if (LocalMode) return;
            string path = !string.IsNullOrEmpty(LocalFile) ? LocalFile : Path.Combine(Application.persistentDataPath, "local-world.json");
            _local = new LocalWorld(path, () => _link.HostNow, (type, body) => _link.DeliverLocal(type, body));
            _local.Status = line => _lcStatus = line;
            _link.GoLocal(_local.Handle);
            _lc = LcState.Local; _lcSince = Time.time; _lcHello = false; _ldAsked = false; _ldAsking = false;
            Debug.Log($"[MR] local: playing without the PC ({why}): {_local.StartNote}; {_local.Line()}; kept in {path}");
        }

        void LeaveLocal(string why)
        {
            if (!LocalMode) return;
            _local.Flush();
            Debug.Log($"[MR] local: back to looking for the PC ({why}); {_local.Line()}");
            _local = null; _snap = null;
            _link.LeaveLocal();      // (the link starts looking for a host on the network again)
            _lc = LcState.Pc; _wlShown = ""; _wlSet = false;
            _hostWasOk = false;      // (asked for: the boards are put away as when a PC goes - MrUi - but "I lost the PC" is not said)
        }

        void OnApplicationPause(bool paused) { if (paused && _local != null) _local.Flush(); }
        void OnApplicationQuit() { if (_local != null) _local.Flush(); }

        /// <summary>Once a frame, before the link is pumped: the script's commands that are due, and the local world's own clock (its snapshots).</summary>
        void LocalTick()
        {
            FeedLocalScript();
            if (_lcLeave) { _lcLeave = false; LeaveLocal("the menu"); }      // "Connect PC" in Tools
            if (_local != null && LocalMode) _local.Tick();
        }
        bool _lcLeave;

        // ------------------------------------------------------------------ getting in: "Play now" / "Connect my PC"
        /// <summary>
        /// Called by UpdatePairing (MrUi), with the boards. True: the pairing numpad stays away (the PC is still being
        /// looked for, the card with the two buttons is up, or there is no PC at all).
        /// </summary>
        bool UpdateWelcome(float dt)
        {
            var p = _welcome;
            bool found = _link.Connected && !_link.Local && !_link.NeedsPairing;
            switch (_lc)
            {
                case LcState.Off: return false;
                case LcState.Local: p.Hide(); return true;
                case LcState.Probing:
                    _lcT += dt;
                    if (found) { _lc = LcState.Pc; Debug.Log($"[MR] local: the PC was found after {_lcT:0.0} s - everything as it was"); return false; }
                    if (_link.NeedsPairing || _lcT >= ProbeSeconds)
                    {
                        _lc = LcState.Ask; _wlSet = false; _wlShown = "";
                        Debug.Log($"[MR] local: {(_link.NeedsPairing ? "the PC asks for its code" : $"no PC answered within {ProbeSeconds:0} s")} - offering 'Play now' / 'Connect my PC'");
                    }
                    return true;
                case LcState.Ask:
                    if (found) { _lc = LcState.Pc; p.Hide(); Debug.Log("[MR] local: the PC has come up - everything as it was"); return false; }
                    ShowWelcome(dt, true);
                    return true;
                default:
                    if (!_link.Connected && _snap == null)
                    {
                        _lc = LcState.Probing; _lcT = 0f;
                    }
                    if (!_link.Connected || !_link.NeedsPairing) { p.Hide(); return false; }
                    ShowWelcome(dt, false);
                    return false;
            }
        }

        void ShowWelcome(float dt, bool full)
        {
            var p = _welcome;
            string key = full ? "full" : "side";
            if (key != _wlShown)
            {
                _wlShown = key;
                p.title.text = full ? "밤비와 어떻게 놀까요?" : "컴퓨터 연결하기";
                p.body.SetWrapped(full ? "Mac이나 Windows에서 밤비를 실행하세요. 헤드셋만으로도 놀 수 있어요."
                                       : "컴퓨터와 Quest를 같은 Wi-Fi에 연결한 뒤 밤비를 실행하세요.", p.width - 0.040f);
                if (_welcomePc.gameObject.activeSelf != full) _welcomePc.gameObject.SetActive(full);
                // (the side card: its few words above its one button)
                p.body.transform.localPosition = new Vector3(0f, full ? -0.046f : 0.050f, -0.001f);
                _welcomePlay.transform.localPosition = new Vector3(0f, full ? 0.024f : -0.030f, 0f); _welcomePlay.homeSet = false;
            }
            // In front of the user, a little below the eyes. It stays where it is (a board that follows every turn of the
            // head cannot be pressed calmly) and comes after the user only when they have turned or moved away from it.
            Vector3 head = Input.HeadPos, flat = Input.HeadRot * Vector3.forward; flat.y = 0f;
            flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, flat);
            Vector3 want = full ? head + flat * 0.50f + Vector3.down * 0.07f : head + flat * 0.47f + side * 0.27f + Vector3.down * 0.08f;
            if (Input.Simulated) want = head + Input.HeadRot * (full ? new Vector3(0f, 0f, 0.45f) : new Vector3(0.27f, -0.08f, 0.48f));      // (in the middle of the simulator's picture)
            if (!_wlSet || !p.Shown) { _wlSet = true; _wlPos = want; }
            else if (!full || (_wlPos - want).magnitude > 0.22f || _wlFollow)
            {
                _wlFollow = (_wlPos - want).magnitude > 0.03f;
                _wlPos = Vector3.Lerp(_wlPos, want, 1f - Mathf.Exp(-dt * (full ? 3.5f : 12f)));
            }
            p.Show();
            FaceUser(p.transform, _wlPos);
            UpdateButtons(p, dt);
        }
        bool _wlFollow;

        // ------------------------------------------------------------------ the menu in local mode
        /// <summary>A sticker of Bambi / Tools was pressed (MrMenuBoard.MenuPressAct). True: there is no PC, and this is what it does then.</summary>
        bool LocalMenuAct(int act, Vector3 at)
        {
            if (!LocalMode) return false;
            switch (act)
            {
                case ActGoIn: case ActComeOut: MenuSay("Stepping into your screen needs the PC app"); return true;
                case ActLook: MenuSay("Reading your screen needs the PC app"); return true;
                case ActMarket: MenuSay(LocalWorld.NeedsPc); return true;
                case ActScreen:      // (here it is labelled "Connect PC": out of the local world, into the pairing as it was - the card beside it still offers Play now)
                    _lcLeave = true;      // (at the start of the next frame - LocalTick: not from inside the fan's own buttons)
                    return true;
                case ActFind: LocalFindDesk("the menu"); MenuSay("Put your hand flat on the desk"); MenuComet(at); return true;
            }
            return false;
        }

        /// <summary>The partner was pinched to talk (MrVoice.ToggleTalk): said once in a sitting - the same pinch also picks it up.</summary>
        void LocalNoTalk()
        {
            if (_lcTalkSaid) return;
            _lcTalkSaid = true;
            Caption("I can talk when the PC app is on. Out here, tap my things!", 3.5f);
        }
        bool _lcTalkSaid;

        // ------------------------------------------------------------------ the desk, by hand
        void LocalFindDesk(string how)
        {
            _ldAsking = true; _ldAsked = true; _ldByMenu = how != "sensing"; _ldUntil = Time.time + 25f; _ldHeld[0] = _ldHeld[1] = 0f;
            Debug.Log($"[MR] local: asking for the desk - a hand laid flat on it ({how})");
        }

        void UpdateLocalDesk(float dt, bool ready)
        {
            if (!LocalMode) { _ldAsking = false; return; }
            // no surface was sensed in front of the user (no access to spatial data, or nothing there): asked for once
            if (ready && !_ldAsked && !Input.Simulated && !_deskSensed && !_hasDesk && Time.time - _lcSince > 7f) { LocalFindDesk("sensing"); Debug.Log("[MR] local: no surface was sensed in front of the user within 7 s"); }
            if (!_ldAsking) return;
            _pokeAt = Time.time;      // (the hand that is laid on the desk does not also press a thing that stands there)
            if (Time.time > _ldUntil || (_deskSensed && !_hasDesk && !_ldByMenu))
            {
                _ldAsking = false; _fsAt = Time.time;
                Debug.Log($"[MR] local: {(_deskSensed ? "the desk was sensed meanwhile" : "no flat hand was seen")} - the desk is {Input.HeadPos.y - _deskY:0.00} m below the eyes ({(_deskSensed ? "sensed" : "an estimate")})");
                return;
            }
            Vector3 head = Input.HeadPos, look = Input.HeadRot * Vector3.forward;
            for (int h = 0; h < 2; h++)
            {
                // a hand that lies flat, palm down, below the eyes, within reach, where the user is looking (a hand resting in the
                // lap while they look ahead is not it), and stays within 2 cm of where it was laid for a second
                var hs = h == 0 ? Input.Right : Input.Left;
                Vector3 d = hs.palmPos - head; float below = -d.y, away = new Vector2(d.x, d.z).magnitude;
                bool flatHand = hs.tracked && Vector3.Dot(hs.palmNormal, Vector3.down) > 0.80f && below > 0.12f && below < 1.10f && away > 0.10f && away < 0.80f && Vector3.Angle(look, d) < 50f;
                if (!flatHand || (hs.palmPos - _ldWas[h]).magnitude > 0.02f) { _ldWas[h] = hs.palmPos; _ldHeld[h] = 0f; continue; }
                _ldHeld[h] += dt;
                if (_ldHeld[h] < 1.0f) continue;
                // the palm joint is inside the hand: the desk is a finger's thickness below it
                float y = hs.palmPos.y - 0.015f;
                Vector3 fwd = Input.HeadRot * Vector3.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
                Vector3 spot = head + fwd * 0.35f; spot.y = y;
                _deskY = y; _deskTarget = y; _deskSensed = true; _hasDesk = true; _deskHome = spot; _deskHomeValid = true; _deskHits.Clear();
                _homeSet = false;      // the partner stands there from now on, its things laid out round it
                _ldAsking = false; _fsAt = Time.time;
                Vector3 right = Vector3.Cross(Vector3.up, fwd);
                _sparks.Burst(new Vector3(hs.palmPos.x, y + 0.01f, hs.palmPos.z), right, Vector3.up, 0.06f, 14);
                Sfx("sparkle", spot, 0.5f);
                Caption("That's our desk!", 2.5f);
                _fsQuietUntil = Time.time + 2.2f;
                Debug.Log($"[MR] local: the desk is where the {(h == 0 ? "right" : "left")} hand lay flat: {head.y - y:0.00} m below the eyes (y={y:0.000}); the partner's spot is 0.35 m in front of the user, at {spot.ToString("0.00")}");
                return;
            }
        }

        // ------------------------------------------------------------------ the first steps, the tag, the hint
        void LocalCheer()
        {
            if (_ch.owner != Env.Mr) return;
            _emoteForced = Emote.Happy; _emoteT = 1.5f;
            if (_rig.Visible) _sparks.Burst(_rig.HeadTopWorld + Vector3.up * 0.02f, Input.HeadRot * Vector3.right, Vector3.up, 0.04f, 10);
            Sfx("sparkle", _pos + Vector3.up * _H, 0.45f);
        }

        void FirstStep(int step, string what)
        {
            _fsStep = step; _fsAt = Time.time; _fsQuietUntil = Time.time + 1.6f;
            PlayerPrefs.SetInt("lp.firstSteps", step); PlayerPrefs.Save();
            LocalCheer();
            if (step >= FsDone) Caption("Yay!", 1.6f);
            Debug.Log($"[MR] local: first steps - {what}; {(step >= FsDone ? "all three are done: nothing more is explained" : "next: step " + (step + 1) + " of 3")}");
        }

        /// <summary>Once a frame, after the boards (Update): the market's line, the tag over Tools, the desk by hand, the first steps and their one hint.</summary>
        void UpdateLocalUi(float dt, bool hostOk, bool show)
        {
            if (_lcStatus != null) { _shopOpen = false; MenuSay(_lcStatus); _lcStatus = null; }      // (the marketplace was asked for some other way than its sticker)
            bool local = LocalMode && hostOk;
            bool ready = local && _introDone && show && _ch.owner == Env.Mr;

            // the tag over the Tools page
            bool tag = local && _mbOpen && _mbTab == 2 && _menu.panel.Shown && _mbOpenT > 0.30f;
            if (_localTagBg.enabled != tag) { _localTagBg.enabled = tag; _localTag.text = tag ? "Playing without the PC" : ""; }
            if (tag)
            {
                Transform pt = _menu.panel.transform;
                Vector3 at = pt.TransformPoint(new Vector3(0f, V.CarHeaderY + 0.030f, -0.004f));
                _localTag.transform.SetPositionAndRotation(at, pt.rotation);
                _localTagBg.transform.SetPositionAndRotation(at + pt.forward * 0.0015f, pt.rotation);
                Ui.SizePanel(_localTagBg, _localTag.Width + 0.018f, 0.0165f, 0.0082f, 0.0009f);
            }

            UpdateLocalDesk(dt, ready);

            // hello, once per sitting, when the partner is first seen
            if (ready && !_lcHello && Time.time - _lcSince > 0.6f)
            {
                _lcHello = true;
                if (_mode == Mode.Free) { _emoteForced = Emote.Wave; _emoteT = 2.4f; }
                Caption(_local != null && _local.Restored ? "Welcome back!" : "Hi! I'm Bambi.", 2.6f);
                _fsQuietUntil = Mathf.Max(_fsQuietUntil, Time.time + 2.8f);
            }

            // which hint, if any
            string want = "";
            if (ready && _lcHello && !_calibrating)
            {
                if (_ldAsking) want = "Put your hand flat on the desk";
                else if (_fsStep < FsDone && !GameOn)
                {
                    bool thingOut = false;
                    foreach (var i in World.items) if (i.inRoom && string.IsNullOrEmpty(i.wornOn)) { thingOut = true; break; }
                    if (_fsStep == 0 && _mbOpen) FirstStep(1, "the fan is open");
                    else if (_fsStep == 1 && _mbPressedAt > _fsAt) FirstStep(2, "a sticker was tapped");
                    else if (_fsStep == 2 && _pokeAt > _fsAt) FirstStep(FsDone, "a thing on the desk was tapped");
                    if (Time.time >= _fsQuietUntil)
                        want = _fsStep == 0 ? PalmHint : _fsStep == 1 || (_fsStep == 2 && !thingOut) ? "Tap a sticker to bring it out" : _fsStep == 2 ? "Now tap the thing itself" : "";
                }
            }
            if (want != _hintShown)
            {
                _hintShown = want; _hintPop = 0f;
                if (want.Length > 0) { _hint.text = want; Debug.Log($"[MR] local: hint '{want}'"); }
            }
            bool on = _hintShown.Length > 0;
            if (!on) { if (_hint.text.Length > 0) _hint.text = ""; if (_hintBg.enabled) _hintBg.enabled = false; return; }
            _hintPop = Mathf.Min(1f, _hintPop + dt / 0.30f);
            float pop = Mathf.Max(0.001f, Ui.Pop(_hintPop));
            Vector3 head = Input.HeadPos, fwd = Input.HeadRot * Vector3.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            // beside the partner, on the user's right of it, at the height of its chest; the request for the desk: low in front of the user, where the desk is looked for
            Vector3 toPet = _pos - head; toPet.y = 0f;
            Vector3 right = Vector3.Cross(Vector3.up, toPet.sqrMagnitude > 1e-4f ? toPet.normalized : fwd);
            Vector3 anchor = _ldAsking ? head + fwd * 0.42f + Vector3.down * 0.20f
                           : _pos + right * (0.085f + _hint.Width * 0.5f) + Vector3.up * (_H * 0.62f + 0.004f * Mathf.Sin(Time.time * 2.2f));
            _hint.transform.localScale = Vector3.one * pop;
            Place(_hint, anchor, null);
            if (!_hintBg.enabled) _hintBg.enabled = true;
            _hintBg.transform.SetPositionAndRotation(_hint.transform.position + _hint.transform.forward * 0.002f, _hint.transform.rotation);
            Ui.SizePanel(_hintBg, (_hint.Width + 0.024f) * pop, (_hint.Height + 0.013f) * pop, 0.0105f * pop, 0.0012f);
        }

        // ------------------------------------------------------------------ a script of debug commands (checks without a host)
        struct ScriptLine { public float t; public DebugCommand cmd; }
        readonly List<ScriptLine> _lsLines = new List<ScriptLine>(); int _lsAt; float _lsT0;

        void LoadLocalScript()
        {
            _lsT0 = Time.time;
            if (string.IsNullOrEmpty(LocalScript)) return;
            try
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture; var num = System.Globalization.NumberStyles.Float;
                foreach (string raw in File.ReadAllLines(LocalScript))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#') continue;
                    // seconds cmd [value [px [py [text ...]]]]
                    var parts = line.Split(new[] { ' ', '\t' }, 6, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2 || !float.TryParse(parts[0], num, inv, out float t)) { Debug.LogWarning("[MR] local script: cannot read '" + line + "'"); continue; }
                    var d = new DebugCommand { cmd = parts[1] };
                    if (parts.Length > 2) float.TryParse(parts[2], num, inv, out d.value);
                    if (parts.Length > 3) float.TryParse(parts[3], num, inv, out d.px);
                    if (parts.Length > 4) float.TryParse(parts[4], num, inv, out d.py);
                    if (parts.Length > 5) d.text = parts[5].Trim().Trim('"');
                    _lsLines.Add(new ScriptLine { t = t, cmd = d });
                }
                _lsLines.Sort((a, b) => a.t.CompareTo(b.t));
                Debug.Log($"[MR] local script: {_lsLines.Count} command(s) from {LocalScript}, the last one at {(_lsLines.Count > 0 ? _lsLines[_lsLines.Count - 1].t : 0f):0.0} s");
            }
            catch (Exception e) { Debug.LogWarning("[MR] local script: " + LocalScript + " could not be read: " + e.Message); }
        }

        void FeedLocalScript()
        {
            float t = Time.time - _lsT0;
            while (_lsAt < _lsLines.Count && _lsLines[_lsAt].t <= t)
            {
                var d = _lsLines[_lsAt++].cmd;
                Debug.Log($"[MR] local script {t:0.0} s: {d.cmd} {d.value:0.###} {d.px:0.###} {d.py:0.###} '{d.text}'");
                _link.DeliverLocal(Msg.Debug, d);      // (through the link: the simulator's own commands - simShot, simHand ... - listen there too)
            }
        }

        bool OnDebugLocal(DebugCommand d)
        {
            switch (d.cmd)
            {
                case "localPick":      // the card's buttons: 1 = Play now, 2 = Connect my PC
                {
                    var b = d.value > 1.5f ? _welcomePc : _welcomePlay;
                    bool ok = _welcome.Shown && b.gameObject.activeInHierarchy && PressButton(_welcome, b);
                    Debug.Log($"[MR] local: '{(d.value > 1.5f ? "Connect my PC" : "Play now")}' pressed by a check -> {(ok ? "pressed" : "the card is not up (or not ready)")}");
                    return true;
                }
                case "localMode": if (d.value > 0.5f) EnterLocal("a check"); else LeaveLocal("a check"); return true;
                case "localSteps": _fsStep = Mathf.Clamp((int)d.value, 0, FsDone); _fsAt = Time.time; PlayerPrefs.SetInt("lp.firstSteps", _fsStep); Debug.Log($"[MR] local: the first steps are at {_fsStep} of {FsDone}"); return true;
                case "localForget": PlayerPrefs.DeleteKey("lp.playLocal"); PlayerPrefs.DeleteKey("lp.firstSteps"); _fsStep = 0; _fsAt = Time.time; Debug.Log("[MR] local: forgot that this device played without the PC, and its first steps (as on a first start)"); return true;
                case "localDesk": LocalFindDesk("a check"); return true;
                case "localHands": Caption(HandsLine, d.value > 0.5f ? d.value : 8f); Debug.Log("[MR] local: the line about the hands is shown by a check (the simulator always has hands)"); return true;
                case "localSave": if (_local != null) { _local.Flush(); Debug.Log($"[MR] local: saved ({_local.Saves} so far{(_local.SaveError.Length > 0 ? ", ERROR " + _local.SaveError : "")})"); } return true;
                case "localInfo":
                    Debug.Log($"[MR] local: state {_lc}, mode {(LocalMode ? "LOCAL" : "pc")}, first steps {_fsStep}/{FsDone}, hint '{_hintShown}', desk {Input.HeadPos.y - _deskY:0.00} m below the eyes ({(_hasDesk ? "by hand" : _deskSensed ? "sensed" : "an estimate")}), fps {_fps:0}"
                              + (_local != null ? "; " + _local.Line() : ""));
                    return true;
            }
            return false;
        }
    }
}
