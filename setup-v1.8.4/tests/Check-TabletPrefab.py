"""Verify native tablet prefab dependencies and mod hitboxes against game assets.

Usage: python Check-TabletPrefab.py <game folder>. Reads two serialized files;
does not launch Unity or modify game data. No third-party packages required.
"""
import json
import pathlib
import re
import struct
import sys


def asset(path):
    data = path.read_bytes()
    assert struct.unpack_from('>I', data, 8)[0] == 22, 'Unsupported Unity asset version'
    base = struct.unpack_from('>q', data, 32)[0]
    pos = data.index(0, 48) + 1 + 4
    assert not data[pos], 'Expected stripped type tree'
    pos += 1
    count = struct.unpack_from('<i', data, pos)[0]; pos += 4
    types = []
    for _ in range(count):
        cid = struct.unpack_from('<i', data, pos)[0]; pos += 7
        pos += 32 if cid == 114 else 16
        types.append(cid)
    count = struct.unpack_from('<i', data, pos)[0]; pos += 4
    objs = {}
    for _ in range(count):
        pos = (pos + 3) & ~3
        oid, offset, size, ti = struct.unpack_from('<qqIi', data, pos); pos += 24
        objs[oid] = (types[ti], base + offset, size)
    names, transforms, by_go, scripts, monos = {}, {}, {}, {}, {}
    def string(offset):
        n = struct.unpack_from('<i', data, offset)[0]
        return data[offset + 4:offset + 4 + n].decode(), (offset + 4 + n + 3) & ~3
    for oid, (cid, start, size) in objs.items():
        if cid == 1:
            n = struct.unpack_from('<i', data, start)[0]
            names[oid] = string(start + 4 + n * 12 + 4)[0]
        elif cid == 4:
            go = struct.unpack_from('<q', data, start + 4)[0]
            n = struct.unpack_from('<i', data, start + 52)[0]
            parent = struct.unpack_from('<q', data, start + 60 + n * 12)[0]
            transforms[oid] = (go, parent, struct.unpack_from('<ffff', data, start + 12), struct.unpack_from('<fff', data, start + 28), struct.unpack_from('<fff', data, start + 40))
            by_go[go] = oid
        elif cid == 115:
            name, q = string(start)
            classname, q = string(q + 20)
            scripts[oid] = classname
        elif cid == 114:
            go = struct.unpack_from('<q', data, start + 4)[0]
            script = struct.unpack_from('<q', data, start + 20)[0]
            _, q = string(start + 28)
            monos[oid] = (go, script, data[q:start + size])
    def name_path(oid):
        go, parent, *_ = transforms[oid]
        return (name_path(parent) + '/' if parent in transforms else '') + names[go]
    paths = {name_path(t): go for go, t in by_go.items()}
    return names, transforms, by_go, scripts, monos, paths


def rotate(q, p):
    x, y, z, w = q
    # Quaternion-vector multiplication, independent of Unity.
    u = (x, y, z)
    dot = sum(a*b for a, b in zip(u, p))
    norm = sum(a*a for a in u)
    cross = (y*p[2]-z*p[1], z*p[0]-x*p[2], x*p[1]-y*p[0])
    return tuple(2*dot*u[i] + (w*w-norm)*p[i] + 2*w*cross[i] for i in range(3))


def main():
    root = pathlib.Path(sys.argv[1]) / 'A Township Tale_Data'
    scripts = asset(root / 'globalgamemanagers.assets')[3]
    names, transforms, by_go, _, monos, paths = asset(root / 'resources.assets')
    base = 'Social Tablet/Body/Touch Screen/Components/Pages/'
    def components(path):
        return [(scripts[m[1]], m[2]) for m in monos.values() if m[0] == paths[path]]
    def payload(path, name):
        return next(p for n, p in components(path) if n == name)
    checks = 0
    def check(ok, label):
        nonlocal checks
        assert ok, label
        checks += 1
        print('PASS', label)
    mute = base + 'Player Actions Page/Mute Button'
    check([n for n, _ in components(mute)] == ['TouchScreenButton', 'TouchScreenButtonVisual'], 'mute template has no native navigation or persistent action callbacks')
    p = payload('Social Tablet', 'Window')
    refs = [struct.unpack_from('<iq', p, i * 12) for i in range(5)]
    check(refs[0][1] != 0 and refs[2][1] != 0, 'native tablet has starting page and loading indicator')
    check(refs[1][1] == refs[3][1] == refs[4][1] == 0, 'native tablet popups are absent and must not be called')
    rp = payload(base + 'Recent Players Page', 'RecentPlayersPage')
    check(struct.unpack_from('<i', rp, 64)[0] == 5, 'native roster provides five reusable paginated rows')
    check(abs(struct.unpack_from('<ff', payload(mute, 'TouchScreenButton'), len(payload(mute, 'TouchScreenButton')) - 8)[0] - .218) < .001, 'native touch width matches calibrated clone width')
    def world(tid, point):
        while tid in transforms:
            _, tid_parent, q, pos, scale = transforms[tid]
            point = tuple(point[i]*scale[i] for i in range(3))
            point = rotate(q, point)
            point = tuple(point[i]+pos[i] for i in range(3))
            tid = tid_parent
        return point
    def local(tid, point):
        chain=[]
        while tid in transforms:
            chain.append(transforms[tid]); tid=transforms[tid][1]
        for _, _, q, pos, scale in reversed(chain):
            point=tuple(point[i]-pos[i] for i in range(3))
            point=rotate((-q[0],-q[1],-q[2],q[3]),point)
            point=tuple(point[i]/scale[i] for i in range(3))
        return point
    screen_go = next(m[0] for m in monos.values() if scripts.get(m[1]) == 'TouchScreenMenuBase' and m[0] == paths['Social Tablet/Body/Touch Screen'])
    screen = by_go[screen_go]
    source = (pathlib.Path(__file__).parent.parent / 'mod-src/NativeSocialTablet.cs').read_text()
    check('new Vector3(-.02588f, y, x + .026f)' in source, 'clone placement uses native screen depth and horizontal Z axis')
    check('ShowOptions(' not in source and 'ShowConfirmPopup(' not in source, 'tablet uses controls instead of absent popup components')
    for page, parent in [('Actions','Player Actions Page'), ('List','Recent Players Page')]:
        positions=[]
        for key, x, y in re.findall(r'view\.(\w+) = Clone\(view, view\.'+page+r'\.transform, "[^"]+", (-?[.\d]+)f, (-?[.\d]+)f',source):
            width, height = ((.218*.31, .05*.64) if page == 'List' else (.218*.47, .05*.82))
            point = local(screen, world(by_go[paths[base+parent]],(-.02588,float(y),float(x)+.026)))
            positions.append((key,point,width,height))
        if page == 'Actions':
            point=local(screen,world(by_go[paths[base+parent]],(-.02588,-.022,-.084+.026)))
            positions.append(('Mute',point,.218*.47,.05*.82))
        check(len(positions) == (6 if page == 'Actions' else 3), page+' control count is complete')
        for i, (name,p,w,h) in enumerate(positions):
            for other,q,ow,oh in positions[i+1:]:
                check(abs(p[0]-q[0]) >= (w+ow)/2 or abs(p[1]-q[1]) >= (h+oh)/2, page+' hitboxes do not overlap: '+name+'/'+other)
        # The template and all clones must remain in the same screen plane.
        template = local(screen,world(by_go[paths[mute]],(0,0,0)))
        check(all(abs(p[2]-template[2]) < .001 for _,p,_,_ in positions), page+' clone hitboxes remain on the native screen plane')
    print('PASS',checks,'native prefab/layout checks (static asset verification; no VR interaction claimed)')


if __name__ == '__main__':
    main()
