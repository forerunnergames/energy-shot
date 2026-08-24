using com.forerunnergames.energyshot.players;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// Poison wears off (Aaron, 2026-08-24) & bread heals through it: each dart runs its
// own clock, & a tick is not an attacker's swing.
[TestSuite]
public class PoisonTest
{
  [TestCase]
  public void OneDartPoisonsYouThreeTimes()
  {
    var player = AutoFree (new Player())!;
    AssertInt (player.PoisonTicksPerDart).IsEqual (3);
  }

  [TestCase]
  public void OneDartCostsThirtyPercentOverItsLife()
  {
    var player = AutoFree (new Player())!;
    AssertFloat (Player.DartLifetimeFraction (player.PoisonTicksPerDart, player.PoisonTickFractionPerDart)).IsEqualApprox (0.3f, 0.0001f);
  }

  [TestCase]
  public void DartsAreCumulativeWithinATick()
  {
    var player = AutoFree (new Player())!;
    AssertFloat (Player.TickFractionFor (1, player.PoisonTickFractionPerDart)).IsEqualApprox (0.1f, 0.0001f);
    AssertFloat (Player.TickFractionFor (3, player.PoisonTickFractionPerDart)).IsEqualApprox (0.3f, 0.0001f);
    AssertFloat (Player.TickFractionFor (5, player.PoisonTickFractionPerDart)).IsEqualApprox (0.5f, 0.0001f);
  }

  [TestCase]
  public void PoisonEndsInFiniteTime()
  {
    // The spec's point: a pincushion clears on its own, it never rides you forever.
    var player = AutoFree (new Player())!;
    AssertInt (player.PoisonTicksPerDart).IsGreater (0);
    AssertFloat (player.PoisonTicksPerDart * player.PoisonTickSeconds).IsLess (60.0f);
  }

  [TestCase]
  public void OneDartIsOutEatable()
  {
    // Bread heals to full & a dart costs 10% of max per 5s tick, so a single dart
    // can be out-eaten (issue #194's spec) - the eat is 3s, shorter than a tick.
    var player = AutoFree (new Player())!;
    AssertFloat (player.PoisonTickFractionPerDart).IsLess (1.0f);
    AssertFloat (player.BreadEatSeconds).IsLess (player.PoisonTickSeconds);
  }
}
