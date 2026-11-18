"use strict";
const el = id => document.getElementById(id);
let state, previousGame = false, registered = false, nextPoll, lastScreen;
const full = () => !!(document.fullscreenElement || document.webkitFullscreenElement);
const post = async (path, value) => {
  const r = await fetch(path, {method:"POST", headers:{"Content-Type":"application/json"}, body:value === undefined ? undefined : JSON.stringify(value)});
  if (!r.ok) { const message = await r.text(); throw new Error(message.replace(/^"|"$/g, "") || "Mac 연결을 확인해 주세요."); }
};
async function act(cmd) {
  try { await post("/action/"+encodeURIComponent(cmd)); el("error").textContent=""; }
  catch (error) { el("error").textContent=error.message; }
}
for (const button of document.querySelectorAll("[data-command]"))
  button.addEventListener("click", () => act(button.dataset.command));
el("play").onclick = () => {
  if (el("games").value==="game-swarm") { location.assign("/demon-gate/"); return; }
  act("game:"+el("games").value);
};
el("travel").onclick = () => act(state?.owner === "pc" ? "return" : "enter");
el("games").onchange = () => { el("demon-cover").hidden=el("games").value!=="game-swarm"; };
el("speak").onchange = () => act(el("speak").checked ? "speech-on" : "speech-off");
el("talk").onsubmit = async event => {
  event.preventDefault();
  try { await post("/utter", {text:el("words").value}); el("words").value=""; el("error").textContent=""; }
  catch (error) { el("error").textContent=error.message; }
};
function fold(value) { el("panel").hidden=value; el("unfold").hidden=!value; }
el("fold").onclick=()=>fold(true);
el("unfold").onclick=()=>fold(false);
let jobsKey="";
el("queue-toggle").onclick=()=>{
  el("jobs").hidden=!el("jobs").hidden;
  el("queue-toggle").setAttribute("aria-expanded",String(!el("jobs").hidden));
};
async function workAction(cmd,id) {
  try { await post("/work/"+encodeURIComponent(cmd)+"/"+encodeURIComponent(id)); }
  catch(error) { fold(false); el("error").textContent=error.message; }
}
function drawWork(jobs=[]) {
  el("queue").hidden=!jobs.length;
  const active=jobs.filter(j=>j.status==="working"||j.status==="queued");
  el("queue-toggle").textContent="작업 "+(active.length?active.length+"개 진행 중":"결과 "+jobs.length+"개");
  const key=JSON.stringify(jobs); if(key===jobsKey) return; jobsKey=key;
  const list=el("jobs"); list.replaceChildren();
  const addButton=(parent,label,action,id)=>{const b=document.createElement("button"); b.textContent=label;b.onclick=()=>workAction(action,id);parent.append(b);};
  const status={working:"진행 중",queued:"대기 중",done:"완료",failed:"실패",cancelled:"취소됨"};
  for(const j of jobs){
    const row=document.createElement("article"),title=document.createElement("strong"),step=document.createElement("p");
    title.textContent=j.title; step.textContent=[...new Set([status[j.status],j.step,j.note].filter(Boolean))].join(" · ");row.append(title,step);
    if(j.status==="working"||j.status==="queued"){
      const bar=document.createElement("progress");bar.max=1;bar.value=j.progress;row.append(bar);addButton(row,"취소","cancel",j.id);
    } else if(j.status==="done"){
      addButton(row,"열기","open",j.id);
      for(const f of j.files||[]){if(!f.url?.startsWith("/work-file/"))continue;const a=document.createElement("a");a.textContent=f.name;a.href=f.url;a.download=f.name;row.append(a);}
    }
    list.append(row);
  }
  if(jobs.length>active.length)addButton(list,"완료된 작업 지우기","clear","all");
}
el("fold").onclick = () => fold(true); el("unfold").onclick = () => fold(false);
function screenInfo() {
  return {id:el("display").value, width:Math.round(innerWidth*devicePixelRatio),height:Math.round(innerHeight*devicePixelRatio),inches:Number(el("inches").value)||0,active:full()};
}
async function updateScreen() {
  const s=screenInfo();
  if (!s.active && !registered) return;
  const key=JSON.stringify(s); if (key === lastScreen) return;
  try { await post("/display",s); lastScreen=key; registered=s.active; el("error").textContent=""; }
  catch (error) { el("error").textContent=error.message; }
  if (!s.active) fold(false);
}
el("fullscreen").onclick = async () => {
  try {
    const page=document.documentElement;
    if (page.requestFullscreen) await page.requestFullscreen(); else if(page.webkitRequestFullscreen) page.webkitRequestFullscreen();
    else throw new Error("이 브라우저에서 전체 화면을 열 수 없어요. Safari 또는 Chrome에서 열어 주세요.");
    await updateScreen();
  } catch (error) { el("error").textContent=error.message; }
};
document.addEventListener("fullscreenchange", updateScreen);
document.addEventListener("webkitfullscreenchange", updateScreen);
window.addEventListener("resize", updateScreen);
el("display").onchange=updateScreen; el("inches").onchange=updateScreen;
function draw(s) {
  state=s;
  const table=el("gomoku-board"),showTable=s.active&&s.tableMonitor===el("display").value;
  if(showTable&&!table.getAttribute("src"))table.src="/table/";
  if(!showTable&&table.getAttribute("src"))table.removeAttribute("src");
  table.hidden=!showTable;
  document.body.classList.toggle("table-open",showTable);
  document.body.classList.toggle("connected",s.connected);
  el("connection").textContent=s.connected ? (s.device || "Quest")+" 연결됨" : "같은 Wi-Fi에서 Quest를 연결하세요";
  el("pair").hidden=s.connected; el("code").textContent=s.pairCode;
  el("setup").hidden=s.active; el("controls").hidden=!s.connected || !s.active;
  el("fold").hidden=!s.active || !s.connected;
  el("travel").textContent=s.owner === "pc" ? "밤비를 방으로 부르기" : "밤비를 TV로 보내기";
  el("travel").disabled=!s.calibrated || s.portal;
  el("instruction").textContent=s.calibrated ? "TV 위치가 맞춰졌어요." : "헤드셋으로 TV를 보고 위치를 확인해 주세요.";
  const game=!!s.game||showTable;
  el("speak").checked=s.speak;
  if (s.active && (game && !previousGame || s.portal && !el("panel").hidden)) fold(true);
  previousGame=game;
  const b=el("bambi"); b.hidden=!s.active||s.opacity<=0||game;
  b.style.left=(100*s.x)+"%"; b.style.top=(100*s.y)+"%"; b.style.height=(100*s.height)+"%"; b.style.opacity=s.opacity;
  el("portal").hidden=!s.portal; el("portal").style.left=(s.u*100)+"%"; el("portal").style.top=(s.v*100)+"%";
  const flash=el("flash"); flash.hidden=!s.flash; if(s.flash) flash.style.backgroundColor="rgb("+s.flash.join(",")+")";
  el("bubble").textContent=s.speech || ""; el("bubble").hidden=!s.speech;
  window.drawGlassGame?.(s.game?.native ? null : s.game);
  window.drawNativeGame?.(s.game?.native ? s.game : null);
  window.playGlassSounds?.(s.game);
  drawWork(s.work);
}
async function poll() {
  clearTimeout(nextPoll);
  try {
    const response=await fetch("/state",{cache:"no-store"}); if(!response.ok) throw new Error();
    const s=await response.json();
    if (full() && !s.active) { lastScreen=null; await updateScreen(); }
    draw(s);
  } catch { el("connection").textContent="Mac 호스트에 다시 연결하는 중"; }
  nextPoll=setTimeout(poll,document.hidden?750:state?.game?40:state?.portal?60:150);
}
async function loadDisplays() {
 const select=el("display"), old=select.value;
 try {
  const displays=await fetch("/displays").then(r=>r.json());
  while(select.options.length>1) select.remove(1);
  for(const d of displays) { const opt=new Option(d.name+" · "+d.width+" × "+d.height,d.id); el("display").add(opt); }
  const external=displays.filter(d=>d.name==="외부 디스플레이");
  if(displays.some(d=>d.id===old)) select.value=old;
  else if(external.length===1) select.value=external[0].id;
 } catch {}
}
window.addEventListener("focus",()=>{if(!full())loadDisplays();}); loadDisplays();
poll();
