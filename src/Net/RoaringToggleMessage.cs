using System;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;
using Sts2RoaringEarring.Models;

namespace Sts2RoaringEarring.Net;

/// <summary>
/// One relic's auto-play switch, travelling in both directions: client -> host as a request, host -> clients as
/// the value that was applied. Mod message types are discovered and id-assigned by name (MessageTypes.cs:13-19,
/// NetTypeCache.cs:22), so this name must stay unique across installed mods and must never be renamed once
/// released. Only this bool travels: every other value stays frozen in <see cref="RoaringConfigMessage"/>.
/// </summary>
public class RoaringToggleMessage : INetMessage, IPacketSerializable
{
	/// <summary>Net id of the player whose relic is switched; a host rejects a request that names anyone else.</summary>
	public ulong OwnerNetId { get; set; }

	/// <summary>The wanted value in a request, the applied value in a broadcast.</summary>
	public bool Enabled { get; set; }

	// False on purpose: a client's packet must be judged by the host before it reaches anyone else, and
	// ShouldBroadcast would forward it before the handler runs (NetHostGameService.cs:159-162). The host
	// broadcasts the value it actually applied instead.
	public bool ShouldBroadcast => false;

	// A lost toggle would leave two peers playing the rest of the run with different numbers.
	public NetTransferMode Mode => NetTransferMode.Reliable;

	// VeryDebug: one message per click, which is not "very infrequent" (INetMessage.cs:20-22).
	public LogLevel LogLevel => LogLevel.VeryDebug;

	public bool ShouldBuffer => true;

	public void Serialize(PacketWriter writer)
	{
		writer.WriteULong(OwnerNetId);
		writer.WriteBool(Enabled);
	}

	public void Deserialize(PacketReader reader)
	{
		OwnerNetId = reader.ReadULong();
		Enabled = reader.ReadBool();
	}
}

/// <summary>
/// Moves the auto-play switch between peers around one authoritative copy. Singleplayer and the host own the
/// value: a client only asks (NetClientGameService.cs:83-95 sends to the host and nowhere else), the host applies
/// it and broadcasts to every client (NetHostGameService.cs:114-130), and a client only ever applies what it is
/// told. Nothing else is synced — the run's parameters stay where <see cref="RoaringConfigSync"/> put them.
/// </summary>
public static class RoaringToggleSync
{
	private static INetGameService? _attached;
	private static RunState? _runState;

	/// <summary>
	/// Called from <see cref="RoaringEarringMod.OnRunStarted"/>: keeps this run's state for the owner lookup and
	/// installs one handler on the net service. RunStarted fires again on load, and registering twice would run
	/// every handler twice, so the service instance itself is the guard (same idiom as RoaringConfigSync).
	/// </summary>
	public static void AttachForRun(RunState state)
	{
		_runState = state;
		INetGameService? service = RunManager.Instance.NetService;
		NetGameType type = service?.Type ?? NetGameType.None;
		if (service == null || (type != NetGameType.Host && type != NetGameType.Client))
		{
			// Singleplayer and replays have no peer above them; the click is applied locally.
			return;
		}
		if (ReferenceEquals(_attached, service))
		{
			return;
		}
		_attached = service;
		if (type == NetGameType.Host)
		{
			service.RegisterMessageHandler<RoaringToggleMessage>(OnRequestFromClient);
		}
		else
		{
			service.RegisterMessageHandler<RoaringToggleMessage>(OnBroadcastFromHost);
		}
		Log.Info("[sts2-roaring-earring] toggle sync armed; this peer is the " + type);
	}

	/// <summary>
	/// The left click on the local player's own relic. The client never writes the flag: it asks the host, and the
	/// value it ends up with comes from the host's broadcast, so both peers always agree on which one is in force.
	/// </summary>
	public static void ToggleFromLocalClick(RoaringEarring relic)
	{
		Player? owner = relic.Owner;
		if (owner == null || !LocalContext.IsMine(relic))
		{
			return;
		}
		bool enabled = !relic.AutoPlayEnabled;
		INetGameService? service = RunManager.Instance.NetService;
		NetGameType type = service?.Type ?? NetGameType.None;
		if (type == NetGameType.Client)
		{
			if (service == null)
			{
				return;
			}
			try
			{
				service.SendMessage(new RoaringToggleMessage { OwnerNetId = owner.NetId, Enabled = enabled });
				Log.Info("[sts2-roaring-earring] asked the host for auto-play=" + enabled + " on player " + owner.NetId);
			}
			catch (Exception exception)
			{
				Log.Error("[sts2-roaring-earring] sending the toggle request failed: " + exception);
			}
			return;
		}
		SetEnabled(owner.NetId, relic, enabled, "click");
		if (type == NetGameType.Host && service != null)
		{
			Broadcast(service, owner.NetId, enabled);
		}
	}

	private static void OnRequestFromClient(RoaringToggleMessage message, ulong senderId)
	{
		INetGameService? service = RunManager.Instance.NetService;
		if (service == null || service.Type != NetGameType.Host)
		{
			return;
		}
		if (message.OwnerNetId != senderId)
		{
			// A client may only switch its own relic; anything else is a stale peer or a spoof.
			Log.Warn("[sts2-roaring-earring] ignored auto-play request for player " + message.OwnerNetId + " from player " + senderId);
			return;
		}
		RoaringEarring? relic = FindRelic(message.OwnerNetId);
		if (relic == null)
		{
			Log.Warn("[sts2-roaring-earring] auto-play request for player " + message.OwnerNetId + " found no relic to switch");
			return;
		}
		SetEnabled(message.OwnerNetId, relic, message.Enabled, "request");
		// Forward what was applied, not what was asked: the host's state is the one every peer ends up on.
		Broadcast(service, message.OwnerNetId, relic.AutoPlayEnabled);
	}

	private static void OnBroadcastFromHost(RoaringToggleMessage message, ulong senderId)
	{
		INetGameService? service = RunManager.Instance.NetService;
		if (service == null || service.Type != NetGameType.Client)
		{
			return;
		}
		RoaringEarring? relic = FindRelic(message.OwnerNetId);
		if (relic == null)
		{
			Log.Warn("[sts2-roaring-earring] host set auto-play for player " + message.OwnerNetId + " but this peer knows no such relic");
			return;
		}
		SetEnabled(message.OwnerNetId, relic, message.Enabled, "host " + senderId);
	}

	private static RoaringEarring? FindRelic(ulong ownerNetId)
	{
		RunState? state = _runState;
		if (state == null)
		{
			return null;
		}
		foreach (Player player in state.Players)
		{
			if (player.NetId == ownerNetId)
			{
				return player.GetRelic<RoaringEarring>();
			}
		}
		return null;
	}

	private static void SetEnabled(ulong ownerNetId, RoaringEarring relic, bool enabled, string source)
	{
		try
		{
			if (relic.AutoPlayEnabled == enabled)
			{
				return;
			}
			relic.AutoPlayEnabled = enabled;
			// The turn snapshot inside the relic decides when this takes effect: the turn already running keeps
			// the value it started with, so "next turn" is the same moment on every peer.
			Log.Info("[sts2-roaring-earring] auto-play " + (enabled ? "on" : "off") + " for player " + ownerNetId + " (" + source + "), effective from the next turn");
		}
		catch (Exception exception)
		{
			Log.Error("[sts2-roaring-earring] could not switch auto-play for player " + ownerNetId + ": " + exception);
		}
	}

	private static void Broadcast(INetGameService service, ulong ownerNetId, bool enabled)
	{
		try
		{
			service.SendMessage(new RoaringToggleMessage { OwnerNetId = ownerNetId, Enabled = enabled });
			Log.Info("[sts2-roaring-earring] host: broadcast auto-play=" + enabled + " for player " + ownerNetId);
		}
		catch (Exception exception)
		{
			Log.Error("[sts2-roaring-earring] broadcasting the toggle failed: " + exception);
		}
	}
}
