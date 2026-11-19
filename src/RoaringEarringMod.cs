using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Runs;
using Sts2RoaringEarring.Config;
using Sts2RoaringEarring.Models;

namespace Sts2RoaringEarring;

[ModInitializer("Init")]
public static class RoaringEarringMod
{
	public const string ModId = "sts2-roaring-earring";

	public static void Init()
	{
		RoaringConfig config = RoaringConfigStore.Load();
		ApplyPatches();
		RunManager.Instance.RunStarted += OnRunStarted;
		Log.Info("[sts2-roaring-earring] initialized; energy_per_turn=" + config.EnergyPerTurn
			+ ", max_cards_per_turn=" + config.MaxCardsPerTurn
			+ ", play_order=" + RoaringConfig.PlayOrderToken(config.Order)
			+ ", auto_end_turn=" + config.AutoEndTurn
			+ ", start_with_relic=" + config.StartWithRelic);
	}

	/// <summary>
	/// A DLL-only mod owns its Harmony instance: ModManager only auto-patches assemblies that carry no
	/// initializer, so the localization and relic-icon postfixes would never be applied otherwise.
	/// </summary>
	private static void ApplyPatches()
	{
		try
		{
			new Harmony(ModId).PatchAll(Assembly.GetExecutingAssembly());
			Log.Info("[sts2-roaring-earring] harmony patches applied");
		}
		catch (Exception exception)
		{
			Log.Error("[sts2-roaring-earring] harmony patching failed: " + exception);
		}
	}

	/// <summary>
	/// RunManager.RunStarted also fires when a save is loaded (RunManager.cs:788, NGame.cs:1182), which is why
	/// <see cref="GrantStartingRelic"/> skips a run that already sits on a floor.
	/// </summary>
	private static void OnRunStarted(RunState state)
	{
		try
		{
			if (!RoaringConfigStore.Effective.StartWithRelic)
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
