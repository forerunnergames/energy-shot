using com.forerunnergames.energyshot.weapons;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// Dart stashes (Aaron, 2026-08-24): every spawn point holds a cluster, & the level's
// dart count divides evenly into them so no lonely singles are left over.
[TestSuite]
public class DartClusterTest
{
  [TestCase]
  public void DartsSpawnInThrees()
  {
    var spawner = AutoFree (new WeaponSpawner())!;
    AssertInt (spawner.DartsPerCluster).IsEqual (3);
  }

  [TestCase]
  public void TheLevelHoldsWholeClusters()
  {
    var spawner = AutoFree (new WeaponSpawner())!;
    AssertInt (spawner.MaxDarts % spawner.DartsPerCluster).IsEqual (0);
    AssertInt (spawner.MaxDarts / spawner.DartsPerCluster).IsGreaterEqual (3); // Several stashes to hunt, not one.
  }
}
