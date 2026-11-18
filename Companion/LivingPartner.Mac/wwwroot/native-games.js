(() => {
  const byId = id => document.getElementById(id);
  const section=byId('native-game'), actors=byId('native-actors'), court=byId('native-court');
  const paint=court.getContext('2d'), portrait=new Image();
  portrait.src='/bambi.png';
  const types=['Hopper','Flyer','Tank','Splitter','Mini','Boss'];
  let gl, program, locations, sphere, felt, meshes, loading, meshError=false;
  let frame, animation, previous=0, lastAck=-1000, received=0, previousBall, swing=0, choiceKey='';
  const figures=new Map(), sparks=[];
  let gpuLost=false, gpuGeneration=0;
  actors.addEventListener('webglcontextlost',event=>{
    event.preventDefault();
    gpuLost=true;gpuGeneration++;
    if(animation)cancelAnimationFrame(animation);animation=0;
  });
  actors.addEventListener('webglcontextrestored',()=>{
    gpuLost=false;program=sphere=meshes=loading=null;meshError=false;
    if(frame&&!animation)animation=requestAnimationFrame(render);
  });
  const vertex=`attribute vec3 position,normal; attribute vec4 color;
    uniform vec2 opening; uniform vec3 origin; uniform float radius,eye,yaw,pitch;
    varying vec3 n,local; varying vec4 tint;
    void main(){
      float a=cos(yaw),b=sin(yaw),c=cos(pitch),d=sin(pitch);
      mat3 rot=mat3(a,b*d,-b*c,0.,c,d,b,-a*d,a*c);
      n=rot*normal; local=position; tint=color; vec3 p=origin+radius*rot*position;
      gl_Position=vec4(p.xy/opening, p.z-.75,1.+p.z/eye);
    }`;
  const fragment=`precision mediump float;
    varying vec3 n,local; varying vec4 tint; uniform sampler2D felt; uniform int surface;
    void main(){vec3 normal=normalize(n),dir=normalize(local);
      vec3 color=tint.rgb;
      if(surface==1){
        vec3 a=abs(dir); float p=max(dot(a.xy,vec2(.525731,.850651)),max(dot(a.yz,vec2(.525731,.850651)),dot(a.xz,vec2(.850651,.525731))));
        float h=max(dot(a,vec3(.57735)),max(dot(a.xy,vec2(.934172,.356822)),max(dot(a.yz,vec2(.934172,.356822)),dot(a.xz,vec2(.356822,.934172)))));
        float edge=p*.979-h;
        color=mix(vec3(.93,.94,.89),vec3(.075,.10,.13),smoothstep(-.004,.004,edge))*(.78+.22*smoothstep(.0,.013,abs(edge)));
      }else if(surface==2){
        float seam=abs(abs(dir.y)-(.42+.21*cos(2.*atan(dir.x,dir.z))));
        color=mix(vec3(.97,.99,.83),vec3(.76,.91,.17),smoothstep(.018,.056,seam));
      }else{
        vec3 weight=abs(normalize(dir)); weight/=max(.01,weight.x+weight.y+weight.z);
        float fabric=dot(weight,vec3(texture2D(felt,local.yz*2.).r,texture2D(felt,local.xz*2.).r,texture2D(felt,local.xy*2.).r));
        color*=1.+(fabric-.5)*.18;
      }
      float key=max(0.,dot(normal,normalize(vec3(-.48,.72,-.72))));
      float rim=pow(1.-abs(normal.z),2.);
      gl_FragColor=vec4(clamp(color*(.61+.43*key)+vec3(.08,.05,.09)*rim,0.,1.),tint.a);
    }`;
  function upload(m) {
    const buffer=(target,value)=>{const b=gl.createBuffer();gl.bindBuffer(target,b);gl.bufferData(target,value,gl.STATIC_DRAW);return b;};
    const count=m.vertices.length/3;
    if(count>65535 || m.normals.length!==count*3 || m.colors.length!==count*4) throw Error('Unsupported mesh');
    return {p:buffer(gl.ARRAY_BUFFER,new Float32Array(m.vertices)),n:buffer(gl.ARRAY_BUFFER,new Float32Array(m.normals)),
      c:buffer(gl.ARRAY_BUFFER,new Float32Array(m.colors)),i:buffer(gl.ELEMENT_ARRAY_BUFFER,new Uint16Array(m.indices)),count:m.indices.length};
  }
  function makeSphere(){
    const m={vertices:[],normals:[],colors:[],indices:[]};
    for(let y=0;y<=24;y++) for(let x=0;x<=40;x++){
      const v=y*Math.PI/24,a=x*2*Math.PI/40, p=[Math.sin(v)*Math.cos(a),Math.cos(v),Math.sin(v)*Math.sin(a)];
      m.normals.push(...p);m.vertices.push(...p.map(t=>t*.5));m.colors.push(1,1,1,1);
      if(y<24 && x<40){const i=y*41+x;m.indices.push(i,i+41,i+1,i+1,i+41,i+42);}
    }
    return upload(m);
  }
  function setup(){
    if(program)return true;
    gl=actors.getContext('webgl',{alpha:true,antialias:true,premultipliedAlpha:true});
    if(!gl)return false;
    const shader=(kind,src)=>{const s=gl.createShader(kind);gl.shaderSource(s,src);gl.compileShader(s);
      if(!gl.getShaderParameter(s,gl.COMPILE_STATUS))throw Error(gl.getShaderInfoLog(s));return s;};
    program=gl.createProgram();gl.attachShader(program,shader(gl.VERTEX_SHADER,vertex));gl.attachShader(program,shader(gl.FRAGMENT_SHADER,fragment));
    gl.linkProgram(program);if(!gl.getProgramParameter(program,gl.LINK_STATUS))throw Error(gl.getProgramInfoLog(program));
    locations={};for(const name of ['position','normal','color'])locations[name]=gl.getAttribLocation(program,name);
    for(const name of ['opening','origin','eye','radius','yaw','pitch','surface','felt'])locations[name]=gl.getUniformLocation(program,name);
    gl.useProgram(program);gl.enable(gl.DEPTH_TEST);gl.depthFunc(gl.LEQUAL);
    felt=gl.createTexture();gl.bindTexture(gl.TEXTURE_2D,felt);
    gl.texImage2D(gl.TEXTURE_2D,0,gl.RGB,1,1,0,gl.RGB,gl.UNSIGNED_BYTE,new Uint8Array([128,128,128]));
    gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,gl.LINEAR);
    const generation=gpuGeneration,image=new Image();image.onload=()=>{if(gpuLost||generation!==gpuGeneration)return;gl.bindTexture(gl.TEXTURE_2D,felt);gl.texImage2D(gl.TEXTURE_2D,0,gl.RGB,gl.RGB,gl.UNSIGNED_BYTE,image);
      gl.generateMipmap(gl.TEXTURE_2D);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,gl.LINEAR_MIPMAP_LINEAR);};
    image.src='/art/demon-felt.png';sphere=makeSphere();return true;
  }
  function loadMeshes(){
    if(loading)return;
    const generation=gpuGeneration;
    loading=fetch('/art/swarm-meshes.json').then(r=>{if(!r.ok)throw Error();return r.json();}).then(data=>{
      if(gpuLost || generation!==gpuGeneration)return;
      meshes=types.map(k=>upload(data.meshes[k]));
    }).catch(()=>{if(generation===gpuGeneration)meshError=true;});
  }
  function drawMesh(m, x,y,z, size, yaw,pitch, surface) {
    for(const [attr,buf,n] of [['position',m.p,3],['normal',m.n,3],['color',m.c,4]]){
      const loc=locations[attr];gl.bindBuffer(gl.ARRAY_BUFFER,buf);gl.enableVertexAttribArray(loc);gl.vertexAttribPointer(loc,n,gl.FLOAT,false,0,0);
    }
    gl.uniform3f(locations.origin,x,y,z);gl.uniform1f(locations.radius,size);gl.uniform1f(locations.yaw,yaw);
    gl.uniform1f(locations.pitch,pitch);gl.uniform1i(locations.surface,surface);
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER,m.i);gl.drawElements(gl.TRIANGLES,m.count,gl.UNSIGNED_SHORT,0);
  }
  function project(x,y,z,hw,hh,eye) {
    const k=1/(1+z/eye);return [(1+x*k/hw)*court.width*.5,(1-y*k/hh)*court.height*.5];
  }
  function line(a,b,color,thickness){
    paint.strokeStyle=color;paint.lineWidth=thickness;paint.beginPath();paint.moveTo(...a);paint.lineTo(...b);paint.stroke();
  }
  function ballCourt(g,eye,time){
    const w=court.width,h=court.height,hw=g.width*.5,hh=g.height*.5,deep=g.depth;
    const p=(x,y,z)=>project(x,y,z,hw,hh,eye);
    const sky=paint.createLinearGradient(0,0,0,h);sky.addColorStop(0,'#152d3c');sky.addColorStop(.62,'#608783');sky.addColorStop(1,'#91a792');paint.fillStyle=sky;paint.fillRect(0,0,w,h);
    const poly=(color,...points)=>{paint.fillStyle=color;paint.beginPath();points.forEach((pt,i)=>i?paint.lineTo(...pt):paint.moveTo(...pt));paint.closePath();paint.fill();};
    poly('#376b63',p(-hw,-hh,0),p(hw,-hh,0),p(hw,-hh,deep),p(-hw,-hh,deep));
    for(const x of [-hw,-hw*.78,0,hw*.78,hw])line(p(x,-hh,0),p(x,-hh,deep),'#c8e2ce',Math.max(1,h*.004));
    for(const z of [0,deep*.45,deep])line(p(-hw,-hh,z),p(hw,-hh,z),'#d9ecda',Math.max(1,h*.004));
    if(g.native==='keeper'){
      const z=deep*.85,a=hw*.7,b=hh*.50;
      for(let i=-5;i<=5;i++)line(p(a*i/5,-hh,z),p(a*i/5,b,z),'#cddfc686',h*.002);
      for(let i=0;i<=5;i++){let y=-hh+(hh+b)*i/5;line(p(-a,y,z),p(a,y,z),'#cddfc686',h*.002);}
      line(p(-a,-hh,z),p(-a,b,z),'#eef1df',h*.009);line(p(a,-hh,z),p(a,b,z),'#eef1df',h*.009);line(p(-a,b,z),p(a,b,z),'#eef1df',h*.009);
    }else{
      const bottom=p(0,-hh,0)[1],top=bottom-h*.16;
      for(let col=0;col<=36;col++)line([w*col/36,top],[w*col/36,bottom],'#daece74e',h*.0015);
      for(let row=0;row<=6;row++)line([0,top+(bottom-top)*row/6],[w,top+(bottom-top)*row/6],'#daece74e',h*.0015);
      line([0,top],[w,top],'#e3ecda',h*.006);
    }
    const foot=p(g.partnerX,-hh,g.partnerZ),ph=Math.min(h*.34,.14/(2*hh)*h/(1+g.partnerZ/eye));
    if(portrait.naturalWidth){const pw=ph*portrait.naturalWidth/portrait.naturalHeight;paint.drawImage(portrait,foot[0]-pw/2,foot[1]-ph,pw,ph);}
    if(g.native==='tennis'){
      const x=foot[0]+ph*.23,y=foot[1]-ph*.35;
      paint.save();paint.translate(x,y);paint.rotate(-.55+Math.sin(time*.012)*swing*.72);line([0,0],[0,-ph*.32],'#683f32',ph*.041);
      paint.beginPath();paint.ellipse(0,-ph*.38,ph*.086,ph*.13,0,0,2*Math.PI);paint.fillStyle='#cce4c67d';paint.strokeStyle='#f5b887';paint.lineWidth=ph*.023;paint.fill();paint.stroke();paint.restore();
    }
  }
  const stateText={1:'왼손바닥으로 막고 오른손 검지로 조준하세요',2:'문을 빠져나온 악마는 방 안에서 막으세요',3:'헤드셋에서 세 가지 강화 중 하나를 고르세요',4:'전투가 끝났어요'};
  const charms=['속사','쌍발','관통','폭발','연쇄 번개','별 고리','감속 안개','거대 탄환','회복','보석 자석','밤비의 지원','손바닥 방패'];
  function drawChoices(choices){
    const key=choices.join(',');
    if(choiceKey===key)return;choiceKey=key;
    const list=byId('swarm-choices');list.hidden=!choices.length;list.replaceChildren();
    for(const n of choices){
      const item=document.createElement('li'),icon=new Image();icon.alt='';icon.src='/art/demon-charm-'+String(n+1).padStart(2,'0')+'.png';
      item.append(icon,document.createTextNode(charms[n]));list.append(item);
    }
  }
  function render(time){
    animation=0;if(!frame || section.hidden || gpuLost)return;
    const dt=Math.min(.1,(time-previous)/1000);previous=time;
    const dpr=Math.min(1.5,devicePixelRatio||1),w=Math.max(1,Math.round(actors.clientWidth*dpr)),h=Math.max(1,Math.round(actors.clientHeight*dpr));
    if(actors.width!==w||actors.height!==h){actors.width=court.width=w;actors.height=court.height=h;}
    paint.clearRect(0,0,w,h);
    const g=frame,s=g.native==='swarm',hw=s?g.halfW:g.width*.5,hh=s?g.halfH:g.height*.5,eye=Math.max(.55,hw*1.25);
    if(!s)ballCourt(g,eye,time);
    if(!setup()){byId('game-hint').textContent='TV에서 WebGL을 지원하는 브라우저로 열어 주세요.';return;}
    gl.viewport(0,0,w,h);gl.clearColor(0,0,0,0);gl.clear(gl.COLOR_BUFFER_BIT|gl.DEPTH_BUFFER_BIT);
    gl.useProgram(program);gl.uniform2f(locations.opening,hw,hh);gl.uniform1f(locations.eye,eye);
    if(s){
      loadMeshes();
      if(meshError)byId('game-hint').textContent='TV의 3D 모델을 불러오지 못했어요. 브라우저를 새로 고쳐 주세요.';
      if(meshes){
        for(const f of figures.values()){
          const t=time*.001+f.id*.37,k=1-Math.exp(-dt*14);f.x+=(f.tx-f.x)*k;f.y+=(f.ty-f.y)*k;f.p+=(f.press-f.p)*k;
          const r=f.radius,depth=(1-f.p)*.13+r*.7;
          const size=2*r*(f.kind===5?.28+.72*f.p:.82+.29*f.p);
          drawMesh(meshes[f.kind],f.x*(hw-r),f.y*(hh-r)+.003*Math.sin(4*t),depth,size,.25*Math.sin(t*1.8),.12*Math.sin(t*2.3),0);
        }
        if(time-lastAck>450 && !gl.isContextLost() && document.visibilityState==='visible' && (document.fullscreenElement || document.webkitFullscreenElement)){
          lastAck=time;fetch('/swarm/visible',{method:'POST'}).catch(()=>{});
        }
      }
      for(let i=sparks.length-1;i>=0;i--){const p=sparks[i],age=(time-p.at)/500;if(age>=1){sparks.splice(i,1);continue;}
        const pos=project(p.x*(hw-p.r),p.y*(hh-p.r),0,hw,hh,eye);paint.strokeStyle='rgba(255,200,104,'+(1-age)+')';paint.lineWidth=(1-age)*h*.01;
        paint.beginPath();paint.ellipse(...pos,Math.max(1,p.r/hw*w*(.35+age)),Math.max(1,p.r/hh*h*(.35+age)),0,0,2*Math.PI);paint.stroke();}
    }else if(g.ball){
      const age=Math.min(1,(time-received)/70),a=previousBall||g.ball;
      drawMesh(sphere,a.x+(g.ball.x-a.x)*age,a.y+(g.ball.y-a.y)*age,a.z+(g.ball.z-a.z)*age,g.radius*2,time*.003,time*.0013,g.native==='keeper'?1:2);
    }
    swing=Math.max(0,swing-dt*2);animation=requestAnimationFrame(render);
  }
  window.drawNativeGame=g=>{
    if(!g){section.hidden=true;frame=null;figures.clear();sparks.length=0;drawChoices([]);if(animation)cancelAnimationFrame(animation);animation=0;return;}
    const s=g.native==='swarm',keeper=g.native==='keeper';
    previousBall=frame?.native===g.native?frame.ball:null;
    if(!frame || frame.native!==g.native){figures.clear();sparks.length=0;previous=performance.now();}
    if(g.partnerHitAt!==frame?.partnerHitAt || g.phase!==frame?.phase)swing=1;
    frame=g;received=performance.now();section.hidden=false;section.classList.toggle('swarm',s);
    byId('game-title').textContent=s?'악마의 문':keeper?'골키퍼':'테니스';
    byId('game-score').textContent=s?('Lv. '+g.level+'　♥ '+g.hearts+'/'+g.maxHearts+'　XP '+g.xp+'/'+g.need+'　'+g.score+'점'):(keeper?'막은 공 ':'나 ')+g.scoreUser+'　'+(keeper?'실점 ':'밤비 ')+g.scorePartner;
    byId('game-hint').textContent=s?(g.phase===4?(g.won?'악마의 문을 지켰어요!':'전투가 끝났어요'):(stateText[g.phase]||'오른손 검지로 조준하세요')):g.phase==='point'?(g.lastPoint==='user'?'득점!':'밤비 득점'):g.phase==='over'?'경기 종료':keeper?'TV 밖으로 나온 축구공을 손바닥으로 막으세요':'공이 TV 밖으로 나오면 손으로 받아치세요';
    drawChoices(s&&g.phase===3?g.choices||[]:[]);
    if(s){const seen=new Set();for(const a of g.actors){seen.add(a.id);
      if(!figures.has(a.id))figures.set(a.id,{id:a.id,kind:a.kind,x:a.u,y:a.v,p:a.press,radius:a.radius});
      Object.assign(figures.get(a.id),{tx:a.u,ty:a.v,press:a.press});}
      for(const [id,f] of figures)if(!seen.has(id)){sparks.push({x:f.x,y:f.y,r:f.radius,at:received});figures.delete(id);} }
    if(!animation)animation=requestAnimationFrame(render);
  };
})();
