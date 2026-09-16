using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Alta.Api.DataTransferModels.Models.Responses;
using Alta.Menu;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using Alta.Serialization;
using NLog;
using UnityEngine;

public class FriendSelector : NetworkEntityBehaviour, IProvideEnumerable<string>
{
	private static NLog.Logger logger = LogManager.GetCurrentClassLogger();

	[SerializeField]
	private ScrollerSelector scroller;

	private List<UserInfo> friends = new List<UserInfo>();

	private IPlayer currentPlayer;

	private MethodSync syncFilter;

	private Task filteringTask;

	private bool isCancelling;

	public int CurrentFriend
	{
		get
		{
			if (friends == null || friends.Count == 0)
			{
				return -1;
			}
			return friends[scroller.CurrentIndex].Identifier;
		}
	}

	IEnumerable<string> IProvideEnumerable<string>.Values
	{
		get
		{
			for (int i = 0; i < friends.Count; i++)
			{
				yield return friends[i].Username;
			}
		}
	}

	public event Action<int> UpdatedFriendSelection;

	public event Action<IProvideEnumerable<string>> UpdatedValues;

	public override void Initialize()
	{
		base.Initialize();
		syncFilter = new MethodSync(base.Entity, EntityMessageType.SyncB, SyncFilter);
		scroller.UpdatedValue += UpdateFriend;
	}

	private void UpdateFriend(int index)
	{
		if (friends != null && index < friends.Count)
		{
			this.UpdatedFriendSelection?.Invoke(friends[index].Identifier);
		}
	}

	private void SyncFilter(IPlayer player, Stream stream)
	{
		int value = friends.Count;
		stream.SerializeInteger(ref value, 0, int.MaxValue);
		int value2 = 0;
		if (stream.IsWriting)
		{
			for (int i = 0; i < value; i++)
			{
				value2 = friends[i].Identifier;
				stream.SerializeInteger(ref value2, 0, int.MaxValue);
			}
			return;
		}
		friends.Clear();
		HashSet<int> hashSet = new HashSet<int>();
		for (int j = 0; j < value; j++)
		{
			stream.SerializeInteger(ref value2, 0, int.MaxValue);
			hashSet.Add(value2);
		}
		if (Player.Current == null)
		{
			return;
		}
		foreach (UserInfo friend in Player.Current.FriendshipManager.Friends)
		{
			if (hashSet.Contains(friend.Identifier))
			{
				friends.Add(new UserInfo(friend.Identifier, friend.Username));
			}
		}
		this.UpdatedValues?.Invoke(this);
	}

	public async void SetPlayer(IPlayer player)
	{
	}
}
