using System.Collections.Generic;
using com.forerunnergames.energyshot.players;
using com.forerunnergames.energyshot.weapons;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// Boundary coverage for weapon cycling (issue #186, CodeRabbit on #219). The
// carried list is what SelectableSlots() builds: fists always, then only held
// slots in order - so "skipping empty slots" means absent entries here.
[TestSuite]
public class WeaponCycleTest
{
  // Bread & laser held (key order: 0 bread, 1 fists, 2 laser); the other slots empty.
  private static List <SelectedWeapon> Carried() =>
    new() { SelectedWeapon.Bread, SelectedWeapon.Fists, SelectedWeapon.Laser };

  [TestCase]
  public void ForwardStepsInSlotOrderSkippingEmptySlots()
  {
    // Laser -> Bread wraps past the six empty gun slots (issue #223: the loaf leads the wheel).
    AssertObject (Player.NextCycleSlot (Carried(), SelectedWeapon.Laser, 1)).IsEqual (SelectedWeapon.Bread);
  }

  [TestCase]
  public void ForwardFromBreadLandsOnFists()
  {
    AssertObject (Player.NextCycleSlot (Carried(), SelectedWeapon.Bread, 1)).IsEqual (SelectedWeapon.Fists);
  }

  [TestCase]
  public void ReverseFromFistsLandsOnBread()
  {
    // One notch back from fists is the loaf (issue #223).
    AssertObject (Player.NextCycleSlot (Carried(), SelectedWeapon.Fists, -1)).IsEqual (SelectedWeapon.Bread);
  }

  [TestCase]
  public void UnlistedCurrentSlotFallsBackToFists()
  {
    // The selected weapon just dropped out of the carried list mid-cycle.
    AssertObject (Player.NextCycleSlot (Carried(), SelectedWeapon.Banana, 1)).IsEqual (SelectedWeapon.Fists);
  }

  [TestCase]
  public void FullLoadoutForwardVisitsEverySlotOnce()
  {
    var all = new List <SelectedWeapon>
    {
      SelectedWeapon.Bread, SelectedWeapon.Fists, SelectedWeapon.Laser, SelectedWeapon.Banana, SelectedWeapon.Boomerang,
      SelectedWeapon.Slingshot, SelectedWeapon.PaperAirplane, SelectedWeapon.Blowgun
    };
    var current = SelectedWeapon.Bread;
    var visited = new List <SelectedWeapon>();
    for (var i = 0; i < all.Count; ++i) { current = Player.NextCycleSlot (all, current, 1); visited.Add (current); }
    AssertObject (current).IsEqual (SelectedWeapon.Bread); // Full lap wraps home.
    AssertInt (visited.Count).IsEqual (8); // Blowgun joined the wheel (issue #194).
  }
}
