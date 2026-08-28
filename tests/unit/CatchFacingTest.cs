using com.forerunnergames.energyshot.players;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// The airplane catch needs your eyes (caleb, issue #427): the facing cone is pure
// vector math, every branch pinned here. 0.82 is roughly 35 degrees off-center.
[TestSuite]
public class CatchFacingTest
{
  private const float MinDot = 0.82f;

  [TestCase]
  public void DeadAheadIsFacing() => AssertBool (Player.IsFacing (Vector3.Forward, Vector3.Forward * 10.0f, MinDot)).IsTrue();

  [TestCase]
  public void SlightlyOffCenterStillCatches()
  {
    var toTarget = (Vector3.Forward + Vector3.Right * 0.3f) * 5.0f; // ~17 degrees off.
    AssertBool (Player.IsFacing (Vector3.Forward, toTarget, MinDot)).IsTrue();
  }

  [TestCase]
  public void NinetyDegreesIsBlind() => AssertBool (Player.IsFacing (Vector3.Forward, Vector3.Right * 5.0f, MinDot)).IsFalse();

  [TestCase]
  public void BehindYouIsBlind() => AssertBool (Player.IsFacing (Vector3.Forward, Vector3.Back * 5.0f, MinDot)).IsFalse();

  [TestCase]
  public void WellOutsideTheConeIsBlind()
  {
    var toTarget = (Vector3.Forward + Vector3.Right) * 5.0f; // 45 degrees off.
    AssertBool (Player.IsFacing (Vector3.Forward, toTarget, MinDot)).IsFalse();
  }

  [TestCase]
  public void AZeroVectorNeverFaces() => AssertBool (Player.IsFacing (Vector3.Forward, Vector3.Zero, MinDot)).IsFalse();
}
