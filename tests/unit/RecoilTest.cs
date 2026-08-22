using com.forerunnergames.energyshot.players;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// The recoil model (issue #237): a tap still kicks, a full burst climbs well past a
// tap but stays under the cap, & the cap is a real ceiling.
[TestSuite]
public class RecoilTest
{
  [TestCase]
  public void AQuickTapStillKicks()
  {
    var player = AutoFree (new Player())!;
    AssertFloat (player.LaserTapKickMinRadians).IsGreater (0.0f);
  }

  [TestCase]
  public void AFullAutoBurstClimbsPastATapButUnderTheCap()
  {
    var player = AutoFree (new Player())!;
    var shots = Mathf.FloorToInt (player.FullAutoDurationSeconds / player.FullAutoShotIntervalSeconds);
    var burst = shots * player.FullAutoKickRadians;
    AssertFloat (burst).IsGreater (player.LaserTapKickMinRadians * 3.0f);
    AssertFloat (burst).IsLess (player.MaxRecoilRadians);
  }

  [TestCase]
  public void TheBananaKickFitsUnderTheCap()
  {
    var player = AutoFree (new Player())!;
    AssertFloat (player.BananaRecoilRadians).IsLess (player.MaxRecoilRadians);
  }
}
