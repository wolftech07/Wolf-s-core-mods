using System;
using Alta.Networking;
using Alta.Networking.Scripts.Player;
using Alta.Serialization;
using Alta.Timing;
using NLog;
using UnityEngine;

public class ActionItem : NetworkEntityBehaviour
{
	private static NLog.Logger logger = LogManager.GetCurrentClassLogger();

	[SerializeField]
	private RendererGroup renderersToMaterialize;

	[SerializeField]
	private RendererMaterializer materializer;

	[SerializeField]
	private float fadeDuration;

	private int owner;

	private MethodSync syncOwner;

	private MethodSyncStruct<bool> syncEffect;

	public RendererGroup RenderersToMaterialize => renderersToMaterialize;

	public RendererMaterializer Materializer => materializer;

	public int Owner => owner;

	public bool IsLocalPlayer { get; private set; }

	protected bool HasReceivedOwnerMessage { get; private set; }

	public event Action<IPlayer> OwnerUpdated;

	public override void Initialize()
	{
		base.Initialize();
		syncOwner = new MethodSync(base.Entity, EntityMessageType.SyncA, SyncOwner);
		syncEffect = new MethodSyncStruct<bool>(base.Entity, EntityMessageType.EffectRequest, SyncEffect);
		if (!ApplicationManager.IsHeadless)
		{
			materializer.SetTargets(renderersToMaterialize.Components);
		}
	}

	public override void OnConnected(Player player)
	{
		base.OnConnected(player);
		syncOwner.SendToPlayer(player);
	}

	private void SyncOwner(IPlayer player, Stream stream)
	{
		stream.SerializePlayerIdentifier(ref owner);
		if (stream.IsReadingOnRemoteClient())
		{
			Player player2 = Player.GetPlayer(owner);
			if (player2 != null && player2.IsLocalPlayer)
			{
				IsLocalPlayer = true;
			}
			logger.Info("{0} Action item owner updated, owner: {1}", this, player2);
			HasReceivedOwnerMessage = true;
			this.OwnerUpdated?.Invoke(player2);
		}
	}

	public virtual void SetupOwner(IPlayer player)
	{
		owner = player.UserInfo.Identifier;
		if (player.IsLocalPlayer)
		{
			IsLocalPlayer = true;
		}
		logger.Info("{0} Action item owner updated, owner: {1}", this, player);
		this.OwnerUpdated?.Invoke(player);
		syncOwner.SendToChunks();
	}

	public virtual void Fade(bool isFadingIn)
	{
		if (NetworkSceneManager.IsServer)
		{
			if (!isFadingIn)
			{
				Timeline.Instance.CallAfterDuration(base.Entity.SceneDestroy, fadeDuration);
			}
			syncEffect.SendToChunks(isFadingIn);
		}
		if (!ApplicationManager.IsHeadless)
		{
			StartFade(isFadingIn);
			if (isFadingIn)
			{
				materializer.MaterializeRenderers(fadeDuration, FinishFadeIn);
			}
			else
			{
				materializer.DemateralizeRenderers(fadeDuration);
			}
		}
	}

	protected virtual void StartFade(bool isFadingIn)
	{
	}

	protected virtual void FinishFadeIn()
	{
	}

	private void SyncEffect(IPlayer player, Stream stream, bool isFadingIn)
	{
		stream.SerializeBool(ref isFadingIn);
		if (stream.IsReading && !NetworkSceneManager.IsServer)
		{
			Fade(isFadingIn);
		}
	}
}
