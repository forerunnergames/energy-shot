using com.forerunnergames.energyshot.players;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// The color-dropdown swatch from the canon UI-elements sheet (issue #443): a 62px
// circle (31px in the 1080 design, x2 for 4K) with dual inset rim shadows. CPU-side
// Image assertions - no rendering server involved.
[TestSuite]
public class PlayerColorsSwatchTest
{
  [TestCase]
  public void SwatchIsACircleOfThePaletteColor()
  {
    var image = PlayerColors.SwatchImage (0);
    AssertInt (image.GetWidth()).IsEqual (62);
    AssertInt (image.GetHeight()).IsEqual (62);
    AssertBool (image.GetPixel (31, 31) == PlayerColors.At (0)).IsTrue(); // Center holds the pure fill.
    AssertBool (image.GetPixel (0, 0).A == 0.0f).IsTrue(); // Corners stay transparent - it's a circle.
    AssertBool (image.GetPixel (61, 61).A == 0.0f).IsTrue();
  }

  [TestCase]
  public void SwatchRimCarriesTheInsetShadows()
  {
    var image = PlayerColors.SwatchImage (0);
    var fill = PlayerColors.At (0);
    AssertBool (image.GetPixel (10, 10) != fill).IsTrue(); // Top-left rim darkened.
    AssertBool (image.GetPixel (51, 51) != fill).IsTrue(); // Bottom-right rim cyan-lit.
  }
}
