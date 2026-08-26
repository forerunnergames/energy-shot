using com.forerunnergames.energyshot.core.world;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// KOTH rules (Aaron, 2026-08-24): 100 points to win & the hill rotates on its own
// clock. Both are runtime settings - these pin the DEFAULTS & the rotation math.
[TestSuite]
public class KothRulesTest
{
  [TestCase]
  public void KothDefaultsToAHundredPoints() => AssertInt (Match.DefaultPointLimit (GameMode.KingOfTheHill)).IsEqual (100);

  [TestCase]
  public void ZapsKeepsItsOwnDefault() => AssertInt (Match.DefaultPointLimit (GameMode.Zaps)).IsEqual (Match.DefaultZapLimit);

  [TestCase]
  public void TheHillRotatesEveryMinuteByDefault() => AssertInt (Match.HillRotateSeconds).IsEqual (60);

  [TestCase]
  public void RotationNeverLandsOnTheSameSpot()
  {
    for (var current = 0; current < Hill.Spots.Length; ++current)
      for (var roll = -50; roll < 50; ++roll) // Negative rolls too: GD.Randi cast to int is negative half the time (finding #2).
        AssertInt (Match.NextSpotIndex (current, Hill.Spots.Length, roll)).IsNotEqual (current);
  }

  [TestCase]
  public void RotationStaysInsideThePool()
  {
    for (var roll = -50; roll < 50; ++roll) // Negative rolls stay in-pool, never a negative index broadcast to peers (finding #2).
    {
      var next = Match.NextSpotIndex (0, Hill.Spots.Length, roll);
      AssertInt (next).IsGreaterEqual (0);
      AssertInt (next).IsLess (Hill.Spots.Length);
    }
  }

  [TestCase]
  public void ASingleSpotPoolCannotRotate() => AssertInt (Match.NextSpotIndex (0, 1, 7)).IsEqual (0);

  [TestCase]
  public void ANegativeRollStillRotatesForward()
  {
    // int.MinValue is the nastiest negative (its own negation overflows) - it must
    // still yield a valid, different spot, not a negative index (finding #2).
    var next = Match.NextSpotIndex (0, Hill.Spots.Length, int.MinValue);
    AssertInt (next).IsGreaterEqual (0);
    AssertInt (next).IsLess (Hill.Spots.Length);
    AssertInt (next).IsNotEqual (0);
  }
}
