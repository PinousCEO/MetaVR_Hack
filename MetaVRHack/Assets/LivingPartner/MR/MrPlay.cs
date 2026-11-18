using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// Games that are the headset's own (the PC only referees the ball games, MrTennis), each with its own way of playing:
    ///  - ROCK, PAPER, SCISSORS  what the hand SHOWS. Read from the finger joints; played over the partner's head when
    ///    it is out on the desk, or in front of the screen it is in.
    ///  - STAR POP               how FAST the fingers are. Stars rise from the desk (or come out of the screen); a
    ///    fingertip pops them; half a minute.
    ///  - RED LIGHT              how STILL the hand can be. The partner turns its back and chants; the hand creeps up
    ///    on it and must freeze the moment it turns round ("Red light, green light").
    /// A game can come as a PACK from the marketplace (GamePackDef): one of these ways of playing with its own name,
    /// pictures, pace, tune and surroundings.
    /// The GLASS GAMES (survival, umbrella, wipe) are not played by this file: the PC starts them, on the side of the
    /// glass the partner is on, and MrGlass draws them on the desk when that is out here. (The survival game that was
    /// played here - creatures closing in from all sides, a fingertip sending them off - is gone.) While one runs out
    /// here _game is "glass", so that everything that asks "is a game on?" hears yes.
    /// (Nothing is drawn on the user's hands: in passthrough they see their own. There used to be a light on every
    /// fingertip and a ring on each palm - "too much design on the hands".)
    /// </summary>
    public sealed partial class MrApp
    {
        // ------------------------------------------------------------------ the game board
        [SerializeField] GameObject _gameRoot;
        [SerializeField] FxQuad _gameBg;
        [SerializeField] StoryLabel _gameLabel, _gameSign;
        [Tooltip("Rock-paper-scissors: the user's hand and the partner's, as stickers.")]
        [SerializeField] UiSprite _rpsYou, _rpsIt;
        [Tooltip("The stickers: rock, paper, scissors.")]
        [SerializeField] Texture2D[] _rpsTextures;
        [Tooltip("Star Pop: the stars.")]
        [SerializeField] UiSprite[] _popStars;

        string _game = "";      // "" | rps | pop | redlight | glass (a glass game on the desk: MrGlass)
        public bool RpsActive => _game.Length > 0;
        bool _rpsOn => _game.Length > 0;      // (a game of the headset's own is on: asked by the screen finder, which lights the displays)

        /// <summary>Where a game's board goes: over the partner's head out on the desk, or in front of the screen it is in.</summary>
        bool GameAnchor(out Vector3 at, out Vector3 right, out MonView screen)
        {
            screen = null; right = Vector3.right; at = default;
            if (_ch.owner == Env.Mr && _rig.Visible) at = _rig.HeadTopWorld;
            else if (_ch.owner == Env.Pc && TransitionIdle && (Mon(_snap?.pc?.monitorId) ?? FirstMon()) is MonView mv) { screen = mv; at = mv.Origin - mv.Z * 0.06f - mv.Y * (mv.frame.heightM * 0.20f); }
            else return false;
            Vector3 to = at - Input.HeadPos; to.y = 0;
            if (to.sqrMagnitude > 1e-4f) right = Vector3.Cross(Vector3.up, to.normalized);
            return true;
        }

        /// <summary>How much larger a game's board is drawn when it is far away (in front of a screen) than over the partner's head on the desk.</summary>
        float BoardScale(Vector3 at) => Mathf.Clamp(Vector3.Distance(at, Input.HeadPos) / 0.50f, 1f, 2.4f);

        void Board(Vector3 at, string text)
        {
            _gameLabel.transform.localScale = Vector3.one * BoardScale(at);
            Place(_gameLabel, at, text);
            _gameBg.transform.SetPositionAndRotation(_gameLabel.transform.position + _gameLabel.transform.forward * 0.002f, _gameLabel.transform.rotation);
            float k = BoardScale(at);
            float bw = Mathf.Clamp(_gameLabel.Width + 0.034f * k, 0.12f, 0.9f), bh = _gameLabel.Height + 0.022f * k;
            Ui.SizePanel(_gameBg, bw, bh, Mathf.Min(bh * 0.3f, 0.016f * k), 0.0016f * k);
        }

        /// <summary>
        /// Ends the game that is on: one of the headset's own, and a game at the glass - on the desk or inside the
        /// screen (the PC is told: glass.command stop). Not tennis (ToggleTennis).
        /// </summary>
        void StopGames(string why, bool stopTv = true)
        {
            if (stopTv) StopTvRally();
            EndGomoku(true);
            GlassStop(why);
            EndLocalGame(why);
            StopSwarm(why);      // (Screen Scamps: MrSwarm)
        }
        void StopRps(string why) => StopGames(why);

        /// <summary>A game of the headset's own is over (by itself, or because another one begins). A game at the glass is not touched.</summary>
        void EndLocalGame(string why)
        {
            if (_game.Length == 0 || _game == GlassLocal) return;
            Debug.Log($"[MR] game {_game}: ended ({why})");
            _game = ""; _emoteT = 0f; _pack = null;
        }

        /// <summary>Starts a game by name. Ball games go to the PC's referee (MrTennis), glass games to the PC as well (MrGlass); the others are played right here.</summary>
        void StartGame(string game, string how)
        {
            string asked = (game ?? "").Trim();
            game = asked.ToLowerInvariant();
            if (game == StarterGames.GomokuId || game == "gomoku" || game == "Gomoku") { RequestGomoku(); return; }
            if (SwarmRules.Asked(game)) { StartSwarm(how); return; }
            if (game == "tennis" || game == StarterGames.TennisId || game == "keeper" || game == StarterGames.KeeperId)
            {
                StopGames("a ball game");
                StartTennis(how, game == "keeper" || game == StarterGames.KeeperId ? "keeper" : "");
                return;
            }
            // anything else is a game pack, by its id or its name
            var pack = FindPack(asked);
            if (pack != null) StartPack(pack, how);
            else { Caption("I don't have that game yet - look in the Market.", 4f); Sfx2D("error", 0.5f); }
        }

        /// <summary>Starts a game that is played here (not refereed by the PC). pack dresses and tunes it; null = as it comes.</summary>
        void StartLocal(string game, string how, GamePackDef pack)
        {
            if (_tn.active || _tnPendingAt >= 0f) { Caption("We are already playing!", 3f); Sfx2D("error", 0.5f); return; }
            if (!GameAnchor(out _, out _, out _)) { Caption("One moment, I'm on my way.", 3f); Sfx2D("error", 0.5f); return; }
            if (game == "redlight" && _ch.owner != Env.Mr) { Caption("I'm inside the screen - call me out for this one.", 4f); Sfx2D("error", 0.5f); return; }
            if (_mode == Mode.Held) { Caption("Put me down on the desk first!", 3f); Sfx2D("error", 0.5f); return; }      // (it is hanging from the user's fingers)
            StopGames("another game");
            _game = game; _pack = pack; _gT = 0f; _gPhase = 0; _gRound = 0; _gUser = _gIt = 0; _gameStartedAt = Time.time;
            if (game == "pop")
            {
                _popTotal = _popLeft = Mathf.Clamp(pack != null && pack.seconds > 0 ? pack.seconds : 30f, 10f, 180f); _popSpawn = 0.4f;
                for (int i = 0; i < _popAlive.Length; i++) _popAlive[i] = false;
                if (_popStarTex == null) _popStarTex = _popStars[0].texture;
            }
            if (game == "redlight") { _rlLives = 3; _rlYaw = _rot.eulerAngles.y; _rlCount[0] = _rlCount[1] = 0; _rlBack = 0f; }
            Debug.Log($"[MR] game {game}{(pack != null ? " (pack '" + pack.title + "')" : "")}: started by {how}, the partner is in {_ch.owner}");
        }
        float _gameStartedAt;

        void ToggleLocalGame(string game) { if (_game == game) StopGames("the menu"); else StartGame(game, "the menu"); }

        // shared by the three games
        float _gT; int _gPhase, _gRound, _gUser, _gIt;

        /// <summary>Called every frame: runs the game that is on, and shows or hides what belongs to it.</summary>
        void UpdateRps(float dt)
        {
            UpdateGlass(dt);      // a game at the glass: its stage on the desk, or the sign of the screen it is played in (MrGlass)
            bool on = _game.Length > 0 && _game != GlassLocal;
            if (on && !GameAnchor(out _, out _, out _)) { EndLocalGame("the partner went somewhere"); on = false; }
            if (_gameRoot.activeSelf != on) _gameRoot.SetActive(on);
            if (!on) return;
            bool rps = _game == "rps", pop = _game == "pop";
            if (_rpsYou.enabled != rps) { _rpsYou.enabled = rps; _rpsIt.enabled = rps; }
            if (!pop) foreach (var s in _popStars) if (s.enabled) s.enabled = false;
            if (_gameSign.text.Length > 0) _gameSign.text = "";
            _gT += dt;
            if (rps) RpsStep(dt); else if (pop) PopStep(dt); else RedStep(dt);
        }

        // ------------------------------------------------------------------ rock, paper, scissors
        static readonly string[] RpsNames = { "Rock", "Paper", "Scissors" };
        int _rpsYouSign = -1, _rpsItSign = -1, _rpsBeat = -1;
        readonly int[] _rpsVotes = new int[3];

        /// <summary>What the hand shows: 0 rock, 1 paper, 2 scissors, -1 when it cannot be told.</summary>
        static int RpsSign(in HandState hs)
        {
            if (!hs.tracked || !hs.fingersKnown) return -1;
            bool i = (hs.fingers & 2) != 0, m = (hs.fingers & 4) != 0, r = (hs.fingers & 8) != 0, p = (hs.fingers & 16) != 0;
            int n = (i ? 1 : 0) + (m ? 1 : 0) + (r ? 1 : 0) + (p ? 1 : 0);
            if (n >= 3) return 1;
            if (i && n <= 2) return 2;            // index and middle, or index (and thumb) alone
            return 0;
        }

        void RpsStep(float dt)
        {
            GameAnchor(out Vector3 at, out Vector3 right, out MonView screen);
            Vector3 head = Input.HeadPos, up = Vector3.up;
            float far = BoardScale(at);
            Vector3 mid = at + up * (screen != null ? 0.04f : 0.165f);
            string line; float shake = 0f, popYou = 1f, popIt = 1f; Color cYou = Color.white, cIt = Color.white;
            const float Beat = 0.58f;
            switch (_gPhase)
            {
                case 0:      // the count: the fists come down together - rock ... paper ... scissors ... shoot
                {
                    int beat = Mathf.FloorToInt(_gT / Beat);
                    if (beat != _rpsBeat) { _rpsBeat = beat; if (beat >= 1) Sfx("tick", mid, 0.6f, beat >= 4 ? 1.5f : 1f); }      // the count is heard: rock, paper, scissors - and, higher, "shoot"
                    line = beat <= 0 ? (_gRound == 0 ? "First to 3!  Show your hand on \"Shoot\"" : "Ready ...") : beat == 1 ? "Rock ..." : beat == 2 ? "Paper ..." : "Scissors ...";
                    shake = Mathf.Abs(Mathf.Sin(_gT / Beat * Mathf.PI));
                    _rpsYou.SetTexture(_rpsTextures[0]); _rpsIt.SetTexture(_rpsTextures[0]);
                    if (_ch.owner == Env.Mr) { _emoteForced = Emote.Happy; _emoteT = 0.2f; }
                    if (beat >= 4) { _gPhase = 1; _gT = 0f; _rpsVotes[0] = _rpsVotes[1] = _rpsVotes[2] = 0; _rpsItSign = Random.Range(0, 3); _rpsYouSign = -1; }
                    break;
                }
                case 1:      // shoot: the partner shows its hand, the user's is read for a moment
                {
                    line = "Shoot!";
                    _rpsIt.SetTexture(_rpsTextures[_rpsItSign]); popIt = Ui.Pop(Mathf.Clamp01(_gT / 0.25f));
                    // The hand that is held up plays: of the hands in view the higher one, and the other one when that one's
                    // sign cannot be told. (The left hand used to be read only when the right was not seen at all - a
                    // right hand resting on the desk played every round.)
                    int s;
                    if (Input.Simulated) s = (_gRound * 2 + 1) % 3;
                    else
                    {
                        HandState rh = Input.Right, lh = Input.Left;
                        bool leftFirst = lh.tracked && (!rh.tracked || lh.palmPos.y > rh.palmPos.y);
                        s = RpsSign(leftFirst ? lh : rh);
                        if (s < 0) s = RpsSign(leftFirst ? rh : lh);
                    }
                    if (s >= 0 && _gT > 0.12f) { _rpsVotes[s]++; _rpsYou.SetTexture(_rpsTextures[s]); }
                    if (_gT > 0.8f)
                    {
                        int best = -1, most = 0;
                        for (int q = 0; q < 3; q++) if (_rpsVotes[q] > most) { most = _rpsVotes[q]; best = q; }
                        _rpsYouSign = best; _gPhase = 2; _gT = 0f;
                        if (best < 0) Debug.Log("[MR] rock-paper-scissors: the hand was not seen");
                        else
                        {
                            int outcome = (best - _rpsItSign + 3) % 3;       // 0 draw, 1 the user wins (paper beats rock ...), 2 the partner wins
                            _gRound++;
                            if (outcome == 1) { _gUser++; if (_ch.owner == Env.Mr) { _emoteForced = Emote.Startle; _emoteT = 1.0f; } }
                            else if (outcome == 2) { _gIt++; if (_ch.owner == Env.Mr) { _emoteForced = Emote.Happy; _emoteT = 1.8f; } }
                            Vector3 winAt = mid + right * (outcome == 1 ? -0.062f : 0.062f);
                            if (outcome != 0) { _sparks.Burst(winAt, right, up, 0.05f, 12); Sfx(outcome == 1 ? "win" : "lose", winAt, 0.45f); }
                            Debug.Log($"[MR] rock-paper-scissors: round {_gRound}: user {RpsNames[best]} ({most} readings), partner {RpsNames[_rpsItSign]} -> {(outcome == 0 ? "draw" : outcome == 1 ? "user" : "partner")} ({_gUser}:{_gIt})");
                        }
                    }
                    break;
                }
                case 2:      // the result stays for a moment: the winning hand grows, the other fades
                {
                    if (_rpsYouSign < 0) line = "I couldn't see your hand.\nHold it up where I can see it.";
                    else
                    {
                        int outcome = (_rpsYouSign - _rpsItSign + 3) % 3;
                        line = outcome == 0 ? "A draw!" : outcome == 1 ? "You win this one!" : "Nilo wins this one!";
                        float grow = 1f + 0.22f * Mathf.Clamp01(_gT / 0.3f);
                        if (outcome == 1) { popYou = grow; cIt = new Color(0.72f, 0.72f, 0.72f, 0.75f); }
                        else if (outcome == 2) { popIt = grow; cYou = new Color(0.72f, 0.72f, 0.72f, 0.75f); }
                    }
                    if (_gT > 2.0f)
                    {
                        if (_gUser >= 3 || _gIt >= 3) { _gPhase = 3; _gT = 0f; if (_ch.owner == Env.Mr) { _emoteForced = _gUser > _gIt ? Emote.Thankful : Emote.Happy; _emoteT = 3f; } _sparks.Burst(mid, right, up, 0.08f, 16); Sfx(_gUser > _gIt ? "win" : "lose", mid, 0.8f); }
                        else { _gPhase = 0; _gT = 0f; }
                    }
                    break;
                }
                default:
                    line = _gUser > _gIt ? "You win the game!" : "Nilo wins the game!";
                    if (_gT > 3.2f) { EndLocalGame("game over"); return; }
                    break;
            }
            // the two hands, side by side, facing the user; under them the score and what is happening
            float size = 0.074f * far, drop = -0.016f * shake * far;
            Vector3 you = mid - right * (0.062f * far) + up * drop, it = mid + right * (0.062f * far) + up * drop;
            _rpsYou.transform.SetPositionAndRotation(you, Quaternion.LookRotation(you - head, up)); _rpsYou.transform.localScale = Vector3.one * size * popYou; _rpsYou.SetColor(cYou);
            _rpsIt.transform.SetPositionAndRotation(it, Quaternion.LookRotation(it - head, up)); _rpsIt.transform.localScale = Vector3.one * size * popIt; _rpsIt.SetColor(cIt);
            Board(mid - up * (0.075f * far), $"You  {_gUser} : {_gIt}  Nilo\n{line}");
        }

        // ------------------------------------------------------------------ star pop
        readonly bool[] _popAlive = new bool[8]; readonly Vector3[] _popPos = new Vector3[8], _popVel = new Vector3[8];
        readonly float[] _popAge = new float[8], _popLife = new float[8]; readonly bool[] _popGold = new bool[8];
        float _popLeft, _popTotal = 30f, _popSpawn; int _popCount; Texture2D _popStarTex;

        void PopStep(float dt)
        {
            GameAnchor(out Vector3 at, out Vector3 right, out MonView screen);
            Vector3 head = Input.HeadPos, up = Vector3.up;
            Vector3 fwd = Input.HeadRot * Vector3.forward; fwd.y = 0; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            float speedK = Mathf.Clamp(_pack != null && _pack.speed > 0 ? _pack.speed : 1f, 0.4f, 2f), spawnK = Mathf.Clamp(_pack != null && _pack.spawn > 0 ? _pack.spawn : 1f, 0.4f, 2f);
            Texture2D starTex = (_pack != null ? Icon(_pack.pickupIcon) : null) ?? _popStarTex;
            string popTitle = _pack != null && !string.IsNullOrEmpty(_pack.title) ? Shorten(_pack.title, 22) : "Star Pop";
            if (_gPhase == 0)
            {
                _popLeft -= dt; _popSpawn -= dt;
                if (_popLeft <= 0f) { _gPhase = 1; _gT = 0f; _sparks.Burst(at + up * 0.08f, right, up, 0.08f, 16); Sfx("win", at, 0.8f); if (_ch.owner == Env.Mr) { _emoteForced = Emote.Happy; _emoteT = 3f; } }
                else if (_popSpawn <= 0f)
                {
                    _popSpawn = Mathf.Lerp(0.36f, 0.78f, _popLeft / _popTotal) / spawnK;       // they come faster and faster
                    for (int i = 0; i < _popAlive.Length; i++)
                    {
                        if (_popAlive[i]) continue;
                        _popAlive[i] = true; _popAge[i] = 0f; _popGold[i] = ++_popCount % 6 == 0;
                        if (screen != null)
                        {
                            // out of the screen it is in, towards the user
                            Vector3 from = screen.ToWorld(Random.Range(0.12f, 0.88f), Random.Range(0.25f, 0.85f)) - screen.Z * 0.01f;
                            Vector3 to = head - from; to.y *= 0.3f;
                            _popPos[i] = from; _popVel[i] = to.normalized * Random.Range(0.13f, 0.20f) + up * 0.02f + right * Random.Range(-0.03f, 0.03f);
                            _sparks.Burst(from, screen.X, screen.Y, 0.03f, 5);
                        }
                        else
                        {
                            // up from the desk, somewhere within reach in front of the user
                            _popPos[i] = new Vector3(head.x, _deskY + 0.015f, head.z) + fwd * Random.Range(0.26f, 0.48f) + Vector3.Cross(up, fwd) * Random.Range(-0.24f, 0.24f);
                            _popVel[i] = up * Random.Range(0.10f, 0.17f);
                        }
                        _popVel[i] *= speedK;
                        if (_popGold[i]) _popVel[i] *= 1.5f;
                        _popLife[i] = _popGold[i] ? 2.3f : 3.3f;
                        break;
                    }
                }
            }
            for (int i = 0; i < _popStars.Length; i++)
            {
                var s = _popStars[i];
                if (!_popAlive[i]) { if (s.enabled) s.enabled = false; continue; }
                _popAge[i] += dt;
                _popPos[i] += _popVel[i] * dt + right * (Mathf.Sin(Time.time * 2.3f + i * 1.9f) * 0.02f * dt);
                bool popped = false;
                for (int h = 0; h < 2 && !popped; h++)
                {
                    var hs = h == 0 ? Input.Right : Input.Left;
                    if (!hs.tracked) continue;
                    const float R = 0.036f;
                    popped = (hs.indexTip - _popPos[i]).sqrMagnitude < R * R || (hs.thumbTip - _popPos[i]).sqrMagnitude < R * R ||
                             (hs.middleTip.sqrMagnitude > 1e-6f && (hs.middleTip - _popPos[i]).sqrMagnitude < R * R) || (hs.palmPos - _popPos[i]).sqrMagnitude < 0.05f * 0.05f;
                }
                if (Input.Simulated && _popAge[i] > 1.1f && (i + _popCount) % 2 == 0) popped = true;      // the simulator has no hands: every other star pops by itself
                if (popped && _gPhase == 0)
                {
                    _popAlive[i] = false; _gUser += _popGold[i] ? 3 : 1;
                    _sparks.Burst(_popPos[i], right, up, _popGold[i] ? 0.07f : 0.045f, _popGold[i] ? 16 : 9);
                    Sfx(_popGold[i] ? "sparkle" : "pop", _popPos[i], 0.6f);
                    if (_ch.owner == Env.Mr) { _emoteForced = Emote.Happy; _emoteT = 0.5f; }
                    s.enabled = false; continue;
                }
                if (_popAge[i] > _popLife[i] || _gPhase != 0) { _popAlive[i] = false; s.enabled = false; continue; }
                if (!s.enabled) s.enabled = true;
                s.SetTexture(starTex);
                float grow = Ui.Pop(Mathf.Clamp01(_popAge[i] / 0.22f)), fade = Mathf.Clamp01((_popLife[i] - _popAge[i]) / 0.5f);
                s.transform.SetPositionAndRotation(_popPos[i], Quaternion.LookRotation(_popPos[i] - head, up) * Quaternion.Euler(0, 0, Mathf.Sin(Time.time * 3f + i) * 14f));
                s.transform.localScale = Vector3.one * ((_popGold[i] ? 0.058f : 0.046f) * grow);
                s.SetColor(_popGold[i] ? new Color(1f, 0.82f, 0.55f, fade) : new Color(1f, 1f, 1f, fade));
                s.SetLook(_popGold[i] ? 0.6f : 0f, -1f);
            }
            Vector3 board = at + up * (screen != null ? 0.0f : 0.085f);
            if (_gPhase == 0) Board(board, $"{popTitle}   {_gUser}\n{Mathf.CeilToInt(_popLeft)} s  -  the bright ones count 3");
            else { Board(board, $"Time!\nYou popped {_gUser}."); if (_gT > 3.2f) EndLocalGame("time is up"); }
        }

        // ------------------------------------------------------------------ red light, green light
        const string Chant = "Red light, green light";
        int _rlLives; float _rlYaw, _rlLen, _rlBack;
        // Where each hand's fingertip has been lately (right, then left; RlKeep places each, written round and round).
        const int RlKeep = 32;
        readonly Vector3[] _rlWas = new Vector3[2 * RlKeep]; readonly float[] _rlWasAt = new float[2 * RlKeep];
        readonly int[] _rlCount = new int[2], _rlNext = new int[2];

        /// <summary>
        /// How far this hand's fingertip is from where it was a quarter of a second ago, in metres (0 while that is not
        /// known: the hand is not seen, or has been seen for too short a time). Call once per frame and hand.
        /// "Moved" used to be the speed between two frames: 2 mm of tracking jitter at 72 pictures a second is already
        /// 0.14 m/s, a frame that took long made it more, and when the nearest hand changed it was the distance
        /// between two hands - "You moved!" for a hand held still.
        /// </summary>
        float RedMoved(int h, bool tracked, Vector3 tip, float now)
        {
            if (!tracked) { _rlCount[h] = 0; return 0f; }      // not seen: what is seen next is not compared with what was before
            int first = h * RlKeep;
            float moved = 0f;
            for (int k = _rlCount[h]; k >= 1; k--)             // from the oldest place to the newest
            {
                int i = first + (_rlNext[h] - k + RlKeep) % RlKeep;
                float age = now - _rlWasAt[i];
                if (age > 0.25f) continue;
                if (age >= 0.15f) moved = (tip - _rlWas[i]).magnitude;      // (a shorter stretch than that tells nothing: a hand just found again jumps)
                break;
            }
            // one place every hundredth of a second at most, so that the places kept always reach back far enough
            int last = first + (_rlNext[h] - 1 + RlKeep) % RlKeep;
            if (_rlCount[h] == 0 || now - _rlWasAt[last] >= 0.01f)
            {
                int at = first + _rlNext[h];
                _rlWas[at] = tip; _rlWasAt[at] = now;
                _rlNext[h] = (_rlNext[h] + 1) % RlKeep;
                if (_rlCount[h] < RlKeep) _rlCount[h]++;
            }
            return moved;
        }

        void RedStep(float dt)
        {
            Vector3 up = Vector3.up, body = _pos + up * (_H * 0.5f);
            // the fingertip that is nearest to it, and how far that same hand has moved in the last quarter of a second
            bool tracked = false; Vector3 tip = default; float dist = 9f, moved = 0f;
            for (int h = 0; h < 2; h++)
            {
                var hs = h == 0 ? Input.Right : Input.Left;
                float m = RedMoved(h, hs.tracked, hs.indexTip, Time.unscaledTime);
                if (!hs.tracked) continue;
                float d = Vector3.Distance(hs.indexTip, body);
                if (d < dist) { dist = d; tip = hs.indexTip; tracked = true; moved = m; }
            }
            bool handSeen = tracked;
            if (Input.Simulated) { dist = Mathf.Max(0.02f, 0.5f - (_gRound * 0.2f + (_gPhase == 1 ? _gT * 0.12f : 0f))); tracked = true; }      // the simulator: a hand that creeps up and never flinches
            string line;
            switch (_gPhase)
            {
                case 0:      // hands back: every round starts from a distance
                    line = $"Red light, green light!   Lives {_rlLives}\nCreep up and touch me while I count.\nFreeze when I turn round!";
                    _rlBack = tracked && dist > 0.28f ? _rlBack + dt : 0f;
                    // (with no hand in view it asked for the hand to be "further back", and waited for ever)
                    if (!tracked) line += "\nHold a hand up where I can see it";
                    else if (dist <= 0.28f) line += "\n(start with your hand further back)";
                    if (_gT > 1.6f && _rlBack > 0.6f) { _gPhase = 1; _gT = 0f; _rlLen = Random.Range(1.7f, 3.1f) * Mathf.Max(0.55f, 1f - 0.08f * _gRound); }
                    break;
                case 1:      // its back is turned, it chants: now the hand may move
                {
                    int shown = Mathf.Clamp(Mathf.CeilToInt(Chant.Length * _gT / _rlLen), 1, Chant.Length);
                    line = Chant.Substring(0, shown);
                    if (tracked && dist < 0.05f) { _gPhase = 4; _gT = 0f; _sparks.Burst(body + up * 0.03f, Input.HeadRot * Vector3.right, up, 0.08f, 16); Sfx("win", body, 0.8f); break; }
                    if (_gT >= _rlLen) { _gPhase = 2; _gT = 0f; _rlLen = Random.Range(1.1f, 1.9f); Sfx("tick", body, 0.6f, 0.8f); }      // (it turns round: heard, too)
                    break;
                }
                case 2:      // it has turned round: whoever moves is caught
                    line = Chant + " !";
                    if (_gT > 0.30f && handSeen && moved > 0.025f) { _rlLives--; _gPhase = 3; _gT = 0f; _sparks.Burst(tip, Input.HeadRot * Vector3.right, up, 0.04f, 8); Sfx("error", tip, 0.7f); break; }
                    if (_gT >= _rlLen) { _gRound++; _gPhase = 1; _gT = 0f; _rlLen = Random.Range(1.5f, 2.8f) * Mathf.Max(0.55f, 1f - 0.08f * _gRound); }
                    break;
                case 3:      // caught
                    line = _rlLives > 0 ? $"You moved!   Lives {_rlLives}" : "You moved!";
                    if (_gT > 1.7f) { if (_rlLives <= 0) { _gPhase = 5; _gT = 0f; Sfx("lose", body, 0.8f); } else { _gPhase = 0; _gT = 0f; _rlBack = 0f; } }
                    break;
                case 4:      // touched while its back was turned: the user wins
                    line = "You got me!";
                    if (_gT > 3.2f) { EndLocalGame("the user won"); return; }
                    break;
                default:
                    line = "Three strikes - I win this time!";
                    if (_gT > 3.2f) { EndLocalGame("the partner won"); return; }
                    break;
            }
            Board(_rig.HeadTopWorld + up * 0.085f, line);
        }

        /// <summary>
        /// Lets a game or an activity move the partner itself (instead of the usual idle / walking). True when it did.
        /// </summary>
        bool StagePose(float dt)
        {
            if (GlassPose(dt)) return true;      // a glass game on the desk: the game moves it (MrGlass)
            if (_game == "redlight" && _ch.owner == Env.Mr)
            {
                Vector3 toUser = Input.HeadPos - _pos; toUser.y = 0;
                float yawUser = YawTo(toUser);
                _rlYaw = Mathf.MoveTowardsAngle(_rlYaw, _gPhase == 1 ? yawUser + 180f : yawUser, (_gPhase == 2 ? 900f : 320f) * dt);      // it spins round fast
                float time = Time.time;
                if (_gPhase == 1) PoseLib.CoverEyes(_pose, time);
                else if (_gPhase == 2) PoseLib.Peek(_pose, time);
                else if (_gPhase == 3 || _gPhase == 5) PoseLib.Happy(_pose, time);
                else if (_gPhase == 4) { if (_gT < 0.7f) PoseLib.Startle(_pose, time); else PoseLib.Thankful(_pose, time); }
                else PoseLib.Idle(_pose, time);
                _rot = Quaternion.Euler(0, _rlYaw, 0);
                _motor.Teleport(new Vector2(_pos.x, _pos.z), _rlYaw);
                return true;
            }
            if (_game == "rps" && _ch.owner == Env.Mr && _gPhase == 0 && _gT > 0.58f)
            {
                // its own fist comes down with the count
                PoseLib.RpsShake(_pose, Time.time, _gT / 0.58f);
                LookAtUser(_pose);
                return true;
            }
            return RoomScriptPose(dt) || WorldPose(dt);      // what a thing's script asks of it (MrRoomScript), else what it does with its things by itself
        }
    }
}
