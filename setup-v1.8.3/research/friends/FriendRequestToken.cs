using ATT.Analytics.Social;
using ATT.Saving;
using Alta.Analytics;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Api.DataTransferModels.Models.Responses.DTOs.Responses;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using Alta.Serialization;
using Alta.Timing;
using NLog;
using UnityEngine;
using VContainer;

public class FriendRequestToken : ActionItem
{
	private enum FriendshipSyncResult
	{
		NewFriend,
		ExistingFriend,
		Denied
	}

	private static readonly NLog.Logger logger = LogManager.GetCurrentClassLogger();

	private const float TimeoutTime = 2f;

	[SerializeField]
	private Pickup pickup;

	[SerializeField]
	private TextRenderer[] nameRenderers;

	[SerializeField]
	private int maxNameLength;

	[SerializeField]
	private PositionEvent newFriendshipEffect;

	[SerializeField]
	private PositionEvent alreadyFriendsEffect;

	[SerializeField]
	private PositionEvent deniedFriendshipEffect;

	[SerializeField]
	private HoverPickup hoverPickup;

	private MethodSyncStruct<int> syncFriendshipRequestEffect;

	private MethodSyncStruct<int> syncFriendshipRequest;

	private bool receivedOwnerToken;

	private int otherTokenId = -1;

	private float releaseTimeout = -1f;

	private Timeline.IEntry timelineEntry;

	private IAnalyticsService analyticsService;

	[Inject]
	public void Inject(IAnalyticsService analyticsService)
	{
		this.analyticsService = analyticsService;
	}

	private void Awake()
	{
		pickup.BaseSaveMode = SaveMode.Ignore;
		pickup.InteractionFilter += Filter;
		pickup.InteractionStarted += Grabbed;
		pickup.InteractionEnded += Hover;
		if (!ApplicationManager.IsHeadless)
		{
			base.OwnerUpdated += UpdateName;
		}
	}

	private void Hover(Interactable interactable, Interactor interactor)
	{
		AltaCoroutine.DelayCall(delegate
		{
			if (!interactable.IsInteractedWith && !base.Entity.IsBeingDestroyed)
			{
				hoverPickup.SetTarget(base.transform);
			}
		});
		if (base.IsLocalPlayer && interactor.Controller.PlayerController.NetworkPlayer.UserInfo.Identifier == base.Owner)
		{
			if (!interactor.Actions[ControllerActionType.Interact])
			{
				releaseTimeout = Time.realtimeSinceStartup + 2f;
			}
			else
			{
				releaseTimeout = -1f;
			}
		}
	}

	public override void Initialize()
	{
		base.Initialize();
		syncFriendshipRequestEffect = new MethodSyncStruct<int>(base.Entity, EntityMessageType.syncFriendshipRequest, SyncFriendshipRequestEffect);
		syncFriendshipRequest = new MethodSyncStruct<int>(base.Entity, EntityMessageType.syncFriendshipRequestEffect, SyncFriendshipRequest);
	}

	private void TimeoutFade()
	{
		logger.Warn("Friend Request timed out");
		receivedOwnerToken = false;
		otherTokenId = -1;
		Fade(isFadingIn: false);
	}

	private async void AddFriend()
	{
		Timeline.Cancel(ref timelineEntry);
		int newFriend = otherTokenId;
		receivedOwnerToken = false;
		otherTokenId = -1;
		FriendshipInfo friendshipInfo = await ApiAccess.ApiClient.SocialClient.GetFriendship(base.Owner, newFriend);
		FriendshipSyncResult friendshipSyncResult = FriendshipSyncResult.ExistingFriend;
		if (friendshipInfo == null || friendshipInfo.Type != FriendshipType.Accepted)
		{
			FriendRequestResult friendRequestResult = await SocialService.RequestFriend(base.Owner, newFriend);
			analyticsService.Send(new SocialAnalyticsEvent("Friend Request", SocialAnalyticsEventOrigin.FriendRequestToken));
			if (friendRequestResult.Result == FriendRequestedType.Befriended)
			{
				friendshipSyncResult = FriendshipSyncResult.NewFriend;
				Player player = Player.GetPlayer(base.Owner);
				Player player2 = Player.GetPlayer(newFriend);
				CallFriendshipEvent(player, player2, friendRequestResult.Friendship);
				CallFriendshipEvent(player2, player, friendRequestResult.Friendship);
			}
			else
			{
				friendshipSyncResult = FriendshipSyncResult.Denied;
			}
		}
		RunEffects(friendshipSyncResult);
		syncFriendshipRequestEffect.SendToChunks((int)friendshipSyncResult);
		Fade(isFadingIn: false);
	}

	private void CallFriendshipEvent(Player player, Player friend, FriendshipInfo friendship)
	{
		FriendshipInfo newFriendShip = new FriendshipInfo
		{
			CreatedAt = friendship.CreatedAt,
			IconIndex = friendship.IconIndex,
			Type = friendship.Type,
			Identifier = friend.UserInfo.Identifier,
			Username = friend.UserInfo.Username
		};
		player?.FriendshipManager?.AddFriend(newFriendShip);
	}

	private void RunEffects(FriendshipSyncResult friendshipSyncResult)
	{
		if (!ApplicationManager.IsHeadless)
		{
			switch (friendshipSyncResult)
			{
			case FriendshipSyncResult.NewFriend:
				newFriendshipEffect.Invoke(base.transform.position);
				break;
			case FriendshipSyncResult.ExistingFriend:
				alreadyFriendsEffect.Invoke(base.transform.position);
				break;
			case FriendshipSyncResult.Denied:
				deniedFriendshipEffect.Invoke(base.transform.position);
				break;
			}
		}
	}

	private void SyncFriendshipRequest(IPlayer player, Stream stream, int friendID)
	{
		stream.SerializeInteger(ref friendID, 0, int.MaxValue);
		if (stream.IsReading && NetworkSceneManager.IsServer && !base.Entity.IsBeingDestroyed)
		{
			if (!receivedOwnerToken && otherTokenId < 0)
			{
				timelineEntry = Timeline.Instance.CallAfterDuration(TimeoutFade, 2f);
			}
			if (player.UserInfo.Identifier == base.Owner)
			{
				SyncFriendRequestForPlayer(friendID);
			}
			else if (friendID == base.Owner)
			{
				SyncFriendRequestForTokenOwner(player);
			}
		}
	}

	private void SyncFriendRequestForPlayer(int friendID)
	{
		if (otherTokenId == friendID)
		{
			AddFriend();
			return;
		}
		receivedOwnerToken = true;
		otherTokenId = friendID;
	}

	private void SyncFriendRequestForTokenOwner(IPlayer player)
	{
		if (!receivedOwnerToken)
		{
			otherTokenId = player.UserInfo.Identifier;
		}
		else if (player.UserInfo.Identifier == otherTokenId)
		{
			AddFriend();
		}
	}

	private void SyncFriendshipRequestEffect(IPlayer player, Stream stream, int friendshipResultIndex)
	{
		stream.SerializeInteger(ref friendshipResultIndex, 0, 3);
		if (stream.IsReading && !NetworkSceneManager.IsServer)
		{
			FriendshipSyncResult friendshipSyncResult = (FriendshipSyncResult)friendshipResultIndex;
			RunEffects(friendshipSyncResult);
			if (friendshipSyncResult == FriendshipSyncResult.NewFriend && otherTokenId > 0)
			{
				Player.GetPlayer(otherTokenId)?.FriendshipManager?.UpdateStatus(isFriend: true);
			}
		}
	}

	private void UpdateName(IPlayer owner)
	{
		string username = owner.UserInfo.Username;
		TextRenderer[] array = nameRenderers;
		foreach (TextRenderer textRenderer in array)
		{
			textRenderer.Text = username;
			base.RenderersToMaterialize.Components.AddRange(textRenderer.Renderers);
		}
		base.Materializer.SetTargets(base.RenderersToMaterialize.Components);
	}

	private void Filter(Interactor interactor, Interactable interactable, ref bool result)
	{
		result &= !interactor.IsLocal || interactor.Controller.PlayerController.NetworkPlayer.UserInfo.Identifier == base.Owner || (interactable.IsInteractedWith && interactable.LastInteractorPlayerId == base.Owner);
	}

	private void Grabbed(Interactable interactable, Interactor interactor)
	{
		int identifier = interactor.Controller.PlayerController.NetworkPlayer.UserInfo.Identifier;
		if (identifier != base.Owner)
		{
			if (interactor.IsLocal)
			{
				otherTokenId = base.Owner;
				Player.GetPlayer(base.Owner).FriendshipManager.UpdateStatus(isFriend: true);
				syncFriendshipRequest.SendToChunks(base.Owner);
			}
			else if (base.IsLocalPlayer && releaseTimeout > 0f && Time.realtimeSinceStartup < releaseTimeout)
			{
				otherTokenId = identifier;
				interactor.Controller.PlayerController.NetworkPlayer.FriendshipManager.UpdateStatus(isFriend: true);
				syncFriendshipRequest.SendToChunks(identifier);
			}
			releaseTimeout = -1f;
		}
	}

	private void OnDestroy()
	{
		Timeline.Cancel(ref timelineEntry);
	}
}
