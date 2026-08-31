using Godot;

namespace com.forerunnergames.energyshot.players;

// Selectable body colors (issue #43): a small, distinct, colorblind-friendly palette
// (Okabe-Ito based) that every peer resolves by replicated index. White is excluded
// on purpose - it's exclusively the spawn-armor indicator (issue #114).
public static class PlayerColors
{
  // APPEND-ONLY (the replication rule's spirit): saved & replicated indices must
  // keep meaning the same color forever. Eight more distinct picks (Jacob, 2026-08-23).
  private static readonly string[] Names = { "Blue", "Orange", "Sky Blue", "Green", "Yellow", "Vermilion", "Pink", "Purple", "Teal", "Red", "Magenta", "Lime", "Brown", "Navy", "Charcoal", "Mint", "Gold" };

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
    new("00bfc4"),
    new("b22222"), // Red: true crimson, well clear of Vermilion's orange lean.
    new("ff00cc"),
    new("8bd346"),
    new("8b5a2b"),
    new("16216e"),
    new("3d3d3d"),
    new("9ff2c8"),
    new("c9a227")
  };

  public static int Count => Palette.Length;
  // Out-of-range (e.g. a stale saved setting after a palette change, or a spoofed
  // replicated value) always lands on the default blue - never some other entry -
  // so every consumer (body tint, dialogs, leaderboard) agrees on the fallback.
  public static int NormalizeIndex (int index) => index >= 0 && index < Palette.Length ? index : 0;
  public static Color At (int index) => Palette[NormalizeIndex (index)];
  // Leaderboard name tint (issue #43): lightened so dark palette entries stay readable on the HUD.
  public static string TextHex (int index) => At (index).Lerp (Colors.White, 0.35f).ToHtml (includeAlpha: false);
  // Fills a host/join dialog dropdown with one swatch + name entry per palette color.
  public static void Populate (OptionButton button)
  {
    for (var i = 0; i < Palette.Length; ++i) button.AddIconItem (Swatch (i), Names[i], i);
  }

  private static Texture2D Swatch (int index) => ImageTexture.CreateFromImage (SwatchImage (index));

  // The canon UI-elements sheet's swatch (issue #443): a 31px circle (x2 for 4K) with
  // dual inset shadows - dark along the top-left inner rim, cyan along the bottom-right.
  // CPU-side Image so it stays unit-testable without a rendering server.
  public static Image SwatchImage (int index)
  {
    const int size = 62;
    const float radius = size / 2.0f;
    var inset = new Vector2 (6.0f, 6.0f); // The sheet's 3px shadow offsets, x2.
    var rimCyan = new Color (0.0f, 0.85f, 0.97f); // canon display-p3 (0 0.832 0.961) ~ sRGB.
    var center = new Vector2 (radius, radius);
    var fill = At (index);
    var image = Image.CreateEmpty (size, size, false, Image.Format.Rgba8);

    for (var y = 0; y < size; ++y)
    for (var x = 0; x < size; ++x)
    {
      var p = new Vector2 (x + 0.5f, y + 0.5f);
      if (p.DistanceTo (center) > radius) continue;
      var color = fill;
      if ((p - inset).DistanceTo (center) > radius) color = fill.Lerp (Colors.Black, 0.6f);
      else if ((p + inset).DistanceTo (center) > radius) color = fill.Lerp (rimCyan, 0.75f);
      image.SetPixel (x, y, color);
    }

    return image;
  }
}
