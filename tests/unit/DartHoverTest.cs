using com.forerunnergames.energyshot.weapons;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// Issue #278: a spawned dart floats clear of the floor; a landed (armed) one lies
// flat as a hazard; every other pickup keeps its usual hover.
[TestSuite]
public class DartHoverTest
{
  [TestCase]
  public void SpawnedDartHoversClearOfTheFloor()
  {
    AssertFloat (WeaponPickup.HoverBaseline (HeldWeapon.PoisonDart, armed: false)).IsGreater (0.25f); // Clearly floating...
    AssertFloat (WeaponPickup.HoverBaseline (HeldWeapon.PoisonDart, armed: false)).IsLess (0.5f); // ...never moon-walking (Aaron: don't overcorrect).
  }

  [TestCase]
  public void ArmedDartGetsNoHover() => AssertFloat (WeaponPickup.HoverBaseline (HeldWeapon.PoisonDart, armed: true)).IsEqual (0.0f);

  [TestCase]
  public void OtherPickupsAreUnchanged() => AssertFloat (WeaponPickup.HoverBaseline (HeldWeapon.Laser, armed: false)).IsEqual (0.0f);
}
