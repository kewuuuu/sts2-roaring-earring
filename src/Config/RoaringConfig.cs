using System;
using System.Globalization;
using System.Text.Json;

namespace Sts2RoaringEarring.Config;

/// <summary>How the relic picks the next card out of the owner's hand.</summary>
public enum PlayOrder
{
	LeftToRight,
	RightToLeft,
	Random,
}

/// <summary>
/// The JSON payload of mods/config/sts2-roaring-earring.cfg. Parsing never throws: a malformed file falls
/// back to <see cref="Default"/>, and <see cref="Normalized"/> clamps whatever a hand-edited file asked for.
/// </summary>
public sealed class RoaringConfig
{
	public const int EnergyPerTurnMin = 0;

	public const int EnergyPerTurnMax = 9;

	public const int MaxCardsPerTurnMin = 1;

	public const int MaxCardsPerTurnMax = 100;

	public const int PlayDelayMsMin = 0;

	public const int PlayDelayMsMax = 600;

	public const int AutoPlayAnimSpeedMin = 1;

	public const int AutoPlayAnimSpeedMax = 3;

	public const int DefaultEnergyPerTurn = 1;

	public const int DefaultMaxCardsPerTurn = 50;

	public const PlayOrder DefaultOrder = PlayOrder.LeftToRight;

	public const bool DefaultAutoEndTurn = false;

	public const bool DefaultStartWithRelic = true;

	public const int DefaultPlayDelayMs = 0;

	public const bool DefaultSkipPlayAnimation = true;

	public const int DefaultAutoPlayAnimSpeed = 1;

	/// <summary>Extra max energy per turn, added directly by the relic's ModifyMaxEnergy override.</summary>
	public int EnergyPerTurn { get; init; } = DefaultEnergyPerTurn;

	/// <summary>Upper bound of cards the relic may auto-play within one turn.</summary>
	public int MaxCardsPerTurn { get; init; } = DefaultMaxCardsPerTurn;

	/// <summary>Extra pause between two auto-played cards, in milliseconds. 0 keeps the engine's own pace.</summary>
	public int PlayDelayMs { get; init; } = DefaultPlayDelayMs;

	/// <summary>left_to_right | right_to_left | random.</summary>
	public PlayOrder Order { get; init; } = DefaultOrder;

	/// <summary>End the turn when the loop stopped because the hand ran out or the cap was reached.</summary>
	public bool AutoEndTurn { get; init; } = DefaultAutoEndTurn;

	/// <summary>Hand the relic out at the start of a new run.</summary>
	public bool StartWithRelic { get; init; } = DefaultStartWithRelic;

	/// <summary>Skip the card-pile tween and the engine's 0.25-0.35s wait around an auto-played card. Cosmetic only.</summary>
	public bool SkipPlayAnimation { get; init; } = DefaultSkipPlayAnimation;

	/// <summary>Spine time-scale multiplier used only while the relic's auto-play loop runs; 1 leaves the scale alone.</summary>
	public int AutoPlayAnimSpeed { get; init; } = DefaultAutoPlayAnimSpeed;

	public static RoaringConfig Default => new RoaringConfig();

	public RoaringConfig Normalized()
	{
		return new RoaringConfig
		{
			EnergyPerTurn = Math.Clamp(EnergyPerTurn, EnergyPerTurnMin, EnergyPerTurnMax),
			MaxCardsPerTurn = Math.Clamp(MaxCardsPerTurn, MaxCardsPerTurnMin, MaxCardsPerTurnMax),
			PlayDelayMs = Math.Clamp(PlayDelayMs, PlayDelayMsMin, PlayDelayMsMax),
			Order = IsKnownOrder(Order) ? Order : DefaultOrder,
			AutoEndTurn = AutoEndTurn,
			StartWithRelic = StartWithRelic,
			SkipPlayAnimation = SkipPlayAnimation,
			AutoPlayAnimSpeed = Math.Clamp(AutoPlayAnimSpeed, AutoPlayAnimSpeedMin, AutoPlayAnimSpeedMax),
		};
	}

	public static bool IsKnownOrder(PlayOrder order)
	{
		return order is PlayOrder.LeftToRight or PlayOrder.RightToLeft or PlayOrder.Random;
	}

	/// <summary>The spelling used in the config file and in the startup log line.</summary>
	public static string PlayOrderToken(PlayOrder order)
	{
		return order switch
		{
			PlayOrder.RightToLeft => "right_to_left",
			PlayOrder.Random => "random",
			_ => "left_to_right",
		};
	}

	public static RoaringConfig Parse(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return Default;
		}
		try
		{
			using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
			{
				AllowTrailingCommas = true,
				CommentHandling = JsonCommentHandling.Skip,
			});
			if (document.RootElement.ValueKind != JsonValueKind.Object)
			{
				return Default;
			}
			RoaringConfig defaults = Default;
			return new RoaringConfig
			{
				EnergyPerTurn = ReadInt(document.RootElement, "energy_per_turn", defaults.EnergyPerTurn),
				MaxCardsPerTurn = ReadInt(document.RootElement, "max_cards_per_turn", defaults.MaxCardsPerTurn),
				PlayDelayMs = ReadInt(document.RootElement, "play_delay_ms", defaults.PlayDelayMs),
				Order = ParseOrder(ReadString(document.RootElement, "play_order"), defaults.Order),
				AutoEndTurn = ReadBool(document.RootElement, "auto_end_turn", defaults.AutoEndTurn),
				StartWithRelic = ReadBool(document.RootElement, "start_with_relic", defaults.StartWithRelic),
				SkipPlayAnimation = ReadBool(document.RootElement, "skip_play_animation", defaults.SkipPlayAnimation),
				AutoPlayAnimSpeed = ReadInt(document.RootElement, "auto_play_anim_speed", defaults.AutoPlayAnimSpeed),
			}.Normalized();
		}
		catch (JsonException)
		{
			return Default;
		}
	}

	private static PlayOrder ParseOrder(string? raw, PlayOrder fallback)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return fallback;
		}
		string token = raw.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
		return token switch
		{
			"left_to_right" or "lefttoright" or "ltr" => PlayOrder.LeftToRight,
			"right_to_left" or "righttoleft" or "rtl" => PlayOrder.RightToLeft,
			"random" => PlayOrder.Random,
			_ => fallback,
		};
	}

	private static bool TryFind(JsonElement root, string key, out JsonElement value)
	{
		foreach (JsonProperty property in root.EnumerateObject())
		{
			if (string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase))
			{
				value = property.Value;
				return true;
			}
		}
		value = default(JsonElement);
		return false;
	}

	private static int ReadInt(JsonElement root, string key, int fallback)
	{
		if (!TryFind(root, key, out JsonElement value))
		{
			return fallback;
		}
		if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
		{
			return number;
		}
		if (value.ValueKind == JsonValueKind.String
			&& int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
		{
			return parsed;
		}
		return fallback;
	}

	private static bool ReadBool(JsonElement root, string key, bool fallback)
	{
		if (!TryFind(root, key, out JsonElement value))
		{
			return fallback;
		}
		return value.ValueKind switch
		{
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.String => bool.TryParse(value.GetString(), out bool parsed) ? parsed : fallback,
			_ => fallback,
		};
	}

	private static string? ReadString(JsonElement root, string key)
	{
		return TryFind(root, key, out JsonElement value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
	}
}
