const el=id=>document.getElementById(id);
const board=el('board'),canvas=board.querySelector('canvas'),ctx=canvas.getContext('2d');
const viewer=crypto.randomUUID(), inset=.052;
let state={},registered='',lastLayout=0,layoutReady=false,latest=-1,frameKey='',busy=false,detector,detectorPromise,closed=false,visionError='';
let displaysKey='',lastPaint='',embedded=false;
const stoneArt=new Image();
stoneArt.onload=()=>{lastPaint='';boardPaint(state);};
stoneArt.src='/table/stones.png';
try{embedded=window.parent!==window&&window.parent.location.origin===location.origin;}catch{}
function full(){const d=embedded?window.parent.document:document;return !!(d.fullscreenElement||d.webkitFullscreenElement);}
const post=(action,data,keepalive=false)=>fetch('/table/api/'+action,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(data),keepalive});
const context=()=>({viewer,monitorId:el('monitor').value,round:state.round,revision:state.revision});
function geometry(){
  const r=canvas.getBoundingClientRect();
  return {viewer,monitorId:el('monitor').value,full:full()&&!document.hidden,left:(r.left+r.width*inset)/innerWidth,top:(r.top+r.height*inset)/innerHeight,
    width:r.width*(1-2*inset)/innerWidth,height:r.height*(1-2*inset)/innerHeight};
}
async function register(force=false){
  const g=geometry(),k=JSON.stringify(g);
  if(!g.full&&!registered)return false;
  if(!force&&registered===k&&performance.now()-lastLayout<800)return layoutReady;
  lastLayout=performance.now();registered=k;
  layoutReady=(await post('layout',g)).ok;
  return g.full&&layoutReady;
}
async function button(action){const r=await post(action,context());if(!r.ok)el('help').textContent=await r.text();}
el('start').onclick=async()=>{
  try{
    if(!full()){const p=document.documentElement;if(p.requestFullscreen)await p.requestFullscreen();else if(p.webkitRequestFullscreen)p.webkitRequestFullscreen();else throw Error('이 브라우저에서 전체 화면을 열 수 없어요.');}
    if(await register(true))await button('start');else el('help').textContent='선택한 TV 위치를 헤드셋에서 확인하세요.';
  }catch(e){el('help').textContent=e.message;}
};
el('close').onclick=()=>button('stop');
el('restart').onclick=()=>button('restart');
window.addEventListener('pagehide',()=>{closed=true;detector?.close();if(registered)void post('layout',{...geometry(),full:false},true);});
function boardPaint(s){
  const size=Math.max(120,Math.round(canvas.clientWidth*Math.min(devicePixelRatio,2)));
  const key=[size,s.round,s.revision,s.hover,Math.floor(s.dwell*40)].join(',');
  if(key===lastPaint)return;lastPaint=key;
  if(canvas.width!==size||canvas.height!==size){canvas.width=size;canvas.height=size;}else ctx.clearRect(0,0,size,size);
  const p=size*inset,step=(size-2*p)/14,at=n=>p+n*step;
  ctx.strokeStyle='#644426';ctx.lineWidth=Math.max(.9,size*.0012);ctx.beginPath();
  for(let i=0;i<15;i++){ctx.moveTo(at(i),p);ctx.lineTo(at(i),at(14));ctx.moveTo(p,at(i));ctx.lineTo(at(14),at(i));}ctx.stroke();
  ctx.fillStyle='#674928';
  for(const [x,y] of [[3,3],[11,3],[7,7],[3,11],[11,11]]){ctx.beginPath();ctx.arc(at(x),at(y),step*.09,0,Math.PI*2);ctx.fill();}
  ctx.font=Math.round(step*.29)+'px sans-serif';ctx.textAlign='center';ctx.textBaseline='middle';
  for(let i=0;i<15;i++){ctx.fillText('ABCDEFGHJKLMNOP'[i],at(i),p*.48);ctx.fillText(String(15-i),p*.46,at(i));}
  for(let i=0;i<(s.stones||[]).length;i++){
    const stone=s.stones[i];if(!stone)continue;
    const x=at(i%15),y=at(Math.floor(i/15)),r=step*.45;
    ctx.save();
    ctx.shadowBlur=r*.12;ctx.shadowOffsetX=r*.065;ctx.shadowOffsetY=r*.14;ctx.shadowColor='#684529bd';
    if(stoneArt.complete&&stoneArt.naturalWidth){
      const tile=stoneArt.naturalWidth/2,side=r*2.21;
      ctx.drawImage(stoneArt,(stone-1)*tile,0,tile,stoneArt.naturalHeight,x-side/2,y-side/2,side,side);
    }else{
      ctx.fillStyle=stone===1?'#302534':'#ffe8c2';ctx.beginPath();ctx.arc(x,y,r,0,Math.PI*2);ctx.fill();
    }
    ctx.restore();
    if(i===s.last){ctx.strokeStyle=stone===1?'#eec57c':'#785c3d';ctx.lineWidth=step*.049;ctx.beginPath();ctx.arc(x,y,r*.38,0,Math.PI*2);ctx.stroke();}
  }
  if(s.turn&&s.hover>=0){
    const x=at(s.hover%15),y=at(Math.floor(s.hover/15)),r=step*.45;
    ctx.strokeStyle=s.stones?.[s.hover]?'#ac3e27':s.turn===1?'#314439':'#fffcef';ctx.lineWidth=step*.079;
    ctx.setLineDash([step*.16,step*.09]);ctx.beginPath();ctx.arc(x,y,r,0,Math.PI*2);ctx.stroke();ctx.setLineDash([]);
    if(s.turn===2){ctx.strokeStyle='#187457';ctx.beginPath();ctx.arc(x,y,r*1.2,-Math.PI/2,-Math.PI/2+2*Math.PI*s.dwell);ctx.stroke();}
  }
}
function update(s){
  state=s;const options=s.displays.filter(d=>d.calibrated),key=JSON.stringify(options),select=el('monitor');
  if(key!==displaysKey){displaysKey=key;const previous=select.value;select.replaceChildren(...options.map(d=>new Option(d.name||d.id,d.id)));
    if(options.some(d=>d.id===previous))select.value=previous;}
  if((s.waiting||s.active)&&options.some(d=>d.id===s.monitorId))select.value=s.monitorId;
  const playing=s.active&&full()&&select.value===s.monitorId&&layoutReady;
  el('setup').hidden=playing;el('start').disabled=!options.length||!s.connected;
  el('who').textContent=s.active?(s.turn===1?'흑 · 헤드셋 착용자':s.turn===2?'백 · 맞은편 사람':s.status):'대국 준비';
  el('count').textContent=s.active?s.moves+'수':'';
  el('close').hidden=!playing;el('restart').hidden=!playing||s.turn!==0;
  el('guide').textContent=playing?(s.turn===2&&(s.guestNotice||visionError)||s.status):'';
  el('help').textContent=!s.connected?'Quest와 컴패니언을 먼저 연결하세요.':!options.length?'Quest에서 TV 화면의 위치를 확인하세요.':s.waiting?s.status:'이 창을 실제 TV로 옮기고 시작하세요.';
  boardPaint(s);
}
async function detect(s){
  if(busy||closed||!layoutReady||!full()||document.hidden||s.turn!==2||!s.active)return;
  busy=true;const key=s.round+':'+s.revision;
  try{
    if(!detectorPromise)detectorPromise=import('/table/pointing.mjs').then(m=>m.createPointingDetector());
    detector=await detectorPromise;
    if(frameKey!==key){latest=-1;frameKey=key;detector.reset();}
    const result=await fetch('/table/api/frame?viewer='+viewer+'&after='+latest,{cache:'no-store'});
    if(result.status!==200)return;
    const frame=await result.json();latest=frame.seq;
    const where=await detector.detect(frame);
    if(!closed&&state.active&&state.turn===2&&state.round===s.round&&state.revision===s.revision)
      await post('guest',{viewer,monitorId:s.monitorId,round:s.round,revision:s.revision,seq:frame.seq,u:where?.u??null,v:where?.v??null});
    visionError='';
  }catch{visionError='손 인식을 준비하지 못했어요. 브라우저에서 이 페이지를 다시 여세요.';}
  finally{busy=false;}
}
async function poll(){
  if(closed)return;
  try{
    const s=await fetch('/table/api/state',{cache:'no-store'}).then(r=>r.json());update(s);
    await register();
    if(s.turn===2)void detect(s);else if(frameKey){frameKey='';latest=-1;detector?.reset();visionError='';}
  }catch{el('help').textContent='컴패니언과 다시 연결하는 중이에요.';}
  if(!closed)setTimeout(poll,document.hidden?650:state.active?90:250);
}
poll();
