using com.forerunnergames.energyshot.players;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// Fall damage (#263) & the rope-bounce cap (#262): the free drop covers a normal
// jump, a cap-height bounce comes down hard but survivable, & the cap itself is the
// tenth-ish bounce from the floor.
[TestSuite]
public class FallDamageTest
{
  [TestCase]
  public void ANormalJumpIsFree()
  {
    var player = AutoFree (new Player())!;
    var jumpApex = player.JumpVelocity * player.JumpVelocity / (2.0f * -player.Gravity.Y);
    AssertFloat (jumpApex).IsLess (player.FallDamageFreeMeters);
  }

  [TestCase]
  public void ABounceFromTheCapHurtsButDoesNotZap()
  {
    var player = AutoFree (new Player())!;
    var capApex = player.RopeTopBounceMax * player.RopeTopBounceMax / (2.0f * -player.Gravity.Y);
    var damage = Player.CalculateHealthDecrease ((capApex - player.FallDamageFreeMeters) * player.FallDamagePerMeter);
    AssertInt (damage).IsGreater (0);
    AssertInt (damage).IsLess (Player.MaxHealthFor (2)); // Even an Expert survives the worst rope ride.
  }

  [TestCase]
  public void ADropIsNeverAnInstantZap() => AssertFloat (Player.MaxFallEnergy).IsLess (com.forerunnergames.energyshot.weapons.EnergyWeapon.FullChargeEnergyThreshold);

  [TestCase]
  public void TrampolineChainsConvergeToStanding()
  {
    var player = AutoFree (new Player())!;
    // The chain must CONVERGE below the stand threshold (issue #276): damped bounces
    // honor the no-height-gain ruling & a trampoline loop starves instead of feeding.
    var speed = player.RopeTopBounceMax;
    for (var i = 0; i < 20; ++i) speed = speed >= player.RopeTopMinTrampolineFallSpeed ? Mathf.Min (speed * player.RopeTopBouncePerFallSpeed, player.RopeTopBounceMax) : 0.0f;
    AssertFloat (speed).IsEqual (0.0f); // Even a max bounce settles to standing within 20 landings.
  }
}
