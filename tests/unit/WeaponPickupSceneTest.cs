using com.forerunnergames.energyshot.weapons;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// Smoke tests (issue #72): the weapon pickup scene must load & instantiate cleanly.
[TestSuite]
public partial class WeaponPickupSceneTest : Node
{
  [TestCase]
  public void WeaponPickupSceneInstantiates() => AssertObject (AutoFree (ResourceLoader.Load <PackedScene> ("res://core/weapons/WeaponPickup.tscn").Instantiate <WeaponPickup>())).IsNotNull();

  [TestCase]
  public void WeaponPickupDefaultsToLaser() => AssertBool (AutoFree (ResourceLoader.Load <PackedScene> ("res://core/weapons/WeaponPickup.tscn").Instantiate <WeaponPickup>())!.Weapon == HeldWeapon.Laser).IsTrue();
}
