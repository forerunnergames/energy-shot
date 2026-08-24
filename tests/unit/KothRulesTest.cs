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
      for (var roll = 0; roll < 50; ++roll)
        AssertInt (Match.NextSpotIndex (current, Hill.Spots.Length, roll)).IsNotEqual (current);
  }

  [TestCase]
  public void RotationStaysInsideThePool()
  {
    for (var roll = 0; roll < 50; ++roll)
    {
      var next = Match.NextSpotIndex (0, Hill.Spots.Length, roll);
      AssertInt (next).IsGreaterEqual (0);
      AssertInt (next).IsLess (Hill.Spots.Length);
    }
  }

  [TestCase]
  public void ASingleSpotPoolCannotRotate() => AssertInt (Match.NextSpotIndex (0, 1, 7)).IsEqual (0);
}
