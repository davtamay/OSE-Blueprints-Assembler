import json

data = json.load(open(r'Assets\_Project\Data\Packages\power_cube_frame\machine.json'))
structural = ['base_tube_long','base_tube_short','vertical_post','top_tube_long','top_tube_short','engine_mount_plate']

for pp in data.get('previewConfig',{}).get('partPlacements',[]):
    pid = pp.get('partId','')
    if any(pid.startswith(s) for s in structural):
        sp = pp.get('startPosition',{})
        sr = pp.get('startRotation',{})
        ss = pp.get('startScale',{})
        pp2 = pp.get('playPosition',{})
        pr = pp.get('playRotation',{})
        ps = pp.get('playScale',{})
        print(f"{pid}:")
        sx, sy, sz = sp.get('x',0), sp.get('y',0), sp.get('z',0)
        srx, sry, srz, srw = sr.get('x',0), sr.get('y',0), sr.get('z',0), sr.get('w',1)
        ssx, ssy, ssz = ss.get('x',1), ss.get('y',1), ss.get('z',1)
        print(f"  start pos=({sx:.3f},{sy:.3f},{sz:.3f}) rot=({srx:.3f},{sry:.3f},{srz:.3f},{srw:.3f}) scale=({ssx:.4f},{ssy:.4f},{ssz:.4f})")
        px, py, pz = pp2.get('x',0), pp2.get('y',0), pp2.get('z',0)
        prx, pry, prz, prw = pr.get('x',0), pr.get('y',0), pr.get('z',0), pr.get('w',1)
        psx, psy, psz = ps.get('x',1), ps.get('y',1), ps.get('z',1)
        print(f"  play  pos=({px:.3f},{py:.3f},{pz:.3f}) rot=({prx:.3f},{pry:.3f},{prz:.3f},{prw:.3f}) scale=({psx:.4f},{psy:.4f},{psz:.4f})")
