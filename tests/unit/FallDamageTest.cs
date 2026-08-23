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
  public void ACapHeightRopeBounceIsFree()
  {
    // The 36m free drop (Aaron, 2026-08-23) covers every rope ride: fall damage
    // is for the truly spectacular drops only.
    var player = AutoFree (new Player())!;
    var capApex = player.RopeTopBounceMax * player.RopeTopBounceMax / (2.0f * -player.Gravity.Y);
    AssertFloat (capApex).IsLess (player.FallDamageFreeMeters);
  }

  [TestCase]
  public void AJumpPlusOneFullChargeBoostIsFree()
  {
    // Aaron's spec: shoot the ground once while jumping - the max height reached
    // must NOT cause fall damage.
    var player = AutoFree (new Player())!;
    var jumpApex = player.JumpVelocity * player.JumpVelocity / (2.0f * -player.Gravity.Y);
    var boostSpeed = player.JumpVelocity * player.RocketBoostMultiplier * 1.0f; // A full-charge shot.
    var boostApex = boostSpeed * boostSpeed / (2.0f * -player.Gravity.Y);
    AssertFloat (jumpApex + boostApex).IsLess (player.FallDamageFreeMeters);
  }

  [TestCase]
  public void TheSpawnPlatformHopIsFree()
  {
    // Aaron's spec: jumping off the spawn platform (the ~30m drop, plus the hop)
    // never costs health.
    var player = AutoFree (new Player())!;
    var jumpApex = player.JumpVelocity * player.JumpVelocity / (2.0f * -player.Gravity.Y);
    AssertFloat (30.0f + jumpApex).IsLess (player.FallDamageFreeMeters);
  }

  [TestCase]
  public void APunchShoveReachesTheRopes()
  {
    // Aaron's spec (issue #334): punch someone into the boxing ring ropes & have
    // them bounce back for the repeat punch - the shove speed & the momentum
    // carry (which stops Move() eating the impulse next frame) must both exist.
    var player = AutoFree (new Player())!;
    AssertFloat (player.KnockbackStrength * player.PunchEnergy * player.PunchKnockbackScale).IsGreaterEqual (8.0f);
    AssertFloat (player.KnockbackCarrySeconds).IsGreaterEqual (0.5f);
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
