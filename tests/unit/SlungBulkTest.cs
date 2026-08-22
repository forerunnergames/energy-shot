using com.forerunnergames.energyshot.players;
using com.forerunnergames.energyshot.weapons;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// Slung-item bulk (issue #208): weapons hit harder than a stone, the launcher hardest,
// & the capped energy stays under the one-shot threshold at a full draw.
[TestSuite]
public class SlungBulkTest
{
  [TestCase]
  public void StoneBreadAndChunkStayBaseline()
  {
    AssertFloat (SlingshotStone.BulkFactor (HeldWeapon.None)).IsEqual (1.0f);
    AssertFloat (SlingshotStone.BulkFactor (HeldWeapon.Bread)).IsEqual (1.0f);
    AssertFloat (SlingshotStone.BulkFactor (HeldWeapon.BananaChunk)).IsEqual (1.0f);
  }

  [TestCase]
  public void LauncherIsTheWreckingBall()
  {
    AssertFloat (SlingshotStone.BulkFactor (HeldWeapon.Banana)).IsGreater (SlingshotStone.BulkFactor (HeldWeapon.Laser));
    AssertFloat (SlingshotStone.BulkFactor (HeldWeapon.Laser)).IsGreater (1.0f);
  }

  [TestCase]
  public void OnlyGunsWithTheirOwnAmmoSpray()
  {
    AssertBool (SlingshotStone.Sprays (HeldWeapon.Laser) && SlingshotStone.Sprays (HeldWeapon.Banana) && SlingshotStone.Sprays (HeldWeapon.Slingshot)).IsTrue(); // Issue #244.
    AssertBool (SlingshotStone.Sprays (HeldWeapon.Blowgun) || SlingshotStone.Sprays (HeldWeapon.Bread) || SlingshotStone.Sprays (HeldWeapon.Boomerang) || SlingshotStone.Sprays (HeldWeapon.PaperAirplane)).IsFalse();
  }

  [TestCase]
  public void CapKeepsTheNeverOneHitPromise() => AssertFloat (Player.SlungBulkEnergyCap).IsLess (EnergyWeapon.FullChargeEnergyThreshold);
}
