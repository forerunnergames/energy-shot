using com.forerunnergames.energyshot.ui.hud;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

[TestSuite]
public class MessageGeneratorTest
{
  [TestCase]
  public void ZappedMessagesSubstituteRoles()
  {
    var asVictim = MessageGenerator.OnZapped ("Alice", "Bob", selfIsVictim: true, selfIsZapper: false, fullCharge: false);
    AssertBool (asVictim.ToLower().Contains ("you")).IsTrue();
    AssertBool (asVictim.Contains ("Alice")).IsFalse();
    AssertBool (asVictim.Contains ("Bob")).IsTrue();

    var asZapper = MessageGenerator.OnZapped ("Alice", "Bob", selfIsVictim: false, selfIsZapper: true, fullCharge: false);
    AssertBool (asZapper.ToLower().Contains ("you")).IsTrue();
    AssertBool (asZapper.Contains ("Alice")).IsTrue();
    AssertBool (asZapper.Contains ("Bob")).IsFalse();

    var asBystander = MessageGenerator.OnZapped ("Alice", "Bob", selfIsVictim: false, selfIsZapper: false, fullCharge: false);
    AssertBool (asBystander.Contains ("Alice")).IsTrue();
    AssertBool (asBystander.Contains ("Bob")).IsTrue();
  }

  [TestCase]
  public void FullChargeMessagesMentionBothPlayers()
  {
    var message = MessageGenerator.OnZapped ("Alice", "Bob", selfIsVictim: false, selfIsZapper: false, fullCharge: true);
    AssertBool (message.Contains ("Alice")).IsTrue();
    AssertBool (message.Contains ("Bob")).IsTrue();
  }

  [TestCase]
  public void StreakMessagesMentionThePlayer()
  {
    AssertBool (MessageGenerator.OnZapStreak ("Alice").Contains ("Alice")).IsTrue();
    AssertBool (MessageGenerator.OnZappedStreak ("Alice").Contains ("Alice")).IsTrue();
    AssertBool (MessageGenerator.OnFallStreak ("Alice").Contains ("Alice")).IsTrue();
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
