"use strict";
const el = id => document.getElementById(id);
let state, previousGame = false, registered = false, nextPoll, lastScreen;
const full = () => !!(document.fullscreenElement || document.webkitFullscreenElement);
const post = async (path, value) => {
  const r = await fetch(path, {method:"POST", headers:{"Content-Type":"application/json"}, body:value === undefined ? undefined : JSON.stringify(value)});
  if (!r.ok) { const message = await r.text(); throw new Error(message.replace(/^"|"$/g, "") || "Check the Mac connection."); }
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
  el("queue-toggle").textContent="Jobs: "+(active.length?active.length+" active":"Results: "+jobs.length+"");
  const key=JSON.stringify(jobs); if(key===jobsKey) return; jobsKey=key;
  const list=el("jobs"); list.replaceChildren();
  const addButton=(parent,label,action,id)=>{const b=document.createElement("button"); b.textContent=label;b.onclick=()=>workAction(action,id);parent.append(b);};
  const status={working:"In progress",queued:"Queued",done:"Done",failed:"Failed",cancelled:"Cancelled"};
  for(const j of jobs){
    const row=document.createElement("article"),title=document.createElement("strong"),step=document.createElement("p");
    title.textContent=j.title; step.textContent=[...new Set([status[j.status],j.step,j.note].filter(Boolean))].join(" · ");row.append(title,step);
    if(j.status==="working"||j.status==="queued"){
      const bar=document.createElement("progress");bar.max=1;bar.value=j.progress;row.append(bar);addButton(row,"Cancel","cancel",j.id);
    } else if(j.status==="done"){
      addButton(row,"Open","open",j.id);
      for(const f of j.files||[]){if(!f.url?.startsWith("/work-file/"))continue;const a=document.createElement("a");a.textContent=f.name;a.href=f.url;a.download=f.name;row.append(a);}
    }
    list.append(row);
  }
  if(jobs.length>active.length)addButton(list,"Clear finished jobs","clear","all");
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
    else throw new Error("Open this page in Safari or Chrome for fullscreen.");
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
  el("connection").textContent=s.connected ? (s.device || "Quest")+" connected" : "Connect your Quest on the same Wi-Fi";
  el("pair").hidden=s.connected; el("code").textContent=s.pairCode;
  el("setup").hidden=s.active; el("controls").hidden=!s.connected || !s.active;
  el("fold").hidden=!s.active || !s.connected;
  el("travel").textContent=s.owner === "pc" ? "Bring Nilo into the room" : "Send Nilo into the TV";
  el("travel").disabled=!s.calibrated || s.portal;
  el("instruction").textContent=s.calibrated ? "TV calibrated." : "Look at the TV from your headset to calibrate it.";
  const game=!!s.game||showTable;
  el("speak").checked=s.speak;
  if (s.active && (game && !previousGame || s.portal && !el("panel").hidden)) fold(true);
  previousGame=game;
  const b=el("bambi"); b.hidden=!s.active||s.opacity<=0||showTable||!!(s.game && s.game.pet?.visible !== false);
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
  } catch { el("connection").textContent="Reconnecting to your Mac"; }
  nextPoll=setTimeout(poll,document.hidden?750:state?.game?40:state?.portal?60:150);
}
async function loadDisplays() {
 const select=el("display"), old=select.value;
 try {
  const displays=await fetch("/displays").then(r=>r.json());
  while(select.options.length>1) select.remove(1);
  for(const d of displays) { const opt=new Option(d.name+" · "+d.width+" × "+d.height,d.id); el("display").add(opt); }
  const external=displays.filter(d=>d.name==="External display");
  if(displays.some(d=>d.id===old)) select.value=old;
  else if(external.length===1) select.value=external[0].id;
 } catch {}
}
window.addEventListener("focus",()=>{if(!full())loadDisplays();}); loadDisplays();
poll();
