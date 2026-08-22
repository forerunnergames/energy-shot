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
  public void TrampolineChainsGrowToTheCapWithinTenBounces()
  {
    var player = AutoFree (new Player())!;
    // Aaron's ruling: each bounce gains height, reaching the ceiling around the
    // tenth jump - & a gentle landing (under the stand threshold) never bounces at
    // all, so the old passive forever-loop floor stays dead.
    var speed = 14.0f; // A solid jump onto the rope (14 x 1.1^10 = 36.3, capped).
    for (var i = 0; i < 10; ++i) speed = Mathf.Min (speed * player.RopeTopBouncePerFallSpeed, player.RopeTopBounceMax);
    AssertFloat (speed).IsEqual (player.RopeTopBounceMax); // The cap by the tenth bounce.
    AssertFloat (player.RopeTopMinTrampolineFallSpeed).IsGreater (0.0f); // The stand threshold exists: no minimum-bounce fuel.
  }
}
