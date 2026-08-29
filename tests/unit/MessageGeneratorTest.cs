using System.Linq;
using com.forerunnergames.energyshot.players;
using com.forerunnergames.energyshot.ui.hud;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

[TestSuite]
public class MessageGeneratorTest
{
  private static DeathContext Laser (float energy = 0.5f) => new() { Kind = DamageKind.Laser, Energy = energy };

  [TestCase]
  public void ExactlyOneHundredFiftyFiveUniqueMessageTemplates()
  {
    // 100 from the content wave (issue #84) + 3 through-wall zaps (issue #94)
    // + 4 boomerang zap-outs (issue #98) + 4 slingshot zap-outs (issue #99)
    // + 6 paper airplane zap-outs & 3 airplane catches (issues #102 & #191)
    // + 4 slung-item zap-outs (issue #190) + 4 landmines (issue #191)
    // + 6 zapped-mid-bread-ritual (issue #192) + 4 poison zap-outs (issue #194)
    // + 12 end-of-round superlatives (issue #153) + 5 hill clears (issue #420).
    var templates = MessagePools.All.SelectMany (pool => pool).ToList();
    AssertInt (templates.Count).IsEqual (155);
    AssertInt (templates.Distinct().Count()).IsEqual (155);
  }

  [TestCase]
  public void TemplatesAvoidViolentWording()
  {
    var banned = new[] { "kill", "death", "die", "dead", "blood", "gore", "murder", "corpse" };
    foreach (var template in MessagePools.All.SelectMany (pool => pool))
      foreach (var word in banned)
        AssertBool (template.ToLower().Contains (word)).OverrideFailureMessage ($"'{template}' contains '{word}'").IsFalse();
  }

  [TestCase]
  public void BracketedNamesLandAsTextNotMarkup()
  {
    // The scroller's history renders BBCode (CodeRabbit on #420): a name like
    // "[b]Zapper[/b]" must not style the announcement.
    var message = MessageGenerator.OnHillClear ("[b]Sneaky[/b]");
    AssertBool (message.Contains ("[b]")).IsFalse();
    AssertBool (message.Contains ("Sneaky")).IsTrue();
  }

  [TestCase]
  public void PoisonPoolSelectedByDamageKind() => AssertObject (MessageGenerator.SelectZappedPool (new DeathContext { Kind = DamageKind.Poison })).IsSame (MessagePools.Poison);

  [TestCase]
  public void EveryPoolIsInTheRegistry()
  {
    // 36 scenario pools registered, none empty.
    AssertInt (MessagePools.All.Count).IsEqual (36);
    foreach (var pool in MessagePools.All) AssertBool (pool.Count > 0).IsTrue();
  }

  [TestCase]
  public void LostStreakOutranksEverythingElse()
  {
    AssertObject (MessageGenerator.SelectZappedPool (Laser() with { VictimLostStreak = 5 })).IsSame (MessagePools.StreakEnded);
    AssertObject (MessageGenerator.SelectZappedPool (Laser() with { VictimLostStreak = 3 })).IsSame (MessagePools.StreakLost);
    AssertObject (MessageGenerator.SelectZappedPool (Laser() with { VictimLostStreak = 2 })).IsSame (MessagePools.Zapped);
  }

  [TestCase]
  public void ComboSplatterPunchNeedsBothOverlays()
  {
    AssertObject (MessageGenerator.SelectZappedPool (Laser() with { SplatterActive = true, BlurActive = true })).IsSame (MessagePools.ComboSplatterPunch);
    AssertObject (MessageGenerator.SelectZappedPool (Laser() with { SplatterActive = true })).IsSame (MessagePools.Zapped);
    AssertObject (MessageGenerator.SelectZappedPool (Laser() with { BlurActive = true })).IsSame (MessagePools.Zapped);
  }

  [TestCase]
  public void BananaPoolsSplitOnDirectHitEnergy()
  {
    var blast = new DeathContext { Kind = DamageKind.Banana, Energy = 0.4f };
    var direct = new DeathContext { Kind = DamageKind.Banana, Energy = 0.9f };
    AssertObject (MessageGenerator.SelectZappedPool (blast)).IsSame (MessagePools.BananaBlast);
    AssertObject (MessageGenerator.SelectZappedPool (direct)).IsSame (MessagePools.BananaDirect);
  }

  [TestCase]
  public void BoomerangPoolSelectedByDamageKind()
  {
    // Boomerang zap-outs get their own flavor (issue #98).
    AssertObject (MessageGenerator.SelectZappedPool (new DeathContext { Kind = DamageKind.Boomerang, Energy = 0.4f })).IsSame (MessagePools.Boomerang);
  }

  [TestCase]
  public void SlingshotPoolSelectedByDamageKind()
  {
    // Slingshot zap-outs get their own flavor (issue #99).
    AssertObject (MessageGenerator.SelectZappedPool (new DeathContext { Kind = DamageKind.Slingshot, Energy = 0.6f })).IsSame (MessagePools.Slingshot);
  }

  [TestCase]
  public void SlungItemPoolSelectedByDamageKind()
  {
    // A world item slung out of a slingshot reads differently from a stone (issue #190).
    AssertObject (MessageGenerator.SelectZappedPool (new DeathContext { Kind = DamageKind.SlungItem, Energy = 0.5f })).IsSame (MessagePools.SlungItem);
  }

  [TestCase]
  public void AirplanePoolsSplitOnHowItFoundYou()
  {
    // Thrown or slung onto you vs. stepped on: same hazard, different story (issues #102 & #191).
    AssertObject (MessageGenerator.SelectZappedPool (new DeathContext { Kind = DamageKind.PaperAirplane, Energy = 2.0f })).IsSame (MessagePools.PaperAirplane);
    AssertObject (MessageGenerator.SelectZappedPool (new DeathContext { Kind = DamageKind.Landmine, Energy = 2.0f })).IsSame (MessagePools.Landmine);
  }

  [TestCase]
  public void AirplanePoolsOutrankStanceAndCharge()
  {
    // The airplane picked you personally, so stance & charge flavor never wins (issue #191).
    var airplane = new DeathContext { Kind = DamageKind.PaperAirplane, Energy = 2.0f };
    AssertObject (MessageGenerator.SelectZappedPool (airplane with { KillerSliding = true, KillerAirborne = true })).IsSame (MessagePools.PaperAirplane);
    AssertObject (MessageGenerator.SelectZappedPool (airplane with { Kind = DamageKind.Landmine, VictimHeldBananaGun = true })).IsSame (MessagePools.Landmine);
    // ...but the scenarios ABOVE it still win, so a future reorder can't quietly
    // promote the airplane past them (issue #84's ordering).
    AssertObject (MessageGenerator.SelectZappedPool (airplane with { VictimLostStreak = 5 })).IsSame (MessagePools.StreakEnded);
    AssertObject (MessageGenerator.SelectZappedPool (airplane with { VictimLostStreak = 3 })).IsSame (MessagePools.StreakLost);
    AssertObject (MessageGenerator.SelectZappedPool (airplane with { SplatterActive = true, BlurActive = true })).IsSame (MessagePools.ComboSplatterPunch);
  }

  [TestCase]
  public void AirplaneCatchMessagesMentionBothPlayers()
  {
    // The catch announcement (issue #102): {z} = the catcher, {v} = the thrower.
    var message = MessageGenerator.OnAirplaneCatch ("Alice", "Bob");
    AssertBool (message.Contains ("Alice")).IsTrue();
    AssertBool (message.Contains ("Bob")).IsTrue();
  }

  [TestCase]
  public void EatingPoolSelectedWhenZappedMidRitual()
  {
    // Standing rooted for three seconds is the whole story of that death (issue #192),
    // so it beats the stance & carried-weapon flavors below it...
    var eating = Laser() with { VictimEating = true };
    AssertObject (MessageGenerator.SelectZappedPool (eating)).IsSame (MessagePools.ZappedEating);
    AssertObject (MessageGenerator.SelectZappedPool (eating with { VictimHeldBananaGun = true })).IsSame (MessagePools.ZappedEating);
    AssertObject (MessageGenerator.SelectZappedPool (eating with { KillerSliding = true, KillerAirborne = true })).IsSame (MessagePools.ZappedEating);
    // ...but never the scenarios above it, so a future reorder can't quietly promote it.
    AssertObject (MessageGenerator.SelectZappedPool (eating with { VictimLostStreak = 5 })).IsSame (MessagePools.StreakEnded);
    AssertObject (MessageGenerator.SelectZappedPool (eating with { SplatterActive = true, BlurActive = true })).IsSame (MessagePools.ComboSplatterPunch);
    AssertObject (MessageGenerator.SelectZappedPool (eating with { Kind = DamageKind.Punch })).IsSame (MessagePools.Punch);
    AssertObject (MessageGenerator.SelectZappedPool (eating with { Kind = DamageKind.Boomerang })).IsSame (MessagePools.Boomerang);
    // Not eating: nothing changes.
    AssertObject (MessageGenerator.SelectZappedPool (Laser())).IsSame (MessagePools.Zapped);
  }

  [TestCase]
  public void PunchPoolsSplitOnArmament()
  {
    var punch = new DeathContext { Kind = DamageKind.Punch };
    AssertObject (MessageGenerator.SelectZappedPool (punch with { VictimArmed = true })).IsSame (MessagePools.PunchedOutArmed);
    AssertObject (MessageGenerator.SelectZappedPool (punch with { KillerUnarmed = true })).IsSame (MessagePools.FistsVsFists);
    AssertObject (MessageGenerator.SelectZappedPool (punch)).IsSame (MessagePools.Punch);
  }

  [TestCase]
  public void StancePoolsCoverBananaGunSlidesAndJumps()
  {
    AssertObject (MessageGenerator.SelectZappedPool (Laser() with { VictimHeldBananaGun = true })).IsSame (MessagePools.HoldingBananaGun);
    AssertObject (MessageGenerator.SelectZappedPool (Laser() with { KillerSliding = true })).IsSame (MessagePools.SlideShotKiller);
    AssertObject (MessageGenerator.SelectZappedPool (Laser() with { VictimSliding = true })).IsSame (MessagePools.SlideShotVictim);
    AssertObject (MessageGenerator.SelectZappedPool (Laser() with { KillerAirborne = true })).IsSame (MessagePools.JumpShot);
  }

  [TestCase]
  public void LaserPoolsSplitOnCharge()
  {
    AssertObject (MessageGenerator.SelectZappedPool (Laser (0.96f))).IsSame (MessagePools.FullCharge);
    AssertObject (MessageGenerator.SelectZappedPool (new DeathContext { Kind = DamageKind.FullAuto })).IsSame (MessagePools.FullAuto);
    AssertObject (MessageGenerator.SelectZappedPool (Laser())).IsSame (MessagePools.Zapped);
  }

  [TestCase]
  public void ZapStreakPoolsEscalateByTier()
  {
    AssertObject (MessageGenerator.SelectZapStreakPool (3)).IsSame (MessagePools.ZapStreakTier3);
    AssertObject (MessageGenerator.SelectZapStreakPool (4)).IsSame (MessagePools.ZapStreakTier3);
    AssertObject (MessageGenerator.SelectZapStreakPool (5)).IsSame (MessagePools.ZapStreakTier5);
    AssertObject (MessageGenerator.SelectZapStreakPool (7)).IsSame (MessagePools.ZapStreakTier7);
    AssertObject (MessageGenerator.SelectZapStreakPool (11)).IsSame (MessagePools.ZapStreakTier7);
  }

  [TestCase]
  public void ZappedMessagesSubstituteBothNames()
  {
    var message = MessageGenerator.OnZapped ("Alice", "Bob", Laser());
    AssertBool (message.Contains ("Alice")).IsTrue();
    AssertBool (message.Contains ("Bob")).IsTrue();
    AssertBool (char.IsUpper (message[0])).IsTrue();
  }

  [TestCase]
  public void ThroughWallMessagesMentionBothPlayers()
  {
    var context = new DeathContext { Kind = DamageKind.Laser, Energy = 1.0f, ThroughBarrier = true };
    AssertBool (MessageGenerator.SelectZappedPool (context) == MessagePools.ThroughWall).IsTrue();
    var message = MessageGenerator.OnZapped ("Alice", "Bob", context);
    AssertBool (message.Contains ("Alice")).IsTrue();
    AssertBool (message.Contains ("Bob")).IsTrue();
  }

  [TestCase]
  public void StreakMessagesMentionThePlayer()
  {
    AssertBool (MessageGenerator.OnZapStreak ("Alice", 3).Contains ("Alice")).IsTrue();
    AssertBool (MessageGenerator.OnZappedStreak ("Alice").Contains ("Alice")).IsTrue();
    AssertBool (MessageGenerator.OnFallStreak ("Alice").Contains ("Alice")).IsTrue();
  }

  [TestCase]
  public void TheftRevengeMessagesMentionBothPlayers()
  {
    var message = MessageGenerator.OnTheftRevenge ("Alice", "Bob");
    AssertBool (message.Contains ("Alice")).IsTrue();
    AssertBool (message.Contains ("Bob")).IsTrue();
  }

  [TestCase]
  public void RespawnedFellMessagesAgreeAcrossPeers()
  {
    var selfMessage = MessageGenerator.OnPlayerRespawnedFell (isSelf: true, "Alice", out var index);
    var remoteMessage = MessageGenerator.OnPlayerRespawnedFell (isSelf: false, "Alice", index);
    AssertString (selfMessage).StartsWith ("You ");
    AssertString (remoteMessage).StartsWith ("Alice ");
  }
}
