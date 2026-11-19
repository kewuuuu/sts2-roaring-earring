using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models.Relics;
using Sts2RoaringEarring.Config;

namespace Sts2RoaringEarring.Patches;

/// <summary>
/// WhisperingEarring's 13-card budget is a compile-time constant, so Roslyn inlined it into the async state machine:
/// both the loop bound (WhisperingEarring.cs:59) and the post-loop flag (WhisperingEarring.cs:83) compile to
/// <c>ldc.i4.s 13</c>. Neither site can be named from source, so this transpiler rewrites every such literal in
/// that one MoveNext into a call to <see cref="MaxCardsPerTurn"/>.
/// </summary>
/// <remarks>
/// Deliberately carries no [HarmonyPatch]: the target is compiler-generated and its name can move between game
/// builds, and a null TargetMethod would abort the attribute-driven PatchAll for the whole mod. It is applied by
/// hand from RoaringEarringMod.ApplyPatches instead, so every failure path degrades to today's vanilla behaviour.
/// Our own relic is untouched: it reads RoaringConfigStore.Effective.MaxCardsPerTurn from its own loop.
/// </remarks>
internal static class WhisperingEarringCapPatch
{
	/// <summary>The verified compiler-generated state machine of AfterAutoPrePlayPhaseEnteredLate.</summary>
	private const string StateMachineName = "<AfterAutoPrePlayPhaseEnteredLate>d__8";

	/// <summary>Fallback for a build that renumbers the state machine (Roslyn numbering is positional).</summary>
	private const string StateMachineFragment = "AfterAutoPrePlayPhaseEntered";

	/// <summary>The verified IL contains exactly two ldc.i4.s 13 in MoveNext; anything else means the IL moved.</summary>
	private const int ExpectedReplacements = 2;

	private static MethodInfo? _getter;

	/// <summary>
	/// The card budget the vanilla relic runs with: our configurable cap, floored at the shipped 13 so a config
	/// below 13 can leave the vanilla relic exactly as it is today but never worse.
	/// </summary>
	public static int MaxCardsPerTurn =>
		Math.Max(RoaringConfigStore.Effective.MaxCardsPerTurn, WhisperingEarring.maxCardsToPlay);

	public static void Apply(Harmony harmony)
	{
		try
		{
			Type? machine = ResolveStateMachine();
			if (machine == null)
			{
				Log.Warn("[sts2-roaring-earring] whispering earring: no nested type of " + nameof(WhisperingEarring)
					+ " named '" + StateMachineName + "' or containing '" + StateMachineFragment
					+ "'; vanilla 13-card behaviour is untouched");
				return;
			}
			MethodInfo? moveNext = ResolveMoveNext(machine);
			if (moveNext == null)
			{
				Log.Warn("[sts2-roaring-earring] whispering earring: " + machine.Name
					+ " exposes no MoveNext; vanilla 13-card behaviour is untouched");
				return;
			}
			MethodInfo? getter = ResolveGetter();
			MethodInfo? transpiler = typeof(WhisperingEarringCapPatch).GetMethod(
				nameof(Transpiler), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if (getter == null || transpiler == null)
			{
				Log.Warn("[sts2-roaring-earring] whispering earring: the cap getter could not be resolved; vanilla 13-card behaviour is untouched");
				return;
			}
			_getter = getter;
			harmony.Patch(moveNext, transpiler: new HarmonyMethod(transpiler));
			Log.Info("[sts2-roaring-earring] whispering earring: transpiler attached to " + machine.Name + ".MoveNext");
		}
		catch (Exception exception)
		{
			Log.Error("[sts2-roaring-earring] whispering earring: attaching the cap transpiler failed: " + exception);
		}
	}

	/// <summary>
	/// Rewrites each ldc.i4.s 13 (and ldc.i4 13, should a build emit the wide form) into one call. The whole
	/// rewrite is all-or-nothing: on any count other than two the original instructions are returned untouched.
	/// </summary>
	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		List<CodeInstruction> original = instructions.ToList();
		if (_getter == null)
		{
			Log.Error("[sts2-roaring-earring] whispering earring: transpiler ran without a cap getter; vanilla behaviour kept");
			return original;
		}
		List<CodeInstruction> rewritten = new List<CodeInstruction>(original.Count);
		int replaced = 0;
		foreach (CodeInstruction instruction in original)
		{
			if (IsVanillaCap(instruction))
			{
				// Keep labels and exception blocks: these two instructions sit inside the state machine's
				// try/finally region, and a replacement that drops a block boundary corrupts the method.
				CodeInstruction call = new CodeInstruction(OpCodes.Call, _getter);
				call.labels.AddRange(instruction.labels);
				call.blocks.AddRange(instruction.blocks);
				rewritten.Add(call);
				replaced++;
				continue;
			}
			rewritten.Add(instruction);
		}
		if (replaced != ExpectedReplacements)
		{
			Log.Error("[sts2-roaring-earring] whispering earring: transpiler matched " + replaced + " of "
				+ ExpectedReplacements + " ldc.i4.s 13 instructions in " + StateMachineName
				+ ".MoveNext; patch left INERT so the relic keeps its 13-card budget");
			return original;
		}
		Log.Info("[sts2-roaring-earring] whispering earring: transpiler replaced " + replaced
			+ " ldc.i4.s 13 instruction(s) in " + StateMachineName + ".MoveNext; vanilla cap=" + MaxCardsPerTurn
			+ " (max(13, config.max_cards_per_turn=" + RoaringConfigStore.Effective.MaxCardsPerTurn + "))");
		return rewritten;
	}

	private static bool IsVanillaCap(CodeInstruction instruction)
	{
		if (instruction.opcode != OpCodes.Ldc_I4_S && instruction.opcode != OpCodes.Ldc_I4)
		{
			return false;
		}
		object? operand = instruction.operand;
		int value = operand is sbyte small ? small : operand is int wide ? wide : int.MinValue;
		return value == WhisperingEarring.maxCardsToPlay;
	}

	private static Type? ResolveStateMachine()
	{
		Type[] nested = typeof(WhisperingEarring).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
		foreach (Type candidate in nested)
		{
			if (candidate.Name == StateMachineName)
			{
				return candidate;
			}
		}
		foreach (Type candidate in nested)
		{
			if (candidate.Name.Contains(StateMachineFragment, StringComparison.Ordinal))
			{
				return candidate;
			}
		}
		return null;
	}

	/// <summary>
	/// Roslyn emits MoveNext as an explicit interface implementation, so its metadata name is
	/// "System.Runtime.CompilerServices.IAsyncStateMachine.MoveNext"; the interface map finds it either way.
	/// </summary>
	private static MethodInfo? ResolveMoveNext(Type machine)
	{
		MethodInfo? direct = machine.GetMethod("MoveNext",
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		if (direct != null)
		{
			return direct;
		}
		InterfaceMapping map = machine.GetInterfaceMap(typeof(IAsyncStateMachine));
		for (int index = 0; index < map.InterfaceMethods.Length; index++)
		{
			if (map.InterfaceMethods[index].Name == nameof(IAsyncStateMachine.MoveNext))
			{
				return map.TargetMethods[index];
			}
		}
		return null;
	}

	private static MethodInfo? ResolveGetter()
	{
		return typeof(WhisperingEarringCapPatch)
			.GetProperty(nameof(MaxCardsPerTurn), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?
			.GetGetMethod(nonPublic: true);
	}
}
