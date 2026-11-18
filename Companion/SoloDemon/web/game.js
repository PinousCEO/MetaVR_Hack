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
  const ids=["속사","쌍발","관통","꽃 폭발","연쇄 번개","별 고리","감속 안개","거대 탄환","꿀차","보석 자석","","수호 방패"];
  const notes=["발사 간격이 줄어요","한 발을 더 쏴요","탄환이 다음 적까지 뚫어요","명중한 곳에서 꽃불이 터져요","근처 악마에게 번개가 옮겨가요","조준점 주변을 별이 지켜요","가까운 악마의 발이 느려져요","탄환이 더 커지고 강해져요","생명을 두 칸 회복해요","경험치 보석이 빠르게 모여요","","방패가 넓어지고 반격이 강해져요"];
  let mesh,ws,awaiting=false,restart=false,paused=false,pick=null,frame,previous,received=0,choiceKey="",saved=false,serial=0;
  let startBest=0,cueUntil=0,lastSound=0,ac,fx=[],held=new Set(),left=false,right=false,x=0,y=0;
  const wand=new Image();
  wand.src="/demon-gate/art/demon-wand.png";
  const ground=$("ground"),shade=ground.getContext("2d");
  let firedAt=-1000,fireX=0,fireY=0;
  try {startBest=Number(localStorage.getItem("demon-gate.best.v1"))||0;}catch{}
  if(startBest)$("record").textContent="최고 기록 "+startBest.toLocaleString()+"점";
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
    fire:left||held.has("KeyF"),shield:right||held.has("Space"),pause:paused,pick,restart};}
  function transmit(){
    if(!ws||ws.readyState!==WebSocket.OPEN||awaiting)return;
    const input=controls();ws.send(JSON.stringify(input));awaiting=true;pick=null;restart=false;
  }
  function resetInputs(){held.clear();left=false;right=false;}
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
      img.src="/demon-gate/art/demon-charm-"+String(id+1).padStart(2,"0")+".png";img.alt="";
      title.textContent=ids[id];desc.textContent=notes[id];key.textContent="숫자 "+(slot+1);b.append(img,title,desc,key);b.onclick=()=>choose(slot);cards.append(b);
    }
    cards.firstElementChild?.focus();sound(600,.3,"sine",.06,1.6);
  }
  function choose(slot){if(frame?.phase!==3||slot<0||slot>2)return;pick=slot;transmit();}
  function receive(f){
    const now=performance.now();
    if(frame?.phase!==3&&f.phase===3){paused=false;$("break").hidden=true;}
    const old=frame;previous=old;frame=f;received=now;
    $("over").hidden=f.phase!==4;if(f.phase!==4)saved=false;
    $("hud").hidden=false;$("welcome").hidden=true;
    $("hearts").textContent="♥".repeat(f.hp)+"♡".repeat(Math.max(0,f.maxHp-f.hp));
    $("hearts").setAttribute("aria-label","생명 "+f.hp);$("level").textContent="Lv. "+f.level;$("xp").max=f.need;$("xp").value=f.xp;
    const t=Math.ceil(Math.max(0,f.bossAt-f.time));$("timer-label").textContent=f.boss?"생존 시간":"보스까지";
    const clock=f.boss?Math.floor(f.time):t;$("clock").textContent=Math.floor(clock/60)+":"+String(clock%60).padStart(2,"0");
    const boss=f.monsters.find(m=>m[1]===5);$("boss").hidden=!boss||f.phase===4;if(boss)$("boss-hp").value=boss[10];
    if(!old||old.phase===4&&f.phase!==4)word("악마가 문을 열고 있어요",2100);
    for(const e of f.effects){
      const k=e[0];if(k===11)word("가시 왕이 나타났어요!",2000);if(k===18)word("악마 무리가 다가옵니다");
      if(k===4){firedAt=now;fireX=x;fireY=y;sound(530,.075,"triangle",.032,.48);}
      if(k===6){word("생명 −1",650);sound(155,.22,"triangle",.065,.46);}
      if(k===15)sound(330,.17,"sine",.06,2.2);
      if(k===5&&now-lastSound>80){sound(750,.12,"sine",.025,1.4);lastSound=now;}
      if([2,3,6,15,16,17,19].includes(k))fx.push({e,at:now,seed:++serial});
    }
    showChoices(f);
    if(f.phase===4){
      resetInputs();$("break").hidden=true;$("choice").hidden=true;$("over").hidden=false;
      if(!saved){saved=true;const best=Math.max(startBest,f.score);try{localStorage.setItem("demon-gate.best.v1",String(best));}catch{}startBest=best;
        $("ending").textContent=f.won?"문을 봉인했어요":"문이 무너졌어요";$("summary").textContent="처치 "+f.kills+" · 레벨 "+f.level+" · "+f.score.toLocaleString()+"점";
        $("again").focus();sound(f.won?450:210,.5,"triangle",.045,f.won?1.8:.6);}
    }
    stage.classList.toggle("fighting",!paused&&(f.phase===1||f.phase===2));
  }
  function play(){
    resetInputs();paused=false;saved=false;pick=null;restart=false;x=y=0;fx=[];choiceKey="";
    try{if(!ac)ac=new (window.AudioContext||window.webkitAudioContext)();ac.resume().catch(()=>{});}catch{}
    $("over").hidden=$("choice").hidden=$("break").hidden=true;$("error").textContent="";$("start").disabled=true;
    if(ws?.readyState===WebSocket.OPEN){restart=true;previous=frame=null;transmit();return;}
    ws=new WebSocket(location.origin.replace(/^http/,"ws")+"/demon-gate/play");const current=ws;
    ws.onopen=()=>{awaiting=false;transmit();};
    ws.onmessage=e=>{if(ws!==current)return;awaiting=false;receive(JSON.parse(e.data));};
    ws.onclose=()=>{if(ws!==current)return;ws=null;awaiting=false;resetInputs();frame=previous=null;
      $("welcome").hidden=false;$("over").hidden=$("break").hidden=$("choice").hidden=$("hud").hidden=true;
      $("error").textContent="연결이 끊겼어요. Companion이 실행 중이면 다시 시작할 수 있어요.";$("start").disabled=false;stage.classList.remove("fighting");};
  }
  $("start").onclick=$("again").onclick=play;$("pause").onclick=()=>pause(true);$("resume").onclick=()=>pause(false);
  function aim(e){x=Math.max(-1,Math.min(1,2*e.clientX/innerWidth-1));y=Math.max(-1,Math.min(1,1-2*e.clientY/innerHeight));}
  canvas.onpointermove=aim;
  canvas.onpointerdown=e=>{if(paused||!frame||frame.phase>2)return;aim(e);canvas.setPointerCapture(e.pointerId);if(e.button===0)left=true;if(e.button===2)right=true;e.preventDefault();};
  window.addEventListener("pointerup",e=>{if(e.button===0)left=false;if(e.button===2)right=false;});
  canvas.onpointercancel=resetInputs;canvas.oncontextmenu=e=>e.preventDefault();
  addEventListener("keydown",e=>{
    if(!frame)return;
    if(e.code==="Escape"&&!e.repeat){pause(!paused);return;}
    if(frame.phase===3&&/^Digit[123]$/.test(e.code)){e.preventDefault();choose(+e.code.slice(-1)-1);return;}
    if(paused||frame.phase>2)return;
    if(["Space","ArrowLeft","ArrowRight","KeyA","KeyD","KeyF"].includes(e.code)){e.preventDefault();held.add(e.code);}
  });
  addEventListener("keyup",e=>held.delete(e.code));addEventListener("blur",()=>{resetInputs();pause(true);});
  document.addEventListener("visibilitychange",()=>{if(document.hidden){resetInputs();pause(true);}});
  actors.addEventListener("webglcontextlost",e=>{e.preventDefault();pause(true);word("그래픽 장치를 다시 연결하는 중",4000);});
  actors.addEventListener("webglcontextrestored",async()=>{try{mesh=await demonModels(actors);word("그래픽을 복구했어요");}catch{location.reload();}});
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
  function draw(t){
    requestAnimationFrame(draw);size();if(!frame||!mesh)return;
    const w=canvas.width,h=canvas.height,v=view(),p=v.project,d=h/820;
    const fraction=Math.min(1,(t-received)/32);
    const monsters=frame.monsters.map(m=>{
      const last=previous?.monsters.find(b=>b[0]===m[0]);if(!last)return m;
      const row=m.slice();for(const i of [2,3,4,6])row[i]=last[i]+(m[i]-last[i])*fraction;return row;
    });
    shade.clearRect(0,0,w,h);
    for(const m of monsters){
      const floor=Math.min(.89,m[3]-m[5]),spot=p(m[2],floor,m[4]),height=Math.max(0,m[3]-m[5]-floor);
      const footprint=m[5]*spot[2]*(1+.65*height);
      shade.fillStyle="rgba(67,35,56,"+Math.max(.09,.28-height*.8)+")";
      shade.beginPath();shade.ellipse(spot[0]+footprint*.17,spot[1],footprint*.82,footprint*.18,-.06,0,Math.PI*2);shade.fill();
    }
    mesh.draw(monsters,v);brush.clearRect(0,0,w,h);
    $("backdrop").style.transform="scale(1.08) translateX("+(-v.x*8)+"%)";
    for(const b of frame.bolts){const end=p(b[0],b[1],b[2]),start=p(b[3],b[4],b[5]);line(start,end,"#fff0a4",Math.max(2*d,b[6]*end[2]));ring(end,2.2*d,"#ffe29c",1.4*d);}
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
      const shield=right||held.has("Space");if(shield){const radius=frame.shieldRadius*h/(.55*.8);
        brush.beginPath();brush.arc(cx,cy,radius,0,Math.PI*2);brush.fillStyle="#ffe2a212";brush.fill();
        brush.strokeStyle="#fff0c2";brush.lineWidth=2*d;
        for(let n=0;n<6;n++){const a=n*Math.PI/3+t*.0002;brush.beginPath();brush.arc(cx,cy,radius,a,a+.72);brush.stroke();}
      }
      ring([cx,cy],r,"#29202ac9",4*d);ring([cx,cy],r,"#fff0c4",1.6*d);
      for(let n=0;n<4;n++){const a=Math.PI*n/2;line([cx+Math.cos(a)*(r+2*d),cy+Math.sin(a)*(r+2*d)],[cx+Math.cos(a)*(r+7*d),cy+Math.sin(a)*(r+7*d)],"#f9e5af",2*d);}
    }
    if(t>cueUntil&&$("cue").textContent)$("cue").textContent="";
  }
  // Decode the card art during the title screen so every offered charm appears on its first frame.
  const charmArt=ids.map((name,id)=>{if(!name)return Promise.resolve();
    const image=new Image();image.src="/demon-gate/art/demon-charm-"+String(id+1).padStart(2,"0")+".png";
    return image.decode?image.decode():Promise.resolve();
  });
  size();Promise.all([demonModels(actors),...charmArt]).then(([renderer])=>{mesh=renderer;$("start").disabled=false;$("start").textContent="혼자 시작";}).catch(error=>{$("error").textContent=error.message;});
  setInterval(transmit,1000/36);requestAnimationFrame(draw);
})();

