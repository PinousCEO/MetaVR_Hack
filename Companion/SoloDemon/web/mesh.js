"use strict";
window.demonModels = async function(canvas) {
  const root="/demon-gate/art/", result=await fetch(root+"swarm-meshes.json");
  if(!result.ok) throw Error("악마 모델을 불러오지 못했어요.");
  const source=(await result.json()).meshes;
  const figures=["Hopper","Flyer","Tank","Splitter","Mini","Boss"].map(key=>source[key]);
  const color=(m,i)=>m.colors.slice(i*4,i*4+3);
  // Browsers with GPU acceleration disabled still draw the published triangle meshes with a lit depth-sorted pose.
  function painted() {
    const tiles=figures.map(m=>[-.32,0,.32].map(yaw=>{
      const c=document.createElement("canvas"); c.width=c.height=160;
      const ctx=c.getContext("2d"),p=[],a=Math.cos(yaw),b=Math.sin(yaw);
      for(let i=0;i<m.vertices.length;i+=3) {
        const [x,y,z]=m.vertices.slice(i,i+3),nx=m.normals[i],ny=m.normals[i+1],nz=m.normals[i+2];
        p.push([80+(x*a+z*b)*108,82-y*108,z*a-x*b,.72+.32*Math.max(0,-.35*nx+.64*ny-.69*nz)]);
      }
      const triangles=[];
      for(let i=0;i<m.indices.length;i+=3) triangles.push(m.indices.slice(i,i+3));
      triangles.sort((t,u)=>p[u[0]][2]+p[u[1]][2]+p[u[2]][2]-p[t[0]][2]-p[t[1]][2]-p[t[2]][2]);
      for(const t of triangles){
        const pts=t.map(i=>p[i]),lit=(pts[0][3]+pts[1][3]+pts[2][3])/3;
        const rgb=color(m,t[0]).map(v=>Math.round(Math.min(255,v*255*lit)));
        ctx.fillStyle=ctx.strokeStyle="rgb("+rgb.join(",")+")";ctx.lineWidth=.5;
        ctx.beginPath();ctx.moveTo(pts[0][0],pts[0][1]);ctx.lineTo(pts[1][0],pts[1][1]);ctx.lineTo(pts[2][0],pts[2][1]);ctx.closePath();ctx.fill();ctx.stroke();
      }
      return c;
    }));
    const ctx=canvas.getContext("2d");
    return {mode:"painted mesh",draw(monsters,view) {
      ctx.clearRect(0,0,canvas.width,canvas.height);
      for(const m of [...monsters].sort((a,b)=>b[4]-a[4])){
        const p=view.project(m[2],m[3],m[4]),s=m[5]*p[2]*2.1;
        const tile=tiles[m[1]][Math.round(1+Math.sin(m[6]*2))];
        ctx.drawImage(tile,p[0]-s*.74,p[1]-s*.76,s*1.48,s*1.48);
      }
    }};
  }
  const gl=canvas.getContext("webgl",{alpha:true,antialias:true,premultipliedAlpha:false});
  if(!gl)return painted();
  const vs=`attribute vec3 pt,norm; attribute vec4 col; uniform vec3 origin, camera;
    uniform vec2 spin;uniform float aspect,size,lens,tumble,wingLift;
    varying vec3 normal,local,tint;
    void main(){
      mat3 yaw=mat3(spin.x,0.,-spin.y,0.,1.,0.,spin.y,0.,spin.x);
      mat3 tilt=mat3(1.,0.,0.,0.,cos(tumble),sin(tumble),0.,-sin(tumble),cos(tumble));
      vec3 pose=pt;
      float outer=smoothstep(.31,.68,abs(pt.x))*smoothstep(.035,.12,pt.z);
      pose.y+=outer*wingLift;
      pose.z+=outer*abs(wingLift)*.30;
      mat3 r=yaw*tilt;vec3 eye=origin+size*r*pose-camera;
      normal=r*norm;local=pt;tint=col.rgb;
      gl_Position=vec4(eye.x/(aspect*lens),eye.y/lens,eye.z-.08,eye.z);
    }`;
  const fs=`precision mediump float;varying vec3 normal,local,tint; uniform sampler2D cloth;uniform float flash;
    void main(){vec3 n=normalize(normal);float sun=max(dot(n,normalize(vec3(-.45,.66,-.73))),0.);
      vec3 w=abs(n);w/=max(.01,w.x+w.y+w.z);
      float fiber=dot(w,vec3(texture2D(cloth,local.yz*2.).r,texture2D(cloth,local.xz*2.).r,texture2D(cloth,local.xy*2.).r));
      float diffuse=.61+.24*smoothstep(.16,.25,sun)+.24*smoothstep(.60,.71,sun);
      vec3 lit=tint*vec3(1.035,.99,.93)*diffuse+vec3(.055,.028,.074)*(1.-sun);
      lit=mix(lit,vec3(.16,.095,.19),.66*(1.-smoothstep(.04,.27,abs(n.z))));
      lit*=.98+.035*fiber;
      gl_FragColor=vec4(mix(lit,vec3(1.,.84,.56),flash*.50),1.);}`;
  const shader=(type,src)=>{const sh=gl.createShader(type);gl.shaderSource(sh,src);gl.compileShader(sh);if(!gl.getShaderParameter(sh,gl.COMPILE_STATUS))throw Error(gl.getShaderInfoLog(sh));return sh;};
  const program=gl.createProgram();gl.attachShader(program,shader(gl.VERTEX_SHADER,vs));gl.attachShader(program,shader(gl.FRAGMENT_SHADER,fs));gl.linkProgram(program);
  if(!gl.getProgramParameter(program,gl.LINK_STATUS))throw Error(gl.getProgramInfoLog(program));
  gl.useProgram(program);
  const attributes=Object.fromEntries(["pt","norm","col"].map(name=>[name,gl.getAttribLocation(program,name)]));
  const uniforms=Object.fromEntries(["origin","camera","spin","aspect","size","lens","tumble","wingLift","flash"].map(name=>[name,gl.getUniformLocation(program,name)]));
  function buffer(target,arr){const buf=gl.createBuffer();gl.bindBuffer(target,buf);gl.bufferData(target,arr,gl.STATIC_DRAW);return buf;}
  const models=figures.map(m=>({pt:buffer(gl.ARRAY_BUFFER,new Float32Array(m.vertices)),norm:buffer(gl.ARRAY_BUFFER,new Float32Array(m.normals)),
    col:buffer(gl.ARRAY_BUFFER,new Float32Array(m.colors)),index:buffer(gl.ELEMENT_ARRAY_BUFFER,new Uint16Array(m.indices)),count:m.indices.length}));
  const tex=gl.createTexture();gl.bindTexture(gl.TEXTURE_2D,tex);
  gl.texImage2D(gl.TEXTURE_2D,0,gl.RGB,1,1,0,gl.RGB,gl.UNSIGNED_BYTE,new Uint8Array([128,128,128]));
  gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,gl.LINEAR);
  const felt=new Image();felt.onload=()=>{gl.bindTexture(gl.TEXTURE_2D,tex);gl.texImage2D(gl.TEXTURE_2D,0,gl.RGB,gl.RGB,gl.UNSIGNED_BYTE,felt);};felt.src=root+"demon-felt.png";
  gl.enable(gl.DEPTH_TEST);gl.clearColor(0,0,0,0);
  return {mode:"WebGL",draw(monsters,v) {
    gl.viewport(0,0,canvas.width,canvas.height);gl.clear(gl.COLOR_BUFFER_BIT|gl.DEPTH_BUFFER_BIT);
    gl.uniform3f(uniforms.camera,v.x,1.06,-.39);gl.uniform1f(uniforms.aspect,v.aspect);gl.uniform1f(uniforms.lens,.40);
    for(const m of monsters){
      const model=models[m[1]],yaw=.33*Math.sin(m[6]*2);
      for(const [name,num] of [["pt",3],["norm",3],["col",4]]){
        gl.bindBuffer(gl.ARRAY_BUFFER,model[name]);gl.enableVertexAttribArray(attributes[name]);gl.vertexAttribPointer(attributes[name],num,gl.FLOAT,false,0,0);
      }
      gl.uniform3f(uniforms.origin,m[2],m[3],m[4]);gl.uniform2f(uniforms.spin,Math.cos(yaw),Math.sin(yaw));
      gl.uniform1f(uniforms.tumble,.085*Math.cos(m[6]*3));gl.uniform1f(uniforms.flash,Math.min(1,m[9]*7));
      const amplitude=m[1]===1?.19:m[1]===5?.075:m[1]===0?.08:0;
      gl.uniform1f(uniforms.wingLift,amplitude*Math.sin(m[6]*(m[1]===5?4.5:10)));
      gl.uniform1f(uniforms.size,m[5]*2.12);
      gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER,model.index);gl.drawElements(gl.TRIANGLES,model.count,gl.UNSIGNED_SHORT,0);
    }
  }};
};

