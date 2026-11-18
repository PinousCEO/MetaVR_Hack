# Makes the recordings used by tools\Run-VoiceChecks.ps1: synthetic speech of a "user" (OpenAI text-to-speech, a voice
# different from the partner's), saved as 16 kHz 16-bit mono WAV under samples\voice. They stand in for a person
# speaking into the microphone on machines where nobody is there to speak; every result obtained with them is labelled
# "test-file". Needs OPENAI_API_KEY in the environment (tools\Run-VoiceChecks.ps1 -MakeSamples passes the stored key).
import json, os, struct, sys, urllib.request

KEY = os.environ["OPENAI_API_KEY"]
out = sys.argv[1]
os.makedirs(out, exist_ok=True)
LINES = {
    "en-timer": "Momo, make a five minute timer and start it, please.",
    "ko-chat": "모모야 안녕, 오늘 기분이 어때?",
    "ko-enter": "모모야, 저기 화면 안으로 들어가 볼래?",
    "en-comeback": "Okay Momo, come back out to the desk.",
}
for name, text in LINES.items():
    body = {"model": "gpt-4o-mini-tts", "voice": "ash", "input": text, "response_format": "pcm", "instructions": "An adult speaking naturally to a small companion on the desk."}
    req = urllib.request.Request("https://api.openai.com/v1/audio/speech", data=json.dumps(body).encode(), headers={"Authorization": "Bearer " + KEY, "Content-Type": "application/json"})
    pcm = urllib.request.urlopen(req, timeout=60).read()                  # 24 kHz, 16-bit, mono
    n = len(pcm) // 2
    src = struct.unpack("<%dh" % n, pcm[: n * 2])
    m = n * 2 // 3                                                         # 24 kHz -> 16 kHz, linear interpolation
    dst = []
    for i in range(m):
        x = i * 1.5; j = int(x); f = x - j
        a = src[j]; b = src[min(j + 1, n - 1)]
        dst.append(int(a + (b - a) * f))
    data = struct.pack("<%dh" % m, *dst)
    with open(os.path.join(out, name + ".wav"), "wb") as f:
        f.write(b"RIFF" + struct.pack("<I", 36 + len(data)) + b"WAVEfmt " + struct.pack("<IHHIIHH", 16, 1, 1, 16000, 32000, 2, 16) + b"data" + struct.pack("<I", len(data)) + data)
    with open(os.path.join(out, name + ".txt"), "w", encoding="utf-8") as f:
        f.write(text + "\n")
    print(name, "%.1fs" % (m / 16000.0), text)
