using System;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// Says in the log what the app is actually SENDING to the speakers - and looks at the one thing after the app
    /// that it can look at: the headset's own media volume.
    /// On 2026-11-17 the user heard nothing at all on the headset although the tune and the effects were being
    /// "played". Measured afterwards: on the PC the engine does make sound (peak 0.60 at the listener); on the headset
    /// the system's media volume stood at 0 of 15 (the volume-down button had been held the evening before). So:
    ///  - the mixed output at the listener is read every frame and one line is written when sound starts and every
    ///    15 s - a device log then answers "does the engine make sound?";
    ///  - on the headset (Android) the media volume is read at the start and every 5 s and written into those lines.
    ///    If it is 0 at the start it is raised once to about 55 %; if it is still 0 a few seconds later a caption
    ///    says so ("press the volume + button") for 8 s, and again once a minute for as long as it stays 0;
    ///  - when a spoken reply is played (Msg.VoiceReply), its length and its peak are written, and what reached the
    ///    listener while it played.
    /// </summary>
    public sealed partial class MrApp
    {
        readonly float[] _probe = new float[512];
        float _probePeak, _probeT, _probeNext = 3f; bool _probeHeard, _probeSaidConfig, _probeSaidLua;

        // the device's media volume (Android: AudioManager, STREAM_MUSIC = 3)
        const int StreamMusic = 3;
        int _devVol = -1, _devMax, _devFake = -1; bool _devChecked, _devReadFailed;
        float _devNextRead = 1.5f, _devZeroSince = -1f, _devCaptionAt = -1000f;
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaObject _audioManager;
        AndroidJavaObject DeviceAudio()
        {
            if (_audioManager == null)
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    _audioManager = activity.Call<AndroidJavaObject>("getSystemService", "audio");
            return _audioManager;
        }
#endif

        /// <summary>The device's media volume and its maximum. False where there is none to read (a PC), or when reading it failed.</summary>
        bool DeviceVolume(out int volume, out int max)
        {
            volume = -1; max = 0;
            if (_devFake >= 0) { volume = _devFake; max = 15; return true; }      // (the simulator's "volumeFake": to see what a headset at volume 0 is told)
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var am = DeviceAudio();
                volume = am.Call<int>("getStreamVolume", StreamMusic);
                max = am.Call<int>("getStreamMaxVolume", StreamMusic);
                return true;
            }
            catch (Exception e)
            {
                if (!_devReadFailed) { _devReadFailed = true; Debug.Log("[MR] sound probe: the device's media volume could not be read: " + e.Message); }
            }
#endif
            return false;
        }

        bool SetDeviceVolume(int index, out string problem)
        {
            problem = "";
            if (_devFake >= 0) { problem = "a made-up volume (simulator check) cannot be changed"; return false; }
#if UNITY_ANDROID && !UNITY_EDITOR
            try { DeviceAudio().Call("setStreamVolume", StreamMusic, index, 0); return true; }
            catch (Exception e) { problem = e.Message; }
#else
            problem = "this is not an Android device";
#endif
            return false;
        }

        string DeviceVolumeText => _devVol < 0 ? "not read (not a headset)" : _devVol + "/" + _devMax;

        /// <summary>lpctl debug volumeFake 0: the app behaves as if the device's media volume were this (of 15); -1: the real one again.</summary>
        void SoundProbeFakeVolume(int volume)
        {
            _devFake = volume < 0 ? -1 : Mathf.Min(volume, 15);
            _devChecked = false; _devVol = -1; _devNextRead = 0f; _devZeroSince = -1f; _devCaptionAt = -1000f;
            Debug.Log("[MR] sound probe: " + (_devFake >= 0 ? $"pretending the device's media volume is {_devFake}/15" : "the device's real media volume again"));
        }

        void DeviceVolumeTick()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _devNextRead) return;
            _devNextRead = now + 5f;
            if (!DeviceVolume(out int v, out int max)) return;
            bool first = !_devChecked;
            if (first || v != _devVol) Debug.Log($"[MR] sound probe: device media volume {v}/{max}" + (first ? " at the start" : $" (it was {_devVol}/{_devMax})"));
            _devVol = v; _devMax = max;
            if (first)
            {
                _devChecked = true;
                if (v == 0 && max > 0)
                {
                    // nothing can be heard like this: turned up once, to a little over half
                    int want = Mathf.Clamp(Mathf.RoundToInt(max * 0.55f), 1, max);
                    if (SetDeviceVolume(want, out string problem))
                    {
                        if (DeviceVolume(out int after, out int _)) _devVol = after;
                        Debug.Log($"[MR] sound probe: the media volume was 0 - asked the device for {want}/{max}; it now says {_devVol}/{max}{(_devVol == 0 ? " (the system did not take it)" : "")}");
                    }
                    else Debug.Log($"[MR] sound probe: the media volume is 0 and could not be raised: {problem}");
                    _devNextRead = now + 3f;      // looked at again in a few seconds
                }
            }
            if (_devVol != 0) { _devZeroSince = -1f; return; }
            if (_devZeroSince < 0f) _devZeroSince = now;
            if (now - _devZeroSince >= 2.5f && now - _devCaptionAt >= 60f)
            {
                _devCaptionAt = now;
                Caption("Headset volume is 0 - press the volume + button (under the right side)", 8f);
                Debug.Log($"[MR] sound probe: device media volume still 0/{_devMax} - the user is told to press the volume + button");
            }
        }

        // a spoken reply that is being played: what reaches the listener while it plays
        float _replyT = -1f, _replyPeak, _replySeconds;

        /// <summary>Called when a spoken reply (Msg.VoiceReply) is handed to the partner's voice.</summary>
        void SoundProbeVoice(byte[] pcm16, int sampleRate)
        {
            int n = pcm16 != null ? pcm16.Length / 2 : 0, peak = 0;
            for (int i = 0; i < n; i++) { int s = (short)(pcm16[2 * i] | (pcm16[2 * i + 1] << 8)); if (s < 0) s = -s; if (s > peak) peak = s; }
            _replySeconds = n / (float)Mathf.Max(1, sampleRate); _replyT = 0f; _replyPeak = 0f;
            var src = _voice.Source;
            float dist = Vector3.Distance(src.transform.position, Input.HeadPos);
            Debug.Log($"[MR] sound probe: a spoken reply is played: {_replySeconds:0.0} s at {sampleRate} Hz, peak {peak / 32768f:0.00} in the clip; voice source volume {src.volume:0.00}, mute {src.mute}, enabled {src.enabled && src.gameObject.activeInHierarchy}, spatial {src.spatialBlend:0.0}, {dist:0.00} m from the head (full within {src.minDistance:0.00} m, silent beyond {src.maxDistance:0.0} m); device media volume {DeviceVolumeText}");
        }

        void SoundProbe(float dt)
        {
            if (!_probeSaidConfig)
            {
                _probeSaidConfig = true;
                var cfg = AudioSettings.GetConfiguration();
                Debug.Log($"[MR] sound probe: output {cfg.sampleRate} Hz, {cfg.speakerMode}, buffer {cfg.dspBufferSize}, real voices {cfg.numRealVoices}, spatializer '{AudioSettings.GetSpatializerPluginName()}'; listener volume {AudioListener.volume:0.00}, paused {AudioListener.pause}; music source enabled {_music.enabled}, mute {_music.mute}, effects sources {_sfx.Length}");
            }
            // Once, three seconds after the start: does the Lua machine that runs the modules' scripts work on THIS device?
            // (The first thing to look for in a headset's log. Not in the first frame: the test runs a script into its
            //  budget on purpose, and that was a long first frame while the headset was still coming up.)
            if (!_probeSaidLua && Time.realtimeSinceStartup > 3f)
            {
                _probeSaidLua = true;
                try { Debug.Log("[MR] lua: " + LivingPartner.Shared.LuaSandbox.SelfTest()); }
                catch (System.Exception e) { Debug.Log("[MR] lua: FAILED to start - " + e.GetType().Name + ": " + e.Message); }
            }
            DeviceVolumeTick();
            AudioListener.GetOutputData(_probe, 0);
            float peak = 0f;
            for (int i = 0; i < _probe.Length; i++) { float a = _probe[i] < 0f ? -_probe[i] : _probe[i]; if (a > peak) peak = a; }
            if (peak > _probePeak) _probePeak = peak;
            _probeT += dt;
            bool playing = _music.isPlaying;
            foreach (var s in _sfx) playing |= s.isPlaying;
            playing |= _sfx2d.isPlaying;
            if (!_probeHeard && peak > 0.01f) { _probeHeard = true; Debug.Log($"[MR] sound probe: the app is making sound (peak {peak:0.00} at the listener, music volume {_music.volume:0.00}); device media volume {DeviceVolumeText}"); }
            if (_replyT >= 0f)
            {
                _replyT += dt;
                if (peak > _replyPeak) _replyPeak = peak;
                if ((_replyT > 0.3f && !_voice.Speaking) || _replyT > _replySeconds + 2f)
                {
                    Debug.Log($"[MR] sound probe: the spoken reply was played for {_replyT:0.0} s: peak {_replyPeak:0.000} at the listener (with whatever else was playing){(_replyPeak < 0.005f ? " - NOTHING reached the listener" : "")}; device media volume {DeviceVolumeText}");
                    _replyT = -1f;
                }
            }
            if (_probeT >= _probeNext)
            {
                Debug.Log($"[MR] sound probe: last {_probeT:0} s peak {_probePeak:0.000} at the listener; something playing now: {playing}; music playing {_music.isPlaying} at volume {_music.volume:0.00}, clip {(_music.clip != null ? _music.clip.length.ToString("0.0") + " s" : "none")}; device media volume {DeviceVolumeText}");
                _probeT = 0f; _probePeak = 0f; _probeNext = 15f;
            }
        }
    }
}
