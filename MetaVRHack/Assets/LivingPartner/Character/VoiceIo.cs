using System;
using System.IO;
using UnityEngine;

namespace LivingPartner.Character
{
    /// <summary>
    /// The partner's ears and voice on a device. It sits on the Partner prefab (Visual/…/Head/Voice) next to its
    /// AudioSource, so the voice comes from where the partner is.
    /// Two ways of listening:
    ///  - OPEN EARS (the headset): the microphone stays open and every stretch of speech is cut out by itself and
    ///    handed on, so the user simply talks. Such a clip is marked "microphone-open": it was overheard, and the host
    ///    decides whether it was meant for the partner. If the user asked to talk first (a pinch on the partner, the
    ///    Talk button), the next thing they say is marked "microphone": it certainly is for the partner.
    ///  - ONE UTTERANCE (the PC, where the microphone is opened only on request): StartListening records until the user
    ///    stops speaking.
    /// The partner never hears itself: nothing is taken in while its own voice is playing.
    /// </summary>
    public sealed class VoiceIo : MonoBehaviour
    {
        [SerializeField] AudioSource _source;

        public const int MicRate = 16000;
        const int MaxSeconds = 14;            // one utterance is never longer than this
        const float GiveUpAfter = 6f;         // asked to talk and nothing heard for this long: stop
        const float EndSilence = 0.75f;        // this much quiet after speech ends the utterance
        const int RingSeconds = 30;           // open ears: the microphone records round and round into this much memory
        const float PreRoll = 0.45f;          // open ears: how much before the detected start of speech is kept (the first syllable)

        /// <summary>The user asked to talk and is being listened to (the next thing said is for the partner).</summary>
        public bool Listening { get; private set; }
        /// <summary>Open ears: the microphone is open all the time.</summary>
        public bool OpenEars { get; private set; }
        /// <summary>Open ears: somebody is speaking right now and it is being taken in.</summary>
        public bool Hearing { get; private set; }
        /// <summary>How loud the microphone is right now, 0..1 (for the listening indicator).</summary>
        public float Level { get; private set; }
        public bool Speaking => _source.isPlaying;
        /// <summary>The source the partner's voice is played through (read by the sound probe: its volume, where it is).</summary>
        public AudioSource Source => _source;
        /// <summary>How loud the partner's own voice is at this moment of the playback, 0..1 (drives the talking motion).</summary>
        public float SpeakLevel { get; private set; }
        /// <summary>Why listening could not start or ended without a recording ("" when all is well). English, for a caption.</summary>
        public string Problem { get; private set; } = "";
        /// <summary>Open ears, for diagnosis: clips handed on / stretches of sound thrown away as too short.</summary>
        public int OpenClips { get; private set; }
        public int OpenDropped { get; private set; }

        /// <summary>A finished utterance: 16-bit mono PCM, sample rate, length in seconds, source ("microphone" | "microphone-open" | "test-file").</summary>
        public Action<byte[], int, float, string> OnClip;
        /// <summary>Listening started (true) or stopped (false), with the reason.</summary>
        public Action<bool, string> OnListening;

        AudioClip _mic; string _device; int _readPos; float _elapsed, _speech, _silence, _noise = 0.004f; bool _heard;
        readonly float[] _block = new float[1024];
        AudioClip _playing; float[] _envelope; const float EnvelopeStep = 0.02f;
        // open ears
        AudioClip _ring; int _ringRead, _capStart; bool _capturing, _permissionAsked;
        float _onset, _capSpeech, _capSilence, _capLen, _deaf, _addressedUntil, _retryAt;

        public void SetSpatial(bool spatial) { _source.spatialBlend = spatial ? 1f : 0f; }

        static bool MicAllowed()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone);
#else
            return true;
#endif
        }

        static readonly bool NoMic = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-lp-no-mic") >= 0;

        /// <summary>Opens or closes the ears. Opening needs the microphone permission; it is asked for once.</summary>
        public void SetOpenEars(bool on)
        {
            if (on == OpenEars) return;
            if (!on)
            {
                if (_ring != null) { Microphone.End(null); Destroy(_ring); _ring = null; }
                OpenEars = false; Hearing = false; _capturing = false;
                if (Listening) { Listening = false; OnListening?.Invoke(false, "ears closed"); }
                return;
            }
            if (Time.unscaledTime < _retryAt) return;
            _retryAt = Time.unscaledTime + 2f;
            // "-lp-no-mic": this client never opens the microphone. For test runs of the desktop simulator, which is driven
            // by typed sentences: on 2026-11-17 the Windows clients were found not to exit (47 of them left behind, each
            // holding 0.8 GB), and the always-open recording is the suspect.
            if (NoMic) { Problem = "The microphone is switched off for this run (-lp-no-mic)."; return; }
            if (!MicAllowed())
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (!_permissionAsked) { _permissionAsked = true; UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone); }
#endif
                Problem = "Please allow the microphone so that I can hear you.";
                return;
            }
            if (Microphone.devices.Length == 0) { Problem = "I can't find a microphone on this device."; return; }
            if (_mic != null) { Microphone.End(_device); Destroy(_mic); _mic = null; Listening = false; }
            _ring = Microphone.Start(null, true, RingSeconds, MicRate);
            if (_ring == null) { Problem = "The microphone would not start."; return; }
            Problem = ""; OpenEars = true; _ringRead = 0; _capturing = false; _onset = 0; _noise = 0.004f; _deaf = 0.3f;
        }

        public bool StartListening()
        {
            if (Listening) return true;
            Problem = "";
            if (OpenEars)
            {
                // the ears are open anyway: this only says that what comes next is meant for the partner
                if (_source.isPlaying) _source.Stop();
                Listening = true; _addressedUntil = Time.unscaledTime + GiveUpAfter + 2f;
                OnListening?.Invoke(true, "started");
                return true;
            }
            if (!MicAllowed())
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
#endif
                Problem = "Please allow the microphone, then pinch me again.";
                return false;
            }
            if (Microphone.devices.Length == 0) { Problem = "I can't find a microphone on this device."; return false; }
            _device = null;                                   // the system's default input
            _mic = Microphone.Start(_device, false, MaxSeconds + 1, MicRate);
            if (_mic == null) { Problem = "The microphone would not start."; return false; }
            if (_source.isPlaying) _source.Stop();           // do not record the partner's own voice
            Listening = true; _readPos = 0; _elapsed = 0; _speech = 0; _silence = 0; _noise = 0.004f; _heard = false; Level = 0;
            OnListening?.Invoke(true, "started");
            return true;
        }

        /// <summary>Ends the listening now. send = hand the recording on (when anything was heard), otherwise throw it away.</summary>
        public void StopListening(string reason, bool send)
        {
            if (!Listening) return;
            if (OpenEars)
            {
                Listening = false;
                if (_capturing && send) EmitCapture(true); else if (_capturing && !send) { _capturing = false; Hearing = false; }
                OnListening?.Invoke(false, reason);
                return;
            }
            int end = Mathf.Clamp(Microphone.GetPosition(_device), 0, _mic.samples);
            Microphone.End(_device);
            Listening = false; Level = 0;
            OnListening?.Invoke(false, reason);
            if (send && _heard && end > MicRate / 4)
            {
                var samples = new float[end];
                _mic.GetData(samples, 0);
                OnClip?.Invoke(ToPcm16(samples, end), MicRate, end / (float)MicRate, "microphone");
            }
            else if (send) Problem = "I didn't hear anything.";
            Destroy(_mic); _mic = null;
        }

        void Update()
        {
            if (_playing != null)
            {
                if (_source.isPlaying && _envelope != null)
                {
                    int i = Mathf.Clamp((int)(_source.time / EnvelopeStep), 0, _envelope.Length - 1);
                    SpeakLevel = Mathf.Lerp(SpeakLevel, _envelope[i], 1f - Mathf.Exp(-Time.unscaledDeltaTime * 30f));
                }
                else SpeakLevel = Mathf.MoveTowards(SpeakLevel, 0f, Time.unscaledDeltaTime * 6f);
            }
            if (OpenEars) { UpdateOpenEars(); return; }
            if (!Listening) return;
            _elapsed += Time.unscaledDeltaTime;
            int pos = Microphone.GetPosition(_device);
            if (!Microphone.IsRecording(_device) && pos == 0 && _elapsed > 0.5f) { StopListening("the microphone stopped", true); return; }
            // loudness of what arrived since the last frame, against a slowly tracked background level
            while (pos - _readPos >= _block.Length)
            {
                _mic.GetData(_block, _readPos);
                _readPos += _block.Length;
                float rms = Rms(), blockTime = _block.Length / (float)MicRate;
                if (_elapsed < 0.3f || rms < _noise) _noise = Mathf.Lerp(_noise, rms, 0.3f); else _noise = Mathf.Lerp(_noise, rms, 0.002f);
                bool voice = rms > Mathf.Max(0.012f, _noise * 3.2f);
                Level = Mathf.Lerp(Level, Mathf.Clamp01(rms * 8f), 0.5f);
                if (voice) { _speech += blockTime; _silence = 0; if (_speech > 0.2f) _heard = true; }
                else _silence += blockTime;
            }
            if (_heard && _silence > 1.0f) StopListening("you stopped speaking", true);
            else if (!_heard && _elapsed > GiveUpAfter) StopListening("nothing was heard", true);
            else if (_elapsed > MaxSeconds) StopListening("that was the longest I can listen in one go", true);
        }

        float Rms()
        {
            double sum = 0;
            for (int i = 0; i < _block.Length; i++) sum += _block[i] * _block[i];
            return Mathf.Sqrt((float)(sum / _block.Length));
        }

        /// <summary>The microphone runs round and round; stretches of speech are found in it and cut out.</summary>
        void UpdateOpenEars()
        {
            if (_ring == null || !Microphone.IsRecording(null))
            {
                // the system took the microphone away (the headset slept, another app): open it again
                if (_ring != null) { Microphone.End(null); Destroy(_ring); _ring = null; }
                OpenEars = false; Hearing = false; _capturing = false;
                return;
            }
            int total = _ring.samples, pos = Microphone.GetPosition(null);
            int avail = (pos - _ringRead + total) % total;
            float blockTime = _block.Length / (float)MicRate;
            while (avail >= _block.Length)
            {
                _ring.GetData(_block, _ringRead);                     // reads round the end of the ring by itself
                float rms = Rms();
                if (_source.isPlaying) _deaf = 0.35f; else _deaf -= blockTime;
                if (_deaf > 0f)
                {
                    // its own voice is playing (or has only just stopped): take nothing in
                    _capturing = false; Hearing = false; _onset = 0; Level = 0;
                }
                else
                {
                    if (rms < _noise) _noise = Mathf.Lerp(_noise, rms, 0.25f); else if (!_capturing) _noise = Mathf.Lerp(_noise, rms, 0.004f);
                    bool voice = rms > Mathf.Max(0.014f, _noise * 3.4f);
                    Level = Mathf.Lerp(Level, Mathf.Clamp01(rms * 8f), 0.5f);
                    if (!_capturing)
                    {
                        _onset = voice ? _onset + blockTime : 0f;
                        if (_onset >= 0.08f)
                        {
                            _capturing = true; Hearing = true;
                            int back = (int)((PreRoll + _onset) * MicRate);
                            _capStart = ((_ringRead + _block.Length - back) % total + total) % total;
                            _capLen = PreRoll + _onset; _capSpeech = _onset; _capSilence = 0; _onset = 0;
                        }
                    }
                    else
                    {
                        _capLen += blockTime;
                        if (voice) { _capSpeech += blockTime; _capSilence = 0; } else _capSilence += blockTime;
                        if (_capSilence > EndSilence || _capLen > MaxSeconds) EmitCapture(false);
                    }
                }
                _ringRead = (_ringRead + _block.Length) % total; avail -= _block.Length;
            }
            if (Listening && !_capturing && Time.unscaledTime > _addressedUntil) { Listening = false; OnListening?.Invoke(false, "nothing was heard"); }
        }

        void EmitCapture(bool cutShort)
        {
            _capturing = false; Hearing = false;
            bool addressed = Listening;
            if (addressed) { Listening = false; OnListening?.Invoke(false, "you stopped speaking"); }
            // a cough, a key, a door: too little speech to be worth anything
            // A quick name call can have short voiced syllables separated by unvoiced consonants.
            if (_capSpeech < 0.16f) { OpenDropped++; return; }
            int n = Mathf.Min((int)((_capLen - (cutShort ? 0f : _capSilence * 0.6f)) * MicRate), _ring.samples - 1);
            if (n < MicRate / 4) { OpenDropped++; return; }
            var samples = new float[n];
            _ring.GetData(samples, _capStart);
            OpenClips++;
            OnClip?.Invoke(ToPcm16(samples, n), MicRate, n / (float)MicRate, addressed ? "microphone" : "microphone-open");
        }

        /// <summary>Plays what the partner says (16-bit mono PCM).</summary>
        public void Play(byte[] pcm16, int sampleRate)
        {
            int n = pcm16.Length / 2;
            if (n < sampleRate / 20) return;
            if (Listening && !OpenEars) StopListening("the partner is speaking", false);
            var samples = new float[n];
            for (int i = 0; i < n; i++) samples[i] = (short)(pcm16[2 * i] | (pcm16[2 * i + 1] << 8)) / 32768f;
            // loudness over time, so the body can move with the voice
            int step = Mathf.Max(1, (int)(sampleRate * EnvelopeStep));
            _envelope = new float[n / step + 1];
            for (int e = 0; e < _envelope.Length; e++)
            {
                double sum = 0; int from = e * step, to = Mathf.Min(n, from + step);
                for (int i = from; i < to; i++) sum += samples[i] * samples[i];
                _envelope[e] = Mathf.Clamp01(Mathf.Sqrt((float)(sum / Mathf.Max(1, to - from))) * 5f);
            }
            if (_playing != null) Destroy(_playing);
            _playing = AudioClip.Create("PartnerVoice", n, 1, sampleRate, false);      // sound data, not a scene object
            _playing.SetData(samples, 0);
            _source.Stop(); _source.clip = _playing; _source.Play();
            _deaf = 0.35f;
        }

        /// <summary>
        /// Verification only: hands a recording from a WAV file (16-bit PCM, mono) to the same path a microphone recording
        /// takes, marked "test-file". Used by the voice check script on machines where nobody is there to speak.
        /// </summary>
        public bool SendFile(string wavPath)
        {
            try
            {
                byte[] b = File.ReadAllBytes(wavPath);
                if (b.Length < 46 || b[0] != 'R' || b[8] != 'W') { Problem = "not a WAV file"; return false; }
                int channels = b[22] | (b[23] << 8), rate = BitConverter.ToInt32(b, 24), bits = b[34] | (b[35] << 8);
                int p = 12;                                  // find the "data" chunk
                while (p + 8 <= b.Length && !(b[p] == 'd' && b[p + 1] == 'a' && b[p + 2] == 't' && b[p + 3] == 'a')) p += 8 + BitConverter.ToInt32(b, p + 4);
                if (p + 8 > b.Length || bits != 16 || channels < 1) { Problem = "the WAV file must be 16-bit PCM"; return false; }
                int len = Mathf.Min(BitConverter.ToInt32(b, p + 4), b.Length - p - 8);
                if (len < 0) len = b.Length - p - 8;         // streamed files leave the length open
                int frames = len / (2 * channels);
                var pcm = new byte[frames * 2];
                for (int i = 0; i < frames; i++) { pcm[2 * i] = b[p + 8 + i * 2 * channels]; pcm[2 * i + 1] = b[p + 9 + i * 2 * channels]; }
                OnListening?.Invoke(true, "test file"); OnListening?.Invoke(false, "test file");
                OnClip?.Invoke(pcm, rate, frames / (float)rate, "test-file");
                return true;
            }
            catch (Exception e) { Problem = e.Message; return false; }
        }

        static byte[] ToPcm16(float[] samples, int count)
        {
            var pcm = new byte[count * 2];
            for (int i = 0; i < count; i++)
            {
                short v = (short)Mathf.Clamp(Mathf.RoundToInt(samples[i] * 32767f), -32768, 32767);
                pcm[2 * i] = (byte)(v & 0xff); pcm[2 * i + 1] = (byte)((v >> 8) & 0xff);
            }
            return pcm;
        }

        void OnDisable() { if (OpenEars) SetOpenEars(false); else if (Listening) StopListening("disabled", false); }
    }
}
