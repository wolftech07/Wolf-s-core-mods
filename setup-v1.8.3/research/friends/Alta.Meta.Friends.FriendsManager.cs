using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ATT.Analytics.Social;
using Alta.Analytics;
using Alta.Api.DataTransferModels.Models.Responses;
using UnityEngine;
using VContainer;

namespace Alta.Meta.Friends;

public class FriendsManager : LoggedInTool
{
	public enum Tab
	{
		Friends,
		Requests,
		Oculus
	}

	private readonly Dictionary<int, FriendshipInfo> friendships = new Dictionary<int, FriendshipInfo>();

	private readonly List<FriendshipInfo> friendshipsRequests = new List<FriendshipInfo>();

	private readonly Stack<UserToken> tokenPool = new Stack<UserToken>();

	private readonly List<UserToken> activeTokens = new List<UserToken>();

	[SerializeField]
	private UserToken userTokenPrefab;

	[SerializeField]
	private RopePoweredElementBoard displayBoard;

	private Tab currentTab;

	private bool isProcessing;

	private IAnalyticsService analyticsService;

	private IEnumerable<FriendshipInfo> Friendships => friendships.Values;

	[Inject]
	public void Inject(IAnalyticsService analyticsService)
	{
		this.analyticsService = analyticsService;
	}

	protected override async void LoggedIn()
	{
		currentTab = Tab.Friends;
		await UpdateFriends();
		UpdateDisplay();
	}

	private async Task UpdateFriends()
	{
		if (isProcessing)
		{
			return;
		}
		isProcessing = true;
		IEnumerable<FriendshipInfo> enumerable = await ApiAccess.ApiClient.SocialClient.GetFriends();
		friendships.Clear();
		foreach (FriendshipInfo item in enumerable)
		{
			if (item.Type == FriendshipType.Accepted)
			{
				friendships.Add(item.Identifier, item);
			}
		}
		enumerable = await ApiAccess.ApiClient.SocialClient.GetFriendRequests();
		friendshipsRequests.Clear();
		friendshipsRequests.AddRange(enumerable);
		isProcessing = false;
	}

	private void UpdateDisplay()
	{
		ResetDisplay();
		foreach (UserInfo item in GetUserInfoToDisplay())
		{
			DisplayUser(item);
		}
		displayBoard.CalculateStartValue();
		displayBoard.Value = 0f;
	}

	private void DisplayUser(UserInfo user)
	{
		UserToken userToken = ((tokenPool.Count > 0) ? tokenPool.Pop() : UnityEngine.Object.Instantiate(userTokenPrefab));
		userToken.gameObject.SetActive(value: true);
		ConfigureTokenForTab(userToken, user);
		displayBoard.AddElement(userToken.transform, isUpdatingCurve: false);
		activeTokens.Add(userToken);
	}

	private void ConfigureTokenForTab(UserToken token, UserInfo user)
	{
		switch (currentTab)
		{
		case Tab.Friends:
			token.Setup(user, this, JoinFriendServer, RemoveFriend);
			break;
		case Tab.Requests:
			token.Setup(user, this, AddFriend, RemoveFriend);
			break;
		case Tab.Oculus:
			token.Setup(user, this, AddFriend, null);
			break;
		default:
			throw new ArgumentOutOfRangeException();
		}
	}

	private IEnumerable<UserInfo> GetUserInfoToDisplay()
	{
		List<UserInfo> list = new List<UserInfo>();
		switch (currentTab)
		{
		case Tab.Friends:
			list.AddRange(Friendships);
			break;
		case Tab.Requests:
			list.AddRange(friendshipsRequests);
			break;
		}
		return list.OrderBy((UserInfo user) => user.Username);
	}

	private void ResetDisplay()
	{
		foreach (UserToken activeToken in activeTokens)
		{
			activeToken.gameObject.SetActive(value: false);
			tokenPool.Push(activeToken);
		}
		displayBoard.Elements.Clear();
		activeTokens.Clear();
	}

	protected override void LoggedOut()
	{
		friendships.Clear();
	}

	public async void RemoveFriend(UserToken token)
	{
		if (!isProcessing)
		{
			isProcessing = true;
			await ApiAccess.ApiClient.SocialClient.RemoveFriend(token.UserInfo.Identifier);
			await UpdateFriends();
			ReleaseToken(token);
			isProcessing = false;
		}
	}

	private void ReleaseToken(UserToken token)
	{
		token.gameObject.SetActive(value: false);
		tokenPool.Push(token);
		activeTokens.Remove(token);
		displayBoard.RemoveElement(token.transform);
	}

	public void JoinFriendServer(UserToken token)
	{
	}

	public async void AddFriend(UserToken token)
	{
		if (!isProcessing)
		{
			isProcessing = true;
			await SocialService.RequestFriend(token.UserInfo.Identifier);
			analyticsService.Send(new SocialAnalyticsEvent("Respond Friend Request", SocialAnalyticsEventOrigin.MainMenu));
			await UpdateFriends();
			ReleaseToken(token);
			isProcessing = true;
		}
	}

	public void ChangeTab(int index)
	{
		ChangeTab((Tab)index);
	}

	public void ChangeTab(Tab tab)
	{
		if (tab != currentTab)
		{
			currentTab = tab;
			UpdateDisplay();
		}
	}

	protected override void OnDestroy()
	{
		base.OnDestroy();
		foreach (Transform element in displayBoard.Elements)
		{
			ObjectUtility.Destroy(element.gameObject);
		}
		displayBoard.Elements.Clear();
		foreach (UserToken item in tokenPool)
		{
			ObjectUtility.Destroy(item.gameObject);
		}
		tokenPool.Clear();
	}
}
