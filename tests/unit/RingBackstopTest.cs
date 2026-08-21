using com.forerunnergames.energyshot.core.world;
using GdUnit4;
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
}
