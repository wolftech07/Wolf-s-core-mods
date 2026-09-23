using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace TavernNativeMeshServer
{
    internal static class TabletPolicy
    {
        internal static int Rank(IEnumerable<string> roles)
        {
            if (roles == null) return 0;
            if (roles.Any(x => String.Equals(x, "owner", StringComparison.OrdinalIgnoreCase))) return 2;
            return roles.Any(x => String.Equals(x, "moderator", StringComparison.OrdinalIgnoreCase)) ? 1 : 0;
        }
        internal static bool CanTarget(int issuer, int issuerRank, int target, int targetRank)
        { return issuer > 0 && target > 0 && issuer != target && issuerRank > 0 && targetRank < issuerRank && targetRank < 2; }
        internal static bool ValidRequestId(string value)
        { Guid parsed; return value != null && value.Length == 32 && Guid.TryParseExact(value, "N", out parsed); }
        internal static bool TokenMatches(string expected, string actual)
        {
            if (String.IsNullOrEmpty(expected) || actual == null || expected.Length != actual.Length) return false;
            int difference = 0;
            for (int i = 0; i < expected.Length; i++) difference |= expected[i] ^ actual[i];
            return difference == 0;
        }
        internal static bool AllowCredentials(int requestedId, int tokenId, string serverName, string claimedName, string expectedToken, string claimedToken, bool banned)
        {
            return !banned && tokenId > 0 && requestedId == tokenId && !String.IsNullOrEmpty(serverName) &&
                String.Equals(serverName, claimedName, StringComparison.OrdinalIgnoreCase) && TokenMatches(expectedToken, claimedToken);
        }
    }
    internal sealed class TabletBan
    {
        public int Id;
        public string Name;
        public int Issuer;
        public DateTime CreatedUtc;
    }
    internal sealed class TabletState
    {
        public string ServerKey;
        public List<TabletBan> Bans = new List<TabletBan>();
    }
    // A separate server-owned file avoids races with the launcher's users.json writer.
    // Changes become visible in memory only after the durable replacement succeeds.
    internal sealed class TabletStore
    {
        private readonly string path;
        private readonly object gate = new object();
        private TabletState state;
        internal TabletStore(string path)
        {
            this.path = Path.GetFullPath(path);
            if (File.Exists(this.path))
            {
                if (new FileInfo(this.path).Length > 4 * 1024 * 1024) throw new InvalidDataException("Tablet state is too large.");
                state = JsonConvert.DeserializeObject<TabletState>(File.ReadAllText(this.path));
                Guid key;
                if (state == null || !Guid.TryParseExact(state.ServerKey, "N", out key) || state.Bans == null || state.Bans.Any(x => x == null || x.Id <= 0))
                    throw new InvalidDataException("Invalid tablet state.");
            }
            else
            {
                state = new TabletState { ServerKey = Guid.NewGuid().ToString("N") };
                Save(state);
            }
        }
        internal string ServerKey { get { lock (gate) return state.ServerKey; } }
        internal bool IsBanned(int id) { lock (gate) return state.Bans.Any(x => x.Id == id); }
        internal TabletBan[] Bans { get { lock (gate) return state.Bans.Select(Clone).ToArray(); } }
        private static TabletBan Clone(TabletBan value)
        { return new TabletBan { Id = value.Id, Name = value.Name, Issuer = value.Issuer, CreatedUtc = value.CreatedUtc }; }
        internal void Ban(int id, string name, int issuer)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException("id");
            lock (gate)
            {
                if (state.Bans.Any(x => x.Id == id)) return;
                var next = new TabletState { ServerKey = state.ServerKey, Bans = state.Bans.Select(Clone).ToList() };
                next.Bans.Add(new TabletBan { Id = id, Name = name, Issuer = issuer, CreatedUtc = DateTime.UtcNow });
                Save(next); state = next;
            }
        }
        internal bool Unban(int id)
        {
            lock (gate)
            {
                if (!state.Bans.Any(x => x.Id == id)) return false;
                var next = new TabletState { ServerKey = state.ServerKey, Bans = state.Bans.Where(x => x.Id != id).Select(Clone).ToList() };
                Save(next); state = next; return true;
            }
        }
        private void Save(TabletState value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value, Formatting.Indented));
                if (bytes.Length > 4 * 1024 * 1024) throw new InvalidDataException("Tablet state is too large.");
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
