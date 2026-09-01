using com.forerunnergames.energyshot.weapons;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// Slung items bounce & slide (issue #285): the banana's split bounce, applied to every
// tumbling item, with rest defined as slow-on-a-floor.
[TestSuite]
public class SlungPhysicsTest
{
  [TestCase]
  public void PlungeFlipsAndSquashesWhileSkidMostlySurvives()
  {
    var v = SlingshotStone.Deflect (new Vector3 (10.0f, -20.0f, 0.0f), Vector3.Up, 0.18f, 0.85f);
    AssertFloat (v.Y).IsEqualApprox (3.6f, 0.001f); // -20 into the floor comes back as +3.6.
    AssertFloat (v.X).IsEqualApprox (8.5f, 0.001f); // The skid keeps 85%.
  }

  [TestCase]
  public void RestIsSlowOnAFloorNotSlowOnAWall()
  {
    AssertBool (SlingshotStone.AtRest (new Vector3 (0.3f, 0.0f, 0.2f), Vector3.Up, 1.0f)).IsTrue();
    AssertBool (SlingshotStone.AtRest (new Vector3 (0.3f, 0.0f, 0.2f), Vector3.Right, 1.0f)).IsFalse(); // A wall graze keeps falling.
    AssertBool (SlingshotStone.AtRest (new Vector3 (5.0f, 0.0f, 0.0f), Vector3.Up, 1.0f)).IsFalse(); // Still skidding.
  }
}
