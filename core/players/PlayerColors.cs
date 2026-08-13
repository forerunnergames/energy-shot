using Godot;

namespace com.forerunnergames.energyshot.players;

// Selectable body colors (issue #43): a small, distinct, colorblind-friendly palette
// (Okabe-Ito based) that every peer resolves by replicated index. White is excluded
// on purpose - it's exclusively the spawn-armor indicator (issue #114).
public static class PlayerColors
{
  private static readonly string[] Names = { "Blue", "Orange", "Sky Blue", "Green", "Yellow", "Vermilion", "Pink", "Purple", "Teal" };

  private static readonly Color[] Palette =
  {
    new("0027ff"), // Blue: the original default body color.
    new("e69f00"),
    new("56b4e9"),
    new("009e73"),
    new("f0e442"),
    new("d55e00"),
    new("cc79a7"),
    new("8f45c9"),
    new("00bfc4")
  };

  public static int Count => Palette.Length;
  // Out-of-range (e.g. a stale saved setting after a palette change, or a spoofed
  // value) falls back to the default blue instead of crashing every peer.
  public static Color At (int index) => index >= 0 && index < Palette.Length ? Palette[index] : Palette[0];
  // Leaderboard name tint (issue #43): lightened so dark palette entries stay readable on the HUD.
  public static string TextHex (int index) => At (index).Lerp (Colors.White, 0.35f).ToHtml (includeAlpha: false);
  public static int Clamp (int index) => Mathf.Clamp (index, 0, Palette.Length - 1);
  // Fills a host/join dialog dropdown with one swatch + name entry per palette color.
  public static void Populate (OptionButton button)
  {
    for (var i = 0; i < Palette.Length; ++i) button.AddIconItem (Swatch (i), Names[i], i);
  }

  private static Texture2D Swatch (int index)
  {
    var image = Image.CreateEmpty (64, 64, false, Image.Format.Rgb8);
    image.Fill (At (index));
    return ImageTexture.CreateFromImage (image);
  }
}
