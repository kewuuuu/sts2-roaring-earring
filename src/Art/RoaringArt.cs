using System;
using System.Collections.Concurrent;
using System.IO;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace Sts2RoaringEarring.Art;

/// <summary>
/// A DLL-only mod has no res:// asset pipeline (manifest has_pck:false), so the three relic PNGs are embedded
/// in this assembly (sts2-roaring-earring.csproj) and decoded the first time a getter asks for them. Decoding
/// stays on the calling thread: the relic getters already run on the main thread, which is where Godot allows
/// rendering resources to be created. Every failure returns null, which the patches read as "keep whatever the
/// vanilla getter produced".
/// </summary>
internal static class RoaringArt
{
	private const string ResourcePrefix = "Sts2RoaringEarring.Art.";

	private const string IconPng = "roaring_earring_icon.png";

	private const string IconOutlinePng = "roaring_earring_icon_outline.png";

	private const string BigIconPng = "roaring_earring_big.png";

	/// <summary>Successful decodes only, keyed per relic id + variant; failures retry rather than poison the cache.</summary>
	private static readonly ConcurrentDictionary<string, Texture2D> Cache = new ConcurrentDictionary<string, Texture2D>(StringComparer.Ordinal);

	private static readonly ConcurrentDictionary<string, byte> Reported = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);

	internal static Texture2D? Icon(RelicModel relic)
	{
		return Get(relic, "icon", IconPng);
	}

	internal static Texture2D? IconOutline(RelicModel relic)
	{
		return Get(relic, "outline", IconOutlinePng);
	}

	internal static Texture2D? BigIcon(RelicModel relic)
	{
		return Get(relic, "big", BigIconPng);
	}

	private static Texture2D? Get(RelicModel relic, string variant, string pngName)
	{
		string key;
		try
		{
			key = relic.Id.Entry + "/" + variant;
		}
		catch (Exception exception)
		{
			Report("key/" + variant, exception);
			return null;
		}
		if (Cache.TryGetValue(key, out Texture2D? cached))
		{
			return cached;
		}
		try
		{
			Texture2D? texture = Decode(pngName);
			if (texture == null)
			{
				return null;
			}
			Cache[key] = texture;
			Log.Info("[sts2-roaring-earring] relic art loaded (" + key + ") "
				+ texture.GetWidth() + "x" + texture.GetHeight());
			return texture;
		}
		catch (Exception exception)
		{
			Report(pngName, exception);
			return null;
		}
	}

	private static Texture2D? Decode(string pngName)
	{
		string? resourceName = ResolveResourceName(pngName);
		if (resourceName == null)
		{
			Report("missing/" + pngName, null);
			return null;
		}
		byte[] png;
		using (Stream? stream = typeof(RoaringArt).Assembly.GetManifestResourceStream(resourceName))
		{
			if (stream == null)
			{
				Report("stream/" + pngName, null);
				return null;
			}
			using MemoryStream buffer = new MemoryStream();
			stream.CopyTo(buffer);
			png = buffer.ToArray();
		}
		// GodotSharp binds load_png_from_buffer as an instance method that decodes into the receiver and
		// returns Godot.Error (CS0120 without a receiver, CS0029 when treated as returning an Image).
		using Image image = new Image();
		if (image.LoadPngFromBuffer(png) != Error.Ok || image.GetWidth() <= 0 || image.GetHeight() <= 0)
		{
			Report("decode/" + pngName, null);
			return null;
		}
		return ImageTexture.CreateFromImage(image);
	}

	/// <summary>Exact logical name first, then a suffix match, so a csproj rename degrades to a log line instead of silent placeholder art.</summary>
	private static string? ResolveResourceName(string pngName)
	{
		string expected = ResourcePrefix + pngName;
		string[] names = typeof(RoaringArt).Assembly.GetManifestResourceNames();
		foreach (string name in names)
		{
			if (string.Equals(name, expected, StringComparison.Ordinal))
			{
				return name;
			}
		}
		foreach (string name in names)
		{
			if (string.Equals(name, pngName, StringComparison.Ordinal) || name.EndsWith("." + pngName, StringComparison.Ordinal))
			{
				return name;
			}
		}
		return null;
	}

	private static void Report(string key, Exception? exception)
	{
		if (!Reported.TryAdd(key, 0))
		{
			return;
		}
		Log.Error("[sts2-roaring-earring] relic art unavailable (" + key + "), keeping the vanilla icon"
			+ (exception == null ? "." : ": " + exception.Message));
	}
}
