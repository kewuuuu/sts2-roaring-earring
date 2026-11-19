using System;
using System.Globalization;
using System.Text;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2RoaringEarring.Config;

/// <summary>
/// The config a run is actually played with: the host's values for the length of a multiplayer run and
/// the locally loaded file everywhere else. A client's file is never rewritten on the host's behalf.
/// </summary>
public static class RoaringRuntimeConfig
{
	private static RoaringConfig? _hostOverride;
	private static RoaringConfig? _runSnapshot;
	private static bool _warnedRejectedWrite;

	public static RoaringConfig Effective => _hostOverride ?? RoaringConfigStore.Current;

	/// <summary>
	/// The values this run is played with: frozen when the run started, so a settings change made mid-run
	/// reaches the next run instead of moving numbers two peers already agreed on. Falls back to
	/// <see cref="Effective"/> before any run has started and on a client still waiting for the host.
	/// </summary>
	public static RoaringConfig ForThisRun => _runSnapshot ?? Effective;

	/// <summary>
	/// Freezes this run's values. Called from RoaringEarringMod.OnRunStarted for a singleplayer run or a host,
	/// and from RoaringConfigSync once a client has adopted the host's values.
	/// </summary>
	public static void BeginRun(RoaringConfig config)
	{
		_runSnapshot = config;
		Log.Info("[sts2-roaring-earring] run settings frozen: " + Fingerprint(config));
	}

	/// <summary>
	/// Drops the frozen values, so the next run reads whatever the file holds by then. Called from the
	/// RunStarted hook ahead of <see cref="BeginRun"/>: loading a save starts a run through the same path.
	/// </summary>
	public static void EndRun()
	{
		if (_runSnapshot == null)
		{
			return;
		}
		_runSnapshot = null;
		Log.Info("[sts2-roaring-earring] run settings released; a later run takes the settings as they are then");
	}

	/// <summary>
	/// True only while this peer is a client of a multiplayer run. NetService is still null until a run
	/// initializes (RunManager.cs:182 vs :531), and an unreachable service must read as local authority.
	/// </summary>
	public static bool IsMultiplayerClient
	{
		get
		{
			INetGameService? service = RunManager.Instance.NetService;
			return service != null && service.Type == NetGameType.Client;
		}
	}

	public static bool TryAdoptFromHost(RoaringConfig config, ulong senderId)
	{
		if (!IsMultiplayerClient)
		{
			// A host or a replay that receives this is being fed a stale message; its own file stays source of truth.
			Log.Warn("[sts2-roaring-earring] ignoring a host config from player " + senderId + ": this peer is not a multiplayer client");
			return false;
		}
		RoaringConfig normalized = config.Normalized();
		if (!SameValues(config, normalized))
		{
			// A hand-edited host file must not abort the run: clamp it and play on the usable values.
			Log.Warn("[sts2-roaring-earring] host config from player " + senderId + " was out of range and has been clamped");
		}
		_hostOverride = normalized;
		Log.Info("[sts2-roaring-earring] adopting host config from player " + senderId + ": " + Fingerprint(normalized));
		return true;
	}

	public static void ClearOverride()
	{
		bool hadOverride = _hostOverride != null;
		_hostOverride = null;
		// The write warning is a per-run notice, not a per-slider-move one.
		_warnedRejectedWrite = false;
		if (hadOverride)
		{
			Log.Info("[sts2-roaring-earring] host config override cleared; local settings are in force again");
		}
	}

	/// <summary>
	/// The only way the settings page writes. A client's JSON stays exactly as the player left it, so the
	/// write is refused rather than applied locally.
	/// </summary>
	public static bool TryWriteLocal(RoaringConfig config)
	{
		if (IsMultiplayerClient)
		{
			if (!_warnedRejectedWrite)
			{
				_warnedRejectedWrite = true;
				Log.Warn("[sts2-roaring-earring] settings write refused: a multiplayer client keeps the host's values and leaves its own JSON untouched");
			}
			return false;
		}
		return RoaringConfigStore.Save(config);
	}

	/// <summary>
	/// One line that names every synced value so two logs can be compared by eye. The hash is FNV-1a over
	/// the body: string.GetHashCode is randomized per process and would never match across two machines.
	/// </summary>
	public static string Fingerprint(RoaringConfig config)
	{
		string body = "energy=" + config.EnergyPerTurn
			+ " cards=" + config.MaxCardsPerTurn
			+ " order=" + RoaringConfig.PlayOrderToken(config.Order)
			+ " end=" + Bit(config.AutoEndTurn)
			+ " start=" + Bit(config.StartWithRelic);
		return body + " h=" + Fnv1a(body);
	}

	private static bool SameValues(RoaringConfig left, RoaringConfig right)
	{
		return left.EnergyPerTurn == right.EnergyPerTurn
			&& left.MaxCardsPerTurn == right.MaxCardsPerTurn
			&& left.Order == right.Order
			&& left.AutoEndTurn == right.AutoEndTurn
			&& left.StartWithRelic == right.StartWithRelic;
	}

	private static string Bit(bool value)
	{
		return value ? "1" : "0";
	}

	private static string Fnv1a(string text)
	{
		uint hash = 2166136261u;
		foreach (byte value in Encoding.UTF8.GetBytes(text))
		{
			hash = (hash ^ value) * 16777619u;
		}
		return hash.ToString("x8", CultureInfo.InvariantCulture);
	}
}
