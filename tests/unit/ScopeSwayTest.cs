using com.forerunnergames.energyshot.players;
using Godot;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// Issue #279: the camera sway carries the exact difficulty the drifting dot had -
// drift * RadiusFraction of the FOV - so it scales with zoom & vanishes with drift.
[TestSuite]
public class ScopeSwayTest
{
  [TestCase]
  public void NoDriftNoSway() => AssertObject (Player.SwayRadians (Vector2.Zero, 40.0f)).IsEqual (Vector2.Zero);

  [TestCase]
  public void SwayScalesWithZoomFov()
  {
    var wide = Player.SwayRadians (Vector2.One, 40.0f);
    var tight = Player.SwayRadians (Vector2.One, 3.5f);
    AssertFloat (tight.X).IsLess (wide.X); // Same drift, narrower FOV: a smaller absolute sway...
    AssertFloat (tight.X / Mathf.DegToRad (3.5f)).IsEqualApprox (wide.X / Mathf.DegToRad (40.0f), 0.0001f); // ...but the same fraction of the view.
  }
}
