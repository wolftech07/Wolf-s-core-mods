using System;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Alta.Api.Client;
using Alta.Api.Client.HighLevel;
using Alta.Api.Client.LowLevel;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Api.DataTransferModels.Models.Shared;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TavernNativeMenu
{
    internal static class TavernIdentity
    {
        private static readonly string[] Policies = new string[] {
            "offline", "play_offline", "server_access_pre_alpha", "server_access_tutorial",
            "game_access_public", "game_access_development", "server_access_development",
            "server_access_testing", "game_access_testing", "server_owner", "debug_features",
            "admin_vr_modes", "database_admin", "server_create_development", "reuse_refresh_tokens"
        };

        // Call only after the selected Tavern server has authorized this user and
        // returned its user_id (or after an explicit headless-server selection).
        public static void Apply(string username, int userId, string tavernToken)
        {
            if (CommandLineArguments.Contains("/start_server") || ApplicationManager.IsHeadless)
                throw new InvalidOperationException("The menu identity adapter is client-only.");
            if (GameModeManager.CurrentMode != null)
                throw new InvalidOperationException("Leave the current server before changing player identity.");
            if (userId <= 0)
                throw new ArgumentOutOfRangeException("userId", "The server did not provide a valid player id.");
            if (String.IsNullOrWhiteSpace(username))
                throw new ArgumentException("A player name is required.", "username");

            SetActiveIdentity(CreateCredentials(username, userId, tavernToken ?? String.Empty), userId);
        }

        internal static void InitializeMenu(string username)
        {
            if (!CommandLineArguments.Contains("/tavern_native_menu") ||
                CommandLineArguments.Contains("/start_server") || ApplicationManager.IsHeadless)
                return;
            if (GameModeManager.CurrentMode != null)
                throw new InvalidOperationException("Cannot initialize the menu account while a server is active.");
            if (String.IsNullOrWhiteSpace(username))
                throw new ArgumentException("A player name is required.", "username");
            // /force_offline may load cached credentials and ignore command-line
            // tokens. Explicitly reset this browsing-only session to id 0. The
            // normal Apply entry point never accepts id 0 for a server join.
            SetActiveIdentity(CreateCredentials(username, 0, String.Empty), 0);
        }

        private static void SetActiveIdentity(LoginCredentials replacement, int userId)
        {
            UserInfoWithPermissions user = UserInfoWithPermissions.FromToken(replacement.AccessToken);
            IHighLevelApiClient api = ApiAccess.ApiClient;
            if (api == null || api.UserClient == null || api.UserCredentials == null)
                throw new InvalidOperationException("The game has not finished initializing the menu account.");

            LowLevelApiClient low = api.LowLevel;
            if (low != null)
            {
                // Reuse the API client captured by Unity's dependency container.
                // LoginWithTokens cannot be called again while already logged in.
                // This official setter updates all three credentials and HTTP headers.
                // No login events are raised: those would restart the menu lifecycle.
                PropertyInfo lowUser = typeof(LowLevelApiClient).GetProperty("LoggedInUser",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo setLowUser = lowUser == null ? null : lowUser.GetSetMethod(true);
                if (setLowUser == null)
                    throw new MissingMethodException("This game build cannot update the active Tavern player.");
                low.HandleNewAuthenticationTokens(new TokenResultResponse(
                    replacement.AccessToken, replacement.RefreshToken, replacement.IdentityToken), false);
                setLowUser.Invoke(low, new object[] { user });
            }
            else if (api is OfflineHighLevelApiClient)
            {
                // OfflineHighLevelApiClient keeps a get-only credentials object.
                // Its three token properties are intentionally mutable.
                LoginCredentials credentials = api.UserCredentials;
                credentials.AccessToken = replacement.AccessToken;
                credentials.RefreshToken = replacement.RefreshToken;
                credentials.IdentityToken = replacement.IdentityToken;
            }
            else
            {
                throw new NotSupportedException("This API client does not support Tavern identity changes.");
            }
            api.UserClient.LoggedInUserInfo = user;

            // RequestJoinMessage reads both of these live values. Fail before any
            // network join if the game does not expose the newly authorized identity.
            if (api.UserClient.LoggedInUserInfo.Identifier != userId ||
                api.UserCredentials.IdentityToken == null)
                throw new InvalidOperationException("The game did not accept the selected server's player identity.");
        }

        internal static LoginCredentials CreateCredentials(string username, int userId, string tavernToken)
        {
            string id = userId.ToString(CultureInfo.InvariantCulture);
            JObject access = CommonPayload();
            access["UserId"] = id;
            access["Username"] = username;
            access["role"] = "Access";
            access["is_verified"] = "True";
            access["is_member"] = "True";
            access["Policy"] = new JArray(Policies);
            access["TavernToken"] = tavernToken;

            JObject refresh = CommonPayload();
            refresh["UserId"] = id;
            refresh["role"] = "Refresh";

            JObject identity = CommonPayload();
            identity["UserId"] = id;
            identity["Username"] = username;
            identity["role"] = "Identity";
            identity["is_member"] = "True";
            identity["is_dev"] = "True";
            identity["TavernToken"] = tavernToken;
            return new LoginCredentials(new JwtSecurityToken(Sign(access)),
                new JwtSecurityToken(Sign(refresh)), new JwtSecurityToken(Sign(identity)));
        }

        private static JObject CommonPayload()
        {
            return new JObject {
                { "exp", 9999999999L },
                { "iss", "AltaWebAPI" },
                { "aud", "AltaClient" }
            };
        }

        private static string Sign(JObject payload)
        {
            // Tavern Launcher 1.8.2's local transport envelope. Server authorization
            // comes from the prior Tavern TCP handshake and its TavernToken claim.
            string header = Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
            string body = Base64Url(Encoding.UTF8.GetBytes(payload.ToString(Formatting.None)));
            string message = header + "." + body;
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes("offline")))
                return message + "." + Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(message)));
        }

        private static string Base64Url(byte[] bytes)
        {
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
