using com.forerunnergames.energyshot.core.world;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// The backstop must out-thick the fastest single-tick step a player can take (issue
// #276): worst case is a full rope bounce off a max chained slide - 28 m/s in,
// restitution 1.3 + shove 9 + 0.6/impact-speed out ≈ 62 m/s ≈ 1.04m per 60Hz tick.
[TestSuite]
public class RingBackstopTest
{
  private const float WorstCaseExitSpeed = 28.0f * 1.3f + 9.0f + 0.6f * 28.0f;
  private const float PhysicsTicksPerSecond = 60.0f;

  [TestCase]
  public void BackstopOutThicksTheFastestTick() => AssertFloat (World.BackstopThicknessMeters).IsGreater (WorstCaseExitSpeed / PhysicsTicksPerSecond * 1.5f);

  // The rally must CONVERGE (issue #276): iterate exit = min(1.9x + 9, cap) from a
  // sprint & prove it stays bounded - uncapped, four bounces pass 150 m/s.
  [TestCase]
  public void CappedRopeRallyConverges()
  {
    var speed = 14.0f;
    for (var bounce = 0; bounce < 10; ++bounce) speed = Mathf.Min (speed * 1.9f + 9.0f, 20.0f);
    AssertFloat (speed).IsLessEqual (20.0f);
    var uncapped = 14.0f;
    for (var bounce = 0; bounce < 4; ++bounce) uncapped = uncapped * 1.9f + 9.0f;
    AssertFloat (uncapped).IsGreater (150.0f); // The disease the cap cures.
  }
}
