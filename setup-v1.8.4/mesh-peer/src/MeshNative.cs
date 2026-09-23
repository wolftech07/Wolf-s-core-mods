using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace TavernNativeMenu
{
    // All instance calls belong to the mesh worker. Callback delegates are
    // rooted for its entire lifetime; no Unity API runs inside a native callback.
    internal sealed class MeshNative : IDisposable
    {
        private const string Library = "libtoxcore.dll";
        private IntPtr handle;
        private readonly RequestCallback requestCallback;
        private readonly MessageCallback messageCallback;
        private static IntPtr libraryHandle;
        private static readonly object LibraryGate = new object();
        private Exception callbackError;
        internal Action<string, string> Request;
        internal Action<string, string> Message;
        internal string Address { get; private set; }
        internal bool Connected { get { return tox_self_get_connection_status(handle) != 0; } }

        internal MeshNative(string directory, byte[] saved, string name)
        {
            if (IntPtr.Size != 8) throw new NotSupportedException("The friends network requires the 64-bit game.");
            lock (LibraryGate) if (libraryHandle == IntPtr.Zero)
            {
                string file = Path.GetFullPath(Path.Combine(directory, Library));
                if (!File.Exists(file)) throw new FileNotFoundException("Friends networking files are missing. Run Install / Update again.");
                libraryHandle = LoadLibraryEx(file, IntPtr.Zero, 0x00000008);
                if (libraryHandle == IntPtr.Zero) throw new InvalidOperationException("The bundled friends network could not load (Windows " + Marshal.GetLastWin32Error() + ").");
            }
            if (tox_version_major() != 0 || tox_version_minor() != 2 || tox_version_patch() < 23)
                throw new InvalidOperationException("The friends network library version is unsupported.");
            int error;
            IntPtr options = tox_options_new(out error);
            if (options == IntPtr.Zero) throw new InvalidOperationException("Could not allocate friends network options.");
            try
            {
                tox_options_set_ipv6_enabled(options, true);
                tox_options_set_local_discovery_enabled(options, false);
                tox_options_set_tcp_port(options, 0); // Do not run a public TCP relay on the player's machine.
                if (saved != null && saved.Length > 0)
                {
                    tox_options_set_savedata_type(options, 1);
                    if (!tox_options_set_savedata_data(options, saved, (UIntPtr)saved.Length)) throw new InvalidDataException("The saved peer identity could not be loaded.");
                }
                handle = tox_new(options, out error);
                if (handle == IntPtr.Zero || error != 0) throw new InvalidDataException("Friends identity could not start (network error " + error + "). Existing data was preserved.");
            }
            catch { Dispose(); throw; }
            finally { tox_options_free(options); }
            try
            {
            byte[] address = new byte[38];
            tox_self_get_address(handle, address);
            Address = Hex(address);
            byte[] username = Encoding.UTF8.GetBytes(name);
            if (!tox_self_set_name(handle, username, (UIntPtr)username.Length, out error)) throw new InvalidOperationException("Could not set the peer display name.");
            requestCallback = delegate(IntPtr tox, IntPtr key, IntPtr text, UIntPtr length, IntPtr data)
            {
                Dispatch(delegate { if (Request != null) Request(Hex(Copy(key, 32)), Read(text, length, 921)); });
            };
            messageCallback = delegate(IntPtr tox, uint number, int type, IntPtr text, UIntPtr length, IntPtr data)
            {
                Dispatch(delegate { if (type == 0 && Message != null) Message(PublicKey(number), Read(text, length, 1372)); });
            };
            tox_callback_friend_request(handle, requestCallback);
            tox_callback_friend_message(handle, messageCallback);
            }
            catch { Dispose(); throw; }
        }

        private void Dispatch(Action action)
        {
            // Never unwind a managed exception through C. Storage failures are
            // raised after tox_iterate returns, so the owner stops safely.
            try { action(); }
            catch (InvalidDataException) { }
            catch (ArgumentException) { }
            catch (FormatException) { }
            catch (Exception error) { callbackError = error; }
        }
        internal void Iterate()
        {
            tox_iterate(handle, IntPtr.Zero);
            if (callbackError != null) { Exception error = callbackError; callbackError = null; throw new IOException("Friends networking stopped to preserve saved data.", error); }
        }
        internal byte[] Save()
        {
            ulong size = tox_get_savedata_size(handle).ToUInt64();
            if (size == 0 || size > 4 * 1024 * 1024) throw new InvalidDataException("The peer identity exceeds its storage limit.");
            byte[] bytes = new byte[(int)size]; tox_get_savedata(handle, bytes); return bytes;
        }
        internal void Bootstrap(string host, ushort port, string publicKey, ushort[] tcpPorts)
        {
            int error; byte[] key = Unhex(publicKey, 32);
            tox_bootstrap(handle, host, port, key, out error);
            foreach (ushort tcp in tcpPorts) tox_add_tcp_relay(handle, host, tcp, key, out error);
        }
        internal uint Number(string key)
        {
            int error; uint number = tox_friend_by_public_key(handle, Unhex(key, 32), out error);
            return error == 0 ? number : UInt32.MaxValue;
        }
        internal string[] FriendKeys()
        {
            ulong count = tox_self_get_friend_list_size(handle).ToUInt64();
            if (count > 4096) throw new InvalidDataException("The native peer list exceeds its limit.");
            var numbers = new uint[(int)count];
            if (count > 0) tox_self_get_friend_list(handle, numbers);
            var keys = new string[numbers.Length];
            for (int i = 0; i < numbers.Length; i++) keys[i] = PublicKey(numbers[i]);
            return keys;
        }
        internal bool Online(string key)
        {
            uint number = Number(key); if (number == UInt32.MaxValue) return false;
            int error; return tox_friend_get_connection_status(handle, number, out error) != 0 && error == 0;
        }
        internal string PublicKey(uint number)
        {
            int error; byte[] key = new byte[32];
            if (!tox_friend_get_public_key(handle, number, key, out error)) throw new InvalidDataException("Unknown peer.");
            return Hex(key);
        }
        internal void Add(string address, string request)
        {
            byte[] id = ValidateAddress(address);
            if (Number(Hex(Subarray(id, 32))) != UInt32.MaxValue) return;
            byte[] message = Encoding.UTF8.GetBytes(request); int error;
            tox_friend_add(handle, id, message, (UIntPtr)message.Length, out error);
            if (error != 0) throw new InvalidOperationException("Could not send the friend request (network error " + error + ").");
        }
        internal void AddWithoutRequest(string key)
        {
            if (Number(key) != UInt32.MaxValue) return;
            int error; tox_friend_add_norequest(handle, Unhex(key, 32), out error);
            if (error != 0) throw new InvalidOperationException("Could not establish the friend connection.");
        }
        internal void Delete(string key)
        {
            uint number = Number(key); if (number == UInt32.MaxValue) return;
            int error;
            if (!tox_friend_delete(handle, number, out error)) throw new InvalidOperationException("Could not remove the peer connection.");
        }
        internal bool Send(string key, string message)
        {
            uint number = Number(key); if (number == UInt32.MaxValue) return false;
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            if (bytes.Length > 1372) throw new InvalidDataException("The friends message is too large.");
            int error; tox_friend_send_message(handle, number, 0, bytes, (UIntPtr)bytes.Length, out error);
            return error == 0;
        }
        internal static byte[] ValidateAddress(string value)
        {
            byte[] data = Unhex((value ?? "").Trim(), 38);
            bool nonzero = false; for (int i = 0; i < 32; i++) if (data[i] != 0) nonzero = true;
            if (!nonzero) throw new ArgumentException("The friend code has an invalid public key.");
            byte a = 0, b = 0;
            for (int i = 0; i < 36; i += 2) { a ^= data[i]; b ^= data[i + 1]; }
            if (data[36] != a || data[37] != b) throw new ArgumentException("The friend code checksum is invalid. Check the full code.");
            return data;
        }
        internal static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", ""); }
        internal static byte[] Unhex(string value, int bytes)
        {
            if (value == null || value.Length != bytes * 2) throw new ArgumentException("The friend code or identity has an invalid length.");
            byte[] result = new byte[bytes];
            for (int i = 0; i < bytes; i++)
            {
                byte b;
                if (!Uri.IsHexDigit(value[i * 2]) || !Uri.IsHexDigit(value[i * 2 + 1]) || !Byte.TryParse(value.Substring(i * 2, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out b))
                    throw new ArgumentException("The friend code contains invalid characters.");
                result[i] = b;
            }
            return result;
        }
        private static byte[] Subarray(byte[] data, int count) { byte[] result = new byte[count]; Array.Copy(data, result, count); return result; }
        private static byte[] Copy(IntPtr data, int length) { byte[] result = new byte[length]; Marshal.Copy(data, result, 0, length); return result; }
        private static string Read(IntPtr data, UIntPtr length, int max)
        { ulong size = length.ToUInt64(); if (size > (ulong)max) throw new InvalidDataException(); return new UTF8Encoding(false, true).GetString(Copy(data, (int)size)); }
        public void Dispose() { if (handle != IntPtr.Zero) { tox_kill(handle); handle = IntPtr.Zero; } }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void RequestCallback(IntPtr tox, IntPtr key, IntPtr message, UIntPtr length, IntPtr data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void MessageCallback(IntPtr tox, uint friend, int type, IntPtr message, UIntPtr length, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "LoadLibraryExW")] private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern uint tox_version_major();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern uint tox_version_minor();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern uint tox_version_patch();
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr tox_options_new(out int error);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void tox_options_free(IntPtr options);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void tox_options_set_ipv6_enabled(IntPtr options, [MarshalAs(UnmanagedType.I1)] bool value);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void tox_options_set_local_discovery_enabled(IntPtr options, [MarshalAs(UnmanagedType.I1)] bool value);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void tox_options_set_tcp_port(IntPtr options, ushort value);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void tox_options_set_savedata_type(IntPtr options, int type);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool tox_options_set_savedata_data(IntPtr options, byte[] data, UIntPtr size);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr tox_new(IntPtr options, out int error);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void tox_kill(IntPtr tox);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void tox_iterate(IntPtr tox, IntPtr data);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int tox_self_get_connection_status(IntPtr tox);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern UIntPtr tox_get_savedata_size(IntPtr tox);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void tox_get_savedata(IntPtr tox, byte[] data);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void tox_self_get_address(IntPtr tox, byte[] data);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool tox_self_set_name(IntPtr tox, byte[] name, UIntPtr length, out int error);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool tox_bootstrap(IntPtr tox, string host, ushort port, byte[] key, out int error);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool tox_add_tcp_relay(IntPtr tox, string host, ushort port, byte[] key, out int error);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern uint tox_friend_by_public_key(IntPtr tox, byte[] key, out int error);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern UIntPtr tox_self_get_friend_list_size(IntPtr tox);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void tox_self_get_friend_list(IntPtr tox, [Out] uint[] friends);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern uint tox_friend_add(IntPtr tox, byte[] address, byte[] message, UIntPtr length, out int error);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern uint tox_friend_add_norequest(IntPtr tox, byte[] key, out int error);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool tox_friend_delete(IntPtr tox, uint friend, out int error);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool tox_friend_get_public_key(IntPtr tox, uint friend, byte[] key, out int error);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern int tox_friend_get_connection_status(IntPtr tox, uint friend, out int error);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern uint tox_friend_send_message(IntPtr tox, uint friend, int type, byte[] message, UIntPtr length, out int error);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void tox_callback_friend_request(IntPtr tox, RequestCallback callback);
        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] private static extern void tox_callback_friend_message(IntPtr tox, MessageCallback callback);
    }
}
