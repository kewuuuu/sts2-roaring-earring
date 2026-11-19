using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using Sts2RoaringEarring.Config;

namespace Sts2RoaringEarring.Net;

/// <summary>
/// Host -> clients: the mod parameters this run is played with. Mod message types are discovered and
/// id-assigned by name (MessageTypes.cs:13-19, NetTypeCache.cs:22), so this name must stay unique
/// across all installed mods and must never be renamed once released: a rename changes the wire id.
/// </summary>
public class RoaringConfigMessage : INetMessage, IPacketSerializable
{
	public int EnergyPerTurn { get; set; }

	public int MaxCardsPerTurn { get; set; }

	public int PlayOrderIndex { get; set; }

	public bool AutoEndTurn { get; set; }

	public bool StartWithRelic { get; set; }

	// The host is the only sender, and NetHostGameService only echoes messages it receives
	// (NetHostGameService.cs:159), so this flag has no local effect here.
	public bool ShouldBroadcast => true;

	// A dropped config would leave the two ends playing the same turn with different numbers.
	public NetTransferMode Mode => NetTransferMode.Reliable;

	// Info: one message per run start, which is "very infrequent" by the interface's own definition
	// (INetMessage.cs:20-22), and the line is what proves the sync reached the other end.
	public LogLevel LogLevel => LogLevel.Info;

	public bool ShouldBuffer => true;

	public static RoaringConfigMessage From(RoaringConfig config)
	{
		return new RoaringConfigMessage
		{
			EnergyPerTurn = config.EnergyPerTurn,
			MaxCardsPerTurn = config.MaxCardsPerTurn,
			PlayOrderIndex = ToIndex(config.Order),
			AutoEndTurn = config.AutoEndTurn,
			StartWithRelic = config.StartWithRelic
		};
	}

	public RoaringConfig ToConfig()
	{
		return new RoaringConfig
		{
			EnergyPerTurn = EnergyPerTurn,
			MaxCardsPerTurn = MaxCardsPerTurn,
			Order = ToOrder(PlayOrderIndex),
			AutoEndTurn = AutoEndTurn,
			StartWithRelic = StartWithRelic
		};
	}

	/// <summary>The wire carries a number, not the enum: an out-of-range value is clamped by Normalized later.</summary>
	public static PlayOrder ToOrder(int value)
	{
		PlayOrder order = (PlayOrder)value;
		return RoaringConfig.IsKnownOrder(order) ? order : RoaringConfig.DefaultOrder;
	}

	public static int ToIndex(PlayOrder order)
	{
		return order switch
		{
			PlayOrder.RightToLeft => 1,
			PlayOrder.Random => 2,
			_ => 0,
		};
	}

	public void Serialize(PacketWriter writer)
	{
		// The order below is the wire format: append only, and keep Deserialize in step with it.
		writer.WriteInt(EnergyPerTurn);
		writer.WriteInt(MaxCardsPerTurn);
		writer.WriteInt(PlayOrderIndex);
		writer.WriteBool(AutoEndTurn);
		writer.WriteBool(StartWithRelic);
	}

	public void Deserialize(PacketReader reader)
	{
		EnergyPerTurn = reader.ReadInt();
		MaxCardsPerTurn = reader.ReadInt();
		PlayOrderIndex = reader.ReadInt();
		AutoEndTurn = reader.ReadBool();
		StartWithRelic = reader.ReadBool();
	}
}
