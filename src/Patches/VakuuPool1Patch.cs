using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using Sts2RoaringEarring.Models;

namespace Sts2RoaringEarring.Patches;

/// <summary>
/// Vakuu's first pool is built by the private getter <c>Vakuu.Pool1</c> (Vakuu.cs:43-59) as three relic options
/// (BloodSoakedRose, WhisperingEarring, Fiddle). This postfix makes our relic the fourth candidate, and hides the
/// vanilla earring once ours is owned.
/// </summary>
/// <remarks>
/// Determinism: the pool is rebuilt on every peer while only the chosen index crosses the wire
/// (EventSynchronizer.cs:229-287) and the room is checksummed on exit (EventRoom.cs:94-97), so this postfix reads
/// replicated state only - <see cref="EventModel.Owner"/> and that player's relic list. It never touches local files,
/// local settings or the local player, and it mutates only the freshly built list the getter just returned.
/// </remarks>
[HarmonyPatch(typeof(Vakuu), "Pool1", MethodType.Getter)]
internal static class VakuuPool1Patch
{
	/// <summary>Done() is protected on AncientEventModel (AncientEventModel.cs:237); Vakuu does not re-expose it.</summary>
	private static readonly MethodInfo? DoneMethod = AccessTools.Method(typeof(AncientEventModel), "Done");

	private static int _reportedAdded;

	private static int _reportedHidden;

	private static void Postfix(Vakuu __instance, ref List<EventOption> __result)
	{
		try
		{
			Apply(__instance, __result);
		}
		catch (Exception exception)
		{
			Log.Error("[sts2-roaring-earring] vakuu pool1 postfix failed: " + exception);
		}
	}

	private static void Apply(Vakuu ancient, List<EventOption>? options)
	{
		if (options == null)
		{
			return;
		}
		// 1. The getter rebuilds its list on every call; never append twice.
		if (options.Any((EventOption option) => option != null && option.Relic is RoaringEarring))
		{
			return;
		}
		// 2. A canonical event has no owner, and its pool must stay the untouched vanilla three.
		Player? owner = ancient.Owner;
		if (owner == null)
		{
			return;
		}
		// 3. Owning ours hides the vanilla earring from the same pool.
		if (owner.Relics.Any((RelicModel relic) => relic is RoaringEarring))
		{
			int hidden = options.RemoveAll((EventOption option) => option != null && option.Relic is WhisperingEarring);
			if (hidden > 0 && Interlocked.Exchange(ref _reportedHidden, 1) == 0)
			{
				Log.Info("[sts2-roaring-earring] vakuu: roaring earring already owned, hid " + hidden + " vanilla whispering earring option(s)");
			}
			return;
		}
		// 4. Otherwise the pool grows from three candidates to four.
		RelicModel roaring = ModelDb.Relic<RoaringEarring>().ToMutable();
		// Required twice over: DynamicEventDescription and RelicCmd.Obtain both dereference it.
		roaring.Owner = owner;
		string textKey = "VAKUU.pages.INITIAL.options." + roaring.Id.Entry;
		options.Add(EventOption.FromRelic(roaring, ancient, () => ChooseAsync(ancient, roaring, owner), textKey));
		if (Interlocked.Exchange(ref _reportedAdded, 1) == 0)
		{
			Log.Info("[sts2-roaring-earring] vakuu: pool1 now offers 4 candidates, textKey=" + textKey
				+ ", relic=" + roaring.Id.Entry);
		}
	}

	private static async Task ChooseAsync(AncientEventModel ancient, RelicModel relic, Player owner)
	{
		try
		{
			await RelicCmd.Obtain(relic, owner);
		}
		catch (Exception exception)
		{
			Log.Error("[sts2-roaring-earring] vakuu: obtaining the roaring earring failed: " + exception);
			return;
		}
		FinishEvent(ancient);
	}

	private static void FinishEvent(AncientEventModel ancient)
	{
		if (DoneMethod == null)
		{
			Log.Error("[sts2-roaring-earring] vakuu: AncientEventModel.Done was not found; the event cannot be finished");
			return;
		}
		try
		{
			DoneMethod.Invoke(ancient, null);
		}
		catch (TargetInvocationException exception)
		{
			Log.Error("[sts2-roaring-earring] vakuu: Done() threw: " + (exception.InnerException ?? exception));
		}
		catch (Exception exception)
		{
			Log.Error("[sts2-roaring-earring] vakuu: Done() could not be invoked: " + exception);
		}
	}
}
