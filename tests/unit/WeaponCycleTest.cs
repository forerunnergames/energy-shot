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
  // Laser & bread held, banana/boomerang/slingshot/airplane slots empty.
  private static List <SelectedWeapon> Carried() =>
    new() { SelectedWeapon.Fists, SelectedWeapon.Laser, SelectedWeapon.Bread };

  [TestCase]
  public void ForwardStepsInSlotOrderSkippingEmptySlots()
  {
    // Laser -> Bread skips the four empty slots between them.
    AssertObject (Player.NextCycleSlot (Carried(), SelectedWeapon.Laser, 1)).IsEqual (SelectedWeapon.Bread);
  }

  [TestCase]
  public void ForwardWrapsFromLastSlotToFists()
  {
    AssertObject (Player.NextCycleSlot (Carried(), SelectedWeapon.Bread, 1)).IsEqual (SelectedWeapon.Fists);
  }

  [TestCase]
  public void ReverseWrapsFromFistsToLastSlot()
  {
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
      SelectedWeapon.Fists, SelectedWeapon.Laser, SelectedWeapon.Banana, SelectedWeapon.Boomerang,
      SelectedWeapon.Slingshot, SelectedWeapon.PaperAirplane, SelectedWeapon.Bread
    };
    var current = SelectedWeapon.Fists;
    var visited = new List <SelectedWeapon>();
    for (var i = 0; i < all.Count; ++i) { current = Player.NextCycleSlot (all, current, 1); visited.Add (current); }
    AssertObject (current).IsEqual (SelectedWeapon.Fists); // Full lap wraps home.
    AssertInt (visited.Count).IsEqual (7);
  }
}
