using System;
using System.Collections.Generic;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;
using Pose = LivingPartner.Shared.Pose;
using SN = System.Numerics;

namespace LivingPartner.MR
{
    /// <summary>
    /// Hand interactions and spatial UI of the MR client: hand riding, rain shelter, timer modules, the result of a
    /// change, calibration, pairing. (The menu is MrMenuBoard; how buttons are pressed is MrUiRay.) Every panel, button, rain drop and pooled card is an object under MrCore in the hierarchy,
    /// referenced through the serialized fields below; this code only binds the button actions and drives them.
    /// </summary>
    public sealed partial class MrApp
    {
        [Header("Panels")]
        [SerializeField] MenuBoardView _menu;
        [SerializeField] PairingView _pair;
        [SerializeField] CalibrationView _calib;
        [SerializeField] TaskPanelView _task;
        [Header("Pools")]
        [SerializeField] TimerPanelView[] _timerPool;
        [SerializeField] FxQuad[] _drops;
        [Tooltip("Toon splashes, beads and the water film on the hands (MrCore/Rain/Water).")]
        [SerializeField] WaterFx _water;

        bool _uiConsumedPinch, _calibrating;
        bool _hostWasOk;       // the PC was there in the last frame (so that losing it is said once, and not before it was ever found)
        readonly Dictionary<string, TimerPanelView> _timerPanels = new Dictionary<string, TimerPanelView>();
        string _pairEntry = "";
        float _petT, _startleT, _thanksT, _doneWaveT;
        bool _raining, _sheltered; float _shelterDebounce, _rainExposedT;
        int _calibMon = -1, _calibStep; readonly Vector3[] _calibPts = new Vector3[4]; List<MonitorDesc> _calibList;
        TaskInfo _analysis, _change;
        Vector3 _lastTipL, _lastTipR;
        readonly HashSet<string> _doneAnnounced = new HashSet<string>();
        readonly Vector3[] _handTop = new Vector3[2], _handUp = new Vector3[2], _handFwd = new Vector3[2]; readonly bool[] _handHit = new bool[2];
        readonly List<string> _deadKeys = new List<string>();
        readonly HashSet<string> _aliveKeys = new HashSet<string>();

        /// <summary>Binds what each authored button does. Called once from Start.</summary>
        void InitUi()
        {
            InitMenuBoard();
            InitMarket();
            InitCraft();
            // pairing numpad
            for (int i = 0; i < _pair.digits.Length; i++)
            {
                int digit = i;
                _pair.digits[i].press = () => { if (_pairEntry.Length < 6) _pairEntry += digit; };
            }
            _pair.erase.press = () => { if (_pairEntry.Length > 0) _pairEntry = _pairEntry.Substring(0, _pairEntry.Length - 1); };
            _pair.connect.press = () => { if (_pairEntry.Length == 6) { _link.SubmitPairCode(_pairEntry); _pairEntry = ""; } };

            // calibration guide
            _calib.skip.press = () => NextCalibMonitor(false);
            _calib.finish.press = EndCalibration;

            // change result
            _task.approve.press = () => { if (_change != null) { _link.Send(Msg.TaskCommand, new TaskCommand { cmd = "approvePublish", taskId = _change.taskId }); _taskPanelUntil = Time.time + 20f; } };
            // (Nothing is shown in the room for a review or for notes on a file any more - the partner says what it found,
            // and the PC keeps the document. The result panel below is for a change of code only.)
            _task.preview.press = () => { if (_change != null) _link.Send(Msg.TaskCommand, new TaskCommand { cmd = "preview", taskId = _change.taskId }); };
            _task.close.press = () => _link.Send(Msg.TaskCommand, new TaskCommand { cmd = "clearActivity" });

            // timer modules: each pooled panel acts on the module it currently shows (panel.key)
            foreach (var v in _timerPool)
            {
                var view = v;
                view.toggle.press = () =>
                {
                    var cur = TimerOf(view); if (cur == null) return;
                    string cmd = cur.status == "running" ? "pause" : cur.status == "paused" ? "resume" : "start";
                    _link.Send(Msg.ModuleCommand, new ModuleCommand { cmd = cmd, moduleId = cur.moduleId });
                };
                view.reset.press = () => { var cur = TimerOf(view); if (cur != null) _link.Send(Msg.ModuleCommand, new ModuleCommand { cmd = "reset", moduleId = cur.moduleId }); };
                view.plus.press = () => { var cur = TimerOf(view); if (cur != null) _link.Send(Msg.ModuleCommand, new ModuleCommand { cmd = "setDuration", moduleId = cur.moduleId, durationMs = cur.durationMs + 5 * 60000 }); };
                view.minus.press = () => { var cur = TimerOf(view); if (cur != null && cur.durationMs > 6 * 60000) _link.Send(Msg.ModuleCommand, new ModuleCommand { cmd = "setDuration", moduleId = cur.moduleId, durationMs = cur.durationMs - 5 * 60000 }); };
                view.display.press = () => { var cur = TimerOf(view); if (cur != null) _link.Send(Msg.ModuleCommand, new ModuleCommand { cmd = "setDisplay", moduleId = cur.moduleId, value = cur.display == "ring" ? "digits" : "ring" }); };
                view.panel.gameObject.SetActive(false);
            }

            _pair.panel.gameObject.SetActive(false);
            _calib.panel.gameObject.SetActive(false);
            _task.panel.gameObject.SetActive(false);
            foreach (var d in _drops) d.enabled = false;
        }

        ModuleDef TimerOf(TimerPanelView v) => _snap == null || v.panel.key.Length == 0 ? null : _snap.modules.Find(x => x.moduleId == v.panel.key);

        void FaceUser(Transform t, Vector3 pos)
        {
            Vector3 to = pos - Input.HeadPos;
            t.SetPositionAndRotation(pos, to.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(to.normalized, Vector3.up) : Quaternion.identity);
        }

        // ------------------------------------------------------------------ per-frame UI
        void UpdateUi(float dt, double now, bool hostOk, bool characterVisible)
        {
            _uiConsumedPinch = false;
            UpdateTvRally(dt);
            UiRayBegin(dt);      // which board each hand points at (MrUiRay)
            UpdatePairing(dt);
            if (!hostOk)
            {
                // The PC is gone (or has not been found yet). Nothing below this line runs without it, so what it keeps up
                // would stay as it was, frozen in mid-air, with nothing behind its buttons - the menu, the shop, a screen
                // that was brought over - and a game of the headset's own would go on with no menu to stop it. They are
                // put away (they come back with the PC), and it is said once what happened.
                if (_hostWasOk)
                {
                    _hostWasOk = false;
                    Caption("I lost the PC - is the app still running there?", 6f);
                }
                MenuHostLost();
                _shop.panel.Hide();
                _screenBoard.panel.Hide();
                _made.panel.Hide();
                RecordsHostLost();      // (the record picker and the notes: MrRecords)
                DocsHostLost();         // (the boards of the partner's documents: MrDocs)
                WorkHostLost();         // (the work queue's strip: MrWorkStrip)
                EyesHostLost();         // (a picture the PC asked for is not wanted any more: MrEyes)
                UpdateComet(dt);      // (a shooting star on its way finishes its flight)
                StopGames("the PC is gone", stopTv: false);
                UiRayEnd(dt);
                _lastTipL = Input.Left.indexTip; _lastTipR = Input.Right.indexTip;
                return;
            }
            _hostWasOk = true;
            UpdateMenu(dt);
            UpdateShop(dt);
            UpdateMadeCard(dt);
            UpdateScreenBoard(dt);
            UpdateRecords(dt);      // the record picker over the record player (MrRecords)
            UpdateDocs(dt);         // a report or design options on their board, the strip while one is made (MrDocs)
            UpdateWorkStrip(dt);    // the work queue: what the partner is working on, as cards that can be taken and put anywhere (MrWorkStrip)
            UpdateEyes(dt);         // the one picture of what the user holds up, and the sign "Bambi is looking" (MrEyes)
            UpdateRoomStrip(dt);    // the buttons over a scripted thing that was pressed (MrRoomScript)
            // on a headset nothing can be pointed at until a screen has been calibrated: say how, until it is done
            // no screen is known and looking for them by itself did not work: a line now and then, never a stream of them
            if (!Input.Simulated && _mons.Count == 0 && !_calibrating && _autoFinds >= 2 && Time.time >= _nextCalibHint)
            {
                _nextCalibHint = Time.time + 60f;
                Caption("Say \"find my screens\" while looking at them.", 4f);
            }
            UpdateCalibration(dt);
            UpdateTimers(dt);
            UpdateCards(dt);
            UpdateRain(dt, characterVisible);
            Input.SetCameraEnabled(_settings.cameraEnabled);
            var obs = Input.PollObservation();
            if (obs != null && _settings.cameraEnabled) _link.Send(Msg.Observation, obs);
            UiRayEnd(dt);        // the rays and their cursors, over everything
            _lastTipL = Input.Left.indexTip; _lastTipR = Input.Right.indexTip;
        }

        void OnSnapshotUi(StateSnapshot s)
        {
            _analysis = null; _change = null;
            foreach (var t in s.tasks)
            {
                if ((t.kind == "analysis" || t.kind == "review" || t.kind == "read") && t.status == "ProposalReady") _analysis = t;
                if (t.kind == "change") _change = t;
            }
            OnSnapshotMarket(s);
            if (Input != null && Input.Simulated && !_calibrating) SimAutoCalibrate(s);
        }

        void OnDebugUi(DebugCommand d)
        {
            if (OnDebugMarket(d)) return;
            if (OnDebugCraft(d)) return;
            if (OnDebugHold(d)) return;
            if (OnDebugMenu(d)) return;
            if (d.cmd == "intro") { _introDone = false; _introT = 0f; _introGone = 0f; _introSim = true; return; }      // plays the menu's opening again (captures)
            if (d.cmd == "game") { if (d.text == "stop") { StopRps("the PC"); if (_tn.active || _tnPendingAt >= 0f) ToggleTennis("the PC"); } else StartGame(d.text, "the PC"); return; }
            if (d.cmd == "screensDump") { _dumpShots = Mathf.Clamp((int)d.value, 0, 6); return; }
            if (d.cmd == "calibrate") BeginCalibration(d.value > 0.5f);
            else if (d.cmd == "calibrateTouch") { _touchCalibration = true; BeginCalibration(true); }      // the older way: touching the four corners
            else if (d.cmd == "calibrateSkip" && _calibrating) NextCalibMonitor(false);
            else if (d.cmd == "calibrateEnd" && _calibrating) EndCalibration();
            else if (d.cmd == "rain") SetRain(d.value > 0.5f);
            else if (d.cmd == "select" && _analysis != null && _analysis.modules.Count > 0)
            {
                int i = Mathf.Clamp((int)d.value, 0, _analysis.modules.Count - 1);
                _link.Send(Msg.TaskCommand, new TaskCommand { cmd = "select", taskId = _analysis.taskId, moduleId = _analysis.modules[i].moduleId });
            }
        }

        void OnReturnedToMr()
        {
        }

        Emote EmoteForState()
        {
            if (_startleT > 0) return Emote.Startle;
            if (_emoteT > 0) return _emoteForced;      // a game, or a little show asked for from the menu
            if (TalkLook.Active(_ch.talk)) return TalkLook.Emote(_ch.talk);       // a conversation comes before the weather
            if (_raining && !_sheltered) return _rainExposedT < 1.2f ? Emote.Cower : Emote.Shiver;
            if (_thanksT > 0) return Emote.Thankful;
            if (_petT > 0 || _doneWaveT > 0) return _doneWaveT > 0 ? Emote.Wave : Emote.Happy;
            switch (_ch.activity)
            {
                case "modify": return Emote.Work;       // only while the host reports a real ChangeStarted
                case "analysis": case "test": return Emote.Think;
                case "error": return Emote.Startle;
                case "done": return Emote.Happy;
            }
            return Emote.Idle;
        }

        // ------------------------------------------------------------------ hand riding
        void UpdateHandRide(float dt)
        {
            _petT -= dt; _startleT -= dt; _thanksT -= dt; _doneWaveT -= dt; _emoteT -= dt;
            if (_mode == Mode.OnHand)
            {
                var hs = _rideHand == 0 ? Input.Right : Input.Left;
                Vector3 below = new Vector3(_ridePos.x, _deskY, _ridePos.z);
                if (!hs.tracked)
                {
                    // tracking of the carrying hand dropped: stay exactly where we were for a moment, then step down. Never jump elsewhere.
                    _handLostT += dt;
                    if (_handLostT > 0.7f) { HopDown(below); }
                    return;
                }
                _handLostT = 0;
                if (Vector3.Dot(hs.palmNormal, Vector3.up) < 0.25f) { HopDown(below); return; }   // hand tipped over: slide off to the desk
                Vector3 target = hs.palmPos + hs.palmNormal * 0.010f;
                Vector3 prev = _ridePos;
                _ridePos = Vector3.Lerp(_ridePos, target, 1f - Mathf.Exp(-dt * 28f));
                _palmVel = Vector3.Lerp(_palmVel, (_ridePos - prev) / Mathf.Max(dt, 1e-4f), 0.25f);
                Vector3 up = Vector3.Slerp(Vector3.up, hs.palmNormal, 0.45f).normalized;
                Vector3 toUser = Input.HeadPos - _ridePos; toUser -= up * Vector3.Dot(toUser, up);
                Quaternion want = Quaternion.LookRotation(toUser.sqrMagnitude > 1e-5f ? toUser.normalized : Vector3.forward, up);
                _rideRot = Quaternion.Slerp(_rideRot, want, 1f - Mathf.Exp(-dt * 10f));
                DetectTouch(_rideHand == 0 ? Input.Left : Input.Right, _rideHand == 0 ? _lastTipL : _lastTipR, dt);
                return;
            }
            if (_mode == Mode.Hop || _mode == Mode.Held) return;      // (in the air, or held by a pinch: no hand is an invitation just now)
            // Only the right palm is a perch. The left palm always belongs to the bag.
            var hand = Input.Right;
            Vector3 delta = hand.palmPos - _pos;
            bool offered = hand.tracked && hand.palmUp && _grab < 0 &&
                           new Vector2(delta.x, delta.z).sqrMagnitude < 0.40f * 0.40f &&
                           hand.palmPos.y > _deskY - 0.04f && hand.palmPos.y < _deskY + 0.40f;
            if (!offered || !TransitionIdle) { _handSteadyT = 0; return; }
            float speed = (hand.palmPos - _lastPalm).magnitude / Mathf.Max(dt, 1e-4f);
            _lastPalm = hand.palmPos;
            _handSteadyT = speed < 0.25f ? _handSteadyT + dt : 0f;
            if (_handSteadyT < 0.45f) return;
            // walk to the desk point under the fingertips, then use the fingers as a step up to the palm
            Vector3 fingers = Vector3.Lerp(hand.palmPos, hand.indexTip, 0.75f);
            Vector2 stepPoint = new Vector2(fingers.x, fingers.z) + new Vector2(hand.palmForward.x, hand.palmForward.z).normalized * 0.03f;
            if (Vector2.Distance(_motor.Pos, stepPoint) > 0.025f) { if (!_motor.HasTarget) _motor.MoveTo(stepPoint, false, YawTo(hand.palmPos - _pos)); return; }
            if (!_motor.Arrived) return;
            StartHop(_pos, () => { var g = Input.Right; return Vector3.Lerp(g.palmPos, g.indexTip, 0.6f) + g.palmNormal * 0.010f; }, 0.03f, _rot, () =>
            {
                var g0 = Input.Right;
                if (!g0.tracked || !g0.palmUp) { HopDown(new Vector3(_pos.x, _deskY, _pos.z)); return; }
                StartHop(_pos, () => { var g = Input.Right; return g.palmPos + g.palmNormal * 0.010f; }, 0.015f, _rot, () =>
                {
                    _mode = Mode.OnHand; _rideHand = 0; _ridePos = _pos; _rideRot = _rot; _handLostT = 0; _restT = 0; _handSteadyT = 0;
                    Debug.Log("[MR] partner is on the hand");
                });
            });
        }

        void HopDown(Vector3 below)
        {
            _rideHand = -1; _handLostT = 0;
            StartHop(_pos, () => below, 0.02f, Quaternion.Euler(0, YawTo(Input.HeadPos - below), 0), () => _motor.Teleport(new Vector2(below.x, below.z), YawTo(Input.HeadPos - below)));
        }

        void RidePose(float dt)
        {
            float speed = _palmVel.magnitude;
            _restT = speed < 0.05f ? _restT + dt : 0f;
            Emote e = _startleT > 0 ? Emote.Startle : TalkLook.Active(_ch.talk) ? TalkLook.Emote(_ch.talk) : (_raining && !_sheltered ? Emote.Shiver : _thanksT > 0 ? Emote.Thankful : _petT > 0 ? Emote.Happy : speed > 0.10f ? Emote.Balance : _restT > 4f ? Emote.Sit : Emote.Idle);
            _mixer.Wobble = Mathf.Clamp01(speed / 0.5f) + 0.2f;
            _mixer.Set(e);
            _mixer.Evaluate(Time.time, dt, _pose);
            _pos = _ridePos; _rot = _rideRot;
            LookAtUser(_pose);
        }

        void DetectTouch(HandState other, Vector3 lastTip, float dt)
        {
            if (!other.tracked) return;
            float tipSpeed = (other.indexTip - lastTip).magnitude / Mathf.Max(dt, 1e-4f);
            float dHead = Vector3.Distance(other.indexTip, _rig.HeadWorld);
            if (dHead < 0.035f + 0.21f * _H)
            {
                if (tipSpeed > 0.9f && _startleT <= 0 && _petT <= 0) { _startleT = 0.9f; Sfx("squeak", _rig.HeadWorld, 0.6f); }           // sudden jab: startled
                else if (tipSpeed > 0.03f && tipSpeed < 0.6f) { _petT = 1.4f; }                     // gentle stroke: happy
            }
        }

        // ------------------------------------------------------------------ virtual rain and shelter
        static readonly Vector3 RainDir = new Vector3(0.12f, -1f, 0.05f).normalized;

        void SetRain(bool on)
        {
            _settings.rain = on;
            _link.Send(Msg.Settings, _settings);
        }

        /// <summary>
        /// The surface of a hand that faces the rain (the back of a hand held palm-down, the palm of a hand held palm-up).
        /// top = centre of that surface, up = its normal, fwd = towards the fingers. False when the hand is edge-on to the rain.
        /// </summary>
        static bool HandTop(in HandState hs, out Vector3 top, out Vector3 up, out Vector3 fwd)
        {
            up = Vector3.Dot(hs.palmNormal, RainDir) < 0f ? hs.palmNormal : -hs.palmNormal;
            fwd = hs.palmForward - up * Vector3.Dot(hs.palmForward, up);
            fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.Cross(up, Vector3.right).normalized;
            top = hs.palmPos + up * 0.016f + fwd * 0.025f;          // the palm joint sits inside the hand, a little behind its middle
            return hs.tracked && Vector3.Dot(up, -RainDir) > 0.35f;
        }

        void RespawnDrop(Transform d, Vector3 centre, float minUp, float maxUp)
        {
            Vector2 r = UnityEngine.Random.insideUnitCircle * 0.32f;
            Vector3 p = new Vector3(centre.x + r.x, _deskY + UnityEngine.Random.Range(minUp, maxUp), centre.z + r.y) - RainDir * 0.05f;
            Vector3 toCam = Input.HeadPos - p; toCam -= RainDir * Vector3.Dot(toCam, RainDir);
            d.SetPositionAndRotation(p, Quaternion.LookRotation(-toCam.normalized, -RainDir));
        }

        void UpdateRain(float dt, bool characterVisible)
        {
            bool raining = _settings.rain && characterVisible;
            if (raining != _raining)
            {
                _raining = raining; _rainExposedT = 0;
                if (raining) Caption("Oh, it's raining...", 3f);
                Sfx("drop", _pos + Vector3.up * 0.15f, 0.6f);
                Debug.Log("[MR] rain " + (raining ? "started" : "stopped"));
                for (int i = 0; i < _drops.Length; i++)
                {
                    _drops[i].enabled = raining;
                    if (raining) RespawnDrop(_drops[i].transform, _pos, 0.05f, 0.65f);       // start as a full curtain, not as one sheet
                }
            }

            // hands: where their rain-facing surface is (splashes, the water film and drips are drawn on it)
            Vector3[] handTop = _handTop, handUp = _handUp, handFwd = _handFwd; bool[] handHit = _handHit;
            for (int h = 0; h < 2; h++)
            {
                var hs = h == 0 ? Input.Right : Input.Left;
                handHit[h] = raining && HandTop(hs, out handTop[h], out handUp[h], out handFwd[h]);
                _water.SetHand(h, handHit[h], handTop[h], handUp[h], handFwd[h]);
            }
            _water.Step(dt, Input.HeadPos, Time.time);
            if (!raining) { _sheltered = false; return; }

            // shelter is decided geometrically: the hand must actually block the path of the rain to the head
            Vector3 head = _rig.HeadTopWorld;
            bool sheltered = false;
            for (int h = 0; h < 2; h++)
            {
                var hs = h == 0 ? Input.Right : Input.Left;
                if (!hs.tracked || (h == _rideHand && _mode == Mode.OnHand)) continue;
                if (RainShelter.IsSheltered(Conv.N(head), Conv.N(RainDir), Conv.N(hs.palmPos), Conv.N(hs.palmNormal), 0.062f, 0.30f, out float _, out float _)) sheltered = true;
            }
            if (sheltered != _sheltered)
            {
                _shelterDebounce += dt;
                if (_shelterDebounce > 0.25f)
                {
                    _sheltered = sheltered; _shelterDebounce = 0;
                    Debug.Log("[MR] rain shelter state -> " + (sheltered ? "SHELTERED by hand" : "EXPOSED"));
                    if (sheltered) { _thanksT = 2.4f; Caption("Thank you, I'm staying dry.", 3f); }   // gratitude only on a real change to protected
                    else { _rainExposedT = 0; }
                }
            }
            else _shelterDebounce = 0;
            if (!_sheltered)
            {
                _rainExposedT += dt;
                // look for cover: move under the nearest tracked hand if one is close by
                // (switched off: it ran under any raised hand, also one that was pointing at something for it to look at)
                if (RunsForCover && _mode == Mode.Free && _motor.Arrived && _rainExposedT > 1.5f)
                {
                    for (int h = 0; h < 2; h++)
                    {
                        var hs = h == 0 ? Input.Right : Input.Left;
                        if (!hs.tracked || hs.palmPos.y < head.y) continue;
                        Vector3 under = hs.palmPos + RainDir * ((hs.palmPos.y - _deskY) / Mathf.Max(0.2f, -RainDir.y));
                        if (Vector2.Distance(new Vector2(under.x, under.z), _motor.Pos) is float dd && dd > 0.03f && dd < 0.6f) { _motor.MoveTo(new Vector2(under.x, under.z), true, YawTo(Input.HeadPos - _pos)); break; }
                    }
                }
            }

            // Drops fall along the rain direction around the partner. Each one ends where it really lands: on a hand in its
            // way (the splash is drawn on top of that hand), on the partner's head, or on the desk.
            Vector3 centre = _pos;
            Vector3 headCentre = _rig.HeadWorld; float headR = Skeleton.HeadRadius * _H;
            float fall = 2.4f * dt;
            int frame = Time.frameCount;
            for (int i = 0; i < _drops.Length; i++)
            {
                Transform d = _drops[i].transform;
                Vector3 prev = d.position, p = prev + RainDir * fall;
                bool landed = false;
                for (int h = 0; h < 2 && !landed; h++)
                {
                    if (!handHit[h]) continue;
                    float s0 = Vector3.Dot(prev - handTop[h], handUp[h]), s1 = Vector3.Dot(p - handTop[h], handUp[h]);
                    if (s0 <= 0f || s1 > 0f) continue;                                    // did not pass through the plane of the hand this frame
                    Vector3 hit = Vector3.Lerp(prev, p, s0 / (s0 - s1));
                    Vector3 l = hit - handTop[h];
                    float u = Vector3.Dot(l, Vector3.Cross(handUp[h], handFwd[h])) / 0.050f, v = Vector3.Dot(l, handFwd[h]) / 0.095f;
                    if (u * u + v * v > 1f) continue;                                     // passed beside the hand
                    _water.Splash(hit + handUp[h] * 0.002f, handUp[h], 0.017f, 2, h);      // a small splash where the drop meets the hand; no film of water, no drips
                    landed = true;
                }
                if (!landed && (p - headCentre).sqrMagnitude < headR * headR * 2.2f && p.y > headCentre.y)      // a little generous, so that getting wet reads at this size
                {
                    Vector3 n = (p - headCentre).normalized;
                    _water.Splash(headCentre + n * (headR + 0.002f), n, 0.012f, 3);
                    landed = true;
                }
                if (!landed && p.y < _deskY)
                {
                    if ((i + frame) % 3 == 0) _water.Splash(new Vector3(p.x, _deskY + 0.0015f, p.z), Vector3.up, 0.019f, (i + frame) % 2);
                    landed = true;
                }
                if (landed || (p - centre).sqrMagnitude > 0.5f * 0.5f + 0.6f * 0.6f) { RespawnDrop(d, centre, 0.35f, 0.65f); continue; }
                Vector3 toCam = Input.HeadPos - p; toCam -= RainDir * Vector3.Dot(toCam, RainDir);
                d.SetPositionAndRotation(p, Quaternion.LookRotation(-toCam.normalized, -RainDir));
            }
        }

        // ------------------------------------------------------------------ hand menu
        // ------------------------------------------------------------------ pairing (hand numpad)
        /// <summary>
        /// Where a numpad button currently is in the room (0-9 = digits, 10 = erase, 11 = connect); false while the numpad
        /// is not asking for a code. Used by the simulator's scripted fingertip to type a code the way a hand would.
        /// </summary>
        public bool TryGetPairButton(int index, out Vector3 world)
        {
            world = default;
            if (_link == null || !_link.NeedsPairing || !_pair.panel.Shown || _pair.panel.appear < 1f) return false;
            UiButton b = index >= 0 && index <= 9 ? _pair.digits[index] : index == 10 ? _pair.erase : index == 11 ? _pair.connect : null;
            if (b == null) return false;
            world = b.transform.position;
            return true;
        }

        void UpdatePairing(float dt)
        {
            var p = _pair.panel;
            if (UpdateWelcome(dt) || !_link.Connected || !_link.NeedsPairing) { p.Hide(); return; }
            p.Show();
            FaceUser(p.transform, Input.HeadPos + Input.HeadRot * new Vector3(0, -0.08f, 0.48f));
            p.title.text = "컴퓨터에 표시된 6자리 코드 입력";
            p.body.text = "Code: " + _pairEntry.PadRight(6, '·');
            UpdateButtons(p, dt);
        }

        // ------------------------------------------------------------------ monitor calibration (manual, saved, re-usable)
        void BeginCalibration() => BeginCalibration(false);

        void BeginCalibration(bool evenInSimulator)
        {
            if (_snap == null || _snap.monitors.Count == 0) return;
            if (Input.Simulated && !evenInSimulator) { Caption("The simulator calibrates its virtual layout by itself.", 3f); return; }
            if (!Input.Simulated && !_touchCalibration) { BeginLookCalibration("asked for"); return; }
            _calibrating = true; _lookCal = false; _calibList = new List<MonitorDesc>(_snap.monitors); _calibMon = 0; _calibStep = 0;
            _calibList.Sort((a, b) => (b.primary ? 1 : 0) - (a.primary ? 1 : 0));
            _link.Send(Msg.CalibBegin, new MonitorCalibration { monitorId = _calibList[0].id });
        }

        /// <summary>
        /// The headset was taken off / put back on. A calibration in progress is put aside (the PC takes its corner
        /// markers away) and picked up again at the same corner.
        /// </summary>
        public void OnHeadsetPause(bool paused)
        {
            if (!paused) _roomReadyAt = Mathf.Max(_roomReadyAt, Time.time + 3f);
            // finding the screens with the camera: the colours on the PC go away at once; it starts again by itself later
            if (_lookCal) { if (paused) { _autoFinds = Mathf.Max(0, _autoFinds - 1); _roomReadyAt = Time.time + 3f; FinishFinding(""); } return; }
            if (!_calibrating || _link == null || _calibList == null || _calibMon < 0 || _calibMon >= _calibList.Count) return;
            _link.Send(Msg.CalibBegin, new MonitorCalibration { monitorId = paused ? "" : _calibList[_calibMon].id });
        }

        static readonly string[] CalibNames = { "top-left", "top-right", "bottom-left", "bottom-right", "desk surface" };

        void UpdateCalibration(float dt)
        {
            var p = _calib.panel;
            if (!_calibrating) { p.Hide(); return; }
            if (_lookCal) { UpdateLookCalibration(dt); return; }
            // On the headset the panel with Skip / Finish sits above the LEFT palm, like the hand menu, and only while that
            // palm is turned to the face: a panel floating in front of the user is in the way of the very hand that
            // reaches for the corners of the screen, and that hand pressed Skip on its way (first run on a Quest 3).
            // What to do next is shown as a line of text, which has nothing to press.
            var left = Input.Left;
            Vector3 toHead = (Input.HeadPos - left.palmPos).normalized;
            bool onPalm = !Input.Simulated && left.tracked && Vector3.Dot(left.palmNormal, toHead) > 0.55f;
            if (Input.Simulated) { p.Show(); FaceUser(p.transform, Input.HeadPos + Input.HeadRot * new Vector3(0.0f, -0.16f, 0.45f)); }
            else if (onPalm) { p.Show(); FaceUser(p.transform, left.palmPos + Vector3.up * 0.20f + toHead * 0.03f); }
            else p.Hide();
            var m = _calibList[_calibMon];
            p.title.text = $"Calibrate screen {_calibMon + 1}/{_calibList.Count}: {m.id.Replace("\\\\.\\", "")}";
            p.body.SetWrapped(_calibStep < 4 ? $"On the screen showing white markers, touch the [{CalibNames[_calibStep]}] corner pixel with your index fingertip and pinch. (The lit pixel, not the bezel.)" : "Touch the desk surface with your index fingertip and pinch.", p.width - 0.018f);
            if (p.Shown) UpdateButtons(p, dt, Input.Simulated ? -1 : 1);
            if (_uiConsumedPinch || !_calibrating) return;
            if (!Input.Simulated && !onPalm && Time.time > _calibSaidUntil)
                Caption(_calibStep < 4 ? $"Screen {_calibMon + 1} of {_calibList.Count}, corner {_calibStep + 1} of 4: touch the white marker at the {CalibNames[_calibStep].ToUpperInvariant()} with your index fingertip, then pinch.\n(Left palm up: Skip / Finish)"
                                       : "Last one: touch the DESK with your index fingertip and pinch.", 0.5f);
            // the hand that points: the right one; the left one only while it is not holding the panel up
            var hs = Input.Right.pinchDown ? Input.Right : Input.Left;
            if (!hs.tracked || !hs.pinchDown) return;
            if (!Input.Simulated && onPalm && !Input.Right.pinchDown) return;
            if (!Input.Simulated && Time.time - _calibStepAt < 0.7f) return;           // one pinch, one corner
            _calibStepAt = Time.time;
            Vector3 tip = (hs.indexTip + hs.thumbTip) * 0.5f;
            if (!Input.Simulated)
            {
                Vector3 cr = Input.HeadRot * Vector3.right; cr.y = 0;
                _sparks.Burst(tip, cr.sqrMagnitude > 1e-4f ? cr.normalized : Vector3.right, Vector3.up, 0.03f, 7);
                Caption(_calibStep < 4 ? $"Got the {CalibNames[_calibStep]} corner." : "Got the desk.", 1.0f); _calibSaidUntil = Time.time + 1.0f;
                Debug.Log($"[MR] calibration: {(_calibStep < 4 ? CalibNames[_calibStep] : "desk")} of {m.id} at {tip.ToString("0.000")}");
            }
            if (_calibStep < 4) { _calibPts[_calibStep++] = tip; return; }
            var k = new MonitorCalibration
            {
                monitorId = m.id, tl = Conv.V(_calibPts[0]), tr = Conv.V(_calibPts[1]), bl = Conv.V(_calibPts[2]), br = Conv.V(_calibPts[3]),
                spaceEpoch = Input.SpaceEpoch, hasDesk = true, deskY = tip.y, simulated = Input.Simulated,
            };
            _link.Send(Msg.CalibCommit, k);
            var f = MonitorFrame.FromCalibration(k);
            float res = f.Residual(k.tl, k.tr, k.bl, k.br);
            Caption($"Saved. Error {res * 1000f:0} mm" + (res > 0.012f ? " - that is a bit large. Better try again." : ""), 4f);
            NextCalibMonitor(true);
        }

        void NextCalibMonitor(bool committed)
        {
            if (_lookCal) { LookNext(true); return; }
            _calibMon++; _calibStep = 0;
            if (_calibMon >= _calibList.Count) { EndCalibration(); return; }
            _link.Send(Msg.CalibBegin, new MonitorCalibration { monitorId = _calibList[_calibMon].id });
        }

        void EndCalibration()
        {
            _lookCal = false; _touchCalibration = false;
            _calibrating = false; _calibMon = -1;
            _link.Send(Msg.CalibEnd, new MonitorCalibration());
            _homeSet = false;
        }

        /// <summary>
        /// SIMULATOR ONLY: lays the real Windows displays out in a virtual room (deliberately not coplanar and not in the
        /// Windows arrangement) and commits calibrations flagged simulated = true. These never count as Quest evidence.
        /// </summary>
        void SimAutoCalibrate(StateSnapshot s)
        {
            foreach (var m in s.monitors)
            {
                bool have = false;
                foreach (var c in s.calibrations) if (c.simulated && c.monitorId == m.id && c.pixelW == m.bounds.w && c.pixelH == m.bounds.h) have = true;
                if (have) continue;
                var prim = s.monitors.Find(x => x.primary) ?? s.monitors[0];
                ScreenMath.PhysicalSize(m, null, out float w, out float h, out string _);
                ScreenMath.PhysicalSize(prim, null, out float pw, out float ph, out string _);
                float mpp = pw / prim.bounds.w;
                // centre from the Windows arrangement, then angle side screens inwards and tilt upper screens toward the viewer
                float cx = ((m.bounds.x + m.bounds.w * 0.5f) - (prim.bounds.x + prim.bounds.w * 0.5f)) * mpp;
                float cy = -((m.bounds.y + m.bounds.h * 0.5f) - (prim.bounds.y + prim.bounds.h * 0.5f)) * mpp;
                Vector3 centre = new Vector3(cx * 1.04f, 1.02f + cy * 1.03f, 0.62f);
                float yaw = Mathf.Clamp(cx / 0.5f, -1f, 1f) * 24f, pitch = cy > 0.1f ? 9f : -4f;
                centre.z -= Mathf.Abs(cx) * 0.22f + (cy > 0.1f ? 0.03f : 0f);
                Quaternion rot = Quaternion.Euler(pitch, yaw, 0);
                Vector3 rx = rot * Vector3.right * (w * 0.5f), ry = rot * Vector3.up * (h * 0.5f);
                float lowest = 1.02f - ph * 0.5f;
                var k = new MonitorCalibration
                {
                    monitorId = m.id, tl = Conv.V(centre - rx + ry), tr = Conv.V(centre + rx + ry), bl = Conv.V(centre - rx - ry), br = Conv.V(centre + rx - ry),
                    spaceEpoch = Input.SpaceEpoch, simulated = true, hasDesk = true, deskY = lowest - 0.085f,
                };
                _link.Send(Msg.CalibCommit, k);
            }
        }

        // ------------------------------------------------------------------ timer modules (real stored modules from the host)
        TimerPanelView FreeTimerPanel()
        {
            for (int i = 0; i < _timerPool.Length; i++) if (_timerPool[i].panel.key.Length == 0) return _timerPool[i];
            return null;
        }

        void UpdateTimers(float dt)
        {
            long nowMs = (long)(_link.HostNow * 1000.0);
            _aliveKeys.Clear();
            int slot = 0;
            foreach (var m in _snap.modules)
            {
                if (m.kind != "timer") continue;
                if (!_timerPanels.TryGetValue(m.moduleId, out var v))
                {
                    v = FreeTimerPanel();
                    if (v == null) continue;                       // more timers than authored panels: the extra ones stay on the host only
                    v.panel.key = m.moduleId;
                    v.panel.Show();
                    _timerPanels[m.moduleId] = v;
                    Caption("I put the timer here.", 3f);
                }
                _aliveKeys.Add(m.moduleId);
                var p = v.panel;
                Vector3 baseP = _homeSet ? _home : Input.HeadPos + Input.HeadRot * Vector3.forward * 0.45f;
                Vector3 right = Input.HeadRot * Vector3.right; right.y = 0; right.Normalize();
                Vector3 pos = m.placed && m.spaceEpoch == Input.SpaceEpoch ? Conv.U(m.position) : baseP + right * (0.20f + 0.17f * slot) + Vector3.up * 0.10f;
                slot++;
                FaceUser(p.transform, pos);
                long rem = TimerLogic.Remaining(m, nowMs);
                float frac = m.durationMs > 0 ? 1f - rem / (float)m.durationMs : 0f;
                p.title.text = $"{m.name} · v{m.version}";
                bool digits = m.display == "digits";
                p.body.text = digits ? $"   {TimerLogic.Format(rem)}\n {StatusKo(m.status)}" : $"             {TimerLogic.Format(rem)}\n             {StatusKo(m.status)}";
                p.body.characterSize = (digits ? 0.016f : 0.0105f) * 10f / 64f * 0.72f;
                if (v.ring.enabled == digits) v.ring.enabled = !digits;
                v.ring.SetParam(new Vector4(0.78f, 0.17f, m.status == "idle" ? 1f : 1f - frac, 0));   // what is left, clockwise from the top
                UpdateButtons(p, dt);
                if (m.status == "done" && _doneAnnounced.Add(m.moduleId + ":" + m.version))
                {
                    if (m.alert == "wave") _doneWaveT = 3f;
                    if (m.alert != "silent") { _sparks.Burst(pos, Input.HeadRot * Vector3.right, Vector3.up, 0.03f, 11); Sfx("win", pos, 0.7f); }
                }
            }
            _deadKeys.Clear();
            foreach (var k in _timerPanels.Keys) if (!_aliveKeys.Contains(k)) _deadKeys.Add(k);
            foreach (var k in _deadKeys) { var v = _timerPanels[k]; v.panel.key = ""; v.panel.Hide(); _timerPanels.Remove(k); }
        }

        static string StatusKo(string s) => s == "running" ? "running" : s == "paused" ? "paused" : s == "done" ? "done!" : "ready";

        // ------------------------------------------------------------------ the result of a change
        void UpdateCards(float dt)
        {
            // (An analysis, a review, notes on a file: nothing of those is put up in the room any more. The partner says
            // what it found; the PC has the rest.)
            // result of the selected change: real changed files, test output and the explicit publish approval
            bool showTask = _change != null && _ch.owner == Env.Mr && _change.status != "PublishCompleted" || (_change != null && _change.status == "PublishCompleted" && Time.time < _taskPanelUntil);
            var tp = _task.panel;
            if (!showTask) { tp.Hide(); return; }
            tp.Show();
            Vector3 f2 = Input.HeadRot * Vector3.forward; f2.y = 0; f2.Normalize();
            FaceUser(tp.transform, (_homeSet ? _home : Input.HeadPos + f2 * 0.45f) + Vector3.up * 0.37f);
            tp.title.text = Short(_change.title, 30) + " · " + _change.status + (_change.isTestDouble ? " [TEST DOUBLE]" : "");
            tp.body.SetWrapped(Short(_change.detail, 100) + "\nChanged: " + (_change.changedFiles.Count > 0 ? Short(string.Join(", ", _change.changedFiles), 60) : "-") +
                           "\nBranch: " + (_change.branch.Length > 0 ? _change.branch : "-") + (_change.commit.Length > 0 ? " @" + _change.commit : "") + "\nTests: " + Short(_change.testOutput.Replace("\n", " "), 70), tp.width - 0.018f);
            bool review = _change.status == "ReviewRequired";
            if (_task.approve.gameObject.activeSelf != review) _task.approve.gameObject.SetActive(review);
            UpdateButtons(tp, dt);
        }
        float _taskPanelUntil, _nextCalibHint = 3f, _calibStepAt = -10f, _calibSaidUntil;
        static readonly bool RunsForCover = false;
        bool _touchCalibration;
    }
}
