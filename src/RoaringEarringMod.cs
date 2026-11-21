using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Runs;
using Sts2RoaringEarring.Config;
using Sts2RoaringEarring.Models;
using Sts2RoaringEarring.Net;
using Sts2RoaringEarring.Patches;

namespace Sts2RoaringEarring;

[ModInitializer("Init")]
public static class RoaringEarringMod
{
	public const string ModId = "sts2-roaring-earring";

	public static void Init()
	{
		RoaringConfig config = RoaringConfigStore.Load();
		// Registry only. EventRelicPool has no consumer (RelicGrabBag.cs:70-77 builds rewards from
		// SharedRelicPool plus the character pool), so this keeps the relic out of random rewards while giving it
		// what the rest of the game assumes every relic has: a pool (RelicModel.cs:176, which throws for a
		// pool-less relic and takes hovering, and the Vakuu event, down with it), membership in ModelDb.AllRelics
		// (ModelDb.cs:264), the relic library (NRelicCollectionCategory.cs:157) and the `relic` console command
		// (RelicConsoleCmd.cs:68).
		ModHelper.AddModelToPool<EventRelicPool, RoaringEarring>();
		ApplyPatches();
		RunManager.Instance.RunStarted += OnRunStarted;
		// The client-side self-heal for a config request that raced the host's handler is driven from room
		// entry: it must not depend on the relic being owned, which a client cannot know before it adopts the
		// host's start_with_relic.
		RunManager.Instance.RoomEntered += OnRoomEntered;
#if RITSULIB
		// Optional: without the RitsuLib assemblies this compiles out, and a build without the reference
		// drops the whole file (csproj Compile Remove), so the JSON file stays the only way in.
		Integration.RitsuLibSettingsPage.TryRegister();
#endif
		Log.Info("[sts2-roaring-earring] initialized; energy_per_turn=" + config.EnergyPerTurn
			+ ", max_cards_per_turn=" + config.MaxCardsPerTurn
			+ ", play_delay_ms=" + config.PlayDelayMs
			+ ", play_order=" + RoaringConfig.PlayOrderToken(config.Order)
			+ ", auto_end_turn=" + config.AutoEndTurn
			+ ", start_with_relic=" + config.StartWithRelic
			+ ", show_toggle=" + config.ShowToggle
			+ ", toggle_default=" + RoaringEarring.DefaultAutoPlayEnabled
			+ ", relic_pool=EventRelicPool (registry only)");
	}

	/// <summary>
	/// A DLL-only mod owns its Harmony instance: ModManager only auto-patches assemblies that carry no
	/// initializer, so the localization and relic-icon postfixes would never be applied otherwise.
	/// </summary>
	private static void ApplyPatches()
	{
		Harmony harmony = new Harmony(ModId);
		try
		{
			harmony.PatchAll(Assembly.GetExecutingAssembly());
			Log.Info("[sts2-roaring-earring] harmony patches applied");
		}
		catch (Exception exception)
		{
			Log.Error("[sts2-roaring-earring] harmony patching failed: " + exception);
		}
		// The vanilla earring's 13-card budget lives inside a compiler-generated state machine, so it cannot be
		// named by an attribute; it is patched by hand outside the try above so a resolution failure of this one
		// patch can never swallow the attribute-driven ones.
		WhisperingEarringCapPatch.Apply(harmony);
	}

	/// <summary>
	/// RunManager.RunStarted also fires when a save is loaded (RunManager.cs:788, NGame.cs:1182), which is why
	/// <see cref="GrantStartingRelic"/> skips a run that already sits on a floor.
	/// </summary>
	private static void OnRunStarted(RunState state)
	{
		try
		{
			// A run start is either a fresh run or a save load, so the previous run's frozen values are
			// dropped before this one takes its own.
			RoaringRuntimeConfig.ClearOverride();
			RoaringRuntimeConfig.EndRun();
			RoaringConfigSync.AttachForRun(state);
			// Same lifecycle as the config sync: one handler per net service, re-registered at the next run start.
			RoaringToggleSync.AttachForRun(state);
			if (!RoaringRuntimeConfig.IsMultiplayerClient)
			{
				// Singleplayer and a host both own the values: the file as it is right now is this run's config.
				RoaringRuntimeConfig.BeginRun(RoaringRuntimeConfig.Effective);
			}
		}
		catch (Exception exception)
		{
			Log.Error("[sts2-roaring-earring] config sync failed at run start: " + exception);
		}
		try
		{
			if (RoaringRuntimeConfig.IsMultiplayerClient)
			{
				// A client cannot know the host's start_with_relic yet; OnHostConfigAdopted grants it one
				// round trip later, and granting here would hand out a relic the host may have disabled.
				return;
			}
			if (!RoaringRuntimeConfig.Effective.StartWithRelic)
			{
				return;
			}
			TaskHelper.RunSafely(GrantStartingRelic(state));
		}
		catch (Exception exception)
		{
			Log.Error("[sts2-roaring-earring] run start hook failed: " + exception);
		}
	}

	/// <summary>
	/// A client suppresses the RunStarted grant because it cannot know the host's value yet; this grants the
	/// instant the host's config has been adopted and frozen.
	/// </summary>
	internal static void OnHostConfigAdopted(RunState state)
	{
		try
		{
			if (!RoaringRuntimeConfig.Effective.StartWithRelic)
			{
				return;
			}
			TaskHelper.RunSafely(GrantStartingRelic(state));
		}
		catch (Exception exception)
		{
			Log.Error("[sts2-roaring-earring] granting the starting relic after adopting the host config failed: " + exception);
		}
	}

	/// <summary>
	/// Gameplay-side self-heal for a client whose first ask raced the host's handler registration: the floor
	/// gate and the per-run cap both live in <see cref="RoaringConfigSync.RequestConfigIfMissing"/>.
	/// </summary>
	private static void OnRoomEntered()
	{
		try
		{
			RoaringConfigSync.RequestConfigIfMissing();
		}
		catch (Exception exception)
		{
			Log.Error("[sts2-roaring-earring] re-requesting the host config failed: " + exception);
		}
	}

	private static async Task GrantStartingRelic(RunState state)
	{
		try
		{
			// A fresh run starts at floor 0; anything else is a save load, where the player may have dropped it.
			if (state.TotalFloor > 0)
			{
				return;
			}
			RelicModel template = ModelDb.Relic<RoaringEarring>();
			foreach (Player player in state.Players)
			{
				if (player.GetRelic<RoaringEarring>() != null)
				{
					continue;
				}
				await RelicCmd.Obtain(template.ToMutable(), player);
			}
		}
		catch (Exception exception)
		{
			Log.Error("[sts2-roaring-earring] granting the starting relic failed: " + exception);
		}
	}
}
