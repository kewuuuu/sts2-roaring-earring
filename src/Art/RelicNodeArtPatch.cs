using System;
using System.Collections.Concurrent;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Relics;
using Sts2RoaringEarring.Models;

namespace Sts2RoaringEarring.Art;

/// <summary>
/// Last line of defence for the icons: NRelic.Reload assigns Icon.Texture = Model.Icon / Model.BigIcon
/// (NRelic.cs:139-155), so another mod that postfixes those getters can still overwrite the value we set.
/// Re-running the assignment after Reload has finished makes the visible node carry our texture no matter who
/// wins the getter chain. Reload is private, hence the name lookup.
/// </summary>
[HarmonyPatch(typeof(NRelic), "Reload")]
internal static class RelicNodeArtPatch
{
	private static readonly ConcurrentDictionary<string, byte> Reported = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

	private static void Postfix(NRelic __instance)
	{
		try
		{
			// Reload can run before the model is attached, and NRelic.Model throws in that case, so read the
			// private field: a relic node without a model is simply not ours yet.
			RelicModel? model = Traverse.Create(__instance).Field("_model").GetValue<RelicModel>();
			if (model is not RoaringEarring)
			{
				return;
			}
			TextureRect? icon = __instance.Icon;
			if (icon == null)
			{
				return;
			}
			NRelic.IconSize size = Traverse.Create(__instance).Field("_iconSize").GetValue<NRelic.IconSize>();
			if (size == NRelic.IconSize.Large)
			{
				Texture2D? big = RoaringArt.BigIcon(model);
				if (big != null)
				{
					icon.Texture = big;
				}
				return;
			}
			Texture2D? small = RoaringArt.Icon(model);
			if (small != null)
			{
				icon.Texture = small;
			}
			// The outline is baked into the PNG now, so the layered outline vanilla Reload switched on
			// (NRelic.cs:146-147) is hidden here: no texture write, just the visibility.
			TextureRect? outline = __instance.Outline;
			if (outline != null)
			{
				outline.Visible = false;
			}
			if (Reported.TryAdd("done", 0))
			{
				Log.Info("[sts2-roaring-earring] relic node art applied (" + size + ")");
			}
		}
		catch (Exception exception)
		{
			if (Reported.TryAdd("failed", 0))
			{
				Log.Error("[sts2-roaring-earring] relic node art patch failed: " + exception.Message);
			}
		}
	}
}
