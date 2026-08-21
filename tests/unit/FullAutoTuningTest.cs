using com.forerunnergames.energyshot.players;
using com.forerunnergames.energyshot.weapons;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// Regression coverage for the full-auto buff (issue #218, CodeRabbit on #220):
// the tuning constants must stay strong enough that a full burst is worth its
// ability slot, & each shot must stay far below the charged one-shot threshold.
[TestSuite]
public partial class FullAutoTuningTest
{
  [TestCase]
  public void FullAutoShotDealsDoubledDamage()
  {
    var player = AutoFree (new Player())!;
    AssertInt (Player.CalculateHealthDecrease (player.FullAutoEnergy)).IsEqual (24);
  }

  [TestCase]
  public void FullAutoShotStaysFarBelowOneShotThreshold()
  {
    var player = AutoFree (new Player())!;
    AssertFloat (player.FullAutoEnergy).IsLess (EnergyWeapon.FullChargeEnergyThreshold * 0.5f);
  }

  [TestCase]
  public void FullBurstCanZapTheBiggestHealthPool()
  {
    var player = AutoFree (new Player())!;
    var shots = Mathf.FloorToInt (player.FullAutoDurationSeconds / player.FullAutoShotIntervalSeconds);
    AssertInt (shots * Player.CalculateHealthDecrease (player.FullAutoEnergy)).IsGreaterEqual (Player.MaxHealthFor (0));
  }
}
