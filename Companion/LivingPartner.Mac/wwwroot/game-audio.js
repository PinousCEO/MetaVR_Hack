(() => {
  const buffers = new Map();
  let context, output, run, lastId = -1;
  function unlock(event) {
    if (!event.isTrusted) return;
    if (!context) {
      const Audio = window.AudioContext || window.webkitAudioContext;
      if (!Audio) return;
      context = new Audio();
      output = context.createGain();
      output.connect(context.destination);
    }
    if (context.state !== 'running') context.resume().catch(() => {});
  }
  for (const event of ['click','pointerdown','keydown']) document.addEventListener(event, unlock, true);

  function buffer(name) {
    if (!buffers.has(name)) {
      const pending = fetch('/game-sfx/' + encodeURIComponent(name)).then(response => {
        if (!response.ok) throw Error('Audio unavailable');
        return response.arrayBuffer();
      }).then(bytes => context.decodeAudioData(bytes)).catch(() => { buffers.delete(name); return null; });
      buffers.set(name, pending);
    }
    return buffers.get(name);
  }

  const levels = {tick:.32, squeak:.28, drop:.4, 'ta-da':.62, lose:.62};
  window.playGlassSounds = g => {
    const game = g?.runId;
    if (run !== game) { run = game; lastId = -1; }
    const audible = !!g && !g.quiet && !document.hidden;
    if (output) output.gain.value = audible ? 1 : 0;
    if (!g) return;
    for (const sound of g.sounds || []) {
      if (sound.id <= lastId) continue;
      lastId = sound.id;
      const age = Math.max(0, g.now - sound.at);
      if (!audible || context?.state !== 'running' || age > .65) continue;
      const deadline = performance.now() + 1000 * (.65 - age);
      buffer(sound.name).then(samples => {
        if (!samples || run !== game || !output.gain.value || document.hidden || performance.now() > deadline) return;
        const source = context.createBufferSource(), volume = context.createGain();
        source.buffer = samples;
        volume.gain.value = levels[sound.name] ?? .50;
        source.connect(volume); volume.connect(output);
        source.onended = () => { source.disconnect(); volume.disconnect(); };
        source.start();
      });
    }
  };
})();
