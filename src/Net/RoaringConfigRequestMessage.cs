using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace Sts2RoaringEarring.Net;

/// <summary>
/// Client -> host: "send me the run's parameters". Sent once per run start, which covers both a fresh
/// run and a client that joins or reloads a save mid-run (RunStarted fires in both cases).
/// </summary>
public class RoaringConfigRequestMessage : INetMessage, IPacketSerializable
{
	// False: only the host answers, and the host's reply is the message that reaches the other clients.
	public bool ShouldBroadcast => false;

	public NetTransferMode Mode => NetTransferMode.Reliable;

	// VeryDebug: the answer is logged, the question need not be (INetMessage.cs:20-22).
	public LogLevel LogLevel => LogLevel.VeryDebug;

	public bool ShouldBuffer => true;

	public void Serialize(PacketWriter writer)
	{
	}

	public void Deserialize(PacketReader reader)
	{
	}
}
