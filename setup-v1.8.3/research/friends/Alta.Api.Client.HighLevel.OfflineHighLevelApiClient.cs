using System;
using System.Threading.Tasks;
using Alta.Api.Client.LowLevel;
using Alta.Api.DataTransferModels.Exceptions;
using Alta.Api.DataTransferModels.Models.Requests;
using Alta.Api.DataTransferModels.Models.Responses;

namespace Alta.Api.Client.HighLevel;

public class OfflineHighLevelApiClient : IHighLevelApiClient
{
	public IServerApiClient ServerClient { get; }

	public ISocialClient SocialClient { get; }

	public IUserApiClient UserClient { get; private set; }

	public ILauncherApiClient LauncherClient { get; }

	public IServicesApiClient ServicesClient { get; }

	public LoginCredentials UserCredentials { get; }

	public bool IsLoggedIn => UserClient != null;

	public IShopApiClient ShopClient { get; }

	public IVoiceApiClient VoiceClient { get; }

	public IAchievementClient AchievementClient { get; }

	public ISecurityApiClient SecurityClient { get; }

	public ISettingsClient SettingsClient { get; }

	public IAccountClient Account { get; }

	public IGroupsClient Groups { get; }

	public IMatchMakingClient MatchMaking { get; private set; }

	public IInstancesClient Instances { get; private set; }

	public IAnalyticsClient Analytics { get; }

	public LowLevelApiClient LowLevel { get; }

	public IOculusClient Oculus { get; }

	public IPicoClient Pico { get; }

	public IFutureUserActions FutureUserActions { get; }

	public IPlayerReportsClient PlayerReports { get; }

	public IRecentPlayersClient RecentPlayers { get; }

	public event LoginCredentialsChangedHandler CredentialsChanged;

	public OfflineHighLevelApiClient(LoginCredentials credentials)
	{
		UserCredentials = credentials;
		ServerClient = new OfflineServerApiClient();
		ServicesClient = new OfflineServicesApiClient();
		LauncherClient = new OfflineLauncherApiClient();
		Groups = new OfflineGroupsClient();
		SocialClient = new OfflineSocialClient();
		SecurityClient = new OfflineSecurityApiClient();
		ShopClient = new OfflineShopApiClient();
		AchievementClient = new OfflineAchievementApiClient();
		VoiceClient = new OfflineVoiceClient();
	}

	public Task<IUserApiClient> LoginViaOauth(string clientId, string clientSecret, string scope)
	{
		throw new OfflineException();
	}

	public Task<IUserApiClient> LoginAsync(string username, string passwordHash)
	{
		return Task.FromResult(Login());
	}

	private IUserApiClient Login()
	{
		if (IsLoggedIn)
		{
			throw new AuthorizationException("Already logged in");
		}
		CreateLoggedInApiClients();
		return UserClient;
	}

	private void CreateLoggedInApiClients()
	{
		UserInfoWithPermissions loggedInUserInfo = UserInfoWithPermissions.FromToken(UserCredentials.AccessToken);
		UserClient = new OfflineUserApiClient(loggedInUserInfo, this);
	}

	public Task<IUserApiClient> LoginWithEmailAsync(string email, string passwordHash)
	{
		return Task.FromResult(Login());
	}

	public IUserApiClient LoginWithTokens(string accessToken, string refreshToken, string identityToken)
	{
		return Login();
	}

	public Task<IUserApiClient> LoginWithRefreshTokenAsync(string refreshToken)
	{
		return Task.FromResult(Login());
	}

	public void Logout()
	{
		UserClient = null;
	}

	public Task<UserInfo> RegisterUserAsync(string username, string passwordHash, string email, string referral)
	{
		throw new NotImplementedException();
	}

	public Task<UserInfo> RegisterOculusUserAsync(string username, string passwordHash, string email, string referral, AccountLinkData linkData)
	{
		throw new NotImplementedException();
	}

	public Task<UserInfo> RegisterPicoUserAsync(string username, string passwordHash, string email, string referral, AccountLinkData linkData)
	{
		throw new NotImplementedException();
	}

	public Task<IUserApiClient> LoginWithOculus(string userNonce, ulong userIdentifier)
	{
		return Task.FromResult(Login());
	}

	public Task<IUserApiClient> LoginWithPico(string userToken, ulong userIdentifier)
	{
		return Task.FromResult(Login());
	}
}
