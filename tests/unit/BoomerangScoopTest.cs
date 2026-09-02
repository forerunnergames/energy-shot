using com.forerunnergames.energyshot.players;
using com.forerunnergames.energyshot.weapons;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// The boomerang scoops everything (issue #246): armed hazards ride home & bite the
// catcher, & the clip-steal takes the equipped loaf too.
[TestSuite]
public class BoomerangScoopTest
{
  [TestCase]
  public void OnlyArmedAirplanesAndDartsAreLiveTrouble()
  {
    AssertBool (WeaponSpawner.IsLiveHazard (HeldWeapon.PaperAirplane, armed: true)).IsTrue();
    AssertBool (WeaponSpawner.IsLiveHazard (HeldWeapon.PoisonDart, armed: true)).IsTrue();
    AssertBool (WeaponSpawner.IsLiveHazard (HeldWeapon.PaperAirplane, armed: false)).IsFalse(); // A resting airplane is ordinary cargo.
    AssertBool (WeaponSpawner.IsLiveHazard (HeldWeapon.Laser, armed: true)).IsFalse();
  }

  [TestCase]
  public void TheStealTakesWhatIsInHandAndFallsBackToTheLoaf()
  {
    AssertBool (Player.BoomerangLoot (HeldWeapon.Laser, breadSelected: true, hasBread: true) == HeldWeapon.Bread).IsTrue(); // The selected loaf is the equipped item.
    AssertBool (Player.BoomerangLoot (HeldWeapon.Laser, breadSelected: false, hasBread: true) == HeldWeapon.Laser).IsTrue();
    AssertBool (Player.BoomerangLoot (HeldWeapon.None, breadSelected: false, hasBread: true) == HeldWeapon.Bread).IsTrue();
    AssertBool (Player.BoomerangLoot (HeldWeapon.None, breadSelected: false, hasBread: false) == HeldWeapon.None).IsTrue();
  }
}
