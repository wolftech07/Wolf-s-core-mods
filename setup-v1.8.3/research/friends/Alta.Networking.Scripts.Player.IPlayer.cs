using System;
using System.ComponentModel;
using Alta.Character;
using Alta.Chunks;
using Alta.Customization;
using Alta.PlayerStates;
using Alta.Utilities;
using UnityEngine;
using VivoxUnity;

namespace Alta.Networking.Scripts.Player;

[TypeConverter(typeof(PlayerConverter))]
public interface IPlayer : INetworkEntity
{
	UserInfoAndRole UserInfo { get; }

	Connection ConnectionToRemotePlayer { get; }

	bool IsLocalPlayer { get; }

	PlayerProgressionManager ProgressionManager { get; }

	PlayerUnlockManager UnlockManager { get; }

	PlayerController PlayerController { get; }

	PlayerCharacter PlayerCharacter { get; }

	PlayerStateManager StateManager { get; }

	string UnityObjectName { get; }

	PlayerEventsManager PlayerEventsManager { get; }

	PlayerChunkManager ChunkManager { get; }

	PlayerCharacterCustomizationManager Customization { get; }

	FriendshipManager FriendshipManager { get; }

	PlayerMode PlayerMode { get; set; }

	PlayerSave Save { get; }

	IParticipant Voice { get; set; }

	event Action LoadComplete;

	event PlayerControllerHandler PlayerControllerSet;

	void Kick(string reason);

	bool IsInSavableState(bool isSavingIfDead = false);

	void WriteSave(bool isUnloading);

	void SaveControllerAndState(bool isSavingIfDead = false);

	void DestroyWithAssociatedObjects();

	Vector3 GetSpawnPosition(bool isHome);

	void SafeMoveToChunk(LocationChunk chunk, Vector3 controllerPosition, Action callback);
}
