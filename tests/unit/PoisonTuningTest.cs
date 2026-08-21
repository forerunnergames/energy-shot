using com.forerunnergames.energyshot.players;
using com.forerunnergames.energyshot.weapons;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// Regression coverage for the poison tuning (issue #194): 10% of max health per
// embedded dart per tick, & a single tick can never be a surprise one-shot.
[TestSuite]
public partial class PoisonTuningTest
{
  [TestCase]
  public void OneDartTickCostsTenPercentOfEachHealthPool()
  {
    var player = AutoFree (new Player())!;
    foreach (var difficulty in new[] { 0, 1, 2 })
    {
      var pool = Player.MaxHealthFor (difficulty);
      AssertInt (Player.CalculateHealthDecrease (pool * player.PoisonTickFractionPerDart / 100.0f)).IsEqual (pool / 10);
    }
  }

  [TestCase]
  public void OneDartTickStaysFarBelowTheOneShotThreshold()
  {
    var player = AutoFree (new Player())!;
    AssertFloat (Player.MaxHealthFor (0) * player.PoisonTickFractionPerDart / 100.0f).IsLess (EnergyWeapon.FullChargeEnergyThreshold * 0.5f);
  }

}
