using com.forerunnergames.energyshot.ui.menus;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// Jonathan's main-menu design constants (issue #436): the QUIT parallelogram is his
// SVG path doubled into the 4K design space, & the stroke is the design's cyan.
[TestSuite]
public class MainMenuStyleTest
{
  [TestCase]
  public void QuitOutlineIsAClosedParallelogram()
  {
    AssertInt (TrapezoidButton.OutlinePoints.Length).IsEqual (5); // Four corners + the closing repeat.
    AssertBool (TrapezoidButton.OutlinePoints[0] == TrapezoidButton.OutlinePoints[^1]).IsTrue();
  }

  [TestCase]
  public void QuitStrokeIsTheDesignCyan()
  {
    var button = AutoFree (new TrapezoidButton())!;
    AssertBool (button.StrokeColor.ToHtml (false).ToUpper() == "94FCFE").IsTrue(); // display-p3 (0.580, 0.987, 0.994) mapped to sRGB.
  }

  // The filled-primary variant (issue #443): a fresh button stays the outline family
  // by default, & the canon disabled gray is pinned.
  [TestCase]
  public void DefaultButtonIsUnfilledWithQuitGeometry()
  {
    var button = AutoFree (new TrapezoidButton())!;
    AssertBool (button.Filled).IsFalse();
    AssertBool (button.Points == TrapezoidButton.OutlinePoints).IsTrue();
    AssertBool (TrapezoidButton.DisabledGray.ToHtml (false).ToUpper() == "888888").IsTrue();
  }
}
