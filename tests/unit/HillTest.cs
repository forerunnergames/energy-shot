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

  [TestCase]
  public void HillSitsOnTheBananaPlatform() => AssertFloat (Hill.Spot.Y).IsGreater (20.0f); // Issue #239: a hard-to-reach platform, not open floor.

  [TestCase]
  public void ScoreboardColumnNamesTheMode()
  {
    AssertString (Match.ScoreColumnLabel (GameMode.Zaps)).IsEqual ("Zaps");
    AssertString (Match.ScoreColumnLabel (GameMode.KingOfTheHill)).IsEqual ("Hill pts");
  }
}
