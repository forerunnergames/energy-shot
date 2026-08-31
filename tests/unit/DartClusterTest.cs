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
  public void DartsSpawnInTens()
  {
    var spawner = AutoFree (new WeaponSpawner())!;
    AssertInt (spawner.DartsPerCluster).IsEqual (10); // Stashes of 10 (Aaron, 2026-08-28, issue #421).
    AssertInt (spawner.DartsPerGunPreload).IsEqual (10); // A fresh blowgun ships loaded (issue #421).
  }

  [TestCase]
  public void TheLevelHoldsWholeClusters()
  {
    var spawner = AutoFree (new WeaponSpawner())!;
    AssertInt (spawner.MaxDarts).IsEqual (60); // The exact contract (issue #421): room for a loaded gun...
    AssertInt (spawner.MaxDarts / spawner.DartsPerCluster).IsEqual (6); // ...& whole stashes of 10.
    AssertInt (spawner.MaxDarts % spawner.DartsPerCluster).IsEqual (0);
  }
}
