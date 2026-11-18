using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// Making things, where the user can see it: THE PARTNER HAMMERS AT ITS LITTLE BENCH.
    /// (History: a workbench used to appear on the desk in front of the user - a parcel, a hammer, a ring that filled:
    /// "why is it made in front of me; I cannot move that". Then only light gathered over the partner: "the hammer
    /// animation is cuter. What I meant by light: the object being made already had its final look from the start -
    /// only THAT should be light. Nobody asked for gathering light. And no % is shown." (the user, on the headset,
    /// 2026-11-18). So:)
    ///  - THE BENCH comes up NEXT TO THE PARTNER, where the partner stands when the making starts (beside its own
    ///    spot while it is away) - never in front of the user. The partner turns to it and hammers (PoseLib.Craft):
    ///    sparks and a knock on every blow.
    ///  - THE THING IN THE MAKING is a BALL OF LIGHT on the bench: never its final shape, never a parcel. It grows
    ///    with the progress and jumps under each blow.
    ///  - OVER THE BENCH a paper card says what is being made, HOW FAR IT IS in large figures (with a bar) and, in one
    ///    line, what is being done just now. All of it is read from the WORK QUEUE (Shared/Work.cs, MrWork.cs: the
    ///    open job with where == "mr"); a PC that keeps no queue, and playing without the PC, still say that something
    ///    is being made (WorldState.makingKind, or a thing with the status "making"): then that is what is shown.
    ///    A job that is worked on in the PC's workshop (where == "pc") is not hammered at here.
    ///  - THE BENCH CAN BE TAKEN: thumb and finger closed on it, it goes with the hand (a dashed ring under it says so
    ///    when a hand is near) and stays where it is put down for the rest of the making; the partner walks over.
    ///  - DONE: the light bursts and the real thing stands on the bench - its sticker, for a good second - then it
    ///    flies in an arc to the user's left hand (when that is in view; otherwise to a spot in front of them): into
    ///    the bag. Then the card comes up: "Bambi made this!" - the picture, the name, a price to step up and down,
    ///    "Sell it" / "Keep it". The bench sinks back into the desk.
    /// What was made is NOT left on the desk: it is in the bag, and comes out when its sticker is pressed.
    /// </summary>
    public sealed partial class MrApp
    {
        [Header("Making: the bench beside the partner (MrCore/CraftBench), the ball of light on it (MrCore/Making)")]
        [SerializeField] CraftBenchView _bench;
        [Tooltip("The sparks of light that fly out when the ball bursts.")]
        [SerializeField] FxQuad[] _lightMotes;
        [Tooltip("The ball of light on the bench - the thing in the making - and the soft halo round it.")]
        [SerializeField] FxQuad _lightBall;
        [SerializeField] FxQuad _lightHalo;
        [Tooltip("(Not shown any more: the line under the light is on the bench's card now.)")]
        [SerializeField] FxQuad _lightStripBg;
        [SerializeField] StoryLabel _lightStrip;
        [Tooltip("The sticker of what was made: it stands on the bench, then flies into the bag.")]
        [SerializeField] UiSprite _madeSticker;
        [Header("What it has made")]
        [SerializeField] MadeCardView _made;

        // (Also used by the shows - MrShow: the parcel that is delivered out of a screen, the hammer at the market stall.
        // The pictures have a margin round the sticker. The parcel's quad, how tall the box itself is, and how far below
        // the middle of the picture its lower edge is (as a part of the picture); the hammer's quad, where the hand
        // holds it (from the middle of the picture, as parts of it) and how far its handle leans to the right of upright.)
        const float ParcelQuad = 0.068f, ParcelTall = 0.040f, ParcelFoot = 0.29f;
        const float HammerQuad = 0.048f, HammerLean = 38f;
        static readonly Vector3 HammerGrip = new Vector3(-0.13f, -0.21f, 0f);

        const float BenchBeside = 0.060f;                  // from where the partner stands to the middle of the bench (for a partner of 10 cm): where its hammer comes down
        const float BenchGrab = 0.060f, BenchNear = 0.15f; // thumb and finger closed this near its middle take it; a hand this near shows the ring
        const float LightBurstTime = 0.38f, LightWait = 0.7f, MadeStand = 1.15f, MadeFlyTime = 0.95f, MadeCardTime = 40f;

        // what is being made just now (worked out once a frame by MakingNow)
        bool _mkOn; string _mkName = "", _mkStage = "", _mkPieceId = "", _mkJobId = ""; float _mkProgress;
        bool _mkFake; float _mkFakeProgress; string _mkFakeStage = "";      // lpctl debug making: the bench with nothing really being made
        float _ltShow, _ltProgress, _ltEndT = -1f, _ltBurstT = -1f, _ltGoneAt = -100f, _ltCore = 0.03f, _ltBurstCore = 0.03f;
        Vector3 _ltAt; bool _ltSet, _ltWas;
        // the bench: where it stands (its foot), on which side of the partner (+1: to the user's right of it), how far it is up
        Vector3 _bnPos; bool _bnSet; float _bnSide = 1f, _bnShow, _bnGrip, _bnPulse, _bnStrike, _bnCard;
        int _bnPoseFrame = -10, _bnBlows; Vector2 _bnWalkTo;
        bool _bnDrag, _bnMoved; int _bnDragHand; float _bnDragLost; Vector3 _bnDragOffset;
        int _bnPct = -1; string _bnTitleShown = "\u0001", _bnStepShown = "\u0001"; float _bnCardW = CraftBenchView.CardMinW;

        /// <summary>(MrWorld asks how much a thing on the desk that is being hammered at gives: what is hammered at is the ball of light.)</summary>
        float CraftSquash => 0f;
        /// <summary>(MrHelp asks how much room the making takes above the partner's head: none - it is on the bench beside it.)</summary>
        float LightClear => 0f;
        float BenchScale => Mathf.Clamp(_H / 0.10f, 0.6f, 1.8f);

        /// <summary>Is something being made in the room, what, and how far is it? Called once a frame (UpdateWorld).</summary>
        void MakingNow(WorldState w)
        {
            _mkOn = false; _mkName = ""; _mkStage = ""; _mkProgress = 0f;
            if (_mkFake) { _mkOn = true; _mkProgress = Mathf.Clamp01(_mkFakeProgress); _mkStage = _mkFakeStage; _mkJobId = ""; return; }
            // the work queue: the open job that is hammered in the room. (While the queue has open work that is all in
            // the PC's workshop, nothing is hammered here - whatever else the world says.)
            var jobs = WorkJobs; bool queue = false;
            for (int i = 0; i < jobs.Count; i++)
            {
                var j = jobs[i];
                if (!WorkOpen(j)) continue;
                queue = true;
                if (!WorkJob.AtBench(j)) continue;
                _mkOn = true; _mkName = j.title ?? ""; _mkStage = j.step ?? ""; _mkProgress = Mathf.Clamp01(j.progress); _mkJobId = j.id ?? "";
                return;
            }
            if (queue) return;
            // a PC that keeps no queue (and playing without the PC): the world says that something is being made
            if (WorkJob.IsObject(w.makingKind))
            {
                _mkOn = true; _mkName = w.makingName ?? ""; _mkStage = w.making ?? ""; _mkProgress = Mathf.Clamp01(w.makingProgress); _mkJobId = "";
                return;
            }
            foreach (var d in w.items)
                if (d.status == "making") { _mkOn = true; _mkName = d.name ?? ""; _mkStage = w.making ?? ""; _mkProgress = Mathf.Clamp01(d.progress); _mkJobId = ""; break; }
        }

        /// <summary>
        /// May it hammer at its bench just now? Only while it is out on the desk with nothing else to do: not in a game,
        /// not on a hand or called onto one, not on its way into a screen, not doing what it was ASKED to do.
        /// (The bench stands, and the light on it grows, all the same.)
        /// </summary>
        bool BenchFree => _mkOn && _bnSet && _worldFrame && !_calibrating && _doingSeen.Length == 0 && _game.Length == 0 && !_tn.active && _tnPendingAt < 0f
                          && _ch.owner == Env.Mr && _mode == Mode.Free && TransitionIdle && _handSteadyT < 0.3f;

        /// <summary>
        /// Arms up, looking up, swaying (what it did while light gathered over it; the Editor's pose sheet still draws
        /// it: PartnerPreview "conjure"). reach 0..1: from standing into the pose.
        /// </summary>
        public static void ConjurePose(LivingPartner.Shared.Pose pose, float t, float reach)
        {
            float sway = Mathf.Sin(t * 1.5f), breath = Mathf.Sin(t * 2.9f);
            PoseLib.Idle(pose, t);
            pose[Bone.Spine] = new System.Numerics.Vector3(-5f * reach + 1.5f * breath, 0f, 4.5f * sway * reach);
            pose[Bone.Head] = new System.Numerics.Vector3(-20f * reach, 5f * sway, 3f * sway);
            pose[Bone.ArmUR] = System.Numerics.Vector3.Lerp(pose[Bone.ArmUR], new System.Numerics.Vector3(-30f, 0f, 85f + 9f * sway + 5f * breath), reach);
            pose[Bone.ArmUL] = System.Numerics.Vector3.Lerp(pose[Bone.ArmUL], new System.Numerics.Vector3(-30f, 0f, -85f + 9f * sway - 5f * breath), reach);
            pose[Bone.ArmLR] = new System.Numerics.Vector3(-30f - 8f * breath, 0f, 0f) * reach;
            pose[Bone.ArmLL] = new System.Numerics.Vector3(-30f + 8f * breath, 0f, 0f) * reach;
            pose.HipsOffset.Y += 0.006f * reach * (0.5f + 0.5f * breath);
        }

        /// <summary>To the user's right, seen from the bench (flat).</summary>
        Vector3 BenchRight(out Vector3 toUser)
        {
            toUser = Input.HeadPos - _bnPos; toUser.y = 0f;
            toUser = toUser.sqrMagnitude > 1e-4f ? toUser.normalized : Vector3.back;
            return Vector3.Cross(Vector3.up, -toUser);
        }

        /// <summary>Where the partner stands to hammer at the bench, and which way it is turned there: to the bench, and half to the user, who sees it from the side at its work.</summary>
        Vector3 BenchStand(out float yaw)
        {
            Vector3 right = BenchRight(out Vector3 toUser);
            Vector3 stand = new Vector3(_bnPos.x, _deskY, _bnPos.z) - right * (_bnSide * BenchBeside * BenchScale);
            yaw = YawTo(right * (_bnSide * 0.75f) + toUser * 0.45f);
            return stand;
        }

        /// <summary>
        /// The partner's pose while it makes something (activity "craft" of WorldPose): it walks to its bench when that
        /// is not beside it (the user has put it somewhere else), and hammers there. False: it walks / stands as usual.
        /// </summary>
        bool CraftPose(float dt)
        {
            if (!_mkOn || !_bnSet) return false;                           // (over: UpdateDoing ends the activity)
            if (TalkLook.Active(_ch.talk)) return false;                   // spoken to: it lowers the hammer and listens (the bench stays)
            if (_actFor != "craft") { _actFor = "craft"; _actPhase = 0; _actT = 0f; }
            Vector3 stand = BenchStand(out float yaw);
            var st = new Vector2(stand.x, stand.z);
            float far = Vector2.Distance(new Vector2(_pos.x, _pos.z), st);
            if (_bnDrag)
            {
                // the bench is in the user's hand: it stops and looks after it
                if (_actPhase != 0) { _actPhase = 0; _motor.Stop(); }
                _motor.Face(YawTo(_bnPos - _pos));
                return false;
            }
            if (_actPhase == 0)
            {
                if (far > 0.004f)
                {
                    if (!_motor.HasTarget || Vector2.Distance(_bnWalkTo, st) > 0.01f) { _bnWalkTo = st; _motor.MoveTo(st, far > 0.30f, yaw); }
                    return false;                                          // on its way to the bench
                }
                _actPhase = 1; _actT = 0f; _motor.Stop(); _actYaw = _rot.eulerAngles.y;
            }
            else if (far > 0.03f) { _actPhase = 0; return false; }         // the bench was moved: it goes after it
            float strike = PoseLib.CraftBeat(_actT, out bool pause);
            PoseLib.Craft(_pose, Time.time, strike, pause);
            _actYaw = Mathf.MoveTowardsAngle(_actYaw, yaw, 420f * dt);
            _pos = new Vector3(stand.x, _deskY, stand.z); _rot = Quaternion.Euler(0f, _actYaw, 0f);
            _motor.Teleport(st, _actYaw);
            _bnPoseFrame = Time.frameCount;
            return true;
        }

        static float Hash01(float x) => Mathf.Repeat(Mathf.Sin(x * 12.9898f) * 43758.5453f, 1f);

        /// <summary>Where what was made comes out when there is no bench to stand it on: above the partner - or above the screen it is in - or, when neither is known, in front of the user.</summary>
        Vector3 LightSpot()
        {
            if (_ch.owner == Env.Mr && _rig.Visible) return _rig.HeadTopWorld + Vector3.up * 0.058f;
            var mv = Mon(_snap?.pc?.monitorId) ?? FirstMon();
            return mv != null ? mv.Origin + mv.Y * (mv.frame.heightM * 0.5f + 0.075f) - mv.Z * 0.03f : Input.HeadPos + Input.HeadRot * new Vector3(0f, -0.06f, 0.5f);
        }

        /// <summary>The light bursts: the thing is done.</summary>
        void LightBurst(Vector3 at)
        {
            _ltBurstT = 0f; _ltEndT = -1f; _ltAt = at; _ltSet = true; _ltBurstCore = Mathf.Max(0.03f, _ltCore);
            Vector3 right = Input.HeadRot * Vector3.right;
            _sparks.Burst(at, right, Vector3.up, 0.10f, 18);
            Sfx("sparkle", at, 0.7f); Sfx("pop", at, 0.5f, 0.8f);
        }

        /// <summary>The making starts: the bench comes up beside the partner - on the side where there is more room among the things on the desk.</summary>
        void BenchBegin()
        {
            _ltEndT = -1f; _ltBurstT = -1f; _bnStrike = 0f; _bnBlows = 0; _bnPct = -1;
            if (_bnSet && _bnShow > 0.05f) { Debug.Log("[MR] making: the next thing - the bench is still up, where it stands"); return; }
            Vector3 head = Input.HeadPos;
            bool beside = _ch.owner == Env.Mr && _rig.Visible && _mode == Mode.Free;
            Vector3 flat = Input.HeadRot * Vector3.forward; flat.y = 0f; flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
            Vector3 at = beside ? new Vector3(_pos.x, _deskY, _pos.z) : _homeSet ? new Vector3(_home.x, _deskY, _home.z) : new Vector3(head.x, _deskY, head.z) + flat * 0.45f;
            Vector3 toUser = head - at; toUser.y = 0f; toUser = toUser.sqrMagnitude > 1e-4f ? toUser.normalized : Vector3.back;
            Vector3 right = Vector3.Cross(Vector3.up, -toUser);
            float reach = BenchBeside * BenchScale;
            float Room(Vector3 p)
            {
                float room = 1f;
                foreach (var pr in _props) if (pr.itemId.Length > 0 && pr.Visible) { var d = Item(pr.itemId); if (d != null) room = Mathf.Min(room, Vector2.Distance(new Vector2(pr.shown.x, pr.shown.z), new Vector2(p.x, p.z)) - d.sizeM * 0.5f); }
                return room;
            }
            _bnSide = Room(at + right * reach) + 0.01f >= Room(at - right * reach) ? 1f : -1f;
            _bnPos = at + right * (_bnSide * reach);
            _bnSet = true; _bnShow = 0f; _bnDrag = false; _bnMoved = false; _bnCard = 0f; _ltShow = 0f; _ltProgress = 0f;
            _ltAt = _bnPos + Vector3.up * (CraftBenchView.Top * BenchScale); _ltSet = true;
            Sfx("whoosh", _bnPos, 0.4f);
            _sparks.Burst(_bnPos + Vector3.up * 0.004f, right, Vector3.up, 0.05f, 8);      // out of the desk
            Debug.Log($"[MR] making: the bench comes up at {_bnPos.ToString("0.00")}, to the user's {(_bnSide > 0f ? "right" : "left")} of {(beside ? "the partner" : "the partner's spot (it is not out on the desk just now)")}, {Vector3.Distance(new Vector3(head.x, _deskY, head.z), _bnPos) * 100f:0} cm from the user{(_mkName.Length > 0 ? " ('" + _mkName + "')" : "")}{(_mkJobId.Length > 0 ? " - job " + _mkJobId + " of the work queue" : " (no job in the work queue: the world says so)")}");
        }

        /// <summary>The making is over and nothing was offered within LightWait: what the work queue says came of it.</summary>
        void BenchOver()
        {
            _ltEndT = -1f;
            WorkJob job = null;
            if (_mkJobId.Length > 0) { var jobs = WorkJobs; for (int i = 0; i < jobs.Count; i++) if (jobs[i].id == _mkJobId) job = jobs[i]; }
            if (job != null && (job.status == "failed" || job.status == "cancelled"))
            {
                // nothing came of it: the light goes out quietly
                Sfx("close", _ltAt, 0.4f);
                Debug.Log($"[MR] making: over - the job {job.status} ({job.note}): the light goes out, the bench sinks");
                return;
            }
            var made = job != null && job.status == "done" && job.resultKind == "item" ? Item(job.resultId) : null;
            if (made != null)
            {
                var pic = Icon(made.icon);
                Debug.Log($"[MR] making: over - job {job.id} made '{made.name}': the light bursts and it stands on the bench");
                MadeFlyBegin(pic != null ? pic : _mfAnyTex, pic != null, "", made.name ?? "", 0, false);
                return;
            }
            LightBurst(_ltAt);
            Debug.Log("[MR] making: over - the light bursts (nothing was offered)");
        }

        /// <summary>The bench is taken by thumb and finger closed on it, goes with that hand, and stays where it is put down.</summary>
        void BenchDrag(float dt)
        {
            Vector3 mid = _bnPos + Vector3.up * (CraftBenchView.Top * BenchScale * 0.8f);
            if (!_bnDrag)
            {
                bool near = false;
                if (_bnShow > 0.9f && _ltBurstT < 0f && _mfHold <= 0f)
                    for (int h = 0; h < 2; h++)
                    {
                        var hs = UiHand(h);
                        if (!hs.tracked) continue;
                        Vector3 q = (hs.indexTip + hs.thumbTip) * 0.5f; float d = Vector3.Distance(q, mid);
                        if (d < BenchNear && !hs.palmUp) near = true;
                        if (!hs.pinchDown || d > BenchGrab || _grab >= 0 || HandBusy(h, hs)) continue;
                        _bnDrag = true; _bnDragHand = h; _bnDragLost = 0f; _bnDragOffset = _bnPos - q;
                        Sfx("tap", _bnPos, 0.5f);
                        Debug.Log($"[MR] making: the bench is taken ({(h == 0 ? "right" : "left")} hand)");
                        break;
                    }
                _bnGrip = Mathf.MoveTowards(_bnGrip, near || _bnDrag ? 1f : 0f, dt / 0.2f);
                // (let go in the air, it comes down onto the desk)
                if (_worldFrame && !_bnDrag) _bnPos.y = Mathf.MoveTowards(_bnPos.y, _deskY, dt * 0.9f);
                return;
            }
            _bnGrip = Mathf.MoveTowards(_bnGrip, 1f, dt / 0.2f);
            var hd = UiHand(_bnDragHand);
            _bnDragLost = hd.tracked ? 0f : _bnDragLost + dt;
            bool held = _mode == Mode.Held && _holdHand == _bnDragHand;      // (that pinch took the partner itself)
            if (held || (hd.tracked ? !hd.pinch : _bnDragLost > 0.3f) || _bnShow < 0.5f)
            {
                _bnDrag = false; _bnMoved = true;
                Sfx("land", _bnPos, 0.4f);
                Debug.Log($"[MR] making: the bench is put down at {new Vector3(_bnPos.x, _deskY, _bnPos.z).ToString("0.00")} - it stays there for the rest of the making");
                return;
            }
            if (hd.tracked)
            {
                Vector3 want = (hd.indexTip + hd.thumbTip) * 0.5f + _bnDragOffset;
                if (_worldFrame && want.y < _deskY) want.y = _deskY;
                _bnPos = Vector3.Lerp(_bnPos, want, 1f - Mathf.Exp(-dt * 18f));
            }
        }

        /// <summary>
        /// Once a frame (MrWorld calls it): the bench, the hammer and its blows, the ball of light on the bench, the
        /// card with the percentage, the burst - and the sticker of what was made, standing on the bench and then on
        /// its way into the bag.
        /// </summary>
        void UpdateWorkbench(float dt)
        {
            float t = Time.time; Vector3 head = Input.HeadPos, up = Vector3.up;
            var v = _bench;
            if (v == null) return;
            bool on = _mkOn && !_calibrating && _introDone && _worldFrame;
            if (on && !_ltWas) BenchBegin();
            if (!on && _ltWas && _ltShow > 0.2f && _ltBurstT < 0f) _ltEndT = 0f;      // over: the light waits a moment for what was made, then bursts
            _ltWas = on;
            if (_ltEndT >= 0f) { _ltEndT += dt; if (_ltEndT > LightWait || _calibrating) BenchOver(); }
            bool lit = on || _ltEndT >= 0f;
            _ltShow = _ltBurstT >= 0f ? 0f : Mathf.MoveTowards(_ltShow, lit ? 1f : 0f, dt / 0.45f);
            if (_ltBurstT < 0f) _ltProgress = Mathf.MoveTowards(_ltProgress, _ltEndT >= 0f ? 1f : on ? _mkProgress : 0f, dt * (on || _ltEndT >= 0f ? 0.7f : 3f));
            if (_ltShow > 0.005f) _ltGoneAt = t;
            float p = _ltProgress, k = BenchScale;

            // ---- the bench: up while something is made, while the light bursts, while what was made stands on it
            bool benchUp = _bnSet && (lit || _ltBurstT >= 0f || _mfHold > 0f) && !_calibrating;
            _bnShow = Mathf.MoveTowards(_bnShow, benchUp ? 1f : 0f, dt / (benchUp ? 0.40f : 0.30f));
            if (_bnSet && !benchUp && _bnShow <= 0f) { _bnSet = false; _bnDrag = false; _bnGrip = 0f; }
            bool bench = _bnSet && _bnShow > 0.001f;
            if (v.root.gameObject.activeSelf != bench) { v.root.gameObject.SetActive(bench); v.shadow.enabled = bench; }
            Vector3 right = Vector3.right, toUser = Vector3.back; Quaternion face = Quaternion.identity;
            float rise = 1f;
            if (bench)
            {
                BenchDrag(dt);
                right = BenchRight(out toUser); face = Quaternion.LookRotation(-toUser, up);
                rise = Mathf.Max(0.001f, benchUp ? Ui.Pop(_bnShow) : M.Smooth(_bnShow));
                // (it gives a little under each blow; in the hand it tilts with the way it is carried)
                v.root.SetPositionAndRotation(_bnPos, face);
                v.root.localScale = new Vector3(k * (0.88f + 0.12f * rise) * (1f + 0.05f * _bnPulse), k * rise * (1f - 0.09f * _bnPulse), 1f);
                Vector3 ground = new Vector3(_bnPos.x, _deskY + 0.0015f, _bnPos.z);
                float lifted = Mathf.Clamp01((_bnPos.y - _deskY) / 0.10f);
                v.shadow.transform.SetPositionAndRotation(ground, Quaternion.LookRotation(Vector3.down, -toUser));
                v.shadow.transform.localScale = new Vector3(0.110f, 0.050f, 1f) * (k * (0.9f + 0.5f * lifted));
                v.shadow.SetColor(new Color(0.29f, 0.18f, 0.15f, 0.26f * _bnShow * (1f - 0.5f * lifted)));
                bool grip = _bnGrip > 0.01f;
                if (v.grip.enabled != grip) v.grip.enabled = grip;
                if (grip)
                {
                    float ga = _bnGrip * (_bnDrag ? 0.95f : 0.6f + 0.2f * Mathf.Sin(t * 4f));
                    v.grip.transform.SetPositionAndRotation(ground + up * 0.0006f, Quaternion.Euler(90f, 0f, 0f));
                    v.grip.transform.localScale = Vector3.one * (0.135f * k * (_bnDrag ? 1.08f : 1f));
                    v.grip.SetColors(new Color(1f, 0.78f, 0.34f, ga), new Color(1f, 0.93f, 0.74f, ga));
                    v.grip.SetPhase(t * 0.12f);
                }
            }
            else if (v.grip.enabled) v.grip.enabled = false;

            // ---- the hammer in its right hand, and the blows: sparks and a knock on every one, and the light jumps
            bool hammering = bench && on && _doingNow == "craft" && _actPhase == 1 && Time.frameCount - _bnPoseFrame <= 1 && _rig.Visible;
            if (v.hammer.enabled != hammering) v.hammer.enabled = hammering;
            _bnPulse *= Mathf.Exp(-dt * 9f);
            if (hammering)
            {
                Vector3 hand = _rig.HandRWorld;
                Quaternion r = Quaternion.LookRotation(hand - head, _rig.HandRDir) * Quaternion.Euler(0f, 0f, HammerLean);
                v.hammer.transform.SetPositionAndRotation(hand - r * (HammerGrip * HammerQuad * k), r);
                v.hammer.transform.localScale = Vector3.one * (HammerQuad * k);
                float strike = PoseLib.CraftBeat(_actT, out bool pause);
                if (!pause && _bnStrike < 0.62f && strike >= 0.62f)
                {
                    _bnPulse = 1f; _bnBlows++;
                    _sparks.Burst(_ltAt + up * (_ltCore * 0.35f), right, up, 0.026f, 6);
                    Sfx("hammer", _ltAt, 0.7f, Random.Range(0.92f, 1.06f) + 0.22f * p);      // (the knock rings higher the further the thing is)
                }
                _bnStrike = pause ? 0f : strike;
            }
            else _bnStrike = 0f;

            // ---- the sparks of the burst: all of them fly OUT of the light, and fade (nothing streams in any more)
            int n = _lightMotes.Length;
            bool flying = _ltBurstT >= 0f;
            float ubm = flying ? Mathf.Clamp01(_ltBurstT / LightBurstTime) : 0f;
            for (int i = 0; i < n; i++)
            {
                var q = _lightMotes[i];
                if (q.enabled != flying) q.enabled = flying;
                if (!flying) continue;
                float ba = Hash01(i * 4.19f + 0.3f) * Mathf.PI * 2f, by = Mathf.Lerp(-0.25f, 0.95f, Hash01(i * 6.07f + 2.1f)), bf = Mathf.Sqrt(1f - by * by);
                float outE = 1f - (1f - ubm) * (1f - ubm), reach = 0.07f + 0.15f * Hash01(i * 2.9f + 0.7f);
                Vector3 bp = _ltAt + new Vector3(Mathf.Cos(ba) * bf, by, Mathf.Sin(ba) * bf) * (0.015f + reach * outE);
                if (_worldFrame && bp.y < _deskY + 0.004f) bp.y = _deskY + 0.004f;
                float fade = (1f - ubm) * Mathf.Sqrt(1f - ubm);
                q.transform.SetPositionAndRotation(bp, Quaternion.LookRotation(bp - head, up));
                q.transform.localScale = Vector3.one * ((0.007f + 0.006f * Hash01(i * 5.3f)) * 3.2f * (1f - 0.4f * ubm));
                q.SetColors(new Color(1f, 0.86f, 0.50f, fade), new Color(1f, 0.98f, 0.88f, fade));
            }

            // ---- the ball of light on the bench: THE THING IN THE MAKING. It grows with the progress, jumps under each
            // blow, swells while it waits to burst; then the burst.
            bool burst = _ltBurstT >= 0f;
            if (burst) { _ltBurstT += dt / _mfSlow; if (_ltBurstT >= LightBurstTime) { _ltBurstT = -1f; burst = false; _ltProgress = 0f; } }
            bool ball = (_ltShow > 0.005f && bench) || burst;
            if (_lightBall.enabled != ball) { _lightBall.enabled = ball; _lightHalo.enabled = ball; }
            if (ball)
            {
                float ub = burst ? _ltBurstT / LightBurstTime : 0f, swell = _ltEndT >= 0f ? 1f + 0.45f * Mathf.Clamp01(_ltEndT / 0.5f) : 1f;
                float core = burst ? _ltBurstCore * (1f + 3.2f * (1f - (1f - ub) * (1f - ub)))
                                   : Mathf.Lerp(0.015f, 0.050f, M.Smooth(p)) * k * (1f + 0.05f * Mathf.Sin(t * 3.3f)) * swell * Mathf.Max(0.001f, Ui.Pop(_ltShow));
                if (!burst)
                {
                    _ltCore = core;
                    // it sits on the plank (pressed down a little under a blow, then up again)
                    _ltAt = _bnPos + up * (CraftBenchView.Top * k * rise * (1f - 0.09f * _bnPulse) + core * (0.52f - 0.10f * _bnPulse)); _ltSet = true;
                }
                float a = burst ? (1f - ub) * (1f - ub) : _ltShow, hit = burst ? 0f : _bnPulse;
                Quaternion lf = Quaternion.LookRotation(_ltAt - head, up);
                _lightBall.transform.SetPositionAndRotation(_ltAt, lf);
                _lightBall.transform.localScale = new Vector3(core * 2.3f * (1f + 0.22f * hit), core * 2.3f * (1f - 0.16f * hit), 1f);
                _lightBall.SetColors(new Color(1f, 0.90f + 0.06f * hit, 0.58f + 0.25f * hit, 0.95f * a), new Color(1f, 0.99f, 0.93f, 0.95f * a));
                _lightHalo.transform.SetPositionAndRotation(_ltAt + lf * Vector3.forward * 0.004f, lf);
                _lightHalo.transform.localScale = Vector3.one * (core * (5.2f + 0.5f * Mathf.Sin(t * 2.1f) + 1.6f * hit));
                float ha = (0.20f + 0.26f * p + 0.25f * hit) * a;
                _lightHalo.SetColors(new Color(1f, 0.84f, 0.45f, ha), new Color(1f, 0.95f, 0.78f, ha));
            }

            // ---- the card over the bench: what is being made, how far it is, what is being done just now
            _bnCard = Mathf.MoveTowards(_bnCard, on && bench && _bnShow > 0.6f && !burst ? 1f : 0f, dt / 0.22f);
            bool card = _bnCard > 0.01f && bench;
            if (v.card.gameObject.activeSelf != card) v.card.gameObject.SetActive(card);
            if (card)
            {
                const float H = CraftBenchView.CardH;
                if (on)
                {
                    string title = _mkName.Length > 0 ? _mkName : "Something new";
                    string step = _mkStage.Length > 0 ? _mkStage : "Hammering away ...";
                    bool resize = false;
                    if (title != _bnTitleShown) { _bnTitleShown = title; FitInto(v.title, title, CraftBenchView.CardMaxW - 0.016f); resize = true; }
                    if (step != _bnStepShown) { _bnStepShown = step; FitInto(v.step, step, CraftBenchView.CardMaxW - 0.016f); resize = true; }
                    int pct = Mathf.Clamp(Mathf.RoundToInt(_mkProgress * 100f), 0, 100);
                    if (resize)
                    {
                        _bnCardW = Mathf.Clamp(Mathf.Max(v.title.Width, v.step.Width) + 0.016f, CraftBenchView.CardMinW, CraftBenchView.CardMaxW);
                        Ui.SizePanel(v.cardBg, _bnCardW, H, 0.008f, 0.0011f);
                        Ui.SizePanel(v.barBg, _bnCardW - 0.018f, 0.0044f, 0.0022f, 0f);
                        _bnPct = -1;
                    }
                    if (pct != _bnPct)
                    {
                        _bnPct = pct;
                        v.percent.text = pct + "%";      // (written anew only when the whole number changes)
                        float full = _bnCardW - 0.018f, w = Mathf.Max(0.0044f, full * pct / 100f);
                        Ui.SizePanel(v.barFill, w, 0.0044f, 0.0022f, 0f);
                        v.barFill.transform.localPosition = new Vector3(-full * 0.5f + w * 0.5f, v.barBg.transform.localPosition.y, -0.0006f);
                        if (v.barFill.enabled != pct > 0) v.barFill.enabled = pct > 0;
                    }
                }
                Vector3 at = _bnPos + up * (CraftBenchView.Top * k + 0.058f * k + 0.010f + H * 0.5f);
                FaceUser(v.card, at);
                v.card.localScale = Vector3.one * Mathf.Max(0.001f, Ui.Pop(_bnCard));
                v.percent.transform.localScale = Vector3.one * (1f + 0.10f * _bnPulse);      // the figures jump with the blow
            }
            UpdateMadeFly(dt);
        }

        // ------------------------------------------------------------------ what was made: it stands on the bench, then its sticker flies into the bag
        float _mfT = -1f, _mfHold; Vector3 _mfFrom, _mfTo, _mfFront; string _mfId = "", _mfName = ""; int _mfPrice; bool _mfDebug, _mfHand; float _mfQuad = 0.07f;
        float _mfSlow = 1f;      // (captures from the PC: the burst and the flight drawn out, so that a picture can catch them)
        Texture2D _mfAnyTex;

        /// <summary>The thing is done and offered: the light bursts, it stands on the bench, its sticker sets out for the bag; its card comes up when it has arrived.</summary>
        void MadeFly(MarketListing l, int price, bool debug)
        {
            if ((_mfT >= 0f || _mfHold > 0f) && _mfId.Length == 0 && !debug)
            {
                // (the work queue said "done" first and the thing already stands there: the offer only brings the card)
                _mfId = l.id; _mfPrice = price; _mfDebug = false;
                Debug.Log($"[MR] made: '{l.name}' is offered while it is already on its way into the bag - its card comes up when it has arrived");
                return;
            }
            var tex = ShopIcon(l, out bool own);
            MadeFlyBegin(tex, own, l.id, l.name, price, debug);
        }

        void MadeFlyBegin(Texture2D tex, bool own, string id, string name, int price, bool debug)
        {
            // out of the light on the bench when there is one (or was, a moment ago) - otherwise from over the partner, or the screen it is in
            bool bench = _bnSet && _bnShow > 0.3f;
            bool light = _ltSet && (bench || _ltShow > 0.2f || _ltBurstT >= 0f || Time.time - _ltGoneAt < 1.5f);
            Vector3 from = light ? _ltAt : LightSpot();
            if (_ltBurstT < 0f) LightBurst(from);
            _madeSticker.SetTexture(tex);
            _mfQuad = own ? 0.062f : 0.085f;      // (a picture of its kind has a margin round the badge)
            Vector3 flat = Input.HeadRot * Vector3.forward; flat.y = 0f; flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
            _mfFront = Input.HeadPos + flat * 0.42f + Vector3.down * 0.17f - Vector3.Cross(Vector3.up, flat) * 0.12f;
            _mfFrom = from; _mfTo = _mfFront; _mfHand = false; _mfId = id ?? ""; _mfName = name ?? ""; _mfPrice = price; _mfDebug = debug;
            _mfHold = bench ? MadeStand : 0f; _mfT = bench ? -1f : 0f;
            if (!bench) Sfx("whoosh", from, 0.3f, 1.1f);
            Debug.Log($"[MR] made: '{name}' - the light bursts {(bench ? "on the bench: the thing stands there, then" : light ? "where it was:" : "over the partner (no bench was up):")} its sticker flies into the bag{(debug ? " (a capture: not a real offer)" : "")}");
        }

        void UpdateMadeFly(float dt)
        {
            if (_mfT < 0f && _mfHold <= 0f) { if (_madeSticker.enabled) _madeSticker.enabled = false; return; }
            Vector3 head = Input.HeadPos, right = Input.HeadRot * Vector3.right;
            var flies = _mfId.Length > 0 ? ListingOf(_mfId) : null;
            if (flies != null) { _madeSticker.SetTexture(ShopIcon(flies, out bool ownPic)); _mfQuad = ownPic ? 0.062f : 0.085f; }      // (its own picture may be there a moment after the burst)
            if (_mfHold > 0f)
            {
                // it STANDS ON THE BENCH: grown out of the burst with a bounce, for a good second
                _mfHold -= dt / _mfSlow;
                float su = 1f - Mathf.Clamp01(_mfHold / MadeStand);
                float grow = Mathf.Max(0.001f, 1f - Mathf.Exp(-7.5f * su) * Mathf.Cos(su * 16f));
                Vector3 foot = _bnSet ? _bnPos + Vector3.up * (CraftBenchView.Top * BenchScale) : _mfFrom;
                Vector3 at = foot + Vector3.up * (_mfQuad * 0.40f * grow);
                if (!_madeSticker.enabled) _madeSticker.enabled = true;
                _madeSticker.transform.SetPositionAndRotation(at, Quaternion.LookRotation(at - head, Vector3.up));
                _madeSticker.transform.localScale = Vector3.one * (_mfQuad * grow);
                _madeSticker.SetLook(0.35f * (1f - su), -1f);
                if (su > 0.2f && (Time.frameCount & 15) == 0) _sparks.Burst(at + Vector3.up * (_mfQuad * 0.3f), right, Vector3.up, 0.03f, 1);
                if (_mfHold <= 0f) { _mfHold = 0f; _mfT = 0.24f; _mfFrom = at - Vector3.up * 0.03f; Sfx("whoosh", at, 0.3f, 1.1f); }
                return;
            }
            _mfT += dt / (MadeFlyTime * _mfSlow);
            float u = Mathf.Clamp01(_mfT);
            // to the left palm when that hand is in view, otherwise to a spot in front of the user, low on the left
            var l = Input.Left;
            bool hand = l.tracked && Vector3.Angle(Input.HeadRot * Vector3.forward, l.palmPos - head) < 65f && Vector3.Distance(l.palmPos, head) < 0.9f;
            Vector3 to = hand ? l.palmPos + l.palmNormal * 0.03f : _mfFront;
            _mfTo = _mfT <= dt / (MadeFlyTime * _mfSlow) * 1.5f ? to : Vector3.Lerp(_mfTo, to, 1f - Mathf.Exp(-dt * 14f));
            if (hand) _mfHand = true;
            // it pops out of the burst and hangs there for a moment, then it flies in an arc, and shrinks into the bag
            float e = M.Smooth(Mathf.Clamp01((u - 0.24f) / 0.76f));
            Vector3 start = _mfFrom + Vector3.up * (0.03f * M.Smooth(Mathf.Clamp01(u / 0.24f)));
            Vector3 mid = (start + _mfTo) * 0.5f + Vector3.up * (0.07f + 0.16f * Vector3.Distance(start, _mfTo));
            Vector3 pos = Vector3.Lerp(Vector3.Lerp(start, mid, e), Vector3.Lerp(mid, _mfTo, e), e);
            float size = Mathf.Max(0.001f, Ui.Pop(Mathf.Clamp01(u / 0.2f))) * Mathf.Lerp(1f, 0.30f, M.Smooth(Mathf.Clamp01((u - 0.72f) / 0.28f)));
            if (!_madeSticker.enabled) _madeSticker.enabled = true;
            _madeSticker.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - head, Vector3.up) * Quaternion.Euler(0f, 0f, 14f * Mathf.Sin(u * Mathf.PI * 2f)));
            _madeSticker.transform.localScale = Vector3.one * (_mfQuad * size);
            _madeSticker.SetLook(0.25f * (1f - e), -1f);
            if (u > 0.24f && (Time.frameCount & 3) == 0) _sparks.Burst(pos, right, Vector3.up, 0.012f, 1);       // its tail
            if (_mfT < 1f) return;
            _mfT = -1f; _madeSticker.enabled = false;
            _sparks.Burst(_mfTo, right, Vector3.up, 0.05f, 10);
            Sfx("pop", _mfTo, 0.7f); Sfx("coin", _mfTo, 0.25f, 1.3f);
            var made = _mfId.Length > 0 ? ListingOf(_mfId) : null;
            Debug.Log($"[MR] made: the sticker went into the bag {(_mfHand ? "at the left palm" : "in front of the user (the left hand was not in view)")}");
            MenuTookIn(made != null ? made.name : _mfName);      // (the menu, when it is open: the new sticker is there, and it says so)
            bool partnerOut = _ch.owner == Env.Mr && _rig.Visible;
            if (partnerOut) { _emoteForced = Emote.Happy; _emoteT = 2.5f; }
            if (made == null || (!_mfDebug && (Market.offerId ?? "") != _mfId)) return;      // (not offered, or answered while it flew - by voice: no card)
            ShowMade(made, _mfPrice, _mfDebug);
            Sfx("ta-da", _madeAnchor, 0.8f);
        }

        // ------------------------------------------------------------------ the card: "Bambi made this!"
        string _madeId = "", _madeOfferId = "", _madeShown = ""; int _madeSeq, _madePrice; bool _madeDebug; Vector3 _madeAnchor; float _madeTouchedAt;

        void InitCraft()
        {
            var v = _made;
            v.minus.press = () => MadePriceStep(-5);
            v.plus.press = () => MadePriceStep(+5);
            v.sell.press = () => MadeAnswer(true);
            v.keep.press = () => MadeAnswer(false);
            v.panel.gameObject.SetActive(false);
            foreach (var q in _lightMotes) q.enabled = false;
            _lightBall.enabled = false; _lightHalo.enabled = false; _lightStripBg.enabled = false; _lightStrip.text = ""; _madeSticker.enabled = false;
            _mfAnyTex = _madeSticker.texture;
            var b = _bench;
            if (b == null) { Debug.LogWarning("[MR] making: no bench in this scene (LPSceneBuilder.BuildAll has not run since it was added)"); return; }
            b.root.gameObject.SetActive(false); b.card.gameObject.SetActive(false);
            b.shadow.enabled = false; b.grip.enabled = false; b.hammer.enabled = false;
        }

        MarketListing ListingOf(string id)
        {
            foreach (var l in Market.list) if (l.id == id) return l;
            return null;
        }

        void MadePriceStep(int step) { _madePrice = Mathf.Clamp(_madePrice + step, 0, 995); _madeTouchedAt = Time.time; }

        /// <summary>Brings the card up for this thing, in front of the user and a little to the right, within reach.</summary>
        void ShowMade(MarketListing l, int price, bool debug)
        {
            _madeId = l.id; _madePrice = Mathf.Clamp(price, 0, 995); _madeDebug = debug; _madeShown = ""; _madeTouchedAt = Time.time;
            Vector3 fwd = Input.HeadRot * Vector3.forward; fwd.y = 0; fwd = fwd.sqrMagnitude > 0.01f ? fwd.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            // (placed once: it does not follow the head. Nearer and lower than the shop board, and to the right of its middle.)
            _madeAnchor = Input.Simulated ? Input.HeadPos + Input.HeadRot * new Vector3(0.10f, 0f, 0.62f) : Input.HeadPos + fwd * 0.48f + right * 0.22f + Vector3.down * 0.10f;
            _made.panel.Hide();      // (so that it pops up anew for a new thing)
            Debug.Log($"[MR] made card: '{l.name}' ({l.kind}), suggested price {_madePrice}{(debug ? " - shown for a capture, not a real offer" : "")}");
        }

        void MadeClose(string why)
        {
            if (_madeId.Length == 0) return;
            Debug.Log("[MR] made card: closed - " + why);
            Sfx("close", _madeAnchor, 0.4f);
            _madeId = ""; _madeDebug = false;
            _made.panel.Hide();
        }

        /// <summary>The answer to "put it up in the marketplace?". The card goes at once.</summary>
        void MadeAnswer(bool sell)
        {
            if (_madeId.Length == 0) return;
            Vector3 from = _made.icon.transform.position;
            if (!_madeDebug) SendMarket("offer", _madeId, sell ? "yes" : "no", _madePrice);
            Debug.Log($"[MR] made card: {(sell ? "sell it for " + _madePrice + " stars" : "keep it")}{(_madeDebug ? " (a capture: nothing sent)" : "")} - the card goes");
            if (!sell) Sfx("close", from, 0.4f);
            if (sell)
            {
                Sfx2D("coin", 0.7f);
                MenuComet(from);
                _sparks.Burst(from, _made.panel.transform.right, Vector3.up, 0.07f, 14);
            }
            _madeId = ""; _madeDebug = false;
            _made.panel.Hide();
        }

        void UpdateMadeCard(float dt)
        {
            var m = Market; var v = _made; var p = v.panel;
            string offer = m.offerId ?? "";
            // a new offer: the thing has just been finished - its sticker flies into the bag, then the card comes up
            if (offer.Length > 0 && (m.offerSeq != _madeSeq || offer != _madeOfferId))
            {
                var fresh = ListingOf(offer);
                if (fresh != null)      // (its line of the catalogue may come a snapshot later: then the reveal waits for it)
                {
                    _madeSeq = m.offerSeq; _madeOfferId = offer;
                    if (_madeId.Length > 0) MadeClose("a newer thing was made");
                    if (WorkJob.IsObject(fresh.kind)) { _mfSlow = 1f; MadeFly(fresh, m.offerPrice, false); }
                }
            }
            // the card goes when the offer was answered some other way (by voice, on the PC: the PC has taken the offer
            // back, or the thing is on sale now), and by itself after 40 seconds in which nothing on it was touched
            if (_madeId.Length > 0 && !_madeDebug && offer != _madeId) MadeClose("the offer was answered elsewhere");
            if (_madeId.Length > 0 && Time.time - _madeTouchedAt > MadeCardTime) MadeClose($"{MadeCardTime:0} s without an answer (it stays in the bag)");
            var l = _madeId.Length > 0 ? ListingOf(_madeId) : null;
            if (_madeId.Length > 0 && (l == null || (!_madeDebug && l.listed))) { MadeClose(l == null ? "the thing is gone" : "it is on sale"); l = null; }
            if (_madeId.Length == 0 || _calibrating) { p.Hide(); return; }
            float t = Time.time;
            string key = l.id + "/" + l.name + "/" + l.kind + "/" + _madePrice;
            if (key != _madeShown)
            {
                _madeShown = key;
                FitInto(v.thingName, l.name, 0.26f);
                v.kind.text = KindName(l);
                v.priceLabel.text = _madePrice <= 0 ? "Free" : _madePrice + " stars";
            }
            // its picture: its own on a pastel disc, or the picture of its kind (as on the shop board)
            v.icon.SetTexture(ShopIcon(l, out bool own));
            v.icon.transform.localScale = Vector3.one * (own ? 0.082f : 0.112f) * (1f + 0.025f * Mathf.Sin(t * 2.2f));
            v.icon.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 1.3f) * 4f);
            v.icon.SetLook(0f, Mathf.Repeat(t * 0.35f, 2.8f) < 1f ? Mathf.Repeat(t * 0.35f, 2.8f) : -1f);
            if (v.disc.enabled != own) v.disc.enabled = own;
            if (own) v.disc.SetColors(KindColor(l.kind), KindColor(l.kind));
            p.Show();
            FaceUser(p.transform, _madeAnchor);
            UpdateButtons(p, dt);
        }

        bool OnDebugCraft(DebugCommand d)
        {
            // captures from the PC:
            //   lpctl debug workFake <progress 0..1> 0 0 "mr:Title|step"       a made-up job of the work queue that is hammered here (MrWork)
            //   lpctl debug making 1|0 <progress 0..1> 0 "what is being done"   the bench, with nothing really being made and no queue
            //   lpctl debug craftBench <right m> <away m> 0                    the bench is moved, as if taken and put down: that far to the
            //                                                                  user's right and away from the user (text "hold": it stays in the "hand", 8 cm up, until the next craftBench)
            //   lpctl debug craftInfo                                          one line in the log: where the bench is, what its card says
            //   lpctl debug madeFly [slow]   (slow > 1: that many times slower) the light bursts, the thing stands on the bench, its sticker flies into the bag, the card comes up (not a real offer)
            //   lpctl debug madeCard 1|0 the card alone;  madeCardAge <s>: as if it had been up for s seconds
            if (d.cmd == "making" || d.cmd == "craft")
            {
                _mkFake = d.value > 0.5f; _mkFakeProgress = d.px; _mkFakeStage = d.text ?? "";
                Debug.Log($"[MR] making (made up from the PC): {(_mkFake ? $"on, {_mkFakeProgress * 100f:0} %, '{_mkFakeStage}'" : "off")}");
                return true;
            }
            if (d.cmd == "craftBench")
            {
                if (!_bnSet) { Debug.Log("[MR] making: no bench is up - nothing to move"); return true; }
                Vector3 right = BenchRight(out Vector3 toUser);
                Vector3 was = _bnPos;
                _bnPos += right * d.value - toUser * d.px;
                bool hold = d.text == "hold";
                _bnPos.y = _deskY + (hold ? 0.08f : 0f);
                _bnDrag = hold; _bnDragHand = 0; _bnDragLost = -1e6f; _bnDragOffset = _bnPos - (UiHand(0).indexTip + UiHand(0).thumbTip) * 0.5f; _bnMoved = true;
                // (held: until the next craftBench - or at once, when the simulator shows a right hand that is not pinching)
                Debug.Log($"[MR] making: the bench is moved (from the PC) from {was.ToString("0.00")} to {_bnPos.ToString("0.00")}{(hold ? " and held 8 cm up" : " and put down")} - the partner {(_ch.owner == Env.Mr && _rig.Visible ? $"is {Vector3.Distance(_pos, BenchStand(out _)) * 100f:0} cm from where it will hammer" : "is not out on the desk")}");
                return true;
            }
            if (d.cmd == "craftInfo")
            {
                Debug.Log($"[MR] making: {(_mkOn ? "ON" : "off")} '{_mkName}' {_mkProgress * 100f:0} % '{_mkStage}' job '{_mkJobId}'; bench {(_bnSet ? $"up {_bnShow:0.00} at {_bnPos.ToString("0.00")} (side {_bnSide:+0;-0}, moved by the user: {_bnMoved}, in a hand: {_bnDrag})" : "down")}; card {_bnCard:0.00} shows '{_bnTitleShown}' {_bnPct} % '{_bnStepShown}'; light {_ltShow:0.00} core {_ltCore * 1000f:0} mm at {_ltAt.ToString("0.00")}; partner doing '{_doingNow}' phase {_actPhase}, {(_bnSet ? Vector3.Distance(_pos, BenchStand(out _)) * 100f : 0f):0} cm from its place at the bench, {_bnBlows} blows; hammer {(_bench.hammer.enabled ? "in its hand" : "away")}");
                return true;
            }
            if (d.cmd == "madeCard" || d.cmd == "madeFly")
            {
                if (d.cmd == "madeCard" && d.value < 0.5f) { MadeClose("the PC (debug)"); return true; }
                MarketListing pick = null;
                foreach (var l in Market.list) if (l.mine && !l.listed) { pick = l; break; }
                if (pick == null) foreach (var l in Market.list) { pick = l; break; }      // (nothing of the user's own that is not on sale: anything, for the picture)
                if (pick == null) { Debug.Log("[MR] made card: the catalogue is empty - nothing to show"); return true; }
                if (d.cmd == "madeFly") { _mkFake = false; _mfSlow = Mathf.Clamp(d.value, 1f, 10f); MadeFly(pick, pick.price > 0 ? pick.price : 10, true); }
                else { ShowMade(pick, pick.price > 0 ? pick.price : 10, true); Sfx("open", _madeAnchor, 0.5f); }
                return true;
            }
            if (d.cmd == "madeCardAge") { _madeTouchedAt = Time.time - d.value; return true; }
            return false;
        }
    }
}
