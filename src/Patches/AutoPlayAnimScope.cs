using HarmonyLib;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;

namespace Sts2RoaringEarring.Patches;

/// <summary>
/// Process-wide switch for the relic's auto-play loop: while <see cref="Active"/> is set, every Spine time scale
/// is multiplied by <see cref="Multiplier"/>. The loop arms it right before playing and clears it in a finally,
/// so manual plays and every other animation keep the vanilla speed. Both prefixes scale the argument and let
/// the original method run; neither one skips it.
/// </summary>
internal static class AutoPlayAnimScope
{
	internal static volatile bool Active;

	internal static float Multiplier = 1f;
}

[HarmonyPatch(typeof(MegaAnimationState), nameof(MegaAnimationState.SetTimeScale))]
internal static class MegaAnimationStateTimeScalePatch
{
	private static void Prefix(ref float scale)
	{
		if (AutoPlayAnimScope.Active && AutoPlayAnimScope.Multiplier > 1f)
		{
			scale *= AutoPlayAnimScope.Multiplier;
		}
	}
}

[HarmonyPatch(typeof(MegaTrackEntry), nameof(MegaTrackEntry.SetTimeScale))]
internal static class MegaTrackEntryTimeScalePatch
{
	private static void Prefix(ref float scale)
	{
		if (AutoPlayAnimScope.Active && AutoPlayAnimScope.Multiplier > 1f)
		{
			scale *= AutoPlayAnimScope.Multiplier;
		}
	}
}
