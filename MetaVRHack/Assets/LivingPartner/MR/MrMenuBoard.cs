using System;
using System.Collections.Generic;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;
using V = LivingPartner.MR.MenuBoardView;

namespace LivingPartner.MR
{
    /// <summary>
    /// The menu: THE OPEN LEFT PALM IS THE MENU. The bag is a CAROUSEL: stickers on a ring that revolves above the palm.
    /// (History. First a carousel of badges rose from the left palm whenever that palm was turned to the face: it came
    /// up in the middle of a game, it TURNED WHEN A HAND CAME NEAR, and pressing it meant holding one hand still under
    /// the other. Then a flat board of twelve paper tiles, opened by a launcher that needed the open palm held still
    /// in front of the face: "the open-palm thing fails far too often". Then a "Menu" button that floated over the
    /// left hand whatever that hand did, plus a bag on the desk, opening a board that stood in the room: "the Menu
    /// just follows my hand ... the bag is not needed. What is good: it follows my hand when I OPEN my hand and is
    /// only seen when I open my hand - but I wanted that to work PERFECTLY." Then a fan of twelve stickers with pages
    /// that unfolded from the open palm - and, 2026-11-18, on the headset: "make it the form that goes ROUND AND ROUND
    /// like before", and "the bag appears without me opening my palm". So:)
    ///  - OPENING: hold the left palm open, facing up or towards the eyes. The right hand operates the menu.
    ///  - FOLLOWING: the point the carousel grows from sits just above the palm; it always faces the eyes. Between the
    ///    hand and it: a dead zone (FoDeadPos, FoDeadAng - tracking jitter moves nothing), then a critically damped
    ///    spring whose time is FoTime at rest and shrinks as the hand really moves.
    ///  - THE RING: the sticker at the FRONT is the big one, with its name under it - it is the one that is pressed.
    ///    The ring turns ONLY by a deliberate act: one of the two arrows under it, a press on a sticker at the side
    ///    (it comes round to the front), or a swipe - the right index fingertip at the ring, dragged sideways more than
    ///    2 cm (CarouselInput): the ring goes with the finger, runs on when it is let go, and settles on a sticker with
    ///    a little overshoot, a soft tick for every sticker that passes. A hand that is merely near turns nothing.
    ///  - RINGS: the bag itself holds a folder for each kind of thing the user owns (Things, Games, Skills, Places),
    ///    Nilo, Tools, and the Market. Pressing a folder brings its ring up; the round button beside the ribbon leads
    ///    back. A list longer than the ring (nine stickers) runs through it like a belt. Pressing a sticker does its
    ///    thing at once: a thing comes out onto the desk, pressed again it goes back; the sticker dips and pops, and
    ///    the line over the ring says in words what happened.
    ///  - PRESSING: while the RIGHT index fingertip is within FzNear of the menu it is FROZEN in the room - it does not
    ///    follow the left hand, and nothing about the left hand puts it away (the pressing hand hides the other from
    ///    the cameras) - until the finger has been FzFar away for FzAfter seconds. The left hand's own fingers never
    ///    press or turn it. Touch as on every board (MrUiRay: one poke through the face, one press).
    ///  - DURING A GAME the open palm carries only a "Stop" chip.
    ///  - BY VOICE ("Hey, Nilo, open my bag", intent openMenu / closeMenu) the same carousel comes up standing in the room
    ///    in front of the user and stays until it is closed (its round button, the voice): the one case in which it is
    ///    not on the hand.
    /// </summary>
    public sealed partial class MrApp
    {
        // ------------------------------------------------------------------ the open palm
        const float PmHold = 0.20f, PmHoldGame = 0.40f, PmGrace = 0.30f, PmGraceClear = 0.10f;
        const float PmLift = 0.030f, PmRise = 0.012f;                               // the fan's point: off the palm along its normal, and up
        // following the palm
        const float FoTime = 0.07f, FoDeadPos = 0.003f, FoDeadAng = 1.5f, FoQuick = 0.05f, FoQuickAng = 20f;
        // frozen while the right index fingertip is at it
        const float FzNear = 0.09f, FzFar = 0.12f, FzAfter = 0.15f, FzLost = 0.35f, FzLeftAway = 0.25f;

        bool _pmUp, _pmChipOnly, _pmTargetSet;
        float _pmArm, _pmBad;
        Vector3 _pmTarget;
        Vector3 _foHeld, _foPos, _foVel; Quaternion _foHeldRot = Quaternion.identity, _foRot = Quaternion.identity; bool _foSet; float _foSpeed, _foTurn;
        bool _fzOn; float _fzFarT, _fzLostT;
        /// <summary>The left hand is showing the menu (or is about to): it does not point at boards.</summary>
        bool PalmMenuUp => _pmUp || _pmArm > 0.08f;

        string GlassGameOn => _snap != null && _snap.glass != null ? _snap.glass.game ?? "" : "";
        /// <summary>A game is being played or about to be: tennis (or the partner on its way to it), a game of the headset's own, a game at the glass.</summary>
        bool GameOn => _tn.active || _tnPendingAt >= 0f || _game.Length > 0 || GlassGameOn.Length > 0 || _swOn || TvPlaying || GomokuOn;

        static int Bits(int v) { int n = 0; for (; v != 0; v &= v - 1) n++; return n; }

        /// <summary>Is the left palm open to the face? Comes up strictly, stays leniently (see the top of this file).</summary>
        void UpdatePalm(float dt, bool allowed)
        {
            var l = Input.Left; bool game = GameOn;
            // not a hand that carries a thing, that the partner rides on, or that holds it by a pinch
            bool free = allowed && !(_grab >= 0 && _grabHand == 1) && !(_mode == Mode.Held && _holdHand == 1);
            bool enter = false, stay = false, clear = !free; string why = free ? "the hand was not seen" : "the hand is busy";
            if (free && l.tracked)
            {
                Vector3 toHead = (Input.HeadPos - l.palmPos).normalized;
                float facing = Mathf.Max(Vector3.Dot(l.palmNormal, toHead), l.palmNormal.y);
                int open = Bits((l.fingers >> 1) & 15);
                enter = l.fingersKnown && open >= 3 && !l.pinch && facing >= 0.40f;
                stay = open >= 2 && facing >= 0.20f;
                clear = l.fingersKnown && open <= 1 || facing <= 0f;
                why = open < 2 ? "the hand closed" : "the palm turned away";
                _pmTarget = l.palmPos + l.palmNormal * PmLift + Vector3.up * PmRise; _pmTargetSet = true;      // the point the fan grows from
            }
            if (_fzOn && free && l.tracked && !clear) stay = true;      // (a finger is at the fan: nothing about the left hand puts it away)
            if (!_pmUp)
            {
                _pmArm = enter ? _pmArm + dt : clear || !free ? 0f : Mathf.Max(0f, _pmArm - 2f * dt);
                if (_pmArm >= (game ? PmHoldGame : PmHold))
                {
                    _pmUp = true; _pmBad = 0f; _pmChipOnly = game; _foSet = false;
                    Debug.Log($"[MR] palm: the open left palm is up {(game ? "(a game is on: Stop)" : _mbOpen ? "(the fan already stands in the room)" : "- the fan unfolds from it")}");
                    if (!game && !_mbOpen) OpenBoard(MbFrom.Palm);
                }
            }
            else
            {
                _pmBad = stay ? 0f : _pmBad + dt;
                if (_pmBad > (clear ? PmGraceClear : PmGrace))
                {
                    _pmUp = false; _pmArm = 0f; _fzOn = false;
                    Debug.Log($"[MR] palm: down - {why} (for {_pmBad:0.00} s)");
                    if (_mbOpen && _mbOnPalm) CloseBoard(why);
                }
            }
        }

        /// <summary>
        /// Where the fan's point (and the Stop chip) is: it follows the palm through a dead zone and a spring, and is
        /// frozen in the room while the right index fingertip is at it. tip: how far that fingertip is from what is shown.
        /// </summary>
        void PalmFollow(float dt, float tip, bool shown)
        {
            var r = Input.Right;
            if (!_fzOn)
            {
                if (shown && r.tracked && tip < FzNear && _foSet)
                {
                    _fzOn = true; _fzFarT = 0f; _fzLostT = 0f; _foVel = Vector3.zero;
                    Debug.Log($"[MR] palm: the right index fingertip is {tip * 100f:0.0} cm from the menu - it is frozen in the room");
                }
            }
            else
            {
                _fzLostT = r.tracked ? 0f : _fzLostT + dt;
                if (r.tracked) _fzFarT = tip > FzFar ? _fzFarT + dt : 0f;
                bool leftAway = Input.Left.tracked && _pmTargetSet && Vector3.Distance(_pmTarget, _foPos) > FzLeftAway;
                if (!shown || _fzFarT > FzAfter || _fzLostT > FzLost || leftAway)
                {
                    _fzOn = false;
                    Debug.Log($"[MR] palm: the menu follows the hand again ({(!shown ? "it is gone" : leftAway ? "the left hand went away from it" : _fzLostT > FzLost ? "the right hand is not seen" : "the finger went away")}); the palm is {(_pmTargetSet ? Vector3.Distance(_pmTarget, _foPos) * 1000f : 0f):0} mm from where it was");
                }
            }
            if (!_pmTargetSet) return;
            Vector3 head = Input.HeadPos;
            if (!_foSet)
            {
                _foSet = true; _foHeld = _foPos = _pmTarget; _foVel = Vector3.zero; _foSpeed = 0f; _foTurn = 0f;
                _foHeldRot = _foRot = FanFacingFrom(_foPos, head);
            }
            else if (!_fzOn)
            {
                // the dead zone: what is held only moves when the palm is further from it than FoDeadPos (and then stays that far behind)
                Vector3 heldWas = _foHeld, dd = _pmTarget - _foHeld; float m = dd.magnitude;
                if (m > FoDeadPos) _foHeld = _pmTarget - dd * (FoDeadPos / m);
                _foSpeed = Mathf.Lerp(_foSpeed, (_foHeld - heldWas).magnitude / Mathf.Max(dt, 1e-4f), 1f - Mathf.Exp(-dt / 0.05f));
                _foPos = Vector3.SmoothDamp(_foPos, _foHeld, ref _foVel, FoTime / (1f + _foSpeed / FoQuick), Mathf.Infinity, dt);
                // facing the eyes - the same way: a dead zone, then eased
                Quaternion want = FanFacingFrom(_foPos, head), rotWas = _foHeldRot;
                if (Quaternion.Angle(_foHeldRot, want) > FoDeadAng) _foHeldRot = Quaternion.RotateTowards(want, _foHeldRot, FoDeadAng);
                _foTurn = Mathf.Lerp(_foTurn, Quaternion.Angle(rotWas, _foHeldRot) / Mathf.Max(dt, 1e-4f), 1f - Mathf.Exp(-dt / 0.05f));
                _foRot = Quaternion.Slerp(_foRot, _foHeldRot, 1f - Mathf.Exp(-dt * (1f + _foTurn / FoQuickAng) / FoTime));
            }
            else _foVel = Vector3.zero;
            PalmStat(dt);
        }

        /// <summary>How a fan whose point is at root is turned to face the eyes (from its middle, which is above the point).</summary>
        static Quaternion FanFacingFrom(Vector3 root, Vector3 head)
        {
            Quaternion q = Quaternion.LookRotation((root + Vector3.up * -V.RootY - head).normalized, Vector3.up);
            for (int k = 0; k < 2; k++) q = Quaternion.LookRotation((root + q * Vector3.up * -V.RootY - head).normalized, Vector3.up);
            return q;
        }

        // (numbers for the log, once a second while lpctl debug palmStat 1: how far the palm went, how far the fan, how far behind it was)
        bool _pmStat; float _pqT, _pqIn, _pqOut, _pqLag, _pqLagMax; int _pqN; Vector3 _pqInMin, _pqInMax, _pqOutMin, _pqOutMax, _pqInWas, _pqOutWas;
        void PalmStat(float dt)
        {
            if (!_pmStat || !_foSet || !Input.Left.tracked) return;
            if (_pqN == 0) { _pqInMin = _pqInMax = _pmTarget; _pqOutMin = _pqOutMax = _foPos; }
            else { _pqIn += (_pmTarget - _pqInWas).magnitude; _pqOut += (_foPos - _pqOutWas).magnitude; }
            float lag = (_foPos - _pmTarget).magnitude; _pqLag += lag; _pqLagMax = Mathf.Max(_pqLagMax, lag);
            _pqInMin = Vector3.Min(_pqInMin, _pmTarget); _pqInMax = Vector3.Max(_pqInMax, _pmTarget); _pqOutMin = Vector3.Min(_pqOutMin, _foPos); _pqOutMax = Vector3.Max(_pqOutMax, _foPos);
            _pqInWas = _pmTarget; _pqOutWas = _foPos; _pqN++; _pqT += dt;
            if (_pqT < 1f) return;
            Debug.Log($"[MR] palm follow ({_pqN} frames in {_pqT:0.00} s{(_fzOn ? ", FROZEN" : "")}): the palm went {_pqIn * 1000f:0.0} mm ({_pqIn / _pqT:0.00} m/s) within a box of {(_pqInMax - _pqInMin).magnitude * 1000f:0.0} mm"
                      + $" -> the fan went {_pqOut * 1000f:0.00} mm within {(_pqOutMax - _pqOutMin).magnitude * 1000f:0.00} mm; behind the palm: mean {_pqLag / _pqN * 1000f:0.0} mm, most {_pqLagMax * 1000f:0.0} mm");
            _pqT = _pqIn = _pqOut = _pqLag = _pqLagMax = 0f; _pqN = 0;
        }

        /// <summary>During a game: the Stop chip on the open palm.</summary>
        void UpdateChip(float dt, bool show)
        {
            var v = _menu; var cp = v.chipPanel;
            if (!show || !_foSet) { cp.Hide(); return; }
            v.chip.art.SetLook(M.Smooth(v.chip.hover) * 0.6f, -1f);
            cp.Show();
            FaceUser(cp.transform, _foPos + Vector3.up * 0.015f);
            // (touched by the right index fingertip, from any side; the left hand's own fingers do not press it, and it is not pointed at)
            UpdateButtons(cp, dt, 1, false, true);
        }

        void ChipPressed()
        {
            if (GameOn) StopAllGames("the Stop chip on the palm");
        }

        /// <summary>Ends whatever game is on: one of the headset's own, tennis, or one at the glass.</summary>
        void StopAllGames(string how)
        {
            StopGames(how);      // (one of the headset's own, and a game at the glass on either side of it: the PC is told - MrGlass.GlassStop)
            if (_tn.active || _tnPendingAt >= 0f) ToggleTennis(how);
            _mbGlassPack = null;
        }

        /// <summary>Everything that is going on stops: a crossing, a task, a game, a skill, the tune, the surroundings.</summary>
        void StopEverything(string how)
        {
            StopAllGames(how);
            SendWorld("stop");
            _sel = null;
            _link.Send(Msg.TransitionEvent, new TransitionEventMsg { transitionId = _tr.transitionId, kind = "cancel", from = Role, reason = "cancelled from " + how });
            _link.Send(Msg.TaskCommand, new TaskCommand { cmd = "cancel" });
        }

        // ------------------------------------------------------------------ the carousel
        const int MbTiles = V.Tiles;
        const float K = V.K;                           // (lengths that were chosen for the menu as a board in the room, times K: hand scale)
        const float MbNameWidth = 0.060f;              // the room a name has on its strip
        const float MbBadgeQuad = V.Sticker * 0.94f * 288f / 208f;      // a round badge (208 px of its 288 px picture); a sticker drawn for one thing is V.Sticker
        const float UnfoldTime = 0.28f, UnfoldStep = 0.012f, FoldTime = 0.20f, RayWidth = 0.0105f * K;
        float _mbNameSize;
        // the rings: the bag itself (its folders, and the Market), and what each folder holds
        const int RgHome = 0, RgThings = 1, RgGames = 2, RgSkills = 3, RgPlaces = 4, RgBambi = 5, RgTools = 6;
        static readonly string[] MbRingNames = { "Bag", "Things", "Games", "Skills", "Places", "Nilo", "Tools" };
        // every sticker a little tilted, each its own way (degrees)
        static readonly float[] MbTilt = { -2.4f, 1.7f, -1.2f, 2.8f, 2.2f, -2.7f, 1.0f, -1.9f, -1.5f, 2.6f, -2.9f, 1.3f };

        enum MbKind { None, Item, Game, Skill, Place, Act, Folder }
        struct MbEntry { public MbKind kind; public ItemDef item; public GamePackDef game; public SkillDef skill; public AmbienceDef place; public int act; }
        // what a sticker of Nilo / Tools does
        const int ActGoIn = 0, ActComeOut = 1, ActHere = 2, ActWave = 3, ActLook = 4, ActCheer = 5, ActMarket = 10, ActScreen = 11, ActFind = 12, ActQuiet = 13, ActStop = 14, ActRain = 15;
        /// <summary>How the menu was opened: by the open palm (it is on the hand), by voice or by a check from the PC (it stands in the room).</summary>
        enum MbFrom { Palm, Voice, Pc }

        bool _mbOpen, _mbClosing, _mbDirty, _mbOnPalm; int _mbTab, _mbRing, _mbRev = -1, _mbOnPage;
        MbFrom _mbFrom;
        Vector3 _mbPos, _mbFromLocal; Quaternion _mbRot = Quaternion.identity;
        float _mbOpenT, _mbCloseT, _mbTurnT = 10f;      // seconds since it was opened / closed / another ring came up
        float _mbFilledAt = -10f, _mbCloseAt = -1f, _mbPressedAt = -10f, _mbStatusAt = -10f, _mbClosedAt = -100f;
        Color _mbStatusInk = new Color(0.33f, 0.25f, 0.22f);
        float _mbSlow = 1f;      // (captures from the PC: the unfolding and the turning drawn out, so that a picture can catch them half-way)
        string _mbStatus = "", _mbStatusShown = "\u0001", _mbPopName = ""; float _mbPopAt = -10f;
        readonly List<MbEntry> _mbList = new List<MbEntry>();
        readonly MbEntry[] _mbTile = new MbEntry[MbTiles];
        readonly bool[] _mbOn = new bool[MbTiles], _mbDesk = new bool[MbTiles], _mbFresh = new bool[MbTiles];
        readonly int[] _mbIdx = new int[MbTiles];           // which place of the ring's list each sticker shows (it runs on beyond the list's end: the list goes round)
        readonly float[] _mbNameA = new float[MbTiles];     // how much of each sticker's name is seen (only the one at the front has its name)
        readonly HashSet<string> _mbSeen = new HashSet<string>(); bool _mbSeenInit;      // what was in the bag when the menu was last open ("new" marks the rest)
        bool _menuDrag; int _menuDragHand; bool _menuDragRay; float _menuDragReach, _menuDragLost; Vector3 _menuDragOffset;
        GamePackDef _mbGlassPack; float _mbGlassAt;      // a game at the glass was asked for: the PC has to answer
        bool _mbFake;                                    // captures in the simulator: the places that come with the partner, when the PC sends none
        AmbienceDef[] _mbFakePlaces;
        // the ring: where it is turned to (in stickers: 0 = the first of the list is at the front), how fast it turns, where it is going
        float _caPos, _caVel; int _caTarget, _caFrontWas, _caN;
        readonly int[] _caPosOf = new int[7];              // where each ring was left

        IList<AmbienceDef> MenuPlaces()
        {
            var have = World.ambiences;
            if ((have == null || have.Count == 0) && _mbFake) return _mbFakePlaces ?? (_mbFakePlaces = WorldDefaults.Ambiences());
            return have ?? (IList<AmbienceDef>)Array.Empty<AmbienceDef>();
        }

        void InitMenuBoard()
        {
            var v = _menu;
            v.chip.press = ChipPressed;
            for (int i = 0; i < v.tiles.Length; i++) { int k = i; v.tiles[i].press = () => MenuPress(k); }
            v.prev.press = () => MenuTurn(-1);
            v.next.press = () => MenuTurn(1);
            v.close.press = () => { if (_mbRing != RgHome) MenuGo(RgHome); else CloseBoard("its close button"); };      // (on a folder's ring: back to the bag itself)
            _mbNameSize = v.tiles[0].label.characterSize;
            v.panel.gameObject.SetActive(false);
            v.chipPanel.gameObject.SetActive(false);
            _heard.text = ""; _heardBg.enabled = false;
            InitPutAway();
        }

        /// <summary>An arrow: the ring turns by one sticker (by = 1: the one on the right comes to the front).</summary>
        void MenuTurn(int by)
        {
            if (_caN < 2 || _caDrag) return;
            _caTarget += by;
        }

        /// <summary>A sticker at the side was pressed: it comes round to the front.</summary>
        void CaTurnTo(int index) { if (_caN > 1 && !_caDrag) _caTarget = index; }

        /// <summary>Which sticker of the pool is at the front now (-1: the ring is empty).</summary>
        int CaFrontTile() { int p = _mbOnPage, f = Mathf.RoundToInt(_caPos); return p > 0 ? ((f % p) + p) % p : -1; }

        /// <summary>The place of the ring's list that sticker t of the pool shows now: the one of its places (t, t + p, t - p ...) that is nearest to the front.</summary>
        int CaIndex(int t) { int p = Mathf.Max(1, _mbOnPage); return t + p * Mathf.RoundToInt((_caPos - t) / p); }

        /// <summary>Another ring comes up: a folder was pressed (its things, games ...), or the button that leads back to the bag itself.</summary>
        void MenuGo(int ring)
        {
            ring = Mathf.Clamp(ring, 0, MbRingNames.Length - 1);
            bool back = ring == RgHome;
            _caPosOf[_mbRing] = _caTarget;
            _mbRing = ring; _caTarget = back ? _caPosOf[RgHome] : 0;
            _caPos = _caTarget + (back ? 0.9f : -0.9f); _caVel = 0f; _caDrag = false; _caTouch = false;      // (it comes round into place)
            _caFrontWas = Mathf.RoundToInt(_caPos);
            MenuList(ring); _caN = _mbList.Count; _mbOnPage = Mathf.Min(_caN, V.RingShown);
            _mbDirty = true; _mbTurnT = 0f; MenuSay("");
            Sfx("page", _mbPos + _mbRot * new Vector3(0f, V.CarHeaderY, 0f), 0.5f);
            Debug.Log($"[MR] menu: the ring '{MbRingNames[ring]}' is up ({_caN} stickers)");
        }

        /// <summary>The ring of a section (0 the bag itself, 1 Things, 2 Games, 3 Skills, 4 Places, 5 Nilo, 6 Tools).</summary>
        void MenuSection(int ring) => MenuGo(ring);

        void OpenBoard(MbFrom from)
        {
            Vector3 head = Input.HeadPos, rootLocal = V.FanPoint(0f, V.RootY);
            _mbOnPalm = from == MbFrom.Palm;
            if (_mbOnPalm)
            {
                // on the palm: it grows from the point just above the palm, and goes where the palm goes (PalmFollow)
                PalmFollow(0f, float.MaxValue, false);
                _mbRot = _foRot; _mbPos = _foPos - _foRot * rootLocal;
                _mbFromLocal = rootLocal;
            }
            else
            {
                // asked for by voice: it stands in the room, 40 cm in front of the eyes and a little below them, facing them
                if (Input.Simulated) _mbPos = head + Input.HeadRot * new Vector3(0f, 0f, 0.42f);      // (in the middle of the simulator's picture)
                else
                {
                    Vector3 flat = Input.HeadRot * Vector3.forward; flat.y = 0f;
                    flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
                    _mbPos = head + flat * 0.40f + Vector3.down * 0.12f;
                }
                _mbRot = Quaternion.LookRotation((_mbPos - head).normalized, Vector3.up);
                _mbFromLocal = rootLocal + new Vector3(0f, -0.03f, 0f);
            }
            bool again = _mbOnPalm && Time.time - _mbClosedAt < 20f;      // (the hand was closed for a moment: the page that was open is open again)
                        _mbFrom = from;
            _mbOpen = true; _mbClosing = false; _mbOpenT = 0f; _mbTurnT = 10f; _mbDirty = true; _mbCloseAt = -1f; _menuDrag = false;
            if (!again) { _caPosOf[_mbRing] = _caTarget; _mbRing = RgHome; _caTarget = 0; _caPos = -1f; _caVel = 0f; MenuSay(""); }      // (the ring comes round by one sticker as it unfolds)
            _caDrag = false; _caTouch = false; _caFrontWas = Mathf.RoundToInt(_caPos);
            Vector3 fromW = _mbPos + _mbRot * _mbFromLocal;
            Sfx("open", fromW, 0.45f);
            Sfx("whoosh", fromW, 0.2f, 1.3f);
            Debug.Log($"[MR] menu: the fan is open ({(_mbOnPalm ? "on the open left palm" : from == MbFrom.Voice ? "asked for: it stands in the room" : "the PC: it stands in the room")}), its middle {Vector3.Distance(head, _mbPos):0.00} m from the eyes, {head.y - _mbPos.y:0.00} m below them");
        }

        void CloseBoard(string why)
        {
            _mbCloseAt = -1f; _menuDrag = false;
            if (!_mbOpen) return;
            _mbOpen = false; _mbClosing = true; _mbCloseT = 0f; _mbClosedAt = Time.time;      // (it folds back into its point, then it is gone)
            MenuRemember();
            Sfx("close", _mbPos + _mbRot * V.FanPoint(0f, V.RootY), 0.35f);
            _caPosOf[_mbRing] = _caTarget;
            Debug.Log("[MR] menu: the fan is closed (" + why + ")");
        }

        /// <summary>"Hey, Nilo, open my bag" / "... 닫아 줘": the PC heard it (intent openMenu / closeMenu). The fan it opens stands in the room.</summary>
        void MenuByVoice(bool open)
        {
            if (!open) { if (_mbOpen) CloseBoard("being asked"); return; }
            if (GameOn) { Caption("A game is on - open your left palm and press Stop first.", 4f); return; }
            if ((_calibrating && !_lookCal) || !_introDone) return;
            if (_mbOpen) { CloseBoard("asked for again: it comes to the user"); _mbClosing = false; }      // (already open somewhere: it opens anew in front of the user)
            OpenBoard(MbFrom.Voice);
        }

        /// <summary>The PC is gone: the fan and the chip are put away (they come back with it).</summary>
        void MenuHostLost()
        {
            var v = _menu;
            v.chipPanel.Hide(); v.panel.Hide();
            _pmUp = false; _pmArm = 0f; _fzOn = false; _foSet = false; _pmTargetSet = false;
            _mbOpen = false; _mbClosing = false; _menuDrag = false; _mbCloseAt = -1f;
            PutAwayHide();
            _heardUntil = -1f; _heard.text = ""; _heardBg.enabled = false; _captionLift = 0f;
        }

        void MenuSay(string what) { _mbStatus = what ?? ""; _mbStatusAt = Time.time; }

        /// <summary>Something went into the bag (put away from the desk, or just made): the fan, when it is open, says so, and its sticker pops.</summary>
        void MenuTookIn(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            _mbPopName = name; _mbPopAt = Time.time; _mbDirty = true;
            if (_mbOpen) MenuSay(name + " is in the bag");
        }

        static string MbId(in MbEntry e) => e.kind == MbKind.Item ? e.item.id : e.kind == MbKind.Game ? "game:" + e.game.id : e.kind == MbKind.Skill ? "skill:" + e.skill.id : e.kind == MbKind.Place ? "place:" + e.place.id : "";

        static string MbName(in MbEntry e) => e.kind == MbKind.Folder ? MbRingNames[e.act] : e.kind == MbKind.Item ? e.item.name : e.kind == MbKind.Game ? e.game.title ?? "" : e.kind == MbKind.Skill ? e.skill.name : e.kind == MbKind.Place ? e.place.name
            : e.act == ActGoIn ? "Go in" : e.act == ActComeOut ? "Come out" : e.act == ActHere ? "Come here" : e.act == ActWave ? "Wave" : e.act == ActLook ? "Look" : e.act == ActCheer ? "Cheer"
            : e.act == ActMarket ? "Market" : e.act == ActScreen ? "Bring screen" : e.act == ActFind ? "Find screens" : e.act == ActQuiet ? "Quiet" : e.act == ActStop ? "Stop" : "Rain";

        /// <summary>Everything that is in the bag now has been seen: none of it is "new" the next time the menu is opened.</summary>
        void MenuRemember()
        {
            MenuList(-1);
            foreach (var e in _mbList) _mbSeen.Add(MbId(e));
            _mbSeenInit = true;
        }

        /// <summary>
        /// What a ring holds, in order. The bag itself: a folder for each kind of thing the user owns, Nilo, Tools, and the
        /// Market. Things: the newest first. Skills: the ones it came with, then what it was taught. (ring -1: everything
        /// the user owns in one list - for remembering what has been seen.)
        /// </summary>
        void MenuList(int ring)
        {
            _mbList.Clear();
            var w = World;
            if (ring == RgHome)
            {
                for (int f = RgThings; f <= RgTools; f++) _mbList.Add(new MbEntry { kind = MbKind.Folder, act = f });
                _mbList.Add(new MbEntry { kind = MbKind.Act, act = ActMarket });
            }
            if (ring == RgThings || ring < 0)
                for (int i = w.items.Count - 1; i >= 0; i--) _mbList.Add(new MbEntry { kind = MbKind.Item, item = w.items[i] });
            if ((ring == RgGames || ring < 0) && w.games != null)
                foreach (var g in w.games) _mbList.Add(new MbEntry { kind = MbKind.Game, game = g });
            if (ring == RgSkills || ring < 0)
            {
                // (not a skill of the bag: the passing one the PC makes for some seconds while a thing on the desk is being used)
                foreach (var sk in w.skills) if (sk.builtin && !PassingSkill(sk)) _mbList.Add(new MbEntry { kind = MbKind.Skill, skill = sk });
                for (int i = w.skills.Count - 1; i >= 0; i--) if (!w.skills[i].builtin && !PassingSkill(w.skills[i])) _mbList.Add(new MbEntry { kind = MbKind.Skill, skill = w.skills[i] });
            }
            if (ring == RgPlaces || ring < 0)
                foreach (var a in MenuPlaces()) _mbList.Add(new MbEntry { kind = MbKind.Place, place = a });
            if (ring == RgBambi)
                foreach (int a in ActsBambi) _mbList.Add(new MbEntry { kind = MbKind.Act, act = a });
            if (ring == RgTools)
            {
                foreach (int a in ActsTools) _mbList.Add(new MbEntry { kind = MbKind.Act, act = a });
                // (rain is a place now. Only where the user does not own that place is the old rain on the desk still a tool.)
                bool rainPlace = false;
                foreach (var a in MenuPlaces()) if (a.id == "amb-rain") rainPlace = true;
                if (!rainPlace) _mbList.Add(new MbEntry { kind = MbKind.Act, act = ActRain });
            }
        }
        static readonly int[] ActsBambi = { ActGoIn, ActComeOut, ActHere, ActWave, ActLook, ActCheer }, ActsTools = { ActScreen, ActFind, ActQuiet, ActStop };

        static bool PassingSkill(SkillDef sk) => sk.id == "skill-use" || sk.status == "use";
        static bool GlassTemplate(string t) => GlassGames.Is(t);      // (survival, umbrella, and script: a game the partner wrote - all of them are the PC's to start, at the glass)

        bool PackOn(GamePackDef g)
        {
            if (GlassTemplate(g.template) && GlassGameOn.Length > 0) return _snap.glass.packId == g.id || (string.IsNullOrEmpty(_snap.glass.packId) && _snap.glass.game == g.template);
            return PackRunning(g);
        }

        /// <summary>Writes s into the label, cut short until it is no wider than maxWidth (with dots the font in use can draw).</summary>
        static void FitLabel(StoryLabel l, string s, float maxWidth)
        {
            s = s ?? "";
            string fit = s, dots = StoryGlyphs.CanDraw(s) ? "…" : "...";
            for (int n = s.Length - 1; n > 1 && l.Measure(fit) > maxWidth; n--) fit = s.Substring(0, n).TrimEnd() + dots;
            l.text = fit;
        }

        void MenuFill()
        {
            var v = _menu; var w = World;
            _mbRev = w.revision; _mbFilledAt = Time.time; _mbDirty = false;
            if (!_mbSeenInit) MenuRemember();
            MenuList(_mbRing);
            _mbTab = _mbRing == RgBambi ? 1 : _mbRing == RgTools ? 2 : 0;      // (0 the bag and its folders, 1 Nilo, 2 Tools)
            int n = _mbList.Count, onPage = Mathf.Min(n, V.RingShown);
            _caN = n; _mbOnPage = onPage;
            bool owned = _mbRing >= RgThings && _mbRing <= RgPlaces;
            bool pop = _mbPopName.Length > 0 && Time.time - _mbPopAt < 3f;
            if (!pop) _mbPopName = "";
            for (int i = 0; i < MbTiles; i++)
            {
                var b = v.tiles[i];
                bool used = i < onPage;
                if (b.gameObject.activeSelf != used) b.gameObject.SetActive(used);
                if (!used) { _mbTile[i] = default; _mbOn[i] = false; _mbIdx[i] = i; continue; }
                // (a list longer than the ring runs through it like a belt: a sticker takes its next thing while it is at the back)
                int idx = CaIndex(i); _mbIdx[i] = idx;
                _mbTile[i] = _mbList[((idx % n) + n) % n];
                var e = _mbTile[i];
                Texture2D tex = v.texItem; bool own = false, on = false, desk = false; string title = "";
                switch (e.kind)
                {
                    case MbKind.Folder:
                        tex = e.act == RgThings ? v.texItem : e.act == RgGames ? v.texGame : e.act == RgSkills ? v.texSkill
                            : e.act == RgPlaces ? (v.texPlaces != null ? v.texPlaces : v.texRain) : e.act == RgBambi ? v.texWave : v.texFind;
                        title = MbRingNames[e.act];
                        break;
                    case MbKind.Item:
                    {
                        var it = e.item; var pic = Icon(it.icon);
                        own = pic != null; tex = pic ?? v.texItem;
                        bool making = it.status == "making";
                        title = making ? it.name + " " + Mathf.RoundToInt(it.progress * 100) + "%" : it.name;
                        desk = it.inRoom && !making;
                        break;
                    }
                    case MbKind.Game:
                    {
                        // a game pack looks like any other thing of the bag: the sticker drawn for it, else the picture of what is in it, else its kind's badge
                        var g = e.game; var pic = g.id == SwarmRules.PackId ? v.texDemonGate : Icon(g.icon) ?? Icon(g.enemyIcon) ?? Icon(g.pickupIcon);
                        own = pic != null; tex = pic ?? PlainTile(g) ?? (g.template == "survival" ? v.texSurvival : v.texGame);
                        title = string.IsNullOrEmpty(g.title) ? "Game" : g.title; on = PackOn(g);
                        break;
                    }
                    case MbKind.Skill:
                    {
                        var sk = e.skill; var pic = Icon(sk.icon);
                        own = pic != null; tex = pic ?? (sk.id == "skill-dance" ? v.texDance : sk.id == "skill-music" ? v.texMusic : v.texSkill);
                        title = sk.status == "making" ? "learning ..." : sk.name; on = w.doingSkill == sk.id;
                        break;
                    }
                    case MbKind.Place:
                        title = e.place.name; on = w.ambience != null && w.ambience.id == e.place.id;
                        v.tilePlace[i].SetColors(Hex(e.place.c0, Ui.Pal.Cyan), Hex(e.place.c1, Ui.Pal.Orange));
                        v.tileHills[i].SetColor(Hex(e.place.c2, Color.white));
                        break;
                    case MbKind.Act:
                        switch (e.act)
                        {
                            case ActGoIn: tex = v.texGoIn; title = "Go in"; break;
                            case ActComeOut: tex = v.texComeOut; title = "Come out"; break;
                            case ActHere: tex = v.texHere; title = "Come here"; break;
                            case ActWave: tex = v.texWave; title = "Wave"; break;
                            case ActLook: tex = v.texLook; title = "Look"; break;
                            case ActCheer: tex = v.texCheer; title = "Cheer"; break;
                            case ActMarket: tex = v.texMarket; title = "Market"; on = _shopOpen; break;
                            case ActScreen: tex = v.texScreen; title = LocalMode ? "Connect PC" : "Bring screen"; on = !LocalMode && _scrMon.Length > 0; break;      // (without the PC: the way back to it - MrLocalHost)
                            case ActFind: tex = v.texFind; title = LocalMode ? "Find my desk" : "Find screens"; break;
                            case ActQuiet: tex = v.texQuiet; title = "Quiet"; on = _settings.quietMode; break;
                            case ActStop: tex = v.texCancel; title = "Stop"; break;
                            default: tex = v.texRain; title = "Rain"; on = _settings.rain; break;
                        }
                        break;
                }
                bool place = e.kind == MbKind.Place;
                if (b.art.enabled == place) b.art.enabled = !place;
                if (v.tilePlace[i].enabled != place) { v.tilePlace[i].enabled = place; v.tileHills[i].enabled = place; }
                if (!place)
                {
                    b.art.SetTexture(tex);
                    float size = own ? V.Sticker : MbBadgeQuad;
                    if (!Mathf.Approximately(b.art.transform.localScale.x, size)) b.art.transform.localScale = new Vector3(size, size, 1f);
                }
                // its name on a strip of paper as wide as the name: one line; a long one is written smaller (down to 72 %) before it is cut short
                b.label.characterSize = _mbNameSize;
                float wide = b.label.Measure(title);
                if (wide > MbNameWidth) b.label.characterSize = _mbNameSize * Mathf.Max(0.72f, MbNameWidth / wide);
                FitLabel(b.label, title, MbNameWidth + 0.0005f);
                Ui.SizePanel(v.tileNameBg[i], Mathf.Max(0.030f, b.label.Width + 0.013f), 0.0145f, 0.0054f, 0.0009f);
                // the strip: honey while it is on, a pale green while the thing is out on the desk; the glow behind the sticker is stronger while it is on
                v.tileNameBg[i].SetColors(on ? HoneyPaper : desk ? MintPaper : new Color(1f, 0.975f, 0.91f, 0.98f), on ? new Color(1f, 0.84f, 0.50f, 0.98f) : desk ? new Color(0.80f, 0.92f, 0.79f, 0.98f) : new Color(0.99f, 0.93f, 0.80f, 0.98f));
                b.color = b.color2 = on ? new Color(1f, 0.84f, 0.44f, 0.85f) : new Color(1f, 0.94f, 0.76f, 0.55f);
                _mbOn[i] = on; _mbDesk[i] = desk;
                if (v.tileRing[i].enabled != on) v.tileRing[i].enabled = on;
                bool fresh = owned && e.kind != MbKind.Act && _mbSeenInit && !_mbSeen.Contains(MbId(e));
                _mbFresh[i] = fresh;
                string flag = fresh ? "new" : "";
                if (v.tileNew[i].text != flag) v.tileNew[i].text = flag;
                // (what has just gone into the bag: its sticker dips and pops)
                if (pop && !desk && MbName(e) == _mbPopName) { b.punch = 1f; _mbPopName = ""; pop = false; Sfx("pop", b.transform.position, 0.5f, 1.2f); }
            }
            // the ribbon: the ring's name. Beside it the round button that leads back to the bag itself (or, while the menu stands in the room, closes it)
            if (v.title.text != MbRingNames[_mbRing]) v.title.text = MbRingNames[_mbRing];
            for (int i = 0; i < v.dots.Length; i++) if (v.dots[i].enabled) v.dots[i].enabled = false;
            bool closeOn = !_mbOnPalm || _mbRing != RgHome, arrows = n > 1;
            if (v.close.gameObject.activeSelf != closeOn) v.close.gameObject.SetActive(closeOn);
            if (v.prev.gameObject.activeSelf != arrows) { v.prev.gameObject.SetActive(arrows); v.next.gameObject.SetActive(arrows); }
            string stars = Market.stars.ToString();
            if (v.stars.text != stars) v.stars.text = stars;
            // the line over the ring: what the last press did, for a few seconds; otherwise what can be done here
            string line = Time.time - _mbStatusAt < 5f && _mbStatus.Length > 0 ? _mbStatus
                        : _mbRing == RgHome ? "Swipe or use the arrows - press the big one"
                        : n == 0 ? (_mbRing == RgThings ? "No things yet - ask Nilo to make one" : _mbRing == RgGames ? "No games yet - the Market has some" : _mbRing == RgSkills ? "No skills yet" : "No places yet")
                        : _mbRing == RgThings ? "Press: out on the desk - again: back in"
                        : _mbRing == RgGames ? "Press the big one to play"
                        : _mbRing == RgSkills ? "Press: Nilo does it - again: stop"
                        : _mbRing == RgPlaces ? "Press: go there - again: back home"
                        : _mbRing == RgBambi ? (LocalMode ? "Press what Nilo should do" : "Or simply say it: “Hey, Nilo, come here”")
                        : _mbOnPalm ? "Close your hand to put the menu away" : "Pinch the ribbon to move this";
            bool said = line == _mbStatus && line.Length > 0;
            if (line != _mbStatusShown)
            {
                _mbStatusShown = line;
                FitLabel(v.status, line, 0.285f);
                _mbStatusInk = said ? new Color(0.16f, 0.42f, 0.22f) : new Color(0.33f, 0.25f, 0.22f);
                if (line.Length > 0) Ui.SizePanel(v.statusBg, v.status.Width + 0.020f, 0.0170f, 0.0085f, 0.0009f);
                v.statusBg.SetColors(said ? new Color(0.90f, 0.97f, 0.88f, 0.97f) : new Color(1f, 0.985f, 0.945f, 0.96f), said ? new Color(0.82f, 0.93f, 0.80f, 0.97f) : new Color(0.985f, 0.945f, 0.875f, 0.96f));
            }
        }

        // ------------------------------------------------------------------ pressing a sticker
        void MenuPress(int i)
        {
            if (i >= _mbOnPage) return;
            // only the sticker at the front does its thing: one at the side comes round to the front
            if (_caDrag) return;
            if (_mbIdx[i] != Mathf.RoundToInt(_caPos)) { CaTurnTo(_mbIdx[i]); return; }
            var e = _mbTile[i]; var w = World;
            Vector3 at = _menu.tiles[i].transform.position;
            _mbPressedAt = Time.time; _mbDirty = true;
            switch (e.kind)
            {
                case MbKind.Item:
                {
                    // a thing of the bag: out onto the desk - pressed again: back into the bag
                    var it = e.item;
                    if (it.status == "making") { MenuSay(it.name + " is still being made"); break; }
                    SendWorld(it.inRoom ? "store" : "place", it.id);
                    MenuSay(it.inRoom ? it.name + " is back in the bag" : it.name + " is on the desk");
                    Debug.Log($"[MR] menu: '{it.name}' {(it.inRoom ? "goes back into the bag" : "comes out onto the desk")} (its sticker was pressed)");
                    MenuComet(at);
                    break;
                }
                case MbKind.Game: MenuPressGame(e.game); break;
                case MbKind.Skill:
                {
                    var sk = e.skill;
                    if (sk.status == "making") { MenuSay("Nilo is still learning " + sk.name); break; }
                    if (w.doingSkill == sk.id) { SendWorld("stop"); MenuSay(sk.name + " stopped"); }
                    else { SendWorld("useSkill", sk.id); MenuSay(sk.name + " - press again to stop"); MenuComet(at); }
                    break;
                }
                case MbKind.Place:
                {
                    var a = e.place;
                    if (w.ambience != null && w.ambience.id == a.id) { SendWorld("ambience", "", "clear"); MenuSay("Back in your own room"); }
                    else { SendWorld("ambience", "", a.id); MenuSay(a.name + " is on - press again to turn it off"); }
                    break;
                }
                case MbKind.Act: MenuPressAct(e.act, at); break;
                case MbKind.Folder: MenuGo(e.act); break;
            }
        }

        void MenuPressGame(GamePackDef g)
        {
            string title = string.IsNullOrEmpty(g.title) ? "The game" : g.title;
            if (PackOn(g)) { StopAllGames("the menu"); MenuSay(title + " stopped"); return; }
            if (GlassTemplate(g.template))
            {
                // a game at the glass is the PC's to start: it says on which side of the glass it is played
                _link.Send(Msg.GlassCommand, new GlassCommand { cmd = "start", game = g.id, how = "the bag" });
                _mbGlassPack = g; _mbGlassAt = Time.time;
                MenuSay(title + " started");
                _mbCloseAt = Time.time + 0.9f;
                Debug.Log($"[MR] menu: asked the PC to start '{title}' ({g.template}) at the glass");
                return;
            }
            // (tennis is the PC's to referee: asked for now, it begins with the PC's answer - unless the partner cannot play just now)
            bool asked = (g.template == "tennis" || g.template == "keeper") && TransitionIdle && !(_ch.owner == Env.Pc && _mons.Count == 0) && !(_ch.owner == Env.Mr && _mode != Mode.Free);
            if (g.template == "tennis") ToggleTennis("the bag"); else StartPack(g, "the menu");
            if (GameOn || asked) { MenuSay(title + " started"); _mbCloseAt = Time.time + 0.9f; }
            else MenuSay("Nilo can't play " + title + " right now");
        }

        void MenuPressAct(int act, Vector3 at)
        {
            bool out_ = _ch.owner == Env.Mr;
            if (LocalMenuAct(act, at)) return;      // without the PC: what needs it says so, and "Find screens" is "Find my desk" (MrLocalHost)
            switch (act)
            {
                case ActGoIn:
                    if (!out_) { MenuSay("Nilo is already inside the screen"); break; }
                    OnIntent(new IntentView { kind = "enterPortal", text = "" }); MenuComet(at);
                    MenuSay(_mons.Count == 0 ? "Nilo does not know your screens yet - Tools, Find screens" : "Nilo goes into the screen you are looking at");
                    break;
                case ActComeOut:
                    if (out_) { MenuSay("Nilo is already out here"); break; }
                    RequestReturnDefault(); MenuComet(at); MenuSay("Nilo comes out of the screen");
                    break;
                case ActHere: OnIntent(new IntentView { kind = "comeHere", text = "" }); MenuComet(at); MenuSay("Nilo comes over to you"); break;
                case ActWave:
                    if (!out_) { MenuSay("Nilo is inside the screen - call it out first"); break; }
                    PlayEmote(Emote.Wave, 2.6f); MenuComet(at); MenuSay("Nilo waves");
                    break;
                case ActLook:
                    OnIntent(new IntentView { kind = "lookScreen", text = "What’s on this screen?" }); MenuComet(at);
                    MenuSay("Nilo reads the screen you are looking at and tells you");
                    break;
                case ActCheer:
                    if (!out_) { MenuSay("Nilo is inside the screen - call it out first"); break; }
                    PlayEmote(Emote.Happy, 3.2f); MenuComet(at); MenuSay("Nilo cheers");
                    break;
                case ActMarket:
                    if (_shopOpen) { CloseShop(); MenuSay("The Market is closed"); } else { OpenShop(true); MenuSay("The Market is open"); }
                    break;
                case ActScreen:
                    MenuSay(_scrMon.Length > 0 ? "The screen goes back" : "Bringing the screen you are looking at over");
                    SendMarket("screen", "", _scrMon.Length > 0 ? "close" : "this");
                    break;
                case ActFind: MenuSay("Looking for your screens ..."); BeginCalibration(); break;
                case ActQuiet:
                    _settings.quietMode = !_settings.quietMode; _link.Send(Msg.Settings, _settings);
                    MenuSay(_settings.quietMode ? "Quiet is on: no sounds, the tune turned down" : "Quiet is off");
                    break;
                case ActStop: StopEverything("the menu"); MenuSay("Stopped everything"); break;
                case ActRain: SetRain(!_settings.rain); MenuSay(_settings.rain ? "It rains on the desk - hold a hand over Nilo" : "The rain stops"); break;
            }
        }

        // ------------------------------------------------------------------ every frame
        void UpdateMenu(float dt)
        {
            UpdateComet(dt);
            UpdateHeard(dt);
            UpdatePutAway(dt);      // the "Put away" button over a thing that was pressed (MrPoke)
            var v = _menu; var p = v.panel;
            // no palm menu (and no fan) during the first seconds, while the PC asks for its code, or while the screens are being found
            bool allowed = (!_calibrating || _lookCal) && _introDone && !_link.NeedsPairing;
            UpdatePalm(dt, allowed);
            bool chip = _pmUp && allowed && GameOn;
            bool fanOnPalm = _mbOnPalm && (_mbOpen || _mbClosing);
            if (_pmUp || fanOnPalm)
            {
                // (how far the right index fingertip is from what the palm shows: the fan's curved sheet, or the chip)
                var r = Input.Right;
                float tip = !r.tracked ? float.MaxValue : chip ? Vector3.Distance(r.indexTip, v.chipPanel.transform.position) : _mbOnPalm && _mbOpen && p.Shown ? FanDistance(r.indexTip) : float.MaxValue;
                PalmFollow(dt, tip, chip ? v.chipPanel.Shown : _mbOnPalm && _mbOpen);
            }
            else { _fzOn = false; _foSet = false; }
            UpdateChip(dt, chip);

            // a game at the glass that was asked for: the PC starts it (MrGlass draws it when it is played out here).
            // When no game comes up it is said so - unless the PC has just said why itself (tennis is on ...): then
            // its own line is showing. (No game is played without the PC any more: the old survival is gone.)
            if (_mbGlassPack != null)
            {
                if (GlassGameOn.Length > 0) _mbGlassPack = null;
                else if (Time.time - _mbGlassAt > 1.5f)
                {
                    var g = _mbGlassPack; _mbGlassPack = null;
                    Debug.Log($"[MR] menu: '{g.title}' ({g.template}) was asked for and no game came up within 1.5 s");
                    if (Time.time > _captionUntil) { MenuSay("The PC app did not answer"); Caption("The PC app did not answer - is it up to date?", 4f); Sfx2D("error", 0.5f); }
                }
            }

            if (_mbOpen)
            {
                if ((_calibrating && !_lookCal) || _link.NeedsPairing) CloseBoard("the screens are being found");
                else if (GameOn) { if (_mbCloseAt < 0f) _mbCloseAt = Time.time + (Time.time - _mbPressedAt < 1.2f ? 0.7f : 0.1f); }      // (a game started: the fan goes - after the sticker that started it was seen to dip)
                else if (!_mbOnPalm && Vector3.Distance(Input.HeadPos, _mbPos) > 1.5f) CloseBoard("the user walked away from it");
            }
            if (_mbOpen && _mbCloseAt >= 0f && Time.time >= _mbCloseAt) CloseBoard(GameOn ? "a game started" : "done");
            if (_mbClosing) { _mbCloseT += dt / _mbSlow; if (_mbCloseT >= FoldTime) _mbClosing = false; }
            if (!_mbOpen && !_mbClosing) { p.Hide(); return; }

            float t = Time.time;
            if (_mbOpen)
            {
                _mbOpenT += dt / _mbSlow; _mbTurnT += dt;
                if (!_mbOnPalm) MenuDrag(dt);
                bool statusOver = _mbStatus.Length > 0 && _mbStatusShown == _mbStatus && t - _mbStatusAt >= 5f;
                if (_mbDirty || World.revision != _mbRev || statusOver || t >= _mbFilledAt + 0.25f) MenuFill();
            }
            if (_mbOnPalm && _foSet) { _mbRot = _foRot; _mbPos = _foPos - _foRot * V.FanPoint(0f, V.RootY); }      // on the palm: where the palm has taken it
            if (!p.Shown) p.Show();
            p.appear = 1f;      // (it does not pop up as one sheet: it unfolds - RingLayout)
            p.transform.SetPositionAndRotation(_mbPos, _mbRot);
            CarouselInput(dt);
            if (_mbDirty && _mbOpen) MenuFill();      // (the belt moved on)
            RingLayout(t);
            if (_mbClosing) foreach (var b in p.buttons) b.cool = Mathf.Max(b.cool, 0.3f);      // nothing is pressed while it folds away
            UpdateButtons(p, dt, _mbOnPalm ? 1 : -1);      // (on the palm: the left hand's own fingers never press it)
        }

        // ------------------------------------------------------------------ turning the ring
        // Only a deliberate act turns it: an arrow, a press on a sticker at the side (it comes to the front), or a SWIPE -
        // the index fingertip AT the ring (from SwNear in front of the front sticker's face to SwDeep behind it, within the
        // row of stickers) that then travels sideways SwStart or more, and more sideways than towards the ring. A finger
        // that comes to press does not turn it, and a hand that is merely near does nothing (the first carousel turned
        // whenever a hand came near). While it is dragged the ring goes with the finger; let go, it runs on by the speed
        // it had and settles on a sticker - a spring with a little overshoot, a soft tick for every sticker that passes.
        const float SwNear = 0.035f, SwDeep = 0.050f, SwHalfY = 0.055f, SwStart = 0.022f, SwLost = 0.12f;
        const float CaStiff = 200f, CaDamp = 0.60f;
        bool _caTouch, _caDrag; float _caTouchX, _caTouchZ, _caGrabX, _caGrabPos, _caLostT, _caDragVel; int _caHand;
        float _dbgSwipeAt = -10f, _dbgSwipeDx;      // (a made-up fingertip that swipes: lpctl debug menuSwipe)

        /// <summary>A hand's index fingertip in the menu's own space (x right, y up, z away from the user).</summary>
        bool CaTip(int h, out Vector3 local)
        {
            local = default;
            var hs = h == 0 ? Input.Right : Input.Left;
            if (!hs.tracked || HandBusy(h, hs)) return false;
            Transform pt = _menu.panel.transform;
            local = Quaternion.Inverse(pt.rotation) * (hs.indexTip - pt.position);
            return true;
        }

        static bool CaInZone(Vector3 l, float grow) => Mathf.Abs(l.y - V.RingFrontY) < SwHalfY + grow && Mathf.Abs(l.x) < V.RingRadius + 0.035f + grow
                                                       && l.z > V.RingFrontZ - SwNear - grow && l.z < V.RingFrontZ + SwDeep + grow;

        void CarouselInput(float dt)
        {
            bool ready = _mbOpen && !_mbClosing && _caN > 1 && _mbOpenT > UnfoldTime + 0.1f && !_menuDrag;
            bool have = false; Vector3 l = default;
            if (ready)
            {
                float su = (Time.time - _dbgSwipeAt) / 0.45f;
                if (su >= 0f && su < 1.3f) { have = su <= 1f; l = new Vector3((M.Smooth(su) - 0.5f) * _dbgSwipeDx, V.RingFrontY, V.RingFrontZ - 0.012f); }
                else if (_caTouch) have = CaTip(_caHand, out l);
                else
                {
                    _caHand = 0; have = CaTip(0, out l) && CaInZone(l, 0f);
                    if (!have && !_mbOnPalm) { _caHand = 1; have = CaTip(1, out l) && CaInZone(l, 0f); }      // (standing in the room: either hand; on the palm the left hand's own fingers never turn it)
                }
            }
            if (!ready) { if (_caDrag) CaRelease(); _caTouch = false; }
            else if (!_caTouch)
            {
                if (have && CaInZone(l, 0f)) { _caTouch = true; _caDrag = false; _caTouchX = l.x; _caTouchZ = l.z; _caLostT = 0f; }
            }
            else
            {
                bool inZone = have && CaInZone(l, 0.015f);      // (a little more room once the finger is at the ring: no flicker at the edge)
                _caLostT = inZone ? 0f : _caLostT + dt;
                if (!inZone) { if (have || _caLostT > SwLost) { if (_caDrag) CaRelease(); _caTouch = false; } }
                else if (!_caDrag)
                {
                    // sideways, clearly - and this finger has not just pressed something
                    float dx = l.x - _caTouchX, dz = l.z - _caTouchZ;
                    if (Mathf.Abs(dx) > SwStart && Mathf.Abs(dx) > 1.3f * Mathf.Abs(dz) && _pokeLock[_caHand] == null)
                    {
                        _caDrag = true; _caGrabX = l.x; _caGrabPos = _caPos; _caDragVel = 0f;
                        Debug.Log($"[MR] menu: a swipe turns the ring (the fingertip went {dx * 100f:0.0} cm sideways, {dz * 100f:0.0} cm towards it)");
                    }
                }
                else
                {
                    float arc = V.RingRadius * 2f * Mathf.PI / Mathf.Max(1, _mbOnPage);      // metres from one sticker to the next, at the front
                    float want = _caGrabPos - (l.x - _caGrabX) / arc, was = _caPos;
                    _caPos = Mathf.Lerp(_caPos, want, 1f - Mathf.Exp(-dt * 30f));
                    _caDragVel = Mathf.Lerp(_caDragVel, (_caPos - was) / Mathf.Max(dt, 1e-4f), 1f - Mathf.Exp(-dt / 0.06f));
                }
            }
            if (_caDrag) { var bs = _menu.panel.buttons; for (int i = 0; i < bs.Length; i++) bs[i].cool = Mathf.Max(bs[i].cool, 0.25f); }      // nothing is pressed while it is being turned
            else
            {
                // the spring that takes it to the sticker it is going to
                float sdt = Mathf.Min(dt, 1f / 30f) / _mbSlow, d = _caTarget - _caPos;
                if (Mathf.Abs(d) < 0.002f && Mathf.Abs(_caVel) < 0.03f) { _caPos = _caTarget; _caVel = 0f; }
                else { _caVel += (CaStiff * d - 2f * Mathf.Sqrt(CaStiff) * CaDamp * _caVel) * sdt; _caPos += _caVel * sdt; }
            }
            // a soft tick for every sticker that comes past the front
            int front = Mathf.RoundToInt(_caPos);
            if (front != _caFrontWas)
            {
                _caFrontWas = front;
                if (_mbOpen && _mbOpenT > 0.2f) Sfx("tick", _mbPos + _mbRot * V.RingPoint(0f), 0.32f, 1.25f + 0.06f * (((front % 3) + 3) % 3 - 1));
            }
        }

        /// <summary>The finger let go of the ring: it runs on by the speed it had, and settles on a sticker - the next one at least, when it was pulled a third of the way.</summary>
        void CaRelease()
        {
            _caDrag = false;
            int to = Mathf.RoundToInt(_caPos + Mathf.Clamp(_caDragVel * 0.18f, -2.6f, 2.6f)), from = Mathf.RoundToInt(_caGrabPos);
            float pulled = _caPos - _caGrabPos;
            if (to == from && Mathf.Abs(pulled) > 0.3f) to += pulled > 0f ? 1 : -1;
            _caTarget = to; _caVel = Mathf.Clamp(_caDragVel, -12f, 12f);
            Debug.Log($"[MR] menu: the ring was let go {pulled:0.00} stickers on, at {_caDragVel:0.0} a second - it settles {to - from} from where it was");
        }

        /// <summary>
        /// Where everything of the carousel is in this frame. The stickers stand on a ring that leans to the eyes: the
        /// one at the front is the big one (its name under it), the ones at the back small and a little higher, so that
        /// all of them are seen. Opening, every sticker flies out of the point just above the palm to its place on the
        /// ring; a thin warm ray runs from that point to each. Closing runs it backwards.
        /// </summary>
        void RingLayout(float t)
        {
            var v = _menu;
            Vector3 rootHome = new Vector3(0f, V.RootY, -0.003f);
            float glide = _mbClosing ? 1f : M.Smooth(_mbOpenT / 0.25f);
            Vector3 root = Vector3.Lerp(_mbFromLocal, rootHome, glide);
            float fold = _mbClosing ? 1f - Mathf.Clamp01(_mbCloseT / FoldTime) : Mathf.Clamp01(_mbOpenT / 0.25f);      // 0 folded .. 1 open: the header, the rays, the point
            int p = _mbOnPage; float step = p > 0 ? 2f * Mathf.PI / p : 0f;
            int frontIdx = Mathf.RoundToInt(_caPos);
            const float frontHalf = 0.033f * V.FrontScale;
            for (int i = 0; i < MbTiles; i++)
            {
                var b = v.tiles[i]; var ray = v.rays[i];
                bool used = i < p && b.gameObject.activeSelf;
                if (!used) { if (ray.enabled) ray.enabled = false; continue; }
                int idx = CaIndex(i);
                if (idx != _mbIdx[i]) _mbDirty = true;      // (the belt has moved on: this sticker, at the back now, takes its next thing)
                float a = (_mbIdx[i] - _caPos) * step;      // round the ring from the front (+ = to the right)
                float ca = Mathf.Cos(a), fr = (1f + ca) * 0.5f;
                float u = _mbClosing ? 1f - Mathf.Clamp01((_mbCloseT - (p - 1 - i) * 0.005f) / (FoldTime - 0.06f))
                                     : Mathf.Clamp01((_mbOpenT - i * UnfoldStep) / UnfoldTime);
                float e = 1f - (1f - u) * (1f - u) * (1f - u);      // out fast, in softly
                Vector3 home = V.RingPoint(a);
                float size = V.RingScale(a);
                b.home = Vector3.Lerp(root, home, e); b.homeSet = true;
                b.size = Mathf.Lerp(0.6f, size, e);
                // (its size: from nothing as it flies out; a small pop when another ring comes up)
                b.intro = Mathf.Min(u, Mathf.Lerp(0.3f, 1f, Mathf.Clamp01((_mbTurnT - i * 0.012f) / 0.16f)));
                b.bob = 0.0022f * K * Mathf.Sin(t * 1.25f + i * 1.9f) * e;
                // it turns a little with the ring, is tilted its own way, and is never quite still
                float yaw = Mathf.Atan2(Mathf.Sin(a), ca) * Mathf.Rad2Deg * 0.25f;
                b.transform.localRotation = Quaternion.Euler(0f, yaw * e, (MbTilt[i] * (1f - 0.6f * fr) + 0.7f * Mathf.Sin(t * 0.9f + i * 2.3f)) * e);
                // what a finger can press: the one at the front in full; one at the side only where the front one does not
                // cover it (pressed, it comes to the front); none at the back
                bool isFront = _mbIdx[i] == frontIdx;
                if (isFront) b.half = new Vector2(0.0330f, 0.0310f) * size;
                else if (ca < 0.05f) b.half = new Vector2(0.0004f, 0.0004f);
                else b.half = new Vector2(Mathf.Clamp(Mathf.Min(0.0330f * size, Mathf.Abs(home.x) - frontHalf - 0.003f), 0.0004f, 0.05f), 0.0310f * size);
                // only the one at the front has its name and its marks; the others are a little paler the further back they stand
                float na = Mathf.Clamp01((fr - 0.90f) / 0.07f) * e;
                if (!Mathf.Approximately(na, _mbNameA[i]) || b.label.color.a != na)
                {
                    _mbNameA[i] = na;
                    Color ink = Ui.Pal.Ink; ink.a = na; b.label.color = ink;
                }
                bool name = na > 0.05f, marks = na > 0.5f;
                if (v.tileNameBg[i].enabled != name) v.tileNameBg[i].enabled = name;
                if (v.tileDot[i].enabled != (marks && _mbDesk[i])) v.tileDot[i].enabled = marks && _mbDesk[i];
                if (v.tileNewBg[i].enabled != (marks && _mbFresh[i])) v.tileNewBg[i].enabled = marks && _mbFresh[i];
                Color ink2 = Ui.Pal.Ink; ink2.a = marks && _mbFresh[i] ? 1f : 0f;
                if (v.tileNew[i].color.a != ink2.a) v.tileNew[i].color = ink2;
                if (_mbOn[i]) v.tileRing[i].transform.localScale = Vector3.one * (V.Sticker * 1.20f * (1f + 0.035f * Mathf.Sin(t * 3.2f + i)));
                if (b.art.enabled)
                {
                    float pale = 0.80f + 0.20f * fr;
                    b.art.color = new Color(1f, 1f, 1f, pale);
                    b.art.SetLook(M.Smooth(b.hover) * 0.5f + (isFront ? 0.06f : 0f), -1f);
                }

                // its ray: from the point to the sticker, strongest at the point (and for the one at the front)
                Vector3 to = b.home + new Vector3(0f, V.ArtY * b.size, 0.003f) - root; float len = to.magnitude;
                bool lit = len > 0.02f && fold > 0.02f;
                if (ray.enabled != lit) ray.enabled = lit;
                if (!lit) continue;
                Vector3 dir = to / len, mid = root + to * 0.5f;
                Vector3 face = Vector3.forward - dir * dir.z;
                ray.transform.localPosition = mid;
                ray.transform.localRotation = Quaternion.LookRotation(face.sqrMagnitude > 1e-6f ? face.normalized : Vector3.up, dir);
                ray.transform.localScale = new Vector3(RayWidth, len, 1f);
                float glow = fold * e * (0.86f + 0.14f * Mathf.Sin(t * 1.7f + i * 1.3f)) * (0.35f + 0.65f * fr + 0.4f * b.hover);
                ray.color = new Color(1f, 0.93f, 0.74f, 0.16f * glow); ray.color2 = new Color(1f, 0.98f, 0.88f, 0.78f * glow);
                ray.param = new Vector4(1f, 0f, RayWidth / len, 0.95f);
                ray.Apply();
            }
            // the point itself
            bool point = fold > 0.02f;
            if (v.rootGlow.enabled != point) { v.rootGlow.enabled = point; v.rootSpark.enabled = point; }
            if (point)
            {
                v.rootGlow.transform.localPosition = root;
                v.rootGlow.transform.localScale = Vector3.one * (0.078f * K * fold * (1f + 0.07f * Mathf.Sin(t * 2.1f)));
                v.rootSpark.transform.localPosition = root + new Vector3(0f, 0f, -0.002f);
                v.rootSpark.transform.localRotation = Quaternion.Euler(0f, 0f, t * 28f + _caPos * 40f);      // (the spark turns with the ring)
                v.rootSpark.transform.localScale = Vector3.one * (0.030f * K * fold * (1f + 0.12f * Mathf.Sin(t * 3.3f)));
            }
            // the header, the arrows and the line of words come up with it
            float open = _mbClosing ? fold : Mathf.Clamp01((_mbOpenT - 0.04f) / 0.24f);
            float pop = Mathf.Max(0.001f, Ui.Pop(open));
            v.header.localScale = Vector3.one * pop;
            v.prev.intro = v.next.intro = open;
            // (an arrow nods the way it turns the ring while the ring is turning that way)
            float lean = Mathf.Clamp(_caTarget - _caPos, -1f, 1f);
            if (v.prevArt != null)
            {
                v.prevArt.transform.localRotation = Quaternion.Euler(0f, 0f, 180f + 8f * Mathf.Max(0f, -lean) * Mathf.Sin(t * 22f));
                v.nextArt.transform.localRotation = Quaternion.Euler(0f, 0f, 8f * Mathf.Max(0f, lean) * Mathf.Sin(t * 22f));
                v.prevArt.SetLook(M.Smooth(v.prev.hover) * 0.6f, -1f); v.nextArt.SetLook(M.Smooth(v.next.hover) * 0.6f, -1f);
            }
            bool words = v.status.text.Length > 0 && pop > 0.5f;
            if (v.statusBg.enabled != words) v.statusBg.enabled = words;
            Color sink = _mbStatusInk; sink.a = Mathf.Clamp01((pop - 0.5f) * 2.5f);      // (the words fade in; the label keeps its size - it is measured)
            v.status.color = sink;
            v.starIcon.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 1.7f) * 10f);
        }

        /// <summary>A point of the room in the fan's own measure: x metres along its curve, y up, z in front (-) or behind (+) its curved sheet.</summary>
        Vector3 OnFanWorld(Vector3 world)
        {
            Transform pt = _menu.panel.transform;
            return V.OnFan(Quaternion.Inverse(pt.rotation) * (world - pt.position));
        }

        /// <summary>How far a point is from the curved sheet of the fan (0 on it).</summary>
        float FanDistance(Vector3 world)
        {
            Vector3 f = OnFanWorld(world);
            float dx = Mathf.Max(0f, Mathf.Abs(f.x) - V.FanWidth * 0.5f), dy = Mathf.Max(0f, Mathf.Abs(f.y) - V.FanHeight * 0.5f);
            return Mathf.Sqrt(dx * dx + dy * dy + f.z * f.z);
        }

        /// <summary>How something lying on the fan at this point of the room is turned (it faces the axis of the curve).</summary>
        Quaternion FanFacing(Vector3 world) => _menu.panel.transform.rotation * V.FanTurn(OnFanWorld(world).x);

        /// <summary>Where a ray meets the curved sheet of the fan from its hollow side (the side the user is on).</summary>
        bool FanRay(Vector3 origin, Vector3 dir, float margin, out float along, out Vector3 hit)
        {
            along = 0f; hit = default;
            Transform pt = _menu.panel.transform; Quaternion inv = Quaternion.Inverse(pt.rotation);
            Vector3 o = inv * (origin - pt.position), d = inv * dir;
            // the cylinder: x² + (z + R)² = R², whatever y
            float oz = o.z + V.FanRadius, a = d.x * d.x + d.z * d.z;
            if (a < 1e-6f) return false;
            float b = o.x * d.x + oz * d.z, c = o.x * o.x + oz * oz - V.FanRadius * V.FanRadius, disc = b * b - a * c;
            if (disc < 0f) return false;
            along = (-b + Mathf.Sqrt(disc)) / a;      // the far crossing: going outwards through the sheet
            if (along <= 0.02f) return false;
            Vector3 l = o + d * along;
            if (l.z + V.FanRadius <= 0f) return false;      // the half of the cylinder behind the user
            if ((l.x * d.x + (l.z + V.FanRadius) * d.z) / V.FanRadius < 0.2f) return false;      // grazing it
            Vector3 f = V.OnFan(l);
            if (Mathf.Abs(f.x) > V.FanWidth * 0.5f + margin || Mathf.Abs(f.y) > V.FanHeight * 0.5f + margin) return false;
            hit = pt.position + pt.rotation * l;
            return true;
        }

        /// <summary>Is this point of the room on the ribbon (and not on one of the round buttons beside it)?</summary>
        bool OnRibbon(Vector3 world, float depth)
        {
            Vector3 f = OnFanWorld(world);
            return Mathf.Abs(f.x) < 0.108f * K && Mathf.Abs(f.y - V.CarHeaderY) < 0.030f * K && Mathf.Abs(f.z) < depth;
        }

        /// <summary>
        /// Moving the fan while it stands in the room (asked for by voice): thumb and finger closed on its ribbon
        /// (either hand; with the fingers right on it, or with the ray on it) take hold of it, and it goes with the
        /// hand until they open. It keeps facing the user. (On the palm it goes where the palm goes.)
        /// </summary>
        void MenuDrag(float dt)
        {
            var p = _menu.panel;
            if (!_menuDrag)
            {
                if (!p.Shown || _mbOpenT < 0.5f) return;
                for (int h = 0; h < 2; h++)
                {
                    var hs = UiHand(h);
                    if (!hs.tracked || !hs.pinchDown || HandBusy(h, hs)) continue;
                    Vector3 q = (hs.indexTip + hs.thumbTip) * 0.5f;
                    bool direct = OnRibbon(q, 0.04f);
                    bool byRay = !direct && _rayPanel[h] == p && OnRibbon(_rayAt[h], 0.02f);
                    if (!direct && !byRay) continue;
                    _menuDrag = true; _menuDragHand = h; _menuDragRay = byRay; _menuDragLost = 0f; _uiConsumedPinch = true;
                    _menuDragReach = byRay ? Vector3.Distance(hs.rayOrigin, _rayAt[h]) : 0f;
                    _menuDragOffset = _mbPos - (byRay ? _rayAt[h] : q);
                    Sfx("tap", _mbPos, 0.4f);
                    break;
                }
                return;
            }
            var hd = UiHand(_menuDragHand);
            _menuDragLost = hd.tracked ? 0f : _menuDragLost + dt;
            if (hd.tracked ? !hd.pinch : _menuDragLost > 0.3f) { _menuDrag = false; Sfx("land", _mbPos, 0.3f); return; }
            if (hd.tracked)
            {
                Vector3 hold = _menuDragRay ? hd.rayOrigin + hd.rayDir * _menuDragReach : (hd.indexTip + hd.thumbTip) * 0.5f;
                _mbPos = Vector3.Lerp(_mbPos, hold + _menuDragOffset, 1f - Mathf.Exp(-dt * 18f));
                Vector3 to = _mbPos - Input.HeadPos;
                if (to.sqrMagnitude > 0.01f) _mbRot = Quaternion.Slerp(_mbRot, Quaternion.LookRotation(to.normalized, Vector3.up), 1f - Mathf.Exp(-dt * 12f));
            }
            foreach (var b in p.buttons) b.cool = Mathf.Max(b.cool, 0.3f);      // nothing is pressed while the fan is being moved
        }

        // ------------------------------------------------------------------ for the simulator's scripted hands, and checks from the PC
        /// <summary>The carousel, while it is open and has unfolded: its middle, and the way from it to the user.</summary>
        public bool TryGetFan(out Vector3 mid, out Vector3 toUser)
        {
            Transform pt = _menu.panel.transform;
            mid = pt.position; toUser = -pt.forward;
            return _mbOpen && _menu.panel.Shown && _mbOpenT > UnfoldTime + MbTiles * UnfoldStep;
        }

        /// <summary>Sticker i of the ring (99: whichever is at the front; 100 / 101: the arrows): where it is, and the way from it to the user.</summary>
        public bool TryGetSticker(int i, out Vector3 at, out Vector3 toUser)
        {
            at = toUser = default;
            if (!TryGetFan(out _, out _)) return false;
            Transform bt;
            if (i == 100 || i == 101) bt = (i == 100 ? _menu.prev : _menu.next).transform;
            else
            {
                if (i == 99) i = CaFrontTile();
                if (i < 0 || i >= _mbOnPage) return false;
                bt = _menu.tiles[i].transform;
            }
            if (!bt.gameObject.activeInHierarchy) return false;
            at = bt.position; toUser = -bt.forward;
            return true;
        }

        /// <summary>The Stop chip, while it is on the palm.</summary>
        public bool TryGetChip(out Vector3 at)
        {
            at = _menu.chipPanel.transform.position;
            return _menu.chipPanel.Shown && _menu.chipPanel.appear >= 1f;
        }

        /// <summary>Is the menu frozen in the room just now (a finger is at it)?</summary>
        public bool MenuFrozen => _fzOn;

        bool OnDebugMenu(DebugCommand d)
        {
            // lpctl debug menu 1|0 (the carousel standing in the room, as when asked for by voice), menuTab 0-6 (the ring: 0 the
            // bag itself, 1 Things, 2 Games, 3 Skills, 4 Places, 5 Nilo, 6 Tools), menuNext, menuPrev (the arrows), menuTurn n
            // (the ring turns n stickers on), menuSwipe dx (a made-up fingertip swipes dx metres to the right across the ring
            // in 0.45 s - negative: to the left), menuPress i (sticker i of the pool; 99 = the one at the front; or by name: it is
            // brought to the front first, whichever ring it is on), menuRay i (the same through ray and pinch), menuIntent 1|0
            // (as if asked for by voice), menuFake 1 (the places the partner comes with, when the PC sends none: captures only),
            // menuSlow n (unfolding and turning n times slower: captures), menuInfo, palmStat 1|0.
            // (The open palm itself is the simulator's: simHand palmOpen / palmClosed / palmAway, palmMove, palmFinger, palmPress.)
            if (d.cmd == "menu" || d.cmd == "menuOff" || d.cmd == "menuOpen")
            {
                bool open = d.cmd == "menuOpen" || (d.cmd == "menu" ? d.value > 0.5f : d.value < 0.5f);
                if (!open) CloseBoard("the PC");
                else if (GameOn || _calibrating) Debug.Log("[MR] menu: not opened - " + (GameOn ? "a game is on" : "the screens are being found"));
                else if (!_mbOpen) OpenBoard(MbFrom.Pc);
                return true;
            }
            if (d.cmd == "palmStat") { _pmStat = d.value > 0.5f; _pqT = _pqIn = _pqOut = _pqLag = _pqLagMax = 0f; _pqN = 0; return true; }
            if (d.cmd == "menuTab") { MenuGo((int)d.value); return true; }
            if (d.cmd == "menuTurn" || d.cmd == "menuPage") { if (d.cmd == "menuPage") _caTarget = (int)d.value; else _caTarget += Mathf.RoundToInt(d.value); return true; }
            if (d.cmd == "menuSwipe") { _dbgSwipeAt = Time.time; _dbgSwipeDx = d.value; _caTouch = false; return true; }
            if (d.cmd == "menuInfo")
            {
                Debug.Log($"[MR] menu info: open={_mbOpen} onPalm={_mbOnPalm} palmUp={_pmUp} ring='{MbRingNames[_mbRing]}' stickers={_caN} shown={_mbOnPage} pos={_caPos:0.00} target={_caTarget} vel={_caVel:0.00} drag={_caDrag} front tile={CaFrontTile()} '{(CaFrontTile() >= 0 ? MbName(_mbTile[CaFrontTile()]) : "")}' status='{_mbStatusShown}'");
                return true;
            }
            if (d.cmd == "menuNext" || d.cmd == "menuPrev")
            {
                var b = d.cmd == "menuNext" ? _menu.next : _menu.prev;
                if (_mbOpen) { PressButton(_menu.panel, b); Debug.Log($"[MR] menu: the {(d.cmd == "menuNext" ? "right" : "left")} arrow pressed by the PC -> the ring turns to {_caTarget}"); }
                return true;
            }
            if (d.cmd == "menuSlow") { _mbSlow = Mathf.Clamp(d.value, 1f, 40f); return true; }      // (value > 1: unfolding, folding and turning that many times slower, for a capture)
            if (d.cmd == "menuIntent") { OnIntent(new IntentView { kind = d.value > 0.5f ? "openMenu" : "closeMenu", text = "" }); return true; }
            if (d.cmd == "menuFake") { _mbFake = d.value > 0.5f; _mbDirty = true; return true; }
            if (d.cmd == "menuForget")      // (captures: the first n things of the bag count as not seen before - they get the "new" flag)
            {
                MenuList(-1);
                for (int k = 0; k < _mbList.Count && k < (int)d.value; k++) _mbSeen.Remove(MbId(_mbList[k]));
                _mbSeenInit = true; _mbDirty = true;
                return true;
            }
            if (d.cmd == "menuPress" || d.cmd == "menuRay")
            {
                int i = (int)d.value;
                if (_mbOpen && !string.IsNullOrEmpty(d.text))
                {
                    // (by name: the first thing whose name has these words in it - on this ring, else on any other; it is brought to the front)
                    int at = -1, ring = _mbRing;
                    for (int k = 0; k < MbRingNames.Length && at < 0; k++)
                    {
                        ring = (_mbRing + k) % MbRingNames.Length;
                        MenuList(ring);
                        at = _mbList.FindIndex(e => MbName(e).IndexOf(d.text, StringComparison.OrdinalIgnoreCase) >= 0);
                    }
                    if (at < 0) { Debug.Log($"[MR] menu: nothing called '{d.text}' on any ring"); _mbDirty = true; return true; }
                    if (ring != _mbRing) MenuGo(ring);
                    _caTarget = at; _caPos = at; _caVel = 0f; _caFrontWas = at; _mbTurnT = 10f;
                    MenuFill(); RingLayout(Time.time);
                    i = 99;
                }
                if (i == 99) i = CaFrontTile();
                i = Mathf.Clamp(i, 0, MbTiles - 1);
                var b = _menu.tiles[i];
                if (!_mbOpen || !b.gameObject.activeInHierarchy) { Debug.Log($"[MR] menu: no sticker {i} to press (open={_mbOpen})"); return true; }
                if (d.cmd == "menuPress") { b.intro = 1f; PressButton(_menu.panel, b); Debug.Log($"[MR] menu: sticker {i} ('{MbName(_mbTile[i])}') pressed by the PC -> ring '{MbRingNames[_mbRing]}', '{_mbStatus}'"); }
                else { _dbgRayTo = b.transform.position; _dbgRayUntil = Time.time + 1.6f; _dbgRayPinchAt = Time.time + 0.9f; _dbgRayPinched = false; Debug.Log($"[MR] menu: a made-up hand points at sticker {i} and pinches"); }
                return true;
            }
            if (d.cmd == "heard") { _dbgHeard = d.text ?? ""; _dbgHeardNote = d.value > 0.5f ? "not for me" : ""; _dbgHeardAt = Time.time; return true; }
            return false;
        }
    }
}
