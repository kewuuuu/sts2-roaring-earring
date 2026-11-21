using System;
using System.IO;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2RoaringEarring.Config;

/// <summary>
/// Locates and loads sts2-roaring-earring.cfg (JSON inside; only the extension changed). Never throws: an
/// unreadable or missing file yields <see cref="RoaringConfig.Default"/>.
/// </summary>
public static class RoaringConfigStore
{
	private const string FileName = "sts2-roaring-earring.cfg";

	private static RoaringConfig _current = RoaringConfig.Default;

	public static RoaringConfig Current => _current;

	/// <summary>The values the relic actually uses: <see cref="Current"/> with every clamp applied.</summary>
	public static RoaringConfig Effective => _current.Normalized();

	public static RoaringConfig Load()
	{
		string path = ResolveConfigPath();
		if (path.Length > 0)
		{
			try
			{
				if (File.Exists(path))
				{
					_current = RoaringConfig.Parse(File.ReadAllText(path)).Normalized();
					Log.Info("[sts2-roaring-earring] config loaded from " + path);
					return _current;
				}
			}
			catch (Exception exception)
			{
				Log.Warn("[sts2-roaring-earring] could not read config at " + path + ": " + exception.Message);
			}
		}
		_current = RoaringConfig.Default;
		Log.Info("[sts2-roaring-earring] no config file found, using defaults");
		return _current;
	}

	/// <summary>Writes the whole file back and re-parses it, so memory and the next Load() agree. Never throws.</summary>
	public static bool Save(RoaringConfig config)
	{
		string json = Serialize(config.Normalized());
		_current = RoaringConfig.Parse(json);
		string path = ResolveConfigPath();
		if (path.Length == 0)
		{
			Log.Warn("[sts2-roaring-earring] no config path available, settings are kept in memory only");
			return false;
		}
		try
		{
			string directory = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
			if (directory.Length > 0)
			{
				Directory.CreateDirectory(directory);
			}
			File.WriteAllText(path, json);
			Log.Info("[sts2-roaring-earring] config saved to " + path);
			return true;
		}
		catch (Exception exception)
		{
			Log.Warn("[sts2-roaring-earring] could not write config at " + path + ": " + exception.Message);
			return false;
		}
	}

	/// <summary>The only path read or written: &lt;game executable directory&gt;/mods/config/sts2-roaring-earring.cfg.</summary>
	private static string ResolveConfigPath()
	{
		try
		{
			string directory = Path.GetDirectoryName(Godot.OS.GetExecutablePath()) ?? string.Empty;
			if (directory.Length > 0)
			{
				return Path.Combine(directory, "mods", "config", FileName);
			}
			Log.Warn("[sts2-roaring-earring] the executable directory is unknown; no config file can be read or written");
		}
		catch (Exception exception)
		{
			Log.Warn("[sts2-roaring-earring] could not resolve the executable directory: " + exception.Message);
		}
		return string.Empty;
	}

	private static string Serialize(RoaringConfig config)
	{
		using (MemoryStream stream = new MemoryStream())
		{
			using (Utf8JsonWriter writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
			{
				writer.WriteStartObject();
				writer.WriteNumber("energy_per_turn", config.EnergyPerTurn);
				writer.WriteNumber("max_cards_per_turn", config.MaxCardsPerTurn);
				writer.WriteNumber("play_delay_ms", config.PlayDelayMs);
				writer.WriteString("play_order", RoaringConfig.PlayOrderToken(config.Order));
				writer.WriteBoolean("auto_end_turn", config.AutoEndTurn);
				writer.WriteBoolean("start_with_relic", config.StartWithRelic);
				writer.WriteEndObject();
			}
			return Encoding.UTF8.GetString(stream.ToArray());
		}
	}
}
