(() => {
  const canvas = document.getElementById('glass-game');
  const ctx = canvas.getContext('2d');
  const cache = new Map();
  const ballFrames = new Map();
  const ballNormals = [];
  const phi = (1 + Math.sqrt(5)) / 2;
  for (const a of [-1, 1]) for (const b of [-phi, phi]) {
    for (const xyz of [[0, a, b], [a, b, 0], [b, 0, a]]) {
      ballNormals.push(xyz.map(v => v / Math.hypot(1, phi)));
    }
  }
  function football(time) {
    const frame = Math.floor(time / 34) % 64;
    if (ballFrames.has(frame)) return ballFrames.get(frame);
    const tex = document.createElement('canvas');
    tex.width = tex.height = 96;
    const context = tex.getContext('2d'), pixels = context.createImageData(96, 96);
    const a = frame * Math.PI / 32, c = Math.cos(a), s = Math.sin(a);
    for (let row = 0; row < 96; row++) for (let col = 0; col < 96; col++) {
      const x = (col - 47.5) / 47, y = (47.5 - row) / 47, r = x*x + y*y;
      if (r > 1) continue;
      const z = Math.sqrt(1-r), rx = x*c-z*s, rz=x*s+z*c;
      const closest = Math.max(...ballNormals.map(n => n[0]*rx+n[1]*y+n[2]*rz));
      const base = closest > .93 ? 38 : 248;
      const light = .55 + .44 * Math.max(0,-.45*x+.66*y+.60*z);
      const off = (row*96+col)*4;
      for (let k = 0; k < 3; k++) pixels.data[off+k] = base*light + (k === 2 ? 5 : 0);
      pixels.data[off+3] = Math.min(255,(1-r)*6500);
    }
    context.putImageData(pixels,0,0); ballFrames.set(frame,tex); return tex;
  }
  function picture(url) {
    if (!cache.has(url)) { const img = new Image(); img.src = url; cache.set(url,img); }
    const img = cache.get(url);
    return img.naturalWidth ? img : null;
  }
  const textures = new Set('enemy flyer brute gem shot orb ring bolt puff heart drop blob gold splash star sparkle frost board stone-a stone-b cell rock paper-sign scissors'.split(' '));
  function text(value,x,y,size,color='#fff',maxWidth=canvas.width) {
    if (!value) return;
    ctx.fillStyle=color; ctx.font='600 '+Math.max(10,size)+'px system-ui,sans-serif'; ctx.textAlign='center'; ctx.textBaseline='middle';
    ctx.fillText(value,x,y,maxWidth);
  }
  window.drawGlassGame = g => {
    canvas.hidden=!g;
    if (!g) { canvas.style.display='none'; return; }
    canvas.style.cssText='display:block;position:absolute;inset:0;width:100%;height:100%;pointer-events:none';
    const width = Math.round(canvas.clientWidth), height = Math.round(canvas.clientHeight);
    if (!width || !height) return;
    if (canvas.width!==width || canvas.height!==height) { canvas.width=width; canvas.height=height; }
    const u=height*g.scale, X=x=>width/2+(x-g.w/2)*u, Y=y=>height*(1-g.floor)-y*u;
    const bg=ctx.createLinearGradient(0,0,0,height);
    bg.addColorStop(0,'#08152c'); bg.addColorStop(1,'#20394d');
    ctx.fillStyle=bg; ctx.fillRect(0,0,width,height);
    for (const p of g.sprites) {
      const h=Math.max(1,p.size*u), w=p.w>0?p.w*u:h;
      ctx.save(); ctx.globalAlpha=Math.max(0,Math.min(1,p.alpha)); ctx.translate(X(p.x),Y(p.y)); ctx.rotate(-p.rot*Math.PI/180);
      ctx.fillStyle=p.color>=0?'#'+p.color.toString(16).padStart(6,'0'):'#fbe6a6';
      if (p.art==='football'||p.art==='pack:soccerball'||p.art==='pack:football') ctx.drawImage(football(performance.now()),-h/2,-h/2,h,h);
      else if (p.art==='label') text(p.text,0,0,h,ctx.fillStyle);
      else if (p.art==='card') {
        ctx.fillStyle='#314966'; ctx.beginPath(); ctx.roundRect(-w/2,-h/2,w,h,h*.12); ctx.fill();
        text(p.text,0,-h*.13,h*.19,'#fff',w*.94); text(p.text2,0,h*.18,h*.14,'#cfe6ef',w*.94);
      } else if (p.art==='rect'||p.art==='paper') {
        ctx.beginPath(); ctx.roundRect(-w/2,-h/2,w,h,p.art==='paper'?Math.min(w,h)*.16:0); ctx.fill();
      } else {
        const img=textures.has(p.art)?picture('/art/glass-'+p.art+'.png'):null;
        if (img) { const iw=p.w>0?w:h*img.width/img.height; ctx.drawImage(img,-iw/2,-h/2,iw,h); }
        else { ctx.beginPath(); ctx.arc(0,0,h/2,0,2*Math.PI); if(p.art==='hoop') {ctx.strokeStyle=ctx.fillStyle;ctx.lineWidth=Math.max(1,h*.09);ctx.stroke();} else ctx.fill(); }
      }
      ctx.restore();
    }
    const bambi=picture('/nilo.png'), petHeight=.20*u;
    if (bambi && g.pet.visible !== false) {
      const petWidth=petHeight*bambi.width/bambi.height;
      ctx.save(); ctx.translate(X(g.pet.x),Y(g.pet.y)); if (!g.pet.right) ctx.scale(-1,1);
      ctx.drawImage(bambi,-petWidth/2,-petHeight,petWidth,petHeight); ctx.restore();
    }
    text(g.pet.line,X(g.pet.x),Y(g.pet.y)-petHeight-16,height*.022,'#ffe8b9',width*.46);
    for (const hand of g.hands) {
      ctx.strokeStyle='#abfff0'; ctx.lineWidth=2; ctx.beginPath(); ctx.arc(X(hand.x),Y(hand.y),Math.max(7,height*.014),0,2*Math.PI); ctx.stroke();
    }
    ctx.fillStyle='rgba(5,13,26,.82)'; ctx.fillRect(0,0,width,height*.07);
    text(g.title,width*.23,height*.035,height*.028,'#fff',width*.43);
    text('Score '+g.score+'   ♥ '+g.lives+'   Lv. '+g.level,width*.77,height*.035,height*.025,'#ffebb6',width*.42);
    text(g.result||g.banner,width*.5,height*.11,height*.027,'#fff',width*.9);
  };
})();
