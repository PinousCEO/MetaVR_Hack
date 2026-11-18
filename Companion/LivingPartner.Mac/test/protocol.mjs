// Exercises the Mac host with the same Quest wire messages on any .NET 8 desktop.
import assert from "node:assert/strict";
import {spawn} from "node:child_process";
import {mkdtemp,readFile,readdir,rm} from "node:fs/promises";
import {createServer} from "node:net";
import {createSocket} from "node:dgram";
import {tmpdir} from "node:os";
import {join,resolve} from "node:path";
import {setTimeout as sleep} from "node:timers/promises";

const dll=resolve(process.argv[2] || "Companion/LivingPartner.Mac/bin/Release/net8.0/LivingPartner.Mac.dll");
const stateDir=await mkdtemp(join(tmpdir(),"bambi-mac-test-"));
const listener=createServer();
await new Promise(done=>listener.listen(0,"127.0.0.1",done));
const port=listener.address().port;
await new Promise(done=>listener.close(done));
const origin="http://127.0.0.1:"+port;
const discovery=createSocket("udp4");
await new Promise(done=>discovery.bind(0,"127.0.0.1",done));
let beacon="";
discovery.on("message",buf=>{beacon=buf.toString();});
const env={...process.env,LP_MAC_DOCUMENTS:join(stateDir,"documents")};
if(process.argv[3]) env.LP_MAC_VOICE_MODEL=resolve(process.argv[3]);
const child=spawn("dotnet",[dll,"--port",String(port),"--state-dir",stateDir,"--no-browser","--quest-ip","127.0.0.1","--discovery-port",String(discovery.address().port)],{env,windowsHide:true,stdio:["ignore","pipe","pipe"]});
let errors="";
child.stderr.on("data",s=>{errors+=s;});
let ws, heartbeat;
const state=()=>fetch(origin+"/state").then(r=>r.json());
const put=(path,body)=>fetch(origin+path,{method:"POST",headers:{"Content-Type":"application/json",Origin:origin},body:body===undefined?undefined:JSON.stringify(body)});
async function wait(check,timeout=12000) {
  let until=Date.now()+timeout;
  while(Date.now()<until) {const v=await check();if(v)return v;await sleep(25);}
  throw new Error("Timed out waiting for "+check.toString()+" "+errors);
}
const messages=[];
const written=(type,body)=>ws.send(JSON.stringify({v:1,id:crypto.randomUUID(),type,body}));
const take=(type,check=()=>true,timeout)=>wait(()=>{
  const i=messages.findIndex(m=>m.type===type&&check(m.body));
  return i<0?null:messages.splice(i,1)[0].body;
},timeout);

try {
  await wait(()=>state().catch(()=>null));
  await wait(()=>beacon.startsWith("LIVING-AI-PARTNER/1 "+port+" "));
  assert((await fetch(origin+"/")).ok);
  assert.equal((await fetch(origin+"/action/scan",{method:"POST"})).status,403);
  await put("/display",{id:"mac-tv",width:1920,height:1080,inches:55,active:true});
  heartbeat=setInterval(()=>state().catch(()=>{}),800);
  ws=new WebSocket(origin.replace("http:","ws:")+"/ws");
  await new Promise((yes,no)=>{ws.addEventListener("open",yes);ws.addEventListener("error",no);});
  ws.onmessage=e=>{messages.push(JSON.parse(e.data)); if(messages.length>160) messages.shift();};
  const hello={protocol:1,role:"quest",clientId:"test-headset",deviceName:"Quest test",roomSession:"room-1",token:""};
  written("hello",hello); await take("reject",a=>a.reason==="pairing-required");
  written("pair.request",{code:"xxxxxx"}); await take("pair.result",a=>a.ok===false);
  const s=await state(); written("pair.request",{code:s.pairCode});
  const paired=await take("pair.result",a=>a.ok);
  written("hello",{...hello,token:paired.token}); await take("welcome");
  await take("state.snapshot",a=>a.monitors[0]?.id==="mac-tv");
  written("ping",{c:72}); await take("pong",a=>a.c===72);
  assert.equal((await put("/action/scan")).status,204);
  await take("intent.result",a=>a.kind==="calibrate");
  written("calib.flash",{items:[{monitorId:"mac-tv",r:19,g:150,b:82}]});
  await wait(async()=> (await state()).flash?.[0]===19);
  const v=(x,y,z)=>({x,y,z});
  written("calib.commit",{monitorId:"mac-tv",tl:v(-.55,1,.8),tr:v(.55,1,.8),bl:v(-.55,.4,.8),br:v(.55,.4,.8),spaceEpoch:"room-1"});
  await take("state.snapshot",a=>a.calibrations[0]?.pixelW===1920);
  for(const direction of [0,1]) {
    const action=direction===0?"enter":"return", owner=direction===0?"pc":"mr";
    assert.equal((await put(direction===0 ? "/action/game:game-keeper" : "/action/"+action)).status,204);
    await take("intent.result",a=>a.kind===(direction===0?"enterPortal":"returnToMr"));
    written("target.query",{queryId:"q"+direction,monitorId:"mac-tv",u:.6,v:.55,direction});
    const target=await take("target.result",a=>a.queryId==="q"+direction);
    assert(target.valid && target.metersPerPixel>0);
    const foot=v(0,-.12,-.30);
    written("transition.request",{direction,target,mrFootLocal:foot,worldUpLocal:v(0,1,0)});
    const tr=await take("transition.update",a=>a.state==="Approaching");
    written("transition.event",{transitionId:tr.transitionId,kind:direction===0?"approachDone":"receiverReady",vec:foot});
    await take("transition.update",a=>a.state==="Crossing");
    await take("state.snapshot",a=>a.transition.transitionId===tr.transitionId&&a.transition.state==="Complete"&&a.character.owner===owner);
    if(owner==="pc") {
      for(const mode of ["keeper","tennis"]) {
        if(mode==="tennis") assert.equal((await put("/utter",{text:"Hey, Nilo, play tennis"})).status,204);
        await take("intent.result",a=>a.kind==="playGame" && a.text===mode);
        written("game.command",{cmd:"start",room:false,mode:mode==="keeper"?"keeper":"",monitorId:"mac-tv",up:v(0,1,0),deskHeight:-.30,eye:v(0,.08,-.57)});
        await take("game.state",g=>g.active&&g.phase==="ready");
        // The headset can acknowledge the end of the previous scripted game after starting a ball game.
        written("glass.command",{cmd:"stop",how:"a ball game"});
        const serve=await take("game.state",g=>g.phase==="rally"&&g.shot?.by==="partner");
        await wait(async()=>{const g=(await state()).game;return g?.native===mode&&g.ball&&g.phase==="rally";});
        await wait(async()=>{const g=(await state()).game;return g?.phase==="rally"&&g.ball===null;});
        written("ping",{c:777});
        const {h}=await take("pong",g=>g.c===777);
        written("game.command",{cmd:"hit",seq:serve.shot.seq,t:h,pos:v(0,0,-.02),swing:v(0,.3,1.5),normal:v(0,.25,1),eye:v(0,.08,-.57)});
        if(mode==="keeper") {
          const saved=await take("game.state",g=>g.phase==="point"&&g.lastPoint==="user"&&g.scoreUser===1);
          assert.equal(saved.shot ?? null,null);
        } else {
          const hit=await take("game.state",g=>g.shot?.by==="user"&&g.rally===1);
          assert(hit.shot.seq>serve.shot.seq);
          await wait(async()=> (await state()).game?.ball?.z>0);
        }
        written("settings.update",{quietMode:true});
        await wait(async()=>{const g=(await state()).game;return g?.quiet&&g.sounds.length===0;});
        written("settings.update",{quietMode:false}); await wait(async()=> (await state()).game?.quiet===false);
        assert.equal((await put("/action/game:stop")).status,204);
        await take("game.state",g=>!g.active);
      }
      const clip=await fetch(origin+"/game-sfx/hit");
      const wav=Buffer.from(await clip.arrayBuffer());
      assert.equal(clip.headers.get("content-type"),"audio/wav");assert.equal(wav.toString("ascii",0,4),"RIFF");
      assert(wav.length>1000 && wav.some((s,index)=>index>44 && s>10));
      console.log("PASS real-time TV/Quest first serve, round trip and keeper save through the native game protocol");
    }
  }
  const page=await fetch(origin+"/").then(r=>r.text());
  assert.deepEqual([...page.matchAll(/<option value="(game-[^"]+)"/g)].map(m=>m[1]),["game-keeper","game-tennis","game-swarm","game-gomoku"]);
  for(const removed of ["game-tv-rally","game-shooter","game-vampire","game-umbrella"])
    assert.equal((await put("/action/game:"+removed)).status,400);
  assert.equal((await fetch(origin+"/rally-screen/123456/state")).status,404);
  assert((await put("/utter",{text:"Hey, Nilo, play Demon Gate"})).ok);
  await take("intent.result",g=>g.kind==="playGame"&&g.text==="game-swarm");
  const demons={on:true,monitorId:"mac-tv",count:2,phase:3,halfW:.55,halfH:.3,level:2,hearts:4,maxHearts:5,score:17,xp:1,need:4,choices:[2,4,8],
    id:[110,112],kind:[1,5],u:[-.3,.25],v:[.2,.4],press:[.25,.75]};
  written("swarm.state",demons);
  const g=await wait(async()=>{const x=(await state()).game;return x?.native==="swarm"&&x;});
  assert.deepEqual(g.actors.map(o=>o.kind),[1,5]);assert.deepEqual(g.choices,demons.choices);assert.equal(g.score,17);
  assert(!messages.some(m=>m.type==="swarm.state"&&m.body.count===-1));
  assert((await put("/swarm/visible")).ok);await take("swarm.state",b=>b.count===-1&&b.monitorId==="mac-tv");
  written("swarm.state",{on:false,monitorId:"mac-tv"});await wait(async()=>!(await state()).game);
  console.log("PASS four-game catalog, English game commands and authenticated demon geometry/HUD, WebGL acknowledgement and stop");
  written("utterance",{text:"Hey, Nilo"}); await take("character.say",a=>a.text==="I’m here!");
  assert.equal((await state()).speak,false);
  const cupPhoto = await readFile(new URL("../../LivingPartner.Tests/Fixtures/coffee.jpg",import.meta.url));
  written("utterance",{text:"Hey, Nilo, do you see a cup on my desk?"});
  const eye = await take("eye.request");
  written("eye.frame",{id:eye.id,ok:true,width:600,height:400,jpeg:cupPhoto.toString("base64"),aimX:.95,aimY:.05});
  await take("character.say",a=>a.text==="I see a cup.",25000);
  console.log("PASS room recognition on an actual table scene through the Quest camera protocol");

  written("utterance",{text:"Hey, Nilo, make firewood"});
  const inventory = await take("state.snapshot",a=>a.world?.items?.some(i=>i.name==="Firewood" && i.model),15000);
  const item = inventory.world.items.find(i=>i.name==="Firewood" && i.model);
  assert.equal(item.inRoom,false);
  const original = Buffer.from(await (await fetch(origin+"/world/"+item.model)).arrayBuffer());
  assert.equal(original.toString("ascii",0,4),"glTF");
  written("utterance",{text:"Hey, Nilo, put the firewood on my desk"});
  await take("state.snapshot",a=>a.world?.items?.some(i=>i.id===item.id && i.inRoom && i.model===item.model));
  written("world.command",{cmd:"store",id:item.id});
  await take("state.snapshot",a=>a.world?.items?.some(i=>i.id===item.id && !i.inRoom && i.model===item.model));
  written("utterance",{text:"Hey, Nilo, export the firewood as a GLB"});
  await take("character.say",a=>a.text.includes("saved to your Models folder"));
  const models = join(env.LP_MAC_DOCUMENTS,"Models");
  const [folder] = await readdir(models);
  assert.deepEqual(await readFile(join(models,folder,"Firewood.glb")),original);
  console.log("PASS bag, voice placement, storage and byte-identical file export");
  if(process.argv[4]) {
    await put("/action/speech-on");
    const r=await readFile(process.argv[4]);
    const format=r.indexOf("fmt "),offset=r.indexOf("data");
    assert.equal(r.readUInt16LE(format+10),1);assert.equal(r.readUInt16LE(format+22),16);
    written("voice.clip",{clipId:"audio-1",pcm16:r.subarray(offset+8,offset+8+r.readUInt32LE(offset+4)).toString("base64"),
      sampleRate:r.readUInt32LE(format+12),source:"microphone-open"});
    const wake=await take("voice.reply",a=>a.text==="I’m here!",90000);
    assert(wake.pcm16.length>1000 && wake.playOn==="mr");
    console.log("PASS offline English speech recognition and optional Quest PCM reply");
  }
  ws.close(); await wait(async()=> !(await state()).connected);
  assert.equal((await fetch(origin+"/rally-screen/123456/state")).status,404);
  console.log("PASS UDP discovery, real websocket pairing, monitor flash/calibration, clock sync, TV entry + return, wake and disconnect");
} finally {
  clearInterval(heartbeat); discovery.close();
  if(ws&&ws.readyState===WebSocket.OPEN)ws.close(); child.kill();
  if(child.exitCode===null) await Promise.race([new Promise(done=>child.once("exit",done)),sleep(3500)]);
  await rm(stateDir,{recursive:true,force:true,maxRetries:3,retryDelay:150});
}
