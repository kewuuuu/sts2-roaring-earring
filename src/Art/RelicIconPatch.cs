using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using Sts2RoaringEarring.Models;

namespace Sts2RoaringEarring.Art;

/// <summary>
/// RelicModel.Icon / IconOutline / BigIcon are plain non-virtual getters (RelicModel.cs:144-148) reading
/// res:// paths built by ImageHelper, which a DLL-only mod cannot populate: the atlas loader answers with its
/// "NOPE" placeholder and logs a missing-sprite warning. These prefixes answer first and return false, so the
/// vanilla getter (and its failed res:// lookup) never runs for RoaringEarring; every other relic, and every
/// decode failure that leaves <see cref="RoaringArt"/> null, falls through to the vanilla getter.
/// RoaringEarringMod.PatchAll discovers these classes, so no entry point change is needed.
/// No HarmonyPriority here on purpose: another content mod may patch the same getters (RitsuLib does), and
/// the safe winner is the node-level patch in RelicNodeArtPatch, which runs after the whole getter chain.
/// </summary>
[HarmonyPatch(typeof(RelicModel), nameof(RelicModel.Icon), MethodType.Getter)]
internal static class RelicIconPatch
{
	private static bool Prefix(RelicModel __instance, ref Texture2D __result)
	{
		if (__instance is not RoaringEarring)
		{
			return true;
		}
		Texture2D? texture = RoaringArt.Icon(__instance);
		if (texture == null)
		{
			return true;
		}
		__result = texture;
		return false;
	}
}

[HarmonyPatch(typeof(RelicModel), nameof(RelicModel.IconOutline), MethodType.Getter)]
internal static class RelicIconOutlinePatch
{
	private static bool Prefix(RelicModel __instance, ref Texture2D __result)
	{
		if (__instance is not RoaringEarring)
		{
			return true;
		}
		Texture2D? texture = RoaringArt.IconOutline(__instance);
		if (texture == null)
		{
			return true;
		}
		__result = texture;
		return false;
	}
}

[HarmonyPatch(typeof(RelicModel), nameof(RelicModel.BigIcon), MethodType.Getter)]
internal static class RelicBigIconPatch
{
	private static bool Prefix(RelicModel __instance, ref Texture2D __result)
	{
		if (__instance is not RoaringEarring)
		{
			return true;
		}
		Texture2D? texture = RoaringArt.BigIcon(__instance);
		if (texture == null)
		{
			return true;
		}
		__result = texture;
		return false;
	}
}
