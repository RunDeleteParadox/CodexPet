"""Exclusive USB validation/measurement; never run while the Hub owns the port.
No Wi-Fi. Results are technical observations, not aesthetic acceptance.
"""
import argparse, json, re, time
from pathlib import Path
import serial
p=argparse.ArgumentParser(); p.add_argument('--port',required=True);p.add_argument('--device-id',required=True);p.add_argument('--output',required=True)
p.add_argument('--measure-only',action='store_true',help='Collect performance after separately validating short reactions')
a=p.parse_args(); output=Path(a.output);output.parent.mkdir(parents=True,exist_ok=True)
s=serial.Serial(port=None,baudrate=115200,timeout=.08);s.port=a.port;s.dtr=False;s.rts=False;s.open()
results={'checks':[],'performance':{},'heap':{},'completed':False,'measureOnly':a.measure_only}; reports=[]; phase='functional'
serial_trace=[]
serial_pending=bytearray()
def line():
    # A serial timeout may return only part of a line. Keep it until newline.
    serial_pending.extend(s.readline())
    if not serial_pending.endswith(b'\n'):return ''
    v=serial_pending.decode('utf-8',errors='replace').strip();serial_pending.clear()
    if v:serial_trace.append({'time':time.monotonic(),'phase':phase,'line':v})
    if v.startswith('[avatar perf]'):
        values=dict((k,float(v)) for k,v in re.findall(r'(fps|render_avg|render_max|frame_avg|frame_max)=([\d.]+)',v))
        reports.append((time.monotonic(),phase,values))
    return v

def command(text,kind='ok'):
    for attempt in range(2):
        s.write((text+'\n').encode()); deadline=time.monotonic()+2
        while time.monotonic()<deadline:
            v=line()
            if v.startswith('CP '):
                d=json.loads(v[3:])
                if d.get('type')==kind and (kind!='ok' or d.get('command')==text.split()[0]):return d
    raise RuntimeError('Missing response: '+text)
def wait(seconds):
    end=time.monotonic()+seconds
    while time.monotonic()<end:line()
def check(condition,name):
    if not condition:raise AssertionError(name)
    results['checks'].append(name);print('PASS '+name,flush=True)
def status():return command('STATUS','status')
try:
    wait(1);info=command('INFO','info')
    check(info['deviceId']==a.device_id and info['firmwareVersion']=='1.1.6','identified device and firmware 1.1.6')
    command('WAKE')
    if not a.measure_only:
        command('STATE working');command('REACT success');wait(.2)
        d=status();check(d['state']=='success' and d['requestedState']=='working','Success keeps Working base')
        wait(5.2);check(status()['state']=='working','Success returns to Working')
        command('REACT error');wait(.2);check(status()['state']=='error','Error reaction starts')
        wait(3.2);check(status()['state']=='working','Error returns to Working')
        command('STATE idle');command('REACT stop');wait(.25);d=status();check(d['state']=='stop','neutral Stop starts: '+d['state'])
        wait(1);check(status()['state']=='idle','Stop returns to Idle')
        command('STATE working');command('REACT success');wait(.15);command('STATE waiting')
        check(status()['state']=='waiting','Waiting preempts Success')
        command('SLEEP');wait(1.8);d=status();check(d['sleeping'] and d['requestedState']=='waiting','sleep preserves waiting base')
        command('WAKE');command('REACT wake');wait(.2);check(status()['state']=='wake','Wake starts after panel wake')
        wait(2.2);check(status()['state']=='waiting','Wake returns to Waiting')
    results['heap']['before']=status()['freeHeap']
    for name in ['idle','thinking','working','waiting','success','error']:
        phase=name;print('MEASURE '+name,flush=True)
        if name in ('success','error'):
            command('STATE thinking');start=time.monotonic();next_reaction=start
            while time.monotonic()-start<16:
                if time.monotonic()>=next_reaction:
                    s.write(('REACT '+name+'\n').encode())
                    next_reaction=time.monotonic()+(5 if name=='success' else 3)
                line()
        else:
            command('STATE '+name)
            check(status()['state']==name,'performance expression confirmed: '+name)
            start=time.monotonic();wait(16)
        values=[d for t,n,d in reports if n==name and t-start>=5]
        check(len(values)>=2,'two steady performance windows: '+name)
        results['performance'][name]=values
        results['heap'][name]=status()['freeHeap']
        print(json.dumps({name:values}),flush=True)
    command('STATE idle');results['heap']['after']=status()['freeHeap'];results['completed']=True
finally:
    try:command('STATE idle');command('WAKE')
    except Exception:pass
    s.close();output.write_text(json.dumps(results,indent=2),encoding='utf-8')
    output.with_suffix('.serial.json').write_text(json.dumps(serial_trace,indent=2),encoding='utf-8')
print('RESULT: '+str(len(results['checks']))+' device checks passed',flush=True)
