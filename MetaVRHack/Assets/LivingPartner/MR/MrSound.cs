using System.Collections.Generic;
using LivingPartner.Character;
using LivingPartner.Shared;
using UnityEngine;

namespace LivingPartner.MR
{
    /// <summary>
    /// Sound effects. Until now nothing here made a sound but the partner's voice and its tune: a button pressed, a
    /// thing landing on the desk, a hammer blow were all silent. The sounds are a small kit of soft, toy-like one-shots
    /// that the app makes itself (Shared/SfxSynth: sines, noise, envelopes) - once, at its start, off the main thread -
    /// and plays through a pool of voices authored under MrCore/Sound: six that are put where something happens in
    /// the room, and one that is nowhere, for the interface.
    /// Quiet mode (the Quiet badge) switches all of them off, and turns the tune down.
    /// </summary>
    public sealed partial class MrApp
    {
        [Header("Sound")]
        [Tooltip("Six voices for sounds that have a place in the room (MrCore/Sound/Sfx 0..5).")]
        [SerializeField] AudioSource[] _sfx;
        [Tooltip("One voice for the sounds of the interface: they are not anywhere in the room.")]
        [SerializeField] AudioSource _sfx2d;

        readonly Dictionary<string, AudioClip> _sfxClips = new Dictionary<string, AudioClip>();
        volatile Dictionary<string, float[]> _sfxPcm;       // the samples, made on a worker thread; UpdateSound turns them into clips
        bool _sfxReady;
        float[] _sfxStartedAt;                              // when each of the six voices last began to play
        readonly Dictionary<string, float> _sfxLast = new Dictionary<string, float>();
        struct SfxAsked { public string name; public Vector3 at; public float volume, pitch, when; public bool flat; }
        readonly List<SfxAsked> _sfxWaiting = new List<SfxAsked>();      // asked for before the kit was ready (the intro's first chime)
        bool _shopWasShown, _screenWasShown;
        float _stepSoundAt;

        void InitSound()
        {
            _sfxStartedAt = new float[_sfx.Length];
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var all = new Dictionary<string, float[]>();
                    foreach (string n in SfxSynth.Names) all[n] = SfxSynth.Render(n);
                    _sfxPcm = all;
                }
                catch (System.Exception e) { Debug.Log("[MR] sound: the effects could not be made: " + e.Message); }
            });
        }

        /// <summary>A sound at a place in the room. volume 0..1 (interface about 0.5, things that happen 0.6 - 0.8); the pitch is varied a little every time.</summary>
        void Sfx(string name, Vector3 at, float volume = 1f, float pitch = 1f) => AskSfx(name, at, volume, pitch, false);

        /// <summary>A sound of the interface: not anywhere in the room.</summary>
        void Sfx2D(string name, float volume = 1f, float pitch = 1f) => AskSfx(name, default, volume, pitch, true);

        void AskSfx(string name, Vector3 at, float volume, float pitch, bool flat)
        {
            if (_settings.quietMode || volume <= 0f) return;                                 // Quiet: no sound effects at all
            float now = Time.time;
            // the same sound twice in one moment is one sound (ten things appearing on the desk with one snapshot)
            if (_sfxLast.TryGetValue(name, out float last) && now - last < 0.04f) return;
            _sfxLast[name] = now;
            var asked = new SfxAsked { name = name, at = at, volume = volume, pitch = pitch, when = now, flat = flat };
            if (!_sfxReady) { if (_sfxWaiting.Count < 8) _sfxWaiting.Add(asked); return; }
            PlaySfx(asked);
        }

        void PlaySfx(SfxAsked a)
        {
            if (!_sfxClips.TryGetValue(a.name, out var clip)) return;
            float pitch = a.pitch * Random.Range(0.96f, 1.04f);      // never twice exactly the same: a row of the same sound does not rattle
            if (a.flat)
            {
                _sfx2d.pitch = pitch;
                _sfx2d.PlayOneShot(clip, Mathf.Clamp01(a.volume));
                return;
            }
            // a voice that is free; when all are busy, the one that began longest ago
            int pick = 0; float oldest = float.MaxValue;
            for (int i = 0; i < _sfx.Length; i++)
            {
                if (!_sfx[i].isPlaying) { pick = i; break; }
                if (_sfxStartedAt[i] < oldest) { oldest = _sfxStartedAt[i]; pick = i; }
            }
            var src = _sfx[pick];
            _sfxStartedAt[pick] = Time.time;
            src.transform.position = a.at;
            src.clip = clip; src.volume = Mathf.Clamp01(a.volume); src.pitch = pitch;
            src.Play();
        }

        /// <summary>A board comes up or goes away: the two-note chime, up or down, where the board is.</summary>
        void PanelSound(UiPanel p, ref bool was)
        {
            bool now = p.Shown;
            if (now == was) return;
            was = now;
            Sfx(now ? "open" : "close", p.transform.position, 0.5f);
        }

        /// <summary>Once a frame, after everything else has been placed.</summary>
        void UpdateSound(float dt)
        {
            SoundProbe(dt);      // (what really reaches the speakers, written to the log: MrSoundProbe)
            if (!_sfxReady && _sfxPcm != null)
            {
                // sound data, not scene objects
                foreach (var kv in _sfxPcm)
                {
                    if (kv.Value == null || kv.Value.Length == 0) continue;
                    var clip = AudioClip.Create("Sfx " + kv.Key, kv.Value.Length, 1, SfxSynth.SampleRate, false);
                    clip.SetData(kv.Value, 0);
                    _sfxClips[kv.Key] = clip;
                }
                _sfxPcm = null; _sfxReady = true;
                Debug.Log($"[MR] sound: {_sfxClips.Count} effects ready");
                foreach (var a in _sfxWaiting) if (Time.time - a.when < 0.6f) PlaySfx(a);      // (what was asked for a moment ago is still worth playing)
                _sfxWaiting.Clear();
            }
            PanelSound(_shop.panel, ref _shopWasShown);
            PanelSound(_screenBoard.panel, ref _screenWasShown);
            PanelSound(_records.panel, ref _recordsWasShown);
            // its footsteps while it walks on the desk: very quiet, and never more than four a second
            if (_ch.owner == Env.Mr && _rig.Visible && _mode == Mode.Free && _motor.HasTarget && _motor.Speed > 0.02f && Time.time - _stepSoundAt >= (_motor.Run ? 0.25f : 0.30f))
            {
                _stepSoundAt = Time.time;
                Sfx("step", _pos, 0.22f);
            }
        }
    }
}
