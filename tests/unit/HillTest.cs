using com.forerunnergames.energyshot.core.world;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// The roaming hill (issues #44 & #294): four sky-platform spots, each a real trip
// from the spawn room - the spawn-adjacent banana platform is out of the pool.
[TestSuite]
public class HillTest
{
  [TestCase]
  public void EverySpotContainsItsOwnRingAndNotBeyondIt()
  {
    for (var i = 0; i < Hill.Spots.Length; ++i)
    {
      AssertBool (Hill.Contains (i, Hill.Spots[i])).IsTrue();
      AssertBool (Hill.Contains (i, Hill.Spots[i] + Vector3.Right * Hill.Radius)).IsTrue();
      AssertBool (Hill.Contains (i, Hill.Spots[i] + Vector3.Right * (Hill.Radius + 0.01f))).IsFalse();
      AssertBool (Hill.Contains (i, Hill.Spots[i] + Vector3.Up * 10.0f)).IsFalse();
    }
  }

  [TestCase]
  public void NoSpotSitsNextToTheSpawnRoom()
  {
    // The complaint that moved the hill (#294): the old banana-platform spot sat
    // 11m from the spawn room's column. Every pool spot keeps a real distance.
    foreach (var spot in Hill.Spots)
      AssertFloat (new Vector2 (spot.X, spot.Z).Length()).IsGreater (20.0f);
  }

  [TestCase]
  public void TheOldBananaPlatformSpotIsOutOfThePool()
  {
    foreach (var spot in Hill.Spots)
      AssertBool (spot.IsEqualApprox (new Vector3 (0.0f, 28.25f, 11.0f))).IsFalse();
  }

  [TestCase]
  public void AnOutOfRangeIndexClampsInsteadOfCrashing()
  {
    AssertBool (Hill.Contains (99, Hill.Spots[^1])).IsTrue();
    AssertBool (Hill.Contains (-1, Hill.Spots[0])).IsTrue();
  }
}
