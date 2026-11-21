(() => {
'use strict';
const $=id=>document.getElementById(id),canvas=$('canvas'),stage=$('stage');
const gl=canvas.getContext('webgl2',{alpha:true,premultipliedAlpha:true,antialias:false,preserveDrawingBuffer:true});
function fail(message){$('error').hidden=false;$('error').textContent=message;$('status').textContent='渲染失败';}
if(!gl||!gl.getExtension('EXT_color_buffer_float')){fail('此实现需要 WebGL 2 与浮点渲染目标（EXT_color_buffer_float）。');return;}
const clamp=(x,a,b)=>Math.max(a,Math.min(b,x));
function program(vs,fs){
  const p=gl.createProgram();
  for(const [type,src] of [[gl.VERTEX_SHADER,vs],[gl.FRAGMENT_SHADER,fs]]){
    const s=gl.createShader(type);gl.shaderSource(s,src);gl.compileShader(s);
    if(!gl.getShaderParameter(s,gl.COMPILE_STATUS))throw Error(gl.getShaderInfoLog(s));
    gl.attachShader(p,s);gl.deleteShader(s);
  }
  gl.linkProgram(p);if(!gl.getProgramParameter(p,gl.LINK_STATUS))throw Error(gl.getProgramInfoLog(p));return p;
}
const fullscreen=`#version 300 es
precision highp float;
out vec2 uv;
void main(){vec2 p=vec2((gl_VertexID<<1)&2,gl_VertexID&2);uv=p;gl_Position=vec4(p*2.0-1.0,0,1);}`;
const vertex=`#version 300 es
precision highp float;
layout(location=0)in vec3 position;layout(location=1)in vec3 normal;layout(location=2)in vec3 generated;
uniform mat4 model,projection;uniform float distance;
out vec3 vGenerated,vNormal,vWorld;
void main(){vec4 p=model*vec4(position,1);vWorld=p.xyz;vNormal=mat3(model)*normal;vGenerated=generated;gl_Position=projection*vec4(p.xyz-vec3(0,0,distance),1);}`;
const fragment=`#version 300 es
precision highp float;precision highp int;
in vec3 vGenerated,vNormal,vWorld;out vec4 fragColor;
uniform float distance,sampleOffset;uniform int mode,effect;
${EDGE_CORE}
void main(){
 vec3 N=normalize(vNormal),V=normalize(vec3(0,0,distance)-vWorld);
 vec4 e=evaluate_edge(vGenerated,dot(N,V),gl_FrontFacing);
 if(mode==4){fragColor=vec4(e.x,e.y,e.w,1);return;}
 if(effect==1){
  if(mode==1){float r=linear_ramp(e.y,uEdgeStart,uEdgeEnd,0.0,1.0);fragColor=vec4(vec3(r),1);return;}
  if(mode==2){fragColor=vec4(vec3(e.x),1);return;}
  if(mode==3){fragColor=vec4(vec3(e.w),1);return;}
  // EEVEE's DITHERED depth path: no alpha blending, no backface culling.
  float threshold=alpha_threshold(0.1,sampleOffset,vWorld);
  if(e.w>threshold)discard;
 }
 if(mode==5){fragColor=vec4(1);return;}
 // Deliberately neutral display material; the audited branch controls only visibility.
 vec3 facingN=gl_FrontFacing?N:-N;
 float diffuse=max(dot(facingN,normalize(vec3(-0.6,0.9,1.2))),0.0);
 vec3 color=vec3(0.26,0.24,0.38)*(0.35+0.65*diffuse);
 fragColor=vec4(pow(color,vec3(1.0/2.2)),1);
}`;
let drawProgram,accumProgram,showProgram;
try{
 drawProgram=program(vertex,fragment);
 accumProgram=program(fullscreen,`#version 300 es
precision highp float;in vec2 uv;out vec4 color;uniform sampler2D prior,current;uniform float weight;
void main(){color=mix(texture(prior,uv),texture(current,uv),weight);}`);
 showProgram=program(fullscreen,`#version 300 es
precision highp float;in vec2 uv;out vec4 color;uniform sampler2D image;
void main(){color=texture(image,uv);}`);
}catch(e){fail(e.message);console.error(e);return;}
const uniform=(p,n)=>gl.getUniformLocation(p,n);
let material='Lassyla Material',mode=0,yaw=0,pitch=0,zoom=1,frame=0,sample=0,drag=null,targets=null,mesh=null,parameters={},validationReport=null,nativeResult=null,nativeCurrent=false,nativeBusy=false,revision=0;
const fieldMap={ior:'uIOR',scale:'uScale',edgeStart:'uEdgeStart',edgeEnd:'uEdgeEnd',noiseStart:'uNoiseStart',noiseEnd:'uNoiseEnd'};
function configValues(name){const c=REFERENCE.configs[name];return {ior:c.ior,scale:c.noise.Scale,edgeStart:c.edgeRamp[0].position,edgeEnd:c.edgeRamp[1].position,noiseStart:c.noiseRamp[0].position,noiseEnd:c.noiseRamp[1].position};}
function bindParameters(p,values=parameters){for(const [name,shader]of Object.entries(fieldMap))gl.uniform1f(uniform(p,shader),values[name]);}
function restore(name){material=name;parameters=configValues(name);for(const [k,v]of Object.entries(parameters))$(k).value=String(v);document.querySelectorAll('[data-material]').forEach(b=>b.setAttribute('aria-pressed',String(b.dataset.material===name)));$('readout').textContent='当前使用原工程精确数值';requestDraw();}
function matrix(a,b){const r=new Float32Array(16);for(let c=0;c<4;c++)for(let i=0;i<4;i++)for(let k=0;k<4;k++)r[c*4+i]+=a[k*4+i]*b[c*4+k];return r;}
function model(){const c=Math.cos(yaw),s=Math.sin(yaw),a=Math.cos(pitch),b=Math.sin(pitch);return matrix(new Float32Array([c,0,-s,0,0,1,0,0,s,0,c,0,0,0,0,1]),new Float32Array([1,0,0,0,0,a,b,0,0,-b,a,0,0,0,0,1]));}
function projection(aspect){const f=1/Math.tan(32*Math.PI/360),n=.1,z=100;return new Float32Array([f/aspect,0,0,0,0,f,0,0,0,0,(z+n)/(n-z),-1,0,0,2*z*n/(n-z),0]);}
function loadMesh(){
 if(mesh){gl.deleteVertexArray(mesh.vao);gl.deleteBuffer(mesh.buffer);}
 const m=MESHES[$('shape').value],binary=atob(m.data),bytes=Uint8Array.from(binary,c=>c.charCodeAt(0)),data=new Float32Array(bytes.buffer);
 const vao=gl.createVertexArray(),buffer=gl.createBuffer();gl.bindVertexArray(vao);gl.bindBuffer(gl.ARRAY_BUFFER,buffer);gl.bufferData(gl.ARRAY_BUFFER,data,gl.STATIC_DRAW);
 for(let i=0;i<3;i++){gl.enableVertexAttribArray(i);gl.vertexAttribPointer(i,3,gl.FLOAT,false,36,i*12);}
 gl.bindVertexArray(null);let radius=0;for(let i=0;i<data.length;i+=9)radius=Math.max(radius,Math.hypot(data[i],data[i+1],data[i+2]));
 mesh={vao,buffer,count:data.length/9,radius};restore(m.material);
}
function texture(w,h,format=gl.RGBA16F){const t=gl.createTexture();gl.bindTexture(gl.TEXTURE_2D,t);gl.texStorage2D(gl.TEXTURE_2D,1,format,w,h);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,gl.NEAREST);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MAG_FILTER,gl.NEAREST);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_S,gl.CLAMP_TO_EDGE);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_T,gl.CLAMP_TO_EDGE);return t;}
function target(w,h,depth=false,format=gl.RGBA16F){const tex=texture(w,h,format),fbo=gl.createFramebuffer();gl.bindFramebuffer(gl.FRAMEBUFFER,fbo);gl.framebufferTexture2D(gl.FRAMEBUFFER,gl.COLOR_ATTACHMENT0,gl.TEXTURE_2D,tex,0);let rb=null;if(depth){rb=gl.createRenderbuffer();gl.bindRenderbuffer(gl.RENDERBUFFER,rb);gl.renderbufferStorage(gl.RENDERBUFFER,gl.DEPTH_COMPONENT24,w,h);gl.framebufferRenderbuffer(gl.FRAMEBUFFER,gl.DEPTH_ATTACHMENT,gl.RENDERBUFFER,rb);}if(gl.checkFramebufferStatus(gl.FRAMEBUFFER)!==gl.FRAMEBUFFER_COMPLETE)throw Error('浮点 framebuffer 不完整');return{tex,fbo,rb};}
function destroy(t){gl.deleteTexture(t.tex);gl.deleteFramebuffer(t.fbo);if(t.rb)gl.deleteRenderbuffer(t.rb);}
function resize(){const rect=canvas.getBoundingClientRect(),dpr=Math.min(devicePixelRatio||1,1.5),w=Math.max(2,Math.round(rect.width*dpr)),h=Math.max(2,Math.round(rect.height*dpr));if(targets&&w===canvas.width&&h===canvas.height)return;canvas.width=w;canvas.height=h;if(targets)targets.forEach(destroy);targets=[target(w,h,true),target(w,h),target(w,h)];sample=0;}
function halton(index,base){let f=1,r=0;while(index>0){f/=base;r+=f*(index%base);index=Math.floor(index/base);}return r;}
function requestDraw(){revision++;sample=0;nativeCurrent=false;if($('engine').value==='native'&&!nativeBusy)$('nativeStatus').textContent='参数已改变，点击原生渲染更新。';if(!frame)frame=requestAnimationFrame(render);}
function bindTexture(unit,tex){gl.activeTexture(gl.TEXTURE0+unit);gl.bindTexture(gl.TEXTURE_2D,tex);}
function render(){
 frame=0;resize();const w=canvas.width,h=canvas.height,split=$('compare').checked,half=Math.floor(w/2),count=mode===0?64:1;
 for(let batch=0;batch<2&&sample<count;batch++,sample++){
  gl.bindFramebuffer(gl.FRAMEBUFFER,targets[0].fbo);gl.viewport(0,0,w,h);gl.enable(gl.DEPTH_TEST);gl.depthMask(true);gl.depthFunc(gl.LESS);gl.disable(gl.CULL_FACE);gl.disable(gl.BLEND);gl.clearColor(0,0,0,0);gl.clear(gl.COLOR_BUFFER_BIT|gl.DEPTH_BUFFER_BIT);
  gl.useProgram(drawProgram);gl.bindVertexArray(mesh.vao);bindParameters(drawProgram);gl.uniformMatrix4fv(uniform(drawProgram,'model'),false,model());gl.uniform1i(uniform(drawProgram,'mode'),mode);gl.uniform1f(uniform(drawProgram,'sampleOffset'),Math.fround(halton(sample+1,7)));
  for(const [x,width,effect]of split?[[0,half,0],[half,w-half,1]]:[[0,w,1]]){
   const aspect=width/h;gl.viewport(x,0,width,h);gl.uniform1f(uniform(drawProgram,'distance'),Math.max(4.3,4.3/aspect)*mesh.radius/zoom);gl.uniformMatrix4fv(uniform(drawProgram,'projection'),false,projection(aspect));gl.uniform1i(uniform(drawProgram,'effect'),effect);gl.drawArrays(gl.TRIANGLES,0,mesh.count);
  }
  gl.bindVertexArray(null);gl.disable(gl.DEPTH_TEST);gl.viewport(0,0,w,h);gl.bindFramebuffer(gl.FRAMEBUFFER,targets[2].fbo);gl.useProgram(accumProgram);bindTexture(0,targets[1].tex);bindTexture(1,targets[0].tex);gl.uniform1i(uniform(accumProgram,'prior'),0);gl.uniform1i(uniform(accumProgram,'current'),1);gl.uniform1f(uniform(accumProgram,'weight'),1/(sample+1));gl.drawArrays(gl.TRIANGLES,0,3);[targets[1],targets[2]]=[targets[2],targets[1]];
 }
 gl.bindFramebuffer(gl.FRAMEBUFFER,null);gl.viewport(0,0,w,h);gl.useProgram(showProgram);bindTexture(0,targets[1].tex);gl.uniform1i(uniform(showProgram,'image'),0);gl.drawArrays(gl.TRIANGLES,0,3);
 canvas.dataset.samples=String(sample);canvas.dataset.rendered='true';$('status').textContent=`${mesh.count/3} 原模型三角形 · ${sample}/${count} 采样 · Generated 坐标 · 双面`;
 if(sample<count)frame=requestAnimationFrame(render);
}
function download(name,blob){const a=document.createElement('a'),url=URL.createObjectURL(blob);a.href=url;a.download=name;a.click();setTimeout(()=>URL.revokeObjectURL(url),10000);}
async function renderNative(){
 if(nativeBusy)return;
 nativeBusy=true;$('nativeRender').disabled=true;$('nativeStatus').textContent='正在用作者工程进行原生渲染…';
 await new Promise(requestAnimationFrame);resize();const requestedRevision=revision;
 const split=$('compare').checked,scale=Math.min(1,1200/canvas.width,900/canvas.height),total=Math.round(canvas.width*scale),height=Math.round(canvas.height*scale);
 const request={shape:$('shape').value,material,parameters:{...parameters},mode,yaw,pitch,zoom,compare:split,width:split?Math.floor(total/2):total,height};
 try{
  const response=await fetch('/api/render',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(request)});
  if(!response.ok){let message='请在终端运行 Tools/IrregularEdgeReference/start-native.ps1，然后打开 http://127.0.0.1:8769/IrregularEdgeLab.html';try{const body=await response.json();if(body.error)message=body.error;}catch{}throw Error(message);}
  const result=await response.json();
  await Promise.all([[$('nativeRight'),result.right],[$('nativeLeft'),result.left]].filter(([,src])=>src).map(([img,src])=>new Promise((resolve,reject)=>{img.onload=resolve;img.onerror=()=>reject(Error('原生图像加载失败'));img.src=src;})));
  nativeResult=result;nativeCurrent=revision===requestedRevision;$('nativeLeft').hidden=!split;$('nativeImages').hidden=false;
  $('nativeInfo').hidden=false;$('nativeInfo').textContent=JSON.stringify(result.metadata,null,2);
  $('nativeStatus').textContent=nativeCurrent?`Blender ${result.metadata.blenderVersion} · EEVEE · 原生 Film/TAA · 64 采样`:'参数已改变，点击原生渲染更新。';
 }catch(e){$('nativeStatus').textContent=e.message;}finally{nativeBusy=false;$('nativeRender').disabled=false;}
}
$('engine').addEventListener('change',()=>{const native=$('engine').value==='native';stage.classList.toggle('native',native);$('nativeRender').hidden=!native;$('nativeImages').hidden=!native||!nativeResult;$('stageNote').textContent=native?'作者完整材质与原灯光 · 拖动或改参数后，点击原生渲染':'拖动旋转 · 滚轮缩放 · 底材使用中性着色，便于观察边缘';$('nativeStatus').textContent='';if(native)renderNative();});
$('nativeRender').addEventListener('click',renderNative);
function validate(){
 const W=REFERENCE.width,H=REFERENCE.height,N=W*H;
 const p=program(fullscreen,`#version 300 es
precision highp float;precision highp int;out vec4 result;uniform sampler2D inputs;uniform bool frontFace;
${EDGE_CORE}
void main(){vec4 s=texelFetch(inputs,ivec2(gl_FragCoord.xy),0);result=evaluate_edge(s.xyz,s.w,frontFace);}`);
 const t=texture(W,H,gl.RGBA32F);gl.texSubImage2D(gl.TEXTURE_2D,0,0,0,W,H,gl.RGBA,gl.FLOAT,new Float32Array(REFERENCE.samples.flat()));const out=target(W,H,false,gl.RGBA32F);
 gl.bindFramebuffer(gl.FRAMEBUFFER,out.fbo);gl.viewport(0,0,W,H);gl.disable(gl.DEPTH_TEST);gl.disable(gl.CULL_FACE);gl.disable(gl.BLEND);gl.useProgram(p);bindParameters(p,configValues('Lassyla Material'));bindTexture(0,t);gl.uniform1i(uniform(p,'inputs'),0);gl.uniform1i(uniform(p,'frontFace'),1);gl.drawArrays(gl.TRIANGLES,0,3);
 const actual=new Float32Array(N*4);gl.readPixels(0,0,W,H,gl.RGBA,gl.FLOAT,actual);
 const report={sourceSHA256:REFERENCE.sourceSHA256,renderer:REFERENCE.renderer,sourceCommit:REFERENCE.commit,samples:N,channels:{}};
 for(const [name,index,reference,tolerance]of [['noise',0,REFERENCE.nativeNoise,0.000001],['noiseRamp',2,REFERENCE.nativeNoiseRamp,0.000002],['fresnelVsEEVEE',1,REFERENCE.nativeEEVEE.map(x=>x[1]),0.00051],['maskVsEEVEE',3,REFERENCE.nativeEEVEE.map(x=>x[2]),0.00051]]){
  let max=0,sum=0,worst=0;for(let i=0;i<N;i++){const error=Math.abs(actual[i*4+index]-reference[i]);if(error>max){max=error;worst=i;}sum+=error;}
  report.channels[name]={maxAbs:max,meanAbs:sum/N,worstSample:worst,tolerance,passed:Number.isFinite(max)&&max<=tolerance};
 }
 gl.uniform1i(uniform(p,'frontFace'),0);gl.drawArrays(gl.TRIANGLES,0,3);gl.readPixels(0,0,W,H,gl.RGBA,gl.FLOAT,actual);
 for(const [name,index,reference]of [['backfaceFresnel',1,REFERENCE.nativeBackface.map(x=>x[1])],['backfaceMask',3,REFERENCE.nativeBackface.map(x=>x[2])]]){
  let max=0,sum=0;for(let i=0;i<N;i++){const e=Math.abs(actual[i*4+index]-reference[i]);max=Math.max(e,max);sum+=e;}
  report.channels[name]={maxAbs:max,meanAbs:sum/N,tolerance:0.00051,passed:Number.isFinite(max)&&max<=0.00051};
 }
 report.geometry={};report.singleSampleVisibility={};
 for(const [name,ref]of Object.entries(GEOMETRY_REFERENCE)){
  const bytes=Uint8Array.from(atob(MESHES[name].data),c=>c.charCodeAt(0)),data=new Float32Array(bytes.buffer),vao=gl.createVertexArray(),vbo=gl.createBuffer();gl.bindVertexArray(vao);gl.bindBuffer(gl.ARRAY_BUFFER,vbo);gl.bufferData(gl.ARRAY_BUFFER,data,gl.STATIC_DRAW);
  for(let i=0;i<3;i++){gl.enableVertexAttribArray(i);gl.vertexAttribPointer(i,3,gl.FLOAT,false,36,i*12);}
  const rt=target(256,256,true,gl.RGBA32F);gl.bindFramebuffer(gl.FRAMEBUFFER,rt.fbo);gl.viewport(0,0,256,256);gl.enable(gl.DEPTH_TEST);gl.depthMask(true);gl.depthFunc(gl.LESS);gl.disable(gl.CULL_FACE);gl.clearColor(0,0,0,0);gl.clear(gl.COLOR_BUFFER_BIT|gl.DEPTH_BUFFER_BIT);gl.useProgram(drawProgram);bindParameters(drawProgram,configValues(ref.material));gl.uniformMatrix4fv(uniform(drawProgram,'model'),false,new Float32Array([1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1]));gl.uniformMatrix4fv(uniform(drawProgram,'projection'),false,projection(1));gl.uniform1f(uniform(drawProgram,'distance'),4.3*ref.radius);gl.uniform1i(uniform(drawProgram,'mode'),4);gl.uniform1i(uniform(drawProgram,'effect'),1);gl.drawArrays(gl.TRIANGLES,0,data.length/9);
  const image=new Float32Array(256*256*4);gl.readPixels(0,0,256,256,gl.RGBA,gl.FLOAT,image);
  const maxima=[0,0,0],sums=[0,0,0];let missing=0;
  for(const [x,y,...values]of ref.probes){const i=(y*256+x)*4;if(image[i+3]<0.99)missing++;for(let c=0;c<3;c++){const e=Math.abs(image[i+c]-values[c]);maxima[c]=Math.max(maxima[c],e);sums[c]+=e;}}
  report.geometry[name]={probes:ref.probes.length,missing,maxAbs:maxima,meanAbs:sums.map(x=>x/ref.probes.length),tolerance:0.00051,passed:missing===0&&maxima.every(x=>Number.isFinite(x)&&x<0.00051)};
  gl.clear(gl.COLOR_BUFFER_BIT|gl.DEPTH_BUFFER_BIT);gl.uniform1i(uniform(drawProgram,'mode'),5);gl.uniform1f(uniform(drawProgram,'sampleOffset'),Math.fround(halton(1,7)));gl.drawArrays(gl.TRIANGLES,0,data.length/9);gl.readPixels(0,0,256,256,gl.RGBA,gl.FLOAT,image);
  const alpha=Uint8Array.from(atob(ALPHA_REFERENCE[name].alpha),c=>c.charCodeAt(0));let different=0,nativeCovered=0,webCovered=0,union=0;
  for(let i=0;i<alpha.length;i++){const a=alpha[i]>127,b=image[i*4+3]>.5;if(a)nativeCovered++;if(b)webCovered++;if(a||b)union++;if(a!==b)different++;}
  report.singleSampleVisibility[name]={differentPixels:different,totalPixels:alpha.length,nativeCovered,webCovered,unionPixels:union,agreement:1-different/alpha.length};
  gl.bindVertexArray(null);gl.deleteVertexArray(vao);gl.deleteBuffer(vbo);destroy(rt);
 }
 report.nodeAndGeometryPassed=Object.values(report.channels).every(x=>x.passed)&&Object.values(report.geometry).every(x=>x.passed);report.browserPixelIdentical=Object.values(report.singleSampleVisibility).every(x=>x.differentPixels===0);report.passed=report.nodeAndGeometryPassed&&report.browserPixelIdentical;validationReport=report;
 $('validation').textContent=`${N.toLocaleString()} 组正反面样本 + 3 个原模型 · 节点与模型数值${report.nodeAndGeometryPassed?'通过':'存在差异'}；浏览器透明像素${report.browserPixelIdentical?'一致':'仍有差异'}。Noise 最大误差 ${report.channels.noise.maxAbs.toExponential(2)}；色阶 ${report.channels.noiseRamp.maxAbs.toExponential(2)}。`;
 $('validation').dataset.passed=String(report.passed);
 $('sourceInfo').textContent=JSON.stringify({sourceSHA256:REFERENCE.sourceSHA256,blenderCommit:REFERENCE.commit,originalParameters:REFERENCE.configs['Lassyla Material'],nativeComparison:report.channels,originalMeshComparison:report.geometry,singleSampleVisibility:report.singleSampleVisibility},null,2);
 gl.bindFramebuffer(gl.FRAMEBUFFER,null);gl.deleteProgram(p);gl.deleteTexture(t);destroy(out);
}
document.querySelectorAll('[data-material]').forEach(b=>b.addEventListener('click',()=>restore(b.dataset.material)));
for(const key of Object.keys(fieldMap))$(key).addEventListener('input',()=>{const v=Number($(key).value);if(!Number.isFinite(v))return;const next={...parameters,[key]:v};if(next.edgeEnd<=next.edgeStart||next.noiseEnd<=next.noiseStart||next.ior<=1||next.scale<=0){$('readout').textContent='白点必须大于黑点，IOR 必须大于 1。';return;}parameters=next;document.querySelectorAll('[data-material]').forEach(b=>b.setAttribute('aria-pressed','false'));$('readout').textContent='参数已调整；点击材质名称恢复原工程';requestDraw();});
document.querySelectorAll('[data-mode]').forEach(b=>b.addEventListener('click',()=>{mode=Number(b.dataset.mode);document.querySelectorAll('[data-mode]').forEach(x=>x.setAttribute('aria-pressed',String(x===b)));$('rightLabel').innerHTML='<b>'+['SOURCE','FRESNEL','NOISE','MASK'][mode]+'</b>'+['原工程边缘','边缘 Color Ramp','原始 Noise Fac','Mix Shader Fac'][mode];requestDraw();}));
$('shape').addEventListener('change',()=>{yaw=0;pitch=0;zoom=1;loadMesh();});
$('compare').addEventListener('change',()=>{stage.classList.toggle('single',!$('compare').checked);requestDraw();});
$('background').addEventListener('change',()=>{stage.dataset.background=$('background').value;});
$('resetView').addEventListener('click',()=>{yaw=0;pitch=0;zoom=1;requestDraw();});
canvas.addEventListener('pointerdown',e=>{if(e.button!==0)return;drag={id:e.pointerId,x:e.clientX,y:e.clientY};canvas.setPointerCapture(e.pointerId);canvas.focus({preventScroll:true});});
canvas.addEventListener('pointermove',e=>{if(!drag||e.pointerId!==drag.id)return;yaw+=(e.clientX-drag.x)*.008;pitch=clamp(pitch+(e.clientY-drag.y)*.008,-1.5,1.5);drag.x=e.clientX;drag.y=e.clientY;requestDraw();});
for(const event of ['pointerup','pointercancel','lostpointercapture'])canvas.addEventListener(event,()=>{drag=null;});
canvas.addEventListener('wheel',e=>{e.preventDefault();zoom=clamp(zoom*Math.exp(-e.deltaY*.001),.7,1.6);requestDraw();},{passive:false});
canvas.addEventListener('keydown',e=>{if(!['ArrowLeft','ArrowRight','ArrowUp','ArrowDown'].includes(e.key))return;e.preventDefault();if(e.key==='ArrowLeft')yaw-=.1;if(e.key==='ArrowRight')yaw+=.1;if(e.key==='ArrowUp')pitch-=.1;if(e.key==='ArrowDown')pitch+=.1;requestDraw();});
$('export').addEventListener('click',async()=>{if($('engine').value==='native'){if(!nativeCurrent){$('nativeStatus').textContent='请先点击原生渲染，更新当前参数。';return;}const response=await fetch(nativeResult.right);download('lassyla-native-'+$('shape').value+'.png',await response.blob());return;}if(frame){cancelAnimationFrame(frame);frame=0;}while(sample<(mode===0?64:1)){render();if(frame){cancelAnimationFrame(frame);frame=0;}}canvas.toBlob(blob=>{if(blob)download('lassyla-source-edge-'+$('shape').value+'.png',blob);});});
$('exportReport').addEventListener('click',()=>{if(validationReport)download('lassyla-node-validation.json',new Blob([JSON.stringify(validationReport,null,2)],{type:'application/json'}));});
canvas.addEventListener('webglcontextlost',e=>{e.preventDefault();if(frame)cancelAnimationFrame(frame);frame=0;fail('图形上下文丢失，请刷新页面。');});
try{validate();loadMesh();new ResizeObserver(requestDraw).observe(stage);}catch(e){fail(e.message);console.error(e);}
})();
