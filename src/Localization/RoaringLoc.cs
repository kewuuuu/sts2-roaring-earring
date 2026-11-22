using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2RoaringEarring.Localization;

/// <summary>
/// Injects our localization entries into the game's existing tables at runtime.
/// A pck-bundled loc table is not available to a DLL-only mod (ModManager.GetModdedLocTables reads
/// res://&lt;id&gt;/localization/..., ModManager.cs:1118-1123), so the entries are merged into the live
/// LocTable instead, and re-merged after every locale change.
/// </summary>
public static class RoaringLoc
{
	public const string RelicEntry = "ROARING_EARRING";

	/// <summary>Called after LocManager.Initialize and again after every locale change.</summary>
	public static void InjectAll()
	{
		LocManager? manager = LocManager.Instance;
		if (manager == null)
		{
			Log.Warn("[sts2-roaring-earring] locale injection skipped: LocManager is not initialized yet");
			return;
		}
		Inject(manager, "relics", RelicEntries(IsChinese(manager)));
	}

	/// <summary>The tables are rebuilt per locale, so the text is chosen when it is merged, not once at startup.</summary>
	private static bool IsChinese(LocManager manager)
	{
		try
		{
			return manager.CultureInfo.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
		}
		catch (Exception exception)
		{
			Log.Warn("[sts2-roaring-earring] could not read the active locale, using the Chinese text: " + exception.Message);
			return true;
		}
	}

	private static void Inject(LocManager manager, string table, IEnumerable<KeyValuePair<string, string>> entries)
	{
		try
		{
			manager.GetTable(table).MergeWith(new Dictionary<string, string>(entries, StringComparer.Ordinal));
		}
		catch (Exception exception)
		{
			Log.Warn("[sts2-roaring-earring] localization table '" + table + "' not injected: " + exception.Message);
		}
	}

	private static List<KeyValuePair<string, string>> RelicEntries(bool chinese)
	{
		List<KeyValuePair<string, string>> entries = new List<KeyValuePair<string, string>>();
		Add(entries, "title", chinese ? "狂吼耳环" : "Roaring Earring");
		Add(entries, "description", chinese
			? "每回合开始时获得额外能量。瓦库会接管你的所有回合，自动替你打出卡牌。左键点击遗物可切换自动出牌：关闭后不再获得每回合能量，也不再接管回合，下一回合生效。当前：{AutoPlay}。"
			: "Gain extra Energy each turn. Vakuu takes over every turn and plays your cards for you. Left-click the relic to toggle auto-play: when it is off you gain no extra Energy and Vakuu no longer takes over, starting from the next turn. Now: {AutoPlay}.");
		Add(entries, "flavor", chinese
			? "瓦库的低语从不需要你亲自回应。"
			: "Vakuu's whisper never needed your reply.");
		return entries;
	}

	/// <summary>
	/// Model ids are upper-cased by ModelDb.GetEntry (StringHelper.Slugify), but the key case of the shipped
	/// tables could not be verified from the pck, so every case variant is provided.
	/// </summary>
	private static void Add(List<KeyValuePair<string, string>> entries, string suffix, string text)
	{
		foreach (string entry in new[] { RelicEntry, RelicEntry.ToUpperInvariant(), RelicEntry.ToLowerInvariant() })
		{
			string key = entry + "." + suffix;
			if (entries.Exists(pair => pair.Key == key))
			{
				continue;
			}
			entries.Add(new KeyValuePair<string, string>(key, text));
		}
	}
}
