using System;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// Talking with the partner in MR. The user asks to talk by pinching the partner - with the fingers right on it, or
    /// by pointing at it from a distance and pinching (also through the glass while it is inside the PC) - or with the
    /// Talk button of the hand menu. The partner then listens until the user stops speaking; a second pinch ends it
    /// early. The recording goes to the PC, which answers in the speech bubble and out loud (see the host's Conversation).
    /// </summary>
    public sealed partial class MrApp
    {
        VoiceIo _voice;
        int _talkPinchFrame = -1;          // the frame in which a pinch was used to talk: it must not also select a spot on a screen
        float _talkPinchAt = -10f;         // ... and when that was: a pinch that goes on to pick the partner up was not a request to talk (MrHold)
        double _listenT0;

        void InitVoice()
        {
            _voice = _rig.Voice;
            _voice.SetSpatial(true);
            _voice.OnListening = (on, reason) =>
            {
                if (on) _listenT0 = _link.HostNow;
                _link.Send(Msg.VoiceListen, new VoiceListen { on = on, reason = reason });
                Debug.Log("[MR] voice: listening " + (on ? "started" : "stopped: " + reason));
            };
            _voice.OnClip = (pcm, rate, seconds, source) =>
            {
                double t1 = _link.HostNow;
                // a test file was not spoken just now: its time window is "the last few seconds", like a typed sentence
                double t0 = source == "microphone" && !_voice.OpenEars ? _listenT0 : t1 - Math.Min(seconds + 0.5, 8.0);
                _link.Send(Msg.VoiceClip, new VoiceClip { clipId = Guid.NewGuid().ToString("N"), sampleRate = rate, pcm16 = Convert.ToBase64String(pcm), t0 = t0, t1 = t1, source = source });
                Debug.Log($"[MR] voice: sent {seconds:0.0}s ({source})");
            };
        }

        /// <summary>Starts listening, or ends it early when already listening.</summary>
        void ToggleTalk(string how)
        {
            if (_voice.Listening) { _voice.StopListening("ended by the user (" + how + ")", true); return; }
            if (LocalMode) { LocalNoTalk(); return; }      // (playing without the PC: nobody is there to listen - MrLocalHost)
            if (!_settings.micEnabled) { Caption("Voice is switched off in the settings.", 3f); return; }
            if (_ch.talk == "thinking") { Caption("One moment, I'm still thinking.", 2f); return; }
            Debug.Log("[MR] voice: asked to talk by " + how);
            if (!_voice.StartListening()) { Caption(_voice.Problem, 5f); return; }
            // whoever is spoken to turns round to listen
            if (_mode == Mode.Free && _ch.owner == Env.Mr && !_motor.HasTarget) _motor.Face(YawTo(Input.HeadPos - _pos));
        }

        void OnVoiceReply(VoiceReply r)
        {
            if (r.playOn != Env.Mr) return;             // the side the partner is on speaks
            byte[] pcm;
            try { pcm = Convert.FromBase64String(r.pcm16 ?? ""); } catch { return; }
            _voice.Play(pcm, r.sampleRate);
            SoundProbeVoice(pcm, r.sampleRate);      // (the log says what was played and, when it is over, what of it reached the listener: MrSoundProbe)
            // a conversation goes on: when it has finished speaking it listens once more, without being pinched again
            _followUpAt = r.followUp ? Time.time + r.seconds + 0.45f : -1f;
            Debug.Log($"[MR] voice: playing {r.seconds:0.0}s of speech");
        }

        bool OnDebugVoice(DebugCommand d)
        {
            if (d.cmd == "listen") { ToggleTalk("the PC"); return true; }
            if (d.cmd == "listenCancel") { _voice.StopListening("cancelled", false); return true; }      // stop and throw the recording away
            if (d.cmd == "voiceFile") { if (!_voice.SendFile(d.text)) Debug.LogWarning("[MR] voice file: " + _voice.Problem); return true; }
            return false;
        }

        /// <summary>Where the partner's body is for the hand: its chest in the room, or the spot on the glass where it stands inside the PC.</summary>
        bool PartnerTouchPoint(bool visibleInRoom, out Vector3 point, out float radius)
        {
            point = default; radius = 0;
            if (visibleInRoom) { point = Vector3.Lerp(_rig.ChestWorld, _rig.HeadWorld, 0.55f); radius = 0.50f * _H; return true; }
            if (_ch.owner == Env.Pc && _snap?.pc != null && _snap.pc.overlayVisible && Mon(_snap.pc.monitorId) is MonView mv)
            {
                ScreenMath.PixelToUV(mv.desc, _snap.pc.pxX, _snap.pc.pxY, out float u, out float v);
                point = mv.ToWorld(u, v) + mv.Y * (0.5f * _H); radius = 0.60f * _H;
                return true;
            }
            return false;
        }

        void UpdateVoice(float dt, bool hostOk, bool visibleInRoom)
        {
            // On the headset the ears are open: the user simply talks. (Quiet mode only stops the partner speaking up on its
            // own - with the ears tied to it, a Quiet button pressed by accident left the user talking to nobody.)
            // (Not without the PC: nobody is there to listen, so the microphone is never opened - or asked for - in local mode.)
            bool ears = hostOk && !LocalMode && !Input.Simulated && _settings.micEnabled;
            if (ears != _voice.OpenEars) { _voice.SetOpenEars(ears); if (_voice.OpenEars == ears) Debug.Log("[MR] voice: ears " + (ears ? "open (just talk)" : "closed")); }
            if (Time.time > _nextEarLog && _voice.OpenEars) { _nextEarLog = Time.time + 30f; Debug.Log($"[MR] voice: ears open, {_voice.OpenClips} clip(s) sent, {_voice.OpenDropped} short sound(s) ignored, level {_voice.Level:0.00}"); }
            if (!hostOk) { if (_voice.Listening) _voice.StopListening("the PC is not connected", false); return; }
            bool idle = TransitionMachine.Parse(_tr.state) is TState st && (st == TState.Idle || st == TState.Complete || st == TState.Cancelled);
            if (!idle && _voice.Listening) _voice.StopListening("the partner is on its way", false);
            if (_followUpAt > 0f && Time.time >= _followUpAt)
            {
                _followUpAt = -1f;
                if (!_voice.OpenEars && idle && !_calibrating && !_tn.active && !_voice.Listening && _settings.micEnabled && _ch.talk != "thinking" && _voice.StartListening()) Debug.Log("[MR] voice: listening for a follow-up");
            }
            if (idle && !_calibrating && PartnerTouchPoint(visibleInRoom && _mode != Mode.Hop, out Vector3 body, out float radius))
            {
                for (int h = 0; h < 2; h++)
                {
                    var hs = h == 0 ? Input.Right : Input.Left;
                    if (!hs.tracked || !hs.pinchDown) continue;
                    if (_mode == Mode.OnHand && h == _rideHand) continue;                 // the hand it is standing on is not pinching it
                    // fingers on the partner itself ...
                    bool direct = Vector3.Distance((hs.indexTip + hs.thumbTip) * 0.5f, body) < radius + 0.02f;
                    // ... or pointing at it from a distance (this is also how the simulator's mouse reaches it)
                    bool pointed = false;
                    if (!direct && hs.rayDir.sqrMagnitude > 0.5f)
                    {
                        Vector3 to = body - hs.rayOrigin; float along = Vector3.Dot(to, hs.rayDir);
                        pointed = along > 0.05f && along < 2.5f && (to - hs.rayDir * along).magnitude < radius;
                    }
                    if (direct || pointed) { _talkPinchFrame = Time.frameCount; if (direct) _talkPinchAt = Time.time; ToggleTalk(direct ? "a pinch on the partner" : "pointing at the partner and pinching"); break; }
                }
            }
            // what the user sees while the conversation runs
            if (_voice.Listening) Caption(_voice.Level > 0.12f ? "I'm listening ..." : "I'm listening.", 0.4f);
            else if (_ch.talk == "thinking" && Time.time > _captionUntil) Caption("Hmm ...", 0.4f);
            else if (_voice.Problem.Length > 0 && _voiceProblemShown != _voice.Problem) { _voiceProblemShown = _voice.Problem; Caption(_voice.Problem, 3f); }
            if (_voice.Listening || _voice.Problem.Length == 0) _voiceProblemShown = "";
            _mixer.Voice = _voice.SpeakLevel;
        }
        string _voiceProblemShown = "";
        float _followUpAt = -1f, _nextEarLog;
    }
}
