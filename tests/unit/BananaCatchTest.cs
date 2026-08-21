using com.forerunnergames.energyshot.weapons;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// The caught banana (issue #251) is ammo vocabulary, never a slot or a theft target.
[TestSuite]
public class BananaCatchTest
{
  [TestCase]
  public void GrenadeIsAFreshFlag() => AssertInt ((int)HeldWeapon.BananaGrenade).IsEqual (512);

  [TestCase]
  public void GrenadeIsNeverStealable() => AssertObject (WeaponSpawner.FirstStealableFlag (HeldWeapon.BananaGrenade)).IsEqual (HeldWeapon.None);

  [TestCase]
  public void GrenadeDoesNotSpray() => AssertBool (SlingshotStone.Sprays (HeldWeapon.BananaGrenade)).IsFalse();
}
