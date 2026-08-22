using com.forerunnergames.energyshot.players;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// The drunk walk (issue #261): no sway without darts, more sway with more of them,
// & a ceiling so a heavily poisoned player can still steer a little.
[TestSuite]
public class PoisonFeelTest
{
  [TestCase]
  public void NoDartsNoSway() => AssertFloat (Player.WobbleAngle (0.3f, 0, 0.25f, 0.9f)).IsEqual (0.0f);

  [TestCase]
  public void MoreDartsMoreSway()
  {
    var one = Mathf.Abs (Player.WobbleAngle (0.3f, 1, 0.25f, 0.9f));
    var two = Mathf.Abs (Player.WobbleAngle (0.3f, 2, 0.25f, 0.9f));
    AssertFloat (one).IsGreater (0.0f);
    AssertFloat (two).IsEqual (one * 2.0f);
  }

  [TestCase]
  public void SwayCapsAtFourDarts() => AssertFloat (Player.WobbleAngle (0.3f, 9, 0.25f, 0.9f)).IsEqual (Player.WobbleAngle (0.3f, 4, 0.25f, 0.9f));

  // Issue #277: any embedded dart flips the wheel entirely; a clean body steers true.
  [TestCase]
  public void PoisonInvertsSteering()
  {
    AssertObject (Player.PoisonSteer (new Vector2 (1.0f, -0.5f), 1)).IsEqual (new Vector2 (-1.0f, 0.5f));
    AssertObject (Player.PoisonSteer (new Vector2 (1.0f, -0.5f), 3)).IsEqual (new Vector2 (-1.0f, 0.5f));
    AssertObject (Player.PoisonSteer (new Vector2 (1.0f, -0.5f), 0)).IsEqual (new Vector2 (1.0f, -0.5f));
  }
}
