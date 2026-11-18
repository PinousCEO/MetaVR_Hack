// Node 22+, a locally installed Chromium, and a Release build of LivingPartner.Mac.
import assert from "node:assert/strict";
import {spawn} from "node:child_process";
import {access,mkdir,mkdtemp,readFile,rm,writeFile} from "node:fs/promises";
import {createServer} from "node:net";
import {tmpdir} from "node:os";
import {join,resolve} from "node:path";
import {setTimeout as sleep} from "node:timers/promises";

const dll=resolve(process.argv[2] || "Companion/LivingPartner.Mac/bin/Release/net8.0/LivingPartner.Mac.dll");
const output=process.argv[3];
const candidates=[process.env.CHROME_PATH,"/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
  join(process.env.ProgramFiles||".","Google/Chrome/Application/chrome.exe"),"/usr/bin/chromium","/usr/bin/google-chrome"];
let chrome;
for(const file of candidates.filter(Boolean)) {if(await access(file).then(()=>true,()=>false)){chrome=file;break;}}
if(!chrome)throw Error("Set CHROME_PATH to the installed Chrome or Chromium executable.");
const temporary=await mkdtemp(join(tmpdir(),"bambi-webgl-"));
const listener=createServer();
await new Promise(ok=>listener.listen(0,"127.0.0.1",ok));
const port=listener.address().port;
await new Promise(ok=>listener.close(ok));
const origin="http://127.0.0.1:"+port;
const options={windowsHide:true,stdio:"ignore"};
const host=spawn("dotnet",[dll,"--no-browser","--no-beacon","--port",String(port),"--state-dir",join(temporary,"data")],options);
const profile=join(temporary,"browser");
const browser=spawn(chrome,["--headless=new","--disable-extensions","--disable-background-networking","--no-first-run",
  "--use-gl=angle","--use-angle=swiftshader","--enable-unsafe-swiftshader","--remote-debugging-port=0","--user-data-dir="+profile,"about:blank"],options);
let cdp,quest,heartbeat;
async function until(check,label){
  const end=Date.now()+15000;
  while(Date.now()<end){const result=await check();if(result)return result;await sleep(50);}
  throw Error("Timed out: "+label);
}
function openSocket(url){
  const ws=new WebSocket(url);
  return new Promise((ok,no)=>{ws.onopen=()=>ok(ws);ws.onerror=no;});
}
try{
  const debugPort=await until(()=>readFile(join(profile,"DevToolsActivePort"),"utf8").then(v=>Number(v.split("\n")[0]),()=>0),"headless browser");
  const tabs=await fetch("http://127.0.0.1:"+debugPort+"/json/list").then(r=>r.json());
  cdp=await openSocket(tabs.find(t=>t.type==="page").webSocketDebuggerUrl);
  const pending=new Map(),errors=[];let next=0;
  cdp.onmessage=event=>{
    const m=JSON.parse(event.data);
    if(pending.has(m.id)){pending.get(m.id)(m);pending.delete(m.id);}
    else if(m.method==="Runtime.exceptionThrown")errors.push(m.params.exceptionDetails.exception?.description || m.params.exceptionDetails.text);
  };
  const command=(method,params={})=>new Promise(ok=>{pending.set(++next,ok);cdp.send(JSON.stringify({id:next,method,params}));});
  async function js(expression){
    const {result,error}=await command("Runtime.evaluate",{expression,userGesture:true,returnByValue:true,awaitPromise:true});
    if(error||result.exceptionDetails)throw Error(error?.message || result.exceptionDetails.exception?.description);
    return result.result.value;
  }
  async function picture(name){
    if(!output)return;
    await mkdir(output,{recursive:true});
    const {result}=await command("Page.captureScreenshot",{format:"png"});
    await writeFile(join(output,name+".png"),Buffer.from(result.data,"base64"));
  }
  await until(()=>fetch(origin+"/health").then(r=>r.ok,()=>false),"temporary host");
  await command("Page.enable");await command("Runtime.enable");
  await command("Page.addScriptToEvaluateOnNewDocument",{source:"window.indicesDrawn=new Set();(()=>{const original=WebGLRenderingContext.prototype.drawElements;WebGLRenderingContext.prototype.drawElements=function(...args){window.indicesDrawn.add(args[1]);return original.apply(this,args);};})();"});
  await command("Page.navigate",{url:origin});
  const code=await until(()=>js('document.getElementById("code")?.textContent.match(/^\\d{6}$/)?.[0]'),"pairing code");
  await command("Page.bringToFront");
  await js('document.getElementById("display").value="mac-tv"');
  const point=await js('(()=>{const r=document.getElementById("fullscreen").getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()');
  for(const type of ["mousePressed","mouseReleased"])await command("Input.dispatchMouseEvent",{type,button:"left",clickCount:1,...point});
  await until(()=>fetch(origin+"/state").then(r=>r.json()).then(s=>s.active),"selected TV fullscreen");
  quest=await openSocket(origin.replace("http:","ws:")+"/ws");
  const messages=[];
  quest.onmessage=event=>{messages.push(JSON.parse(event.data));};
  const send=(type,body)=>quest.send(JSON.stringify({v:1,id:crypto.randomUUID(),type,body}));
  const take=(type,check=()=>true)=>until(()=>{
    const i=messages.findIndex(m=>m.type===type&&check(m.body));
    return i<0?null:messages.splice(i,1)[0].body;
  },type);
  send("pair.request",{code});const pair=await take("pair.result",p=>p.ok);
  send("hello",{protocol:1,role:"quest",clientId:"fixture",roomSession:"test",deviceName:"WebGL test",token:pair.token});
  await take("welcome");
  const v=(x,y,z)=>({x,y,z});
  send("calib.commit",{monitorId:"mac-tv",tl:v(-.55,1,.8),tr:v(.55,1,.8),bl:v(-.55,.4,.8),br:v(.55,.4,.8),spaceEpoch:"test"});
  await until(()=>js('!document.getElementById("travel").disabled'),"TV calibration");
  assert.equal(await js('document.querySelector("a.solo-launch").getAttribute("href")'),"/demon-gate/");
  // The Windows/Mac menu starts the standalone solo game. The Quest bag can additionally start the MR renderer.
  const demons={on:true,monitorId:"mac-tv",count:6,phase:2,halfW:.5,halfH:.31,level:3,hearts:4,maxHearts:5,score:31,xp:2,need:7,
    id:[1,2,3,4,5,6],kind:[0,1,2,3,4,5],u:[-.68,-.28,.62,.39,-.7,0],v:[-.04,.5,.3,-.48,-.53,-.10],press:[.8,.98,.4,.6,.7,.95]};
  heartbeat=setInterval(()=>send("swarm.state",demons),150);send("swarm.state",demons);
  await take("swarm.state",m=>m.count===-1 && m.monitorId==="mac-tv");
  const models=await fetch(origin+"/art/swarm-meshes.json").then(r=>r.json());
  const counts=await js("[...window.indicesDrawn]");
  for(const mesh of Object.values(models.meshes))assert(counts.includes(mesh.indices.length));
  await picture("demon-gate");
  demons.phase=3;demons.choices=[0,6,8];
  await until(()=>js('document.querySelectorAll("#swarm-choices img").length===3&&[...document.querySelectorAll("#swarm-choices img")].every(i=>i.complete&&i.naturalWidth>0)'),"three augment icons");
  assert.match(await js('document.getElementById("game-score").textContent'),/31 points/);await picture("demon-choices");
  await js('document.getElementById("unfold").click()');assert.equal(await js('document.getElementById("panel").hidden'),false);
  await js('document.getElementById("fold").click()');assert.equal(await js('document.getElementById("panel").hidden'),true);
  await js('window.loss=document.getElementById("native-actors").getContext("webgl").getExtension("WEBGL_lose_context");window.loss.loseContext()');
  await sleep(550);
  const acknowledgements=()=>messages.filter(m=>m.type==="swarm.state"&&m.body.count===-1).length;
  const before=acknowledgements();await sleep(900);assert.equal(acknowledgements(),before);
  await js("window.loss.restoreContext()");await until(()=>acknowledgements()>before,"restored renderer acknowledgement");
  clearInterval(heartbeat);send("swarm.state",{on:false,monitorId:"mac-tv"});
  await until(()=>js('document.getElementById("native-game").hidden'),"closed game");
  send("gomoku.start",{monitorId:"mac-tv"});
  const black=await take("gomoku.state",s=>s.active&&s.turn===1);
  const tableDoc='document.getElementById("gomoku-board").contentDocument';
  await until(()=>js(tableDoc+'?.getElementById("setup")?.hidden'),"TV Gomoku board on selected fullscreen display");
  const first=await js('(()=>{const c='+tableDoc+'.querySelector("canvas").getBoundingClientRect();return {u:(c.x+c.width*(.052+2*.896/14))/innerWidth,v:(c.y+c.height*(.052+3*.896/14))/innerHeight};})()');
  send("gomoku.input",{monitorId:"mac-tv",round:black.round,revision:black.revision,seq:1,side:"wearer",...first,commit:true});
  const white=await take("gomoku.state",s=>s.active&&s.turn===2);
  const board=()=>fetch(origin+"/table/api/state").then(r=>r.json());
  assert.equal((await board()).stones[3*15+2],1);
  assert.equal((await fetch(origin+"/table/api/frame?viewer="+crypto.randomUUID()+"&after=-1")).status,204);
  if(process.argv[4]){
    const jpeg=(await readFile(process.argv[4])).toString("base64");
    let seq=0;
    const camera={round:white.round,revision:white.revision,monitorId:"mac-tv",w:358,h:376,cx:179,cy:188,fx:1200,fy:1200,
      center:[0,-.21,.81],up:[0,.35,.937],right:[1,0,0],heightM:.5,widthM:1,own:[],jpeg};
    heartbeat=setInterval(()=>send("gomoku.frame",{...camera,seq:++seq}),160);
    await until(async()=>{const b=await board();return b.hover>=0&&b.turn===2;},"real pointing hand on TV");
    await until(()=>js('(()=>{const c='+tableDoc+'.querySelector("canvas"),p=c.getContext("2d").getImageData(0,0,c.width,c.height).data;for(let i=0;i<p.length;i+=4)if(p[i+3]>220&&p[i]<50&&p[i+1]>95&&p[i+2]>55)return true;return false;})()'),"visible green dwell ring for the guest hand");
    await picture("gomoku-candidate");
    const accepted=await take("gomoku.state",s=>s.active&&s.turn===1&&s.revision>white.revision);
    clearInterval(heartbeat);assert.equal((await board()).stones.filter(n=>n===2).length,1);
    await until(()=>js(tableDoc+'.querySelector("canvas").getAttribute("aria-label")==="Gomoku board, 2 stones"'),"both stones drawn on the TV");
    send("gomoku.frame",{...camera,seq:++seq});
    await sleep(300);assert.equal((await board()).revision,accepted.revision);
    await picture("gomoku-two-players");
    console.log("PASS real hand detector + camera frames from authenticated Quest placed exactly one white stone on the TV");
  }
  send("gomoku.stop",{monitorId:"mac-tv",round:white.round});
  await take("gomoku.state",s=>!s.active&&s.round===white.round);
  await until(()=>js('document.getElementById("gomoku-board").hidden'),"closed TV board");
  // Keep ball positions steady for the two render samples. The actual round trips run in protocol.mjs.
  await js("window.renderFixture=window.drawNativeGame;window.drawNativeGame=()=>{}");
  for(const native of ["keeper","tennis"]){
    const game={native,width:.6,height:.375,depth:.5,radius:native==="keeper"?.038:.023,partnerX:-.025,partnerZ:.2,
      phase:"rally",scoreUser:2,scorePartner:1,ball:{x:.09,y:.02,z:.03},now:2};
    await js("window.indicesDrawn.clear();window.renderFixture("+JSON.stringify(game)+")");
    await until(()=>js("window.indicesDrawn.has(5760)"),"indexed ball mesh");
    assert.equal(await js('document.getElementById("native-actors").getContext("webgl").getError()'),0);await picture(native);
  }
  assert.deepEqual(errors,[]);
  console.log("PASS fullscreen TV, all six Unity meshes, three upgrades, WebGL loss/recovery, 3D football and tennis ball");
  // Some Chromium builds close the debugging socket before acknowledging Browser.close.
  await Promise.race([command("Browser.close").catch(()=>{}),sleep(1500)]);
}finally{
  clearInterval(heartbeat);
  if(cdp?.readyState===1)cdp.close();if(quest?.readyState===1)quest.close();
  browser.kill();host.kill();await sleep(1000);
  await rm(temporary,{recursive:true,force:true,maxRetries:4,retryDelay:200});
}
