"""Exercise the actual x64 DLL: identity, loopback friends/messages, save/restore.
No Internet bootstrap or LAN discovery is used. Run with 64-bit Python 3.
"""
import ctypes as c
import pathlib
import time

root = pathlib.Path(__file__).resolve().parent
lib = c.CDLL(str(root / "dist" / "libtoxcore.dll"))
P = c.c_void_p
U8 = c.POINTER(c.c_uint8)
ERR = c.POINTER(c.c_int)

def api(name, result, *args):
    fn = getattr(lib, name)
    fn.restype, fn.argtypes = result, list(args)
    return fn

version = tuple(api("tox_version_" + v, c.c_uint32)() for v in ("major", "minor", "patch"))
assert version == (0, 2, 23), version
options_new = api("tox_options_new", P, ERR)
options_free = api("tox_options_free", None, P)
for key in ("ipv6_enabled", "local_discovery_enabled", "hole_punching_enabled"):
    api("tox_options_set_" + key, None, P, c.c_bool)
api("tox_options_set_savedata_type", None, P, c.c_int)
api("tox_options_set_savedata_data", None, P, U8, c.c_size_t)
new = api("tox_new", P, P, ERR)
kill = api("tox_kill", None, P)
iterate = api("tox_iterate", None, P, P)
getkey = api("tox_self_get_public_key", None, P, U8)
getdht = api("tox_self_get_dht_id", None, P, U8)
getport = api("tox_self_get_udp_port", c.c_uint16, P, ERR)
bootstrap = api("tox_bootstrap", c.c_bool, P, c.c_char_p, c.c_uint16, U8, ERR)
add = api("tox_friend_add_norequest", c.c_uint32, P, U8, ERR)
status = api("tox_friend_get_connection_status", c.c_int, P, c.c_uint32, ERR)
send = api("tox_friend_send_message", c.c_uint32, P, c.c_uint32, c.c_int, U8, c.c_size_t, ERR)
size = api("tox_get_savedata_size", c.c_size_t, P)
save = api("tox_get_savedata", None, P, U8)
callback_type = c.CFUNCTYPE(None, P, c.c_uint32, c.c_int, U8, c.c_size_t, P)
register = api("tox_callback_friend_message", None, P, callback_type)
err = c.c_int()
instances = []

def create(saved=None):
    opts = options_new(c.byref(err))
    assert opts and err.value == 0
    try:
        for key in ("ipv6_enabled", "local_discovery_enabled", "hole_punching_enabled"):
            getattr(lib, "tox_options_set_" + key)(opts, False)
        if saved is not None:
            lib.tox_options_set_savedata_type(opts, 1)
            lib.tox_options_set_savedata_data(opts, saved, len(saved))
        instance = new(opts, c.byref(err))
        assert instance and err.value == 0, ("tox_new", err.value)
        instances.append(instance)
        return instance
    finally:
        options_free(opts)

def key(instance, getter=getkey):
    value = (c.c_uint8 * 32)()
    getter(instance, value)
    return value

def pump(predicate, seconds=30):
    stop = time.monotonic() + seconds
    while time.monotonic() < stop:
        for instance in instances:
            iterate(instance, None)
        if predicate():
            return
        time.sleep(0.01)
    raise AssertionError("Native loopback condition timed out")

try:
    alice, bob = create(), create()
    alice_key, bob_key = key(alice), key(bob)
    assert bytes(alice_key) != bytes(bob_key)
    a_friend = add(alice, bob_key, c.byref(err)); assert err.value == 0
    b_friend = add(bob, alice_key, c.byref(err)); assert err.value == 0
    for src, dst in ((alice, bob), (bob, alice)):
        assert bootstrap(src, b"127.0.0.1", getport(dst, c.byref(err)), key(dst, getdht), c.byref(err))
        assert err.value == 0
    pump(lambda: status(alice, a_friend, c.byref(err)) != 0 and status(bob, b_friend, c.byref(err)) != 0)
    messages = []
    @callback_type
    def receive(tox, friend, kind, data, length, user):
        messages.append(bytes(data[:length]))
    register(bob, receive)
    payload = b'{"v":1,"type":"native-smoke","invite":"loopback-only"}'
    buffer = (c.c_uint8 * len(payload)).from_buffer_copy(payload)
    send(alice, a_friend, 0, buffer, len(payload), c.byref(err)); assert err.value == 0
    pump(lambda: len(messages) == 1)
    assert messages == [payload]
    state = (c.c_uint8 * size(alice))()
    save(alice, state)
    kill(alice); instances.remove(alice)
    restored = create(state)
    assert bytes(key(restored)) == bytes(alice_key)
    print("PASS: native 0.2.23 DLL load; distinct identities; two-peer loopback bootstrap; mutual friend connection; encrypted message callback; saved identity restore.")
finally:
    for instance in instances:
        kill(instance)
