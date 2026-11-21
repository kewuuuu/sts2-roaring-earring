using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Relics;
using Sts2RoaringEarring.Config;
using Sts2RoaringEarring.Models;
using Sts2RoaringEarring.Net;

namespace Sts2RoaringEarring.Patches;

/// <summary>
/// Left-clicking the relic icon switches its auto-play. Where the input actually lands, from the decompiled build:
/// NClickableControl._GuiInput (NClickableControl.cs:504-522) turns the mouse button into the MousePressed /
/// MouseReleased signals, ConnectSignals wires those to OnPressHandler / OnReleaseHandler
/// (NClickableControl.cs:349-364), and a left-button press that started on a visible, focused control
/// (NClickableControl.cs:390-410) reaches OnReleaseHandler (NClickableControl.cs:441-449), which calls the virtual
/// OnRelease() (NClickableControl.cs:495-497). NRelic is visuals only and handles no input (NRelic.cs:14-17); the
/// clickable icon is NRelicInventoryHolder (NRelicInventoryHolder.cs:28), which exposes the model through
/// Relic.Model (NRelicInventoryHolder.cs:162) and does not override OnRelease, so a postfix on the base method
/// sees exactly the relic icons of whichever relic bar was clicked. The local bar is the only one whose own handler
/// runs on that click (NRelicInventory.cs:270-273), and LocalContext.IsMine drops other players' relics
/// (LocalContext.cs:148-155). Hover and the right button never reach OnRelease (NClickableControl.cs:390-410).
/// The vanilla inspect screen still opens: this is additive, and the click stays the same gesture.
/// </summary>
[HarmonyPatch(typeof(NClickableControl), "OnRelease")]
internal static class RelicToggleClickPatch
{
	[HarmonyPostfix]
	private static void Postfix(NClickableControl __instance)
	{
		try
		{
			if (__instance is not NRelicInventoryHolder holder)
			{
				return;
			}
			if (!RoaringRuntimeConfig.ForThisRun.ShowToggle)
			{
				// show_toggle = false: the click stays inert and the relic works as if it were on.
				return;
			}
			NRelic? node = holder.Relic;
			if (node == null)
			{
				return;
			}
			// The getter throws before the model is set (NRelic.cs:85-90); a click that early has nothing to switch.
			RelicModel model = node.Model;
			if (model is not RoaringEarring earring)
			{
				return;
			}
			if (!LocalContext.IsMine(earring))
			{
				return;
			}
			RoaringToggleSync.ToggleFromLocalClick(earring);
		}
		catch (Exception)
		{
			// A Harmony postfix must never throw back into Godot's input loop.
		}
	}
}
