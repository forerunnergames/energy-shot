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
  public void AFreshDartIsNotExpired() => AssertBool (Player.DartExpired (1000, 1000, 20.0f)).IsFalse();

  [TestCase]
  public void ADartSurvivesJustUnderItsLifetime() => AssertBool (Player.DartExpired (1000, 1000 + 19_999, 20.0f)).IsFalse();

  [TestCase]
  public void ADartExpiresAtItsLifetime() => AssertBool (Player.DartExpired (1000, 1000 + 20_000, 20.0f)).IsTrue();

  [TestCase]
  public void PoisonEndsInFiniteTime()
  {
    // The spec's point: a pincushion clears on its own, it never rides you forever.
    var player = AutoFree (new Player())!;
    AssertFloat (player.PoisonDartSeconds).IsGreater (0.0f);
    AssertFloat (player.PoisonDartSeconds).IsLess (120.0f);
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
