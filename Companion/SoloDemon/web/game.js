"use strict";
(() => {
  const $=id=>document.getElementById(id),stage=$("stage"),actors=$("demons"),canvas=$("spell"),brush=canvas.getContext("2d");
  // The dedicated local server follows the lifetime of every open game tab, including its title screen.
  if(document.documentElement.hasAttribute("data-standalone")){
    let presence;
    function connectTab(){if(!presence||presence.readyState>WebSocket.OPEN)
      presence=new WebSocket(location.origin.replace(/^http/,"ws")+"/demon-gate/presence");}
    connectTab();addEventListener("pageshow",connectTab);
    addEventListener("pagehide",()=>{presence?.close();presence=null;});
  }
  const ids=["Rapid Fire","Twin Shot","Piercing Shot","Bloom Burst","Chain Lightning","Star Ring","Frost Mist","Giant Bolt","Honey Tea","Gem Magnet","Comet Shot"];
  const notes=["Fire more often","Launch an extra shot","Shoot through another enemy","Flowers burst on impact","Lightning arcs between enemies","Stars circle your aim point","Slow nearby demons","Fire larger, stronger shots","Recover two hearts","Attract experience gems","Fire comets at nearby enemies"];
  let mesh,ws,awaiting=false,restart=false,paused=false,pick=null,frame,received=0,choiceKey="",saved=false,serial=0;
  const priorById=new Map(), priorPoses=[], monsters=[], poseFields=[2,3,4,6];
  let startBest=0,cueUntil=0,lastSound=0,ac,fx=[],held=new Set(),left=false,x=0,y=0;
  const wand=new Image();
  wand.src="/demon-gate/art/demon-wand.png";
  const ground=$("ground"),shade=ground.getContext("2d");
  let firedAt=-1000,fireX=0,fireY=0;
  try {startBest=Number(localStorage.getItem("demon-gate.best.v1"))||0;}catch{}
  if(startBest)$("record").textContent="Best score "+startBest.toLocaleString()+" points";
  function view(){
    const w=canvas.width,h=canvas.height,unit=h/(2*.4),cx=frame?.playerX||0;
    return {x:cx,aspect:w/h,project:(px,py,pz)=>{const scale=unit/Math.max(.06,pz+.39);return [w/2+(px-cx)*scale,h/2-(py-1.06)*scale,scale];}};
  }
  function size(){
    const scale=Math.min(1.7,devicePixelRatio||1);
    const w=Math.max(1,Math.round(innerWidth*scale)),h=Math.max(1,Math.round(innerHeight*scale));
    if(canvas.width!==w||canvas.height!==h)for(const c of [ground,actors,canvas]){c.width=w;c.height=h;}
  }
  function word(s,ms=1300){$("cue").textContent=s;cueUntil=performance.now()+ms;}
  function sound(hz,duration,type="sine",volume=.025,slide=.8){
    if(!ac||ac.state!=="running")return;
    const at=ac.currentTime,o=ac.createOscillator(),g=ac.createGain();o.type=type;
    o.frequency.setValueAtTime(hz,at);o.frequency.exponentialRampToValueAtTime(hz*slide,at+duration);
    g.gain.setValueAtTime(volume,at);g.gain.exponentialRampToValueAtTime(.0001,at+duration);
    o.connect(g);g.connect(ac.destination);o.start(at);o.stop(at+duration);
  }
  function controls(){return {x,y,aspect:innerWidth/innerHeight,move:(held.has("KeyD")||held.has("ArrowRight")?1:0)-(held.has("KeyA")||held.has("ArrowLeft")?1:0),
    fire:left||held.has("KeyF"),pause:paused,pick,restart};}
  function transmit(){
    if(!ws||ws.readyState!==WebSocket.OPEN||awaiting)return;
    const input=controls();ws.send(JSON.stringify(input));awaiting=true;pick=null;restart=false;
  }
  function resetInputs(){held.clear();left=false;}
  function pause(on){
    if(!frame||frame.phase===4||frame.phase===3)return;
    paused=on;resetInputs();$("break").hidden=!on;stage.classList.toggle("fighting",!on);
    if(on)$("resume").focus();
  }
  function showChoices(f){
    const show=f.phase===3;$("choice").hidden=!show;
    if(!show){choiceKey="";return;}
    const key=f.level+":"+f.choices.join();if(key===choiceKey)return;choiceKey=key;resetInputs();
    const cards=$("cards");cards.replaceChildren();
    for(const [slot,id] of f.choices.entries()){
      const b=document.createElement("button"),img=new Image(),title=document.createElement("b"),desc=document.createElement("small"),key=document.createElement("kbd");
      img.src="/demon-gate/art/demon-charm-"+String(id===10?3:id+1).padStart(2,"0")+".png";img.alt="";
      title.textContent=ids[id];desc.textContent=notes[id];key.textContent="Key "+(slot+1);b.append(img,title,desc,key);b.onclick=()=>choose(slot);cards.append(b);
    }
    cards.firstElementChild?.focus();sound(600,.3,"sine",.06,1.6);
  }
  function choose(slot){if(frame?.phase!==3||slot<0||slot>2)return;pick=slot;transmit();}
  function receive(f){
    const now=performance.now();
    if(frame?.phase!==3&&f.phase===3){paused=false;$("break").hidden=true;}
    const old=frame;frame=f;received=now;
    priorById.clear();
    if(old)for(const m of old.monsters)priorById.set(m[0],m);
    for(let i=0;i<f.monsters.length;i++){
      const current=f.monsters[i], row=monsters[i]||(monsters[i]=[]);
      for(let field=0;field<current.length;field++)row[field]=current[field];
      priorPoses[i]=priorById.get(current[0])||current;
    }
    monsters.length=priorPoses.length=f.monsters.length;
    $("over").hidden=f.phase!==4;if(f.phase!==4)saved=false;
    $("hud").hidden=false;$("welcome").hidden=true;
    $("hearts").textContent="♥".repeat(f.hp)+"♡".repeat(Math.max(0,f.maxHp-f.hp));
    $("hearts").setAttribute("aria-label","Health "+f.hp);$("level").textContent="Lv. "+f.level;$("xp").max=f.need;$("xp").value=f.xp;
    const t=Math.ceil(Math.max(0,f.bossAt-f.time));$("timer-label").textContent=f.boss?"Survival time":"Boss in";
    const clock=f.boss?Math.floor(f.time):t;$("clock").textContent=Math.floor(clock/60)+":"+String(clock%60).padStart(2,"0");
    const boss=f.monsters.find(m=>m[1]===5);$("boss").hidden=!boss||f.phase===4;if(boss)$("boss-hp").value=boss[10];
    if(!old||old.phase===4&&f.phase!==4)word("The gate is opening",2100);
    for(const e of f.effects){
      const k=e[0];if(k===11)word("The Thorn King is here!",2000);if(k===18)word("Demons approaching");
      if(k===4){firedAt=now;fireX=x;fireY=y;sound(530,.075,"triangle",.032,.48);}
      if(k===6){word("Health −1",650);sound(155,.22,"triangle",.065,.46);}
      if(k===15)sound(330,.17,"sine",.06,2.2);
      if(k===5&&now-lastSound>80){sound(750,.12,"sine",.025,1.4);lastSound=now;}
      if([2,3,6,15,16,17,19].includes(k))fx.push({e,at:now,seed:++serial});
    }
    showChoices(f);
    if(f.phase===4){
      resetInputs();$("break").hidden=true;$("choice").hidden=true;$("over").hidden=false;
      if(!saved){saved=true;const best=Math.max(startBest,f.score);try{localStorage.setItem("demon-gate.best.v1",String(best));}catch{}startBest=best;
        $("ending").textContent=f.won?"Gate sealed":"The gate fell";$("summary").textContent="Defeated "+f.kills+" · Level "+f.level+" · "+f.score.toLocaleString()+" points";
        $("again").focus();sound(f.won?450:210,.5,"triangle",.045,f.won?1.8:.6);}
    }
    stage.classList.toggle("fighting",!paused&&(f.phase===1||f.phase===2));
  }
  function play(){
    resetInputs();paused=false;saved=false;pick=null;restart=false;x=y=0;fx=[];choiceKey="";
    try{if(!ac)ac=new (window.AudioContext||window.webkitAudioContext)();ac.resume().catch(()=>{});}catch{}
    $("over").hidden=$("choice").hidden=$("break").hidden=true;$("error").textContent="";$("start").disabled=true;
    if(ws?.readyState===WebSocket.OPEN){restart=true;frame=null;transmit();return;}
    ws=new WebSocket(location.origin.replace(/^http/,"ws")+"/demon-gate/play");const current=ws;
    ws.onopen=()=>{awaiting=false;transmit();};
    ws.onmessage=e=>{if(ws!==current)return;awaiting=false;receive(JSON.parse(e.data));};
    ws.onclose=()=>{if(ws!==current)return;ws=null;awaiting=false;resetInputs();frame=null;
      $("welcome").hidden=false;$("over").hidden=$("break").hidden=$("choice").hidden=$("hud").hidden=true;
      $("error").textContent="Disconnected. Reopen the game when the companion is available.";$("start").disabled=false;stage.classList.remove("fighting");};
  }
  $("start").onclick=$("again").onclick=play;$("pause").onclick=()=>pause(true);$("resume").onclick=()=>pause(false);
  function aim(e){x=Math.max(-1,Math.min(1,2*e.clientX/innerWidth-1));y=Math.max(-1,Math.min(1,1-2*e.clientY/innerHeight));}
  canvas.onpointermove=aim;
  canvas.onpointerdown=e=>{if(paused||!frame||frame.phase>2)return;aim(e);canvas.setPointerCapture(e.pointerId);if(e.button===0)left=true;e.preventDefault();};
  window.addEventListener("pointerup",e=>{if(e.button===0)left=false;});
  canvas.onpointercancel=resetInputs;canvas.oncontextmenu=e=>e.preventDefault();
  addEventListener("keydown",e=>{
    if(!frame)return;
    if(e.code==="Escape"&&!e.repeat){pause(!paused);return;}
    if(frame.phase===3&&/^Digit[123]$/.test(e.code)){e.preventDefault();choose(+e.code.slice(-1)-1);return;}
    if(paused||frame.phase>2)return;
    if(["ArrowLeft","ArrowRight","KeyA","KeyD","KeyF"].includes(e.code)){e.preventDefault();held.add(e.code);}
  });
  addEventListener("keyup",e=>held.delete(e.code));addEventListener("blur",()=>{resetInputs();pause(true);});
  document.addEventListener("visibilitychange",()=>{if(document.hidden){resetInputs();pause(true);}});
  actors.addEventListener("webglcontextlost",e=>{e.preventDefault();pause(true);word("Reconnecting graphics",4000);});
  actors.addEventListener("webglcontextrestored",async()=>{try{mesh=await demonModels(actors);word("Graphics restored");}catch{location.reload();}});
  function line(a,b,color,width){
    brush.beginPath();brush.strokeStyle=color;brush.lineWidth=width;brush.moveTo(a[0],a[1]);brush.lineTo(b[0],b[1]);brush.stroke();
  }
  function ring(p,r,color,width){
    brush.strokeStyle=color;brush.lineWidth=width;brush.beginPath();brush.arc(p[0],p[1],Math.max(.1,r),0,2*Math.PI);brush.stroke();
  }
  function drawWand(t,w,h,d) {
    if(!wand.complete||!wand.naturalWidth)return;
    const age=Math.max(0,t-firedAt), kick=Math.max(0,1-age/180);
    const size=Math.min(w*.31,h*.405), sway=Math.sin(t*.0023)*2*d;
    const ox=w-size*.86+x*size*.06, oy=h-size*.80-y*size*.04+sway+kick*kick*12*d;
    brush.drawImage(wand,ox,oy,size,size);
    if(age>=165)return;
    const tip=[ox+size*.075,oy+size*.14], aim=[(fireX+1)*w/2,(1-fireY)*h/2];
    const dx=aim[0]-tip[0],dy=aim[1]-tip[1],len=Math.max(1,Math.hypot(dx,dy));
    const u=age/165, distance=Math.min(len,160*d);
    const tail=Math.max(0,u-.36)*distance,head=(.18+.82*u)*distance;
    const a=[tip[0]+dx/len*tail,tip[1]+dy/len*tail],b=[tip[0]+dx/len*head,tip[1]+dy/len*head];
    brush.save();brush.globalAlpha=1-u;line(a,b,"#b95035",8*d);line(a,b,"#ffdf84",4*d);
    const r=(1-u)*13*d;brush.fillStyle="#fff0b9";brush.beginPath();
    for(let i=0;i<8;i++){const angle=i*Math.PI/4,rad=i%2?r*.30:r;brush.lineTo(tip[0]+Math.cos(angle)*rad,tip[1]+Math.sin(angle)*rad);}
    brush.closePath();brush.fill();brush.restore();
  }
  function drawBolt(b, project, d, t){
    const tip=project(b[0],b[1],b[2]),prev=project(b[3],b[4],b[5]);
    const dx=tip[0]-prev[0],dy=tip[1]-prev[1],comet=b[7]===1;
    const r=Math.max(3*d,Math.min(15*d,b[6]*tip[2])),tail=[tip[0]-dx*4,tip[1]-dy*4];
    brush.save();brush.lineCap="round";brush.globalCompositeOperation="lighter";
    const ribbon=brush.createLinearGradient(tail[0],tail[1],tip[0]+.01,tip[1]);
    ribbon.addColorStop(0,"transparent");ribbon.addColorStop(1,comet?"#e35eaa":"#f58c37");
    line(tail,tip,ribbon,r*1.8);line([tip[0]-dx*1.6,tip[1]-dy*1.6],tip,"#fff3ca",r*.48);
    brush.translate(tip[0],tip[1]);brush.rotate(t*.003);
    const glow=brush.createRadialGradient(0,0,0,0,0,3*r);
    glow.addColorStop(0,"#fff9df");glow.addColorStop(.28,comet?"#ffb5cb":"#ffd779");
    glow.addColorStop(1,"transparent");brush.fillStyle=glow;brush.fillRect(-3*r,-3*r,6*r,6*r);
    brush.fillStyle="#fff3ce";brush.beginPath();
    for(let i=0;i<8;i++){const a=i*Math.PI/4,size=r*(i%2?.36:2.7);brush.lineTo(Math.cos(a)*size,Math.sin(a)*size);}
    brush.fill();brush.restore();
  }
  function draw(t){
    requestAnimationFrame(draw);size();if(!frame||!mesh)return;
    const w=canvas.width,h=canvas.height,v=view(),p=v.project,d=h/820;
    const fraction=Math.max(0,Math.min(1,(t-received)/32));
    for(let i=0;i<monsters.length;i++){
      const last=priorPoses[i], current=frame.monsters[i], row=monsters[i];
      for(const field of poseFields)row[field]=last[field]+(current[field]-last[field])*fraction;
    }
    shade.clearRect(0,0,w,h);
    for(const m of monsters){
      const floor=Math.min(.89,m[3]-m[5]),spot=p(m[2],floor,m[4]),height=Math.max(0,m[3]-m[5]-floor);
      const footprint=m[5]*spot[2]*(1+.65*height);
      shade.fillStyle="rgba(67,35,56,"+Math.max(.09,.28-height*.8)+")";
      shade.beginPath();shade.ellipse(spot[0]+footprint*.17,spot[1],footprint*.82,footprint*.18,-.06,0,Math.PI*2);shade.fill();
    }
    mesh.draw(monsters,v);brush.clearRect(0,0,w,h);
    $("backdrop").style.transform="scale(1.08) translateX("+(-v.x*8)+"%)";
    for(const bolt of frame.bolts)drawBolt(bolt,p,d,t);
    for(const gems of [frame.gems,frame.orbit])for(const gem of gems){
      const g=p(...gem),r=Math.min(12*d,Math.max(3*d,g[2]*.008));brush.fillStyle=gems===frame.gems?"#94eed1":"#ffe09f";brush.beginPath();brush.moveTo(g[0],g[1]-r);brush.lineTo(g[0]+r*.72,g[1]);brush.lineTo(g[0],g[1]+r);brush.lineTo(g[0]-r*.72,g[1]);brush.closePath();brush.fill();
    }
    fx=fx.filter(f=>t-f.at<410);
    for(const {e,at,seed} of fx){
      const age=Math.max(0,(t-at)/410),pos=p(e[1],e[2],e[3]),color=e[0]===15?"#a2f1ec":e[0]===6?"#f2a1ac":"#ffcf93";
      brush.globalAlpha=1-age;
      if(e[0]===17)line(pos,p(e[4],e[5],e[6]),"#e3d1ff",4*d);
      else{
        const r=(4+age*(e[0]===15?68:30))*d;
        ring(pos,r,color,Math.max(1,(1-age)*3*d));for(let n=0;n<6;n++){const a=n*Math.PI/3+seed;
          line([pos[0]+Math.cos(a)*r,pos[1]+Math.sin(a)*r],[pos[0]+Math.cos(a)*(r+5*d),pos[1]+Math.sin(a)*(r+5*d)],color,2*d);}
      }brush.globalAlpha=1;
    }
    if(frame.phase<3&&!paused){
      const cx=(x+1)*w*.5,cy=(1-y)*h*.5,r=11*d;
      drawWand(t,w,h,d);
      ring([cx,cy],r,"#29202ac9",4*d);ring([cx,cy],r,"#fff0c4",1.6*d);
      for(let n=0;n<4;n++){const a=Math.PI*n/2;line([cx+Math.cos(a)*(r+2*d),cy+Math.sin(a)*(r+2*d)],[cx+Math.cos(a)*(r+7*d),cy+Math.sin(a)*(r+7*d)],"#f9e5af",2*d);}
    }
    if(t>cueUntil&&$("cue").textContent)$("cue").textContent="";
  }
  // Decode the card art during the title screen so every offered charm appears on its first frame.
  const charmArt=ids.map((name,id)=>{if(!name)return Promise.resolve();
    const image=new Image();image.src="/demon-gate/art/demon-charm-"+String(id===10?3:id+1).padStart(2,"0")+".png";
    return image.decode?image.decode():Promise.resolve();
  });
  size();Promise.all([demonModels(actors),...charmArt]).then(([renderer])=>{mesh=renderer;$("start").disabled=false;$("start").textContent="Play solo";}).catch(error=>{$("error").textContent=error.message;});
  setInterval(transmit,1000/36);requestAnimationFrame(draw);
})();

