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
  public void TenBouncesReachTheCap()
  {
    var player = AutoFree (new Player())!;
    var speed = player.RopeTopBounceMin;
    for (var i = 0; i < 10; ++i) speed = Mathf.Clamp (speed * player.RopeTopBouncePerFallSpeed, player.RopeTopBounceMin, player.RopeTopBounceMax);
    AssertFloat (speed).IsEqual (player.RopeTopBounceMax);
  }
}
