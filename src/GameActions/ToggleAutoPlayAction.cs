using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using Sts2RoaringEarring.Models;

namespace Sts2RoaringEarring.GameActions;

/// <summary>
/// One player's auto-play switch, travelling through the game's own action queue instead of a mod message.
/// The queue is the only place where two peers agree on *when* a value changes: a client's click becomes a
/// RequestEnqueueActionMessage, the host rebuilds it and numbers it (ActionQueueSynchronizer.cs:319-328), and every
/// client rebuilds the same action from the broadcast at the same queue position (:330-339). The model write happens
/// inside ExecuteAction, so both peers see the old value in every checksum taken before that position and the new one
/// in every checksum after it. A local write plus a custom message could be observed at different positions on the two
/// peers, which ChecksumTracker reports as a state divergence and the host answers by disconnecting the sender.
/// </summary>
public sealed class ToggleAutoPlayAction : GameAction
{
	private readonly Player _player;

	private readonly bool _enabled;

	public ToggleAutoPlayAction(Player player, bool enabled)
	{
		_player = player;
		_enabled = enabled;
	}

	public override ulong OwnerId => _player.NetId;

	// Any: the switch has to work in and out of combat and must never be cancelled (GameActionType.cs:23-26). NonCombat
	// would be held until the fight ends (ActionQueueSynchronizer.cs:270-283) and CombatPlayPhaseOnly is dropped outside
	// the play phase (:163-168, ActionQueueSet.cs:215), so neither can carry a relic click.
	public override GameActionType ActionType => GameActionType.Any;

	protected override Task ExecuteAction()
	{
		RoaringEarring? relic = _player.GetRelic<RoaringEarring>();
		if (relic == null)
		{
			return Task.CompletedTask;
		}
		relic.AutoPlayEnabled = _enabled;
		relic.RefreshIndicator();
		// The turn snapshot inside the relic decides when this takes effect: the turn already running keeps the value it
		// started with, so "next turn" is the same moment on every peer.
		Log.Info("[sts2-roaring-earring] auto-play " + (_enabled ? "on" : "off") + " for player " + _player.NetId + " (action queue), effective from the next turn");
		return Task.CompletedTask;
	}

	public override INetAction ToNetAction()
	{
		return new NetToggleAutoPlayAction { enabled = _enabled };
	}

	public override string ToString()
	{
		return "ToggleAutoPlayAction for player " + _player.NetId + " enabled " + _enabled;
	}
}

/// <summary>
/// Wire form of <see cref="ToggleAutoPlayAction"/>. Mod INetAction types are discovered by reflection
/// (ActionTypes.cs:14-17) and identified by a single-byte id sorted by type name (RequestEnqueueActionMessage.cs:33,
/// :41-46), so this name has to stay unique across installed mods and must never be renamed once released. Only the
/// wanted value travels: the owner is the player the host already resolved from the sender.
/// </summary>
public struct NetToggleAutoPlayAction : INetAction
{
	public bool enabled;

	public GameAction ToGameAction(Player player)
	{
		return new ToggleAutoPlayAction(player, enabled);
	}

	public void Serialize(PacketWriter writer)
	{
		writer.WriteBool(enabled);
	}

	public void Deserialize(PacketReader reader)
	{
		enabled = reader.ReadBool();
	}

	public override string ToString()
	{
		return "NetToggleAutoPlayAction enabled: " + enabled;
	}
}
