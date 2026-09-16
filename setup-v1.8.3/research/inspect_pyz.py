"""Read code metadata and disassemble only; do not execute bundled client code."""
import marshal, types, dis, zlib, sys, os
data = open(sys.argv[1], 'rb').read()
offset = int.from_bytes(data[8:12], 'big')
toc = dict(marshal.loads(data[offset:]))
os.makedirs(sys.argv[2], exist_ok=True)
with open(os.path.join(sys.argv[2], 'modules.txt'), 'w', encoding='utf-8') as f:
    for name, item in toc.items(): f.write(repr((name, item)) + '\n')
for name, (kind, position, size) in toc.items():
    if not name.startswith(('client.', 'tavern_shared.')): continue
    c = marshal.loads(zlib.decompress(data[position:position+size]))
    with open(os.path.join(sys.argv[2], name + '.txt'), 'w', encoding='utf-8') as f:
        def dump(c):
            f.write('\nFUNCTION ' + c.co_qualname + '\nARGS ' + repr(c.co_varnames) + '\nNAMES ' + repr(c.co_names) + '\nCONSTANTS\n')
            for i, v in enumerate(c.co_consts):
                if isinstance(v, types.CodeType): f.write(str(i) + ' CODE ' + v.co_qualname + '\n')
                else: f.write(str(i) + ' ' + repr(v) + '\n')
            for v in c.co_consts:
                if isinstance(v, types.CodeType): dump(v)
        dump(c)
        dis.dis(c, file=f)
print('Read', len(toc), 'module entries; client/tavern_shared disassembly saved.')
