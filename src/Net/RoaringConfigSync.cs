using System;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;
using Sts2RoaringEarring.Config;

namespace Sts2RoaringEarring.Net;

/// <summary>
/// Exchanges the host's mod parameters once per run. Each side both asks and answers, so whichever side
/// reaches RunStarted first still converges: a message that arrives before the other side registered its
/// handler is dropped by the vanilla bus (NetMessageBus.cs:88), and the opposite direction re-sends.
/// </summary>
public static class RoaringConfigSync
{
	/// <summary>自愈重试的每局上限：跨层累计，不随层数清零。</summary>
	private const int MaxConfigRequestsPerRun = 5;

	private static INetGameService? _attached;
	private static RunState? _runState;
	private static bool _loggedMessageIds;
	private static bool _adoptedHostConfig;
	private static bool _configRequestedThisFloor;
	private static int _configRequestFloor = -1;
	private static int _configRequestsThisRun;
	private static bool _loggedRequestCapReached;

	public static void AttachForRun(RunState state)
	{
		_runState = state;
		// RunStarted fires again on load and the override was cleared just before this call, so the
		// one-shot self-heal starts over from "nothing adopted yet" — including its per-run budget.
		_adoptedHostConfig = false;
		_configRequestedThisFloor = false;
		_configRequestFloor = -1;
		_configRequestsThisRun = 0;
		_loggedRequestCapReached = false;
		INetGameService? service = RunManager.Instance.NetService;
		NetGameType type = service?.Type ?? NetGameType.None;
		LogMessageIdsOnce();
		if (service == null || (type != NetGameType.Host && type != NetGameType.Client))
		{
			// Singleplayer and replays have no peer above them; the loaded file is already the answer.
			Log.Info("[sts2-roaring-earring] singleplayer: settings stay local; " + RoaringRuntimeConfig.Fingerprint(RoaringRuntimeConfig.Effective));
			return;
		}
		if (!ReferenceEquals(_attached, service))
		{
			// RunStarted fires again on load; registering twice would run every handler twice.
			_attached = service;
			if (type == NetGameType.Host)
			{
				service.RegisterMessageHandler<RoaringConfigRequestMessage>(OnConfigRequested);
			}
			else
			{
				service.RegisterMessageHandler<RoaringConfigMessage>(OnConfigReceived);
			}
			Log.Info("[sts2-roaring-earring] config sync handlers registered; this peer is the " + type);
		}
		if (!service.IsConnected)
		{
			// Registration is a local dictionary add (NetMessageBus.cs:127-144), so the handler above is
			// already armed; only the traffic on the wire has to wait for the transport.
			Log.Warn("[sts2-roaring-earring] config sync skipped: the net service is not connected yet");
			return;
		}
		if (type == NetGameType.Host)
		{
			BroadcastConfig(service);
			return;
		}
		service.SendMessage(new RoaringConfigRequestMessage());
		Log.Info("[sts2-roaring-earring] client: asked the host for its config");
	}

	private static void BroadcastConfig(INetGameService service)
	{
		// ForThisRun, not Effective: once this peer has taken its run snapshot (BeginRun runs right after
		// AttachForRun for a host), a settings edit made mid-run must not be handed to a client that asks
		// later in that same run — it would freeze values the host itself is no longer playing with.
		RoaringConfig config = RoaringRuntimeConfig.ForThisRun;
		try
		{
			service.SendMessage(RoaringConfigMessage.From(config));
			Log.Info("[sts2-roaring-earring] host: broadcast config " + RoaringRuntimeConfig.Fingerprint(config));
		}
		catch (Exception exception)
		{
			Log.Error("[sts2-roaring-earring] broadcasting the config failed: " + exception);
		}
	}

	private static void OnConfigRequested(RoaringConfigRequestMessage message, ulong senderId)
	{
		INetGameService? service = RunManager.Instance.NetService;
		if (service == null || service.Type != NetGameType.Host)
		{
			return;
		}
		// Every ask is answered with a fresh broadcast, which is what lets a late joiner catch up.
		Log.Info("[sts2-roaring-earring] config requested by player " + senderId);
		BroadcastConfig(service);
	}

	private static void OnConfigReceived(RoaringConfigMessage message, ulong senderId)
	{
		if (!RoaringRuntimeConfig.TryAdoptFromHost(message.ToConfig(), senderId))
		{
			return;
		}
		_adoptedHostConfig = true;
		// 采纳即冻结：房主给的值就是本局的值，局内再改设置只影响之后创建的局。
		RoaringRuntimeConfig.BeginRun(RoaringRuntimeConfig.Effective);
		RunState? state = _runState;
		if (state != null)
		{
			// A client suppresses the RunStarted grant because it cannot know the host's value yet.
			RoaringEarringMod.OnHostConfigAdopted(state);
		}
	}

	/// <summary>
	/// Self-heal for a client whose ask raced the host's handler registration: vanilla drops a message
	/// with no handler (NetMessageBus.cs:88), and without this the peer would keep its local numbers for
	/// the whole run. One attempt per floor plus a per-run cap of <see cref="MaxConfigRequestsPerRun"/>,
	/// driven by RoaringEarringMod's room-entered hook rather than by the run-start hook.
	/// </summary>
	public static void RequestConfigIfMissing()
	{
		if (_adoptedHostConfig || !RoaringRuntimeConfig.IsMultiplayerClient)
		{
			return;
		}
		int floor = _runState?.TotalFloor ?? -1;
		// 本层已经试过就安静等下一层；换层才允许再来一次，避免每个房间都发一个包。
		if (_configRequestedThisFloor && floor == _configRequestFloor)
		{
			return;
		}
		if (_configRequestsThisRun >= MaxConfigRequestsPerRun)
		{
			if (!_loggedRequestCapReached)
			{
				_loggedRequestCapReached = true;
				Log.Warn("[sts2-roaring-earring] giving up on the host config: " + _configRequestsThisRun
					+ " re-requests used this run; this run keeps the local values");
			}
			return;
		}
		INetGameService? service = RunManager.Instance.NetService;
		if (service == null || service.Type != NetGameType.Client)
		{
			return;
		}
		// Set before sending: a refused send must not become one attempt per room.
		_configRequestedThisFloor = true;
		_configRequestFloor = floor;
		_configRequestsThisRun++;
		try
		{
			service.SendMessage(new RoaringConfigRequestMessage());
			Log.Warn("[sts2-roaring-earring] host config missing at floor " + floor + "; re-requested (attempt "
				+ _configRequestsThisRun + "/" + MaxConfigRequestsPerRun + ", local values in effect until it arrives)");
		}
		catch (Exception exception)
		{
			Log.Error("[sts2-roaring-earring] re-requesting the host config failed: " + exception);
		}
	}

	private static void LogMessageIdsOnce()
	{
		if (_loggedMessageIds)
		{
			return;
		}
		// MessageTypes.Initialize runs after ModManager.Initialize (OneTimeInitialization.cs:59 then :84),
		// so the ids are unreadable during Init and are read here instead, once, inside a guard.
		if (!TryLogMessageId(typeof(RoaringConfigMessage), () => MessageTypes.TypeToId<RoaringConfigMessage>()))
		{
			return;
		}
		if (!TryLogMessageId(typeof(RoaringConfigRequestMessage), () => MessageTypes.TypeToId<RoaringConfigRequestMessage>()))
		{
			return;
		}
		_loggedMessageIds = true;
	}

	private static bool TryLogMessageId(Type messageType, Func<int> id)
	{
		try
		{
			Log.Info("[sts2-roaring-earring] message type " + messageType.Name + " id=" + id());
			return true;
		}
		catch (Exception exception)
		{
			// A missing type reaches here as whatever the caches throw: MessageTypes before Initialize
			// (MessageTypes.cs:21-24, InvalidOperationException) and NetTypeCache's plain
			// Dictionary<Type,int> indexer (NetTypeCache.cs:14, :54, KeyNotFoundException). Nothing may
			// escape the RunStarted hook, and the next run start retries instead of losing the id.
			Log.Warn("[sts2-roaring-earring] message type not registered: " + messageType.Name + ": " + exception.Message);
			return false;
		}
	}
}
