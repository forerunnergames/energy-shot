using com.forerunnergames.energyshot.players;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// Blunt mode (issue #249): a club hits harder than a fist but is never a one-shot.
[TestSuite]
public class BluntTest
{
  [TestCase]
  public void AClubHitsHarderThanAFist()
  {
    var player = AutoFree (new Player())!;
    AssertInt (Player.CalculateHealthDecrease (player.ClubEnergy)).IsEqual (2 * Player.CalculateHealthDecrease (player.PunchEnergy));
  }

  [TestCase]
  public void AClubIsNeverAOneShot()
  {
    var player = AutoFree (new Player())!;
    AssertFloat (player.ClubEnergy).IsLess (com.forerunnergames.energyshot.weapons.EnergyWeapon.FullChargeEnergyThreshold);
  }
}
