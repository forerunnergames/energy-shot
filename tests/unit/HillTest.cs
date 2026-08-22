using com.forerunnergames.energyshot.core.world;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// King of the Hill (issue #44): the occupancy test is the whole scoring rule, so its
// edges are pinned - inside, the rim, outside, & hovering over it.
[TestSuite]
public class HillTest
{
  [TestCase]
  public void CenterAndRimCount()
  {
    AssertBool (Hill.Contains (Hill.Spot)).IsTrue();
    AssertBool (Hill.Contains (Hill.Spot + Vector3.Right * Hill.Radius)).IsTrue();
  }

  [TestCase]
  public void JustOutsideAndFlyingOverDoNot()
  {
    AssertBool (Hill.Contains (Hill.Spot + Vector3.Right * (Hill.Radius + 0.01f))).IsFalse();
    AssertBool (Hill.Contains (Hill.Spot + Vector3.Up * 10.0f)).IsFalse();
  }

  // Issue #239: the full placement contract - centered on the banana platform (World.tscn:
  // BananaPlatform at (0, 28, 11), a 6x6x0.5 slab, so its top is y 28.25) & a radius
  // that stays inside the slab's half-width, so standing on the platform IS the hill.
  [TestCase]
  public void HillSitsCenteredOnTheBananaPlatformTop()
  {
    AssertObject (Hill.Spot).IsEqual (new Vector3 (0.0f, 28.25f, 11.0f));
    AssertFloat (Hill.Radius).IsLess (3.0f);
    AssertFloat (Hill.Radius).IsGreater (2.0f); // Still a real ring, not a dot.
  }

  [TestCase]
  public void ScoreboardColumnNamesTheMode()
  {
    AssertString (Match.ScoreColumnLabel (GameMode.Zaps)).IsEqual ("Zaps");
    AssertString (Match.ScoreColumnLabel (GameMode.KingOfTheHill)).IsEqual ("Hill pts");
  }
}
