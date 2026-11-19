using System;
using System.Reflection;
using MegaCrit.Sts2.Core.Logging;
using STS2RitsuLib.Settings;
using STS2RitsuLib.Utils.Persistence;
using Sts2RoaringEarring.Config;
using Sts2RoaringEarring.Net;

namespace Sts2RoaringEarring.Integration;

/// <summary>
/// 把 <see cref="RoaringConfig"/> 里玩家可调的 5 个键注册成 RitsuLib 设置界面里的一页。
/// 只有 csproj 打开 EnableRitsuLib 时才参与编译；运行期还要 RitsuLib 程序集确实加载了才会注册，
/// 任何失败都被 <see cref="TryRegister"/> 挡成一条警告，模组其余部分不受影响。
/// </summary>
internal static class RitsuLibSettingsPage
{
	private const string SettingsAssemblyName = "STS2-RitsuLib.Settings";
	private const string PageId = "roaring-earring-settings";

	private static bool _loggedReadOnly;

	/// <summary>
	/// 设置页入口：RitsuLib 没加载时只留一条警告；注册过程中任何异常也只留一条警告。
	/// </summary>
	public static void TryRegister()
	{
		if (!IsSettingsAssemblyLoaded())
		{
			Log.Warn("[sts2-roaring-earring] RitsuLib settings page not registered: " + SettingsAssemblyName + " is not loaded");
			return;
		}
		try
		{
			Register();
			Log.Info("[sts2-roaring-earring] RitsuLib settings page registered: mod_id=" + RoaringEarringMod.ModId + ", page_id=" + PageId);
		}
		catch (Exception exception)
		{
			Log.Warn("[sts2-roaring-earring] RitsuLib settings page not registered: " + exception.Message);
		}
	}

	/// <summary>
	/// 探测 RitsuLib 设置程序集是否已加载。只看程序集清单，不触发任何 RitsuLib 类型解析：
	/// 这段代码必须在「RitsuLib 不存在」时也能安全执行。
	/// </summary>
	private static bool IsSettingsAssemblyLoaded()
	{
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		foreach (Assembly assembly in assemblies)
		{
			string? name = assembly.GetName().Name;
			if (string.Equals(name, SettingsAssemblyName, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	private static void Register()
	{
		ModSettingsPageBuilder builder = new ModSettingsPageBuilder(RoaringEarringMod.ModId, PageId);
		builder.WithTitle(ModSettingsText.Literal("狂吼耳环"));
		builder.WithDescription(ModSettingsText.Literal(
			"改动会立刻写回 sts2-roaring-earring.cfg，但从下一局开始生效：本局在开局时就把这 5 个值冻结了，"
			+ "局内再改只影响之后创建的对局（读档开局同理）。"
			+ "联机时以房主配置为准：客户端这里显示的是房主的数值，写入不会改动本机文件。"));
		builder.WithModDisplayName(ModSettingsText.Literal("狂吼耳环 / Roaring Earring"));

		builder.AddSection("auto_play", section =>
		{
			section.WithTitle(ModSettingsText.Literal("自动出牌"));
			section.AddIntSlider(
				"energy_per_turn",
				ModSettingsText.Literal("每回合额外能量"),
				IntBinding("energy_per_turn", () => RoaringRuntimeConfig.Effective.EnergyPerTurn, (target, value) => target.EnergyPerTurn = value),
				RoaringConfig.EnergyPerTurnMin,
				RoaringConfig.EnergyPerTurnMax,
				1,
				value => value.ToString(),
				ModSettingsText.Literal("持有耳环时每回合额外获得的能量上限。改动从下一局开始生效。"));
			section.AddIntSlider(
				"max_cards_per_turn",
				ModSettingsText.Literal("每回合自动出牌上限"),
				IntBinding("max_cards_per_turn", () => RoaringRuntimeConfig.Effective.MaxCardsPerTurn, (target, value) => target.MaxCardsPerTurn = value),
				RoaringConfig.MaxCardsPerTurnMin,
				RoaringConfig.MaxCardsPerTurnMax,
				1,
				value => value.ToString(),
				ModSettingsText.Literal("一回合最多自动打出多少张牌。改动从下一局开始生效。"));
			section.AddIntSlider(
				"play_order",
				ModSettingsText.Literal("自动出牌顺序"),
				IntBinding("play_order", () => RoaringConfigMessage.ToIndex(RoaringRuntimeConfig.Effective.Order), (target, value) => target.Order = RoaringConfigMessage.ToOrder(value)),
				0,
				2,
				1,
				value => RoaringConfig.PlayOrderToken(RoaringConfigMessage.ToOrder(value)),
				ModSettingsText.Literal("从左到右 / 从右到左 / 随机。改动从下一局开始生效。"));
			section.AddToggle(
				"auto_end_turn",
				ModSettingsText.Literal("自动结束回合"),
				BoolBinding("auto_end_turn", () => RoaringRuntimeConfig.Effective.AutoEndTurn, (target, value) => target.AutoEndTurn = value),
				ModSettingsText.Literal("手牌打完或达到上限后自动结束回合；战斗结束或回合已切换时不会重复结束。改动从下一局开始生效。"),
				() => true);
		});

		builder.AddSection("startup", section =>
		{
			section.WithTitle(ModSettingsText.Literal("开局"));
			section.AddToggle(
				"start_with_relic",
				ModSettingsText.Literal("开局自带遗物"),
				BoolBinding("start_with_relic", () => RoaringRuntimeConfig.Effective.StartWithRelic, (target, value) => target.StartWithRelic = value),
				ModSettingsText.Literal("每个玩家在开局直接获得一个「狂吼耳环」；从下一局开始生效。"),
				() => true);
		});

		ModSettingsPage page = builder.Build();
		ModSettingsRegistry.Register(page);
	}

	private static IModSettingsValueBinding<int> IntBinding(
		string dataKey,
		Func<int> read,
		Action<DraftConfig, int> write)
	{
		return ModSettingsBindings.Callback<int>(
			RoaringEarringMod.ModId,
			dataKey,
			read,
			value => WriteLocal(dataKey, target => write(target, value)),
			() => { },
			SaveScope.Global);
	}

	private static IModSettingsValueBinding<bool> BoolBinding(
		string dataKey,
		Func<bool> read,
		Action<DraftConfig, bool> write)
	{
		return ModSettingsBindings.Callback<bool>(
			RoaringEarringMod.ModId,
			dataKey,
			read,
			value => WriteLocal(dataKey, target => write(target, value)),
			() => { },
			SaveScope.Global);
	}

	/// <summary>
	/// 每次改动都从「当前生效配置」拷一份候选、改一项、整份写回；写入走
	/// <see cref="RoaringRuntimeConfig.TryWriteLocal"/>，所以联机客户端既不改本机 JSON，也不改页面显示值。
	/// </summary>
	private static void WriteLocal(string dataKey, Action<DraftConfig> change)
	{
		Log.Info("[sts2-roaring-earring] settings write " + dataKey);
		DraftConfig candidate = new DraftConfig(RoaringRuntimeConfig.Effective);
		change(candidate);
		if (RoaringRuntimeConfig.TryWriteLocal(candidate.ToConfig()))
		{
			return;
		}
		if (!RoaringRuntimeConfig.IsMultiplayerClient || _loggedReadOnly)
		{
			return;
		}
		_loggedReadOnly = true;
		Log.Info("[sts2-roaring-earring] settings page is read-only this run: a multiplayer client follows the host's config");
	}

	/// <summary>
	/// 设置页的候选配置：5 个键全部可变，写回时合成完整的 <see cref="RoaringConfig"/>，不会丢键。
	/// </summary>
	private sealed class DraftConfig
	{
		public DraftConfig(RoaringConfig source)
		{
			EnergyPerTurn = source.EnergyPerTurn;
			MaxCardsPerTurn = source.MaxCardsPerTurn;
			Order = source.Order;
			AutoEndTurn = source.AutoEndTurn;
			StartWithRelic = source.StartWithRelic;
		}

		public int EnergyPerTurn { get; set; }

		public int MaxCardsPerTurn { get; set; }

		public PlayOrder Order { get; set; }

		public bool AutoEndTurn { get; set; }

		public bool StartWithRelic { get; set; }

		public RoaringConfig ToConfig()
		{
			return new RoaringConfig
			{
				EnergyPerTurn = EnergyPerTurn,
				MaxCardsPerTurn = MaxCardsPerTurn,
				Order = Order,
				AutoEndTurn = AutoEndTurn,
				StartWithRelic = StartWithRelic
			};
		}
	}
}
