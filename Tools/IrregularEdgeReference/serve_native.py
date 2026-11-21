"""Local-only HTML preview and native Blender renderer. Python standard library only."""
import argparse, hashlib, json, math, mimetypes, os, shutil, subprocess, threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse

ROOT=Path(__file__).resolve().parent
HTML=ROOT.parents[1]/'Documentation'/'IrregularEdgeLab.html'
CACHE=ROOT/'native-renders'
LOCK=threading.Lock()
DEFAULT_BLENDER=r'F:\Steam\steamapps\common\Blender\blender.exe'

def validate(data):
    if data.get('shape') not in ('Sphere','Cube','Suzanne'):raise ValueError('Unknown model')
    if data.get('material') not in ('Lassyla Material','Lassyla Material Green','Lassyla Material Red'):raise ValueError('Unknown material')
    if data.get('mode') not in (0,1,2,3):raise ValueError('Unknown display mode')
    out={k:data[k] for k in ('shape','material','mode')}
    for k,lo,hi in [('width',64,1600),('height',64,1200),('yaw',-1000,1000),('pitch',-1.5,1.5),('zoom',.7,1.6)]:
        v=float(data[k])
        if not math.isfinite(v) or not lo<=v<=hi:raise ValueError('Invalid '+k)
        out[k]=int(v) if k in ('width','height') else v
    out['compare']=bool(data.get('compare'))
    out['parameters']={}
    for k,lo,hi in [('ior',1.00001,3),('scale',.01,100),('edgeStart',0,1),('edgeEnd',0,1),('noiseStart',0,1),('noiseEnd',0,1)]:
        v=float(data['parameters'][k])
        if not math.isfinite(v) or not lo<=v<=hi:raise ValueError('Invalid '+k)
        out['parameters'][k]=v
    p=out['parameters']
    if p['edgeStart']>=p['edgeEnd'] or p['noiseStart']>=p['noiseEnd']:raise ValueError('Invalid ramp positions')
    return out

class Handler(BaseHTTPRequestHandler):
    def send(self,status,data,kind='application/json'):
        if isinstance(data,(dict,list)):data=json.dumps(data).encode()
        self.send_response(status);self.send_header('Content-Type',kind);self.send_header('Content-Length',str(len(data)));self.send_header('Cache-Control','no-store');self.end_headers();self.wfile.write(data)
    def do_GET(self):
        path=urlparse(self.path).path
        if path=='/api/native':return self.send(200,{'available':Path(self.server.blender).is_file(),'engine':'Blender EEVEE'})
        if path in ('/','/IrregularEdgeLab.html'):return self.send(200,HTML.read_bytes(),'text/html; charset=utf-8')
        parts=path.split('/')
        if len(parts)==4 and parts[1]=='renders' and len(parts[2])==24 and all(c in '0123456789abcdef' for c in parts[2]) and parts[3] in ('left.png','right.png','metadata.json'):
            file=CACHE/parts[2]/parts[3]
            if file.is_file():return self.send(200,file.read_bytes(),mimetypes.guess_type(file.name)[0] or 'application/octet-stream')
        self.send(404,{'error':'Not found'})
    def do_POST(self):
        if self.path!='/api/render':return self.send(404,{'error':'Not found'})
        origin=self.headers.get('Origin')
        if origin and origin!=f'http://127.0.0.1:{self.server.server_port}':return self.send(403,{'error':'Local origin required'})
        try:
            length=int(self.headers.get('Content-Length','0'))
            if not 0<length<8192:raise ValueError('Invalid request size')
            job=validate(json.loads(self.rfile.read(length)))
        except (ValueError,KeyError,TypeError) as e:return self.send(400,{'error':str(e)})
        if not LOCK.acquire(blocking=False):return self.send(429,{'error':'Blender is rendering; try again after it finishes.'})
        try:
            version=hashlib.sha256((ROOT/'native_render.py').read_bytes()+(ROOT/'Lassyla_Material.blend').read_bytes()).hexdigest()
            key=hashlib.sha256((version+json.dumps(job,sort_keys=True)).encode()).hexdigest()[:24]
            directory=CACHE/key;directory.mkdir(parents=True,exist_ok=True)
            if not (directory/'metadata.json').is_file():
                jobfile=directory/'request.json';jobfile.write_text(json.dumps(job))
                flags=subprocess.CREATE_NO_WINDOW if os.name=='nt' else 0
                run=subprocess.run([self.server.blender,'--background','--factory-startup','--disable-autoexec','--python-exit-code','1','--python',str(ROOT/'native_render.py'),'--',str(jobfile)],capture_output=True,timeout=180,creationflags=flags)
                (directory/'render.log').write_bytes(run.stdout+run.stderr)
                if run.returncode or not (directory/'metadata.json').is_file():raise RuntimeError('Native render failed; see '+str(directory/'render.log'))
            self.send(200,{'right':f'/renders/{key}/right.png','left':f'/renders/{key}/left.png' if job['compare'] else None,'metadata':json.loads((directory/'metadata.json').read_text(encoding='utf-8')),'metadataURL':f'/renders/{key}/metadata.json'})
        except (OSError,subprocess.TimeoutExpired,RuntimeError) as e:self.send(500,{'error':str(e)})
        finally:LOCK.release()

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--port',type=int,default=8769);parser.add_argument('--blender',default=os.environ.get('BLENDER_EXE',shutil.which('blender') or DEFAULT_BLENDER));args=parser.parse_args()
    if not Path(args.blender).is_file():parser.error('Blender executable not found; supply --blender PATH')
    server=ThreadingHTTPServer(('127.0.0.1',args.port),Handler);server.blender=args.blender
    print(f'Native HTML preview: http://127.0.0.1:{args.port}/IrregularEdgeLab.html',flush=True)
    try:server.serve_forever()
    except KeyboardInterrupt:server.server_close()

