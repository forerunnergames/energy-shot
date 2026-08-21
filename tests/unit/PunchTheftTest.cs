using com.forerunnergames.energyshot.weapons;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// Validation-reducer coverage for punch theft (issue #193): the server trusts only a
// single flag proven against the victim's replicated state, & unlike the boomerang's
// FirstFlag (#184/#190), a punch can take the airplane & the equipped loaf (#192).
[TestSuite]
public class PunchTheftTest
{
  [TestCase]
  public void ReducesForgedMultiFlagMaskToSingleFlag() => AssertObject (WeaponSpawner.FirstStealableFlag (HeldWeapon.Laser | HeldWeapon.Banana | HeldWeapon.Bread)).IsEqual (HeldWeapon.Laser);

  [TestCase]
  public void StealsEquippedBread() => AssertObject (WeaponSpawner.FirstStealableFlag (HeldWeapon.Bread)).IsEqual (HeldWeapon.Bread);

  [TestCase]
  public void StealsThePaperAirplane() => AssertObject (WeaponSpawner.FirstStealableFlag (HeldWeapon.PaperAirplane)).IsEqual (HeldWeapon.PaperAirplane);

  [TestCase]
  public void EmptyMaskStealsNothing() => AssertObject (WeaponSpawner.FirstStealableFlag (HeldWeapon.None)).IsEqual (HeldWeapon.None);

  [TestCase]
  public void BananaChunkIsNeverStealable() => AssertObject (WeaponSpawner.FirstStealableFlag (HeldWeapon.BananaChunk)).IsEqual (HeldWeapon.None);
}
