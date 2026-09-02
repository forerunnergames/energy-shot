using com.forerunnergames.energyshot.players;
using com.forerunnergames.energyshot.weapons;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// The full-charge proton beam (issue #292): pinned at both ends, writhing between,
// & heavy knockback only at full charge.
[TestSuite]
public class ProtonBeamTest
{
  [TestCase]
  public void WritheIsPinnedAtBothEndsAndAliveInTheMiddle()
  {
    AssertFloat (ProtonBeam.Wobble (0.0f, 3.7f, 11.0f).Length()).IsEqualApprox (0.0f, 0.0001f);
    AssertFloat (ProtonBeam.Wobble (1.0f, 3.7f, 11.0f).Length()).IsEqualApprox (0.0f, 0.0001f);
    AssertFloat (ProtonBeam.Wobble (0.5f, 3.7f, 11.0f).Length()).IsGreater (0.01f);
    AssertFloat (ProtonBeam.Wobble (0.5f, 3.7f, 11.0f).Length()).IsLess (ProtonBeam.WobbleMeters);
  }

  [TestCase]
  public void OnlyAFullChargeHitCarriesTheBeamShove()
  {
    AssertFloat (Player.KnockbackScaleFor (0.5f)).IsEqual (1.0f);
    AssertFloat (Player.KnockbackScaleFor (EnergyWeapon.FullChargeEnergyThreshold)).IsEqual (Player.BeamKnockbackScale);
  }
}
