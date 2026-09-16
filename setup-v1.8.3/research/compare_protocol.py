"""Compare protocol/config code objects without executing launcher code."""
import marshal, sys, types, zlib, os
old = marshal.loads(open(sys.argv[1], 'rb').read())
data = open(sys.argv[2], 'rb').read()
toc = dict(marshal.loads(data[int.from_bytes(data[8:12], 'big'):]))
def find(c, name):
    if c.co_name == name: return c
    for item in c.co_consts:
        if isinstance(item, types.CodeType):
            result = find(item, name)
            if result is not None: return result
def norm(v):
    if isinstance(v, types.CodeType):
        return (v.co_code, v.co_names, v.co_varnames, v.co_freevars, v.co_cellvars,
                v.co_exceptiontable, tuple(norm(c) for c in v.co_consts))
    return v
modules = {}
for module in ('client.core.auth', 'client.core.config'):
    kind, offset, size = toc[module]
    modules[module] = marshal.loads(zlib.decompress(data[offset:offset+size]))
for module, names in {
    'client.core.auth': ('authenticate', 'ping_server', 'build_tokens', '_headless_user_id', '_resolve_ip_for_game', '_jwt'),
    'client.core.config': ('_safe_part', '_token_file', '_legacy_token_file', '_get_or_create_token'),
}.items():
    for name in names:
        a, b = find(old, name), find(modules[module], name)
        print(module + '.' + name, 'IDENTICAL' if a is not None and b is not None and norm(a) == norm(b) else 'DIFFERS')
