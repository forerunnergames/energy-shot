using com.forerunnergames.energyshot.players;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// The recoil model (issue #237, re-landed after the #309 revert): a tap kicks at
// least its floor, full-auto stacks small kicks across a burst, & the ledger caps
// so a long burst can't wrap the view into the sky.
[TestSuite]
public class RecoilTest
{
  private const float TapFloor = 0.03f;
  private const float FullAutoKick = 0.014f;
  private const float PerEnergy = 0.06f;
  private const float Max = 0.5f;

  [TestCase]
  public void AQuickTapKicksAtLeastTheFloor()
  {
    AssertFloat (Player.ShotKick (isFullAuto: false, energy: 0.1f, FullAutoKick, TapFloor, PerEnergy)).IsEqual (TapFloor);
  }

  [TestCase]
  public void AChargedShotKicksByItsEnergy()
  {
    AssertFloat (Player.ShotKick (isFullAuto: false, energy: 1.0f, FullAutoKick, TapFloor, PerEnergy)).IsEqual (PerEnergy);
  }

  [TestCase]
  public void ATwentyRoundBurstClimbsWellPastATapButUnderTheCap()
  {
    var recoil = 0.0f;
    for (var shot = 0; shot < 20; ++shot) recoil = Player.NextRecoil (recoil, Player.ShotKick (isFullAuto: true, energy: 0.1f, FullAutoKick, TapFloor, PerEnergy), Max);
    AssertFloat (recoil).IsGreater (TapFloor * 2.0f);
    AssertFloat (recoil).IsLessEqual (Max);
  }

  [TestCase]
  public void TheLedgerCapsNoMatterHowLongTheBurst()
  {
    var recoil = 0.0f;
    for (var shot = 0; shot < 1000; ++shot) recoil = Player.NextRecoil (recoil, FullAutoKick, Max);
    AssertFloat (recoil).IsEqual (Max);
  }

  [TestCase]
  public void TheBananaKickFitsUnderTheCap()
  {
    var player = AutoFree (new Player())!;
    AssertFloat (Player.NextRecoil (0.0f, player.BananaRecoilRadians, player.MaxRecoilRadians)).IsLess (player.MaxRecoilRadians);
  }

  [TestCase]
  public void ANegativeKickNeverLowersTheLedger()
  {
    AssertFloat (Player.NextRecoil (0.2f, -1.0f, Max)).IsEqual (0.2f);
  }
}
