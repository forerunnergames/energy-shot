using com.forerunnergames.energyshot.weapons;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// The blowgun's scope model (issue #236): the zoom ladder has ends, drift grows with
// zoom, & the heartbeat envelope spikes on the beat & settles into the shot window.
[TestSuite]
public class ScopeTest
{
  [TestCase]
  public void ZoomLadderClampsAtBothEnds()
  {
    AssertInt (Scope.StepOut (0)).IsEqual (0);
    AssertInt (Scope.StepIn (Scope.ZoomFovs.Length - 1)).IsEqual (Scope.ZoomFovs.Length - 1);
    AssertInt (Scope.StepIn (0)).IsEqual (1);
  }

  [TestCase]
  public void FovsShrinkEveryStep()
  {
    for (var i = 1; i < Scope.ZoomFovs.Length; ++i) AssertFloat (Scope.ZoomFovs[i]).IsLess (Scope.ZoomFovs[i - 1]);
  }

  [TestCase]
  public void DriftGrowsWithZoomAndIsNeverZero()
  {
    AssertFloat (Scope.DriftFraction (0)).IsGreater (0.0f); // Aiming is never free, even at the lowest zoom (Aaron).
    AssertFloat (Scope.DriftFraction (Scope.ZoomFovs.Length - 1)).IsGreater (Scope.DriftFraction (0));
  }

  [TestCase]
  public void HeartbeatSpikesThenSettlesForAboutASecond()
  {
    AssertFloat (Scope.BeatEnvelope (0.0f)).IsEqual (1.0f);
    AssertFloat (Scope.BeatEnvelope (Scope.SettleSeconds)).IsEqual (Scope.SettledFraction);
    AssertBool (Scope.IsSettled (Scope.SettleSeconds - 0.01f)).IsFalse();
    AssertBool (Scope.IsSettled (Scope.SettleSeconds)).IsTrue();
    AssertFloat (Scope.BeatPeriodSeconds - Scope.SettleSeconds).IsGreaterEqual (1.0f); // The settled window lasts at least a second.
  }
}
