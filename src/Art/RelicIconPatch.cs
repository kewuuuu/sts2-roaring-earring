using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using Sts2RoaringEarring.Models;

namespace Sts2RoaringEarring.Art;

/// <summary>
/// RelicModel.Icon / IconOutline / BigIcon are plain non-virtual getters (RelicModel.cs:144-148) reading
/// res:// paths built by ImageHelper, which a DLL-only mod cannot populate: the atlas loader answers with its
/// "NOPE" placeholder. RoaringEarringMod.PatchAll discovers these classes, so no entry point change is needed.
/// Only RoaringEarring is replaced; every other relic, and every failure path, keeps the vanilla texture.
/// No HarmonyPriority here on purpose: another content mod may postfix the same getters (RitsuLib does), and
/// the safe winner is the node-level patch in RelicNodeArtPatch, which runs after the whole getter chain.
/// </summary>
[HarmonyPatch(typeof(RelicModel), nameof(RelicModel.Icon), MethodType.Getter)]
internal static class RelicIconPatch
{
	private static void Postfix(RelicModel __instance, ref Texture2D __result)
	{
		if (__instance is not RoaringEarring)
		{
			return;
		}
		Texture2D? texture = RoaringArt.Icon(__instance);
		if (texture != null)
		{
			__result = texture;
		}
	}
}

[HarmonyPatch(typeof(RelicModel), nameof(RelicModel.IconOutline), MethodType.Getter)]
internal static class RelicIconOutlinePatch
{
	private static void Postfix(RelicModel __instance, ref Texture2D __result)
	{
		if (__instance is not RoaringEarring)
		{
			return;
		}
		Texture2D? texture = RoaringArt.IconOutline(__instance);
		if (texture != null)
		{
			__result = texture;
		}
	}
}

[HarmonyPatch(typeof(RelicModel), nameof(RelicModel.BigIcon), MethodType.Getter)]
internal static class RelicBigIconPatch
{
	private static void Postfix(RelicModel __instance, ref Texture2D __result)
	{
		if (__instance is not RoaringEarring)
		{
			return;
		}
		Texture2D? texture = RoaringArt.BigIcon(__instance);
		if (texture != null)
		{
			__result = texture;
		}
	}
}
