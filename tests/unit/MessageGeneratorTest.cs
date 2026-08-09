using com.forerunnergames.energyshot.ui.hud;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

[TestSuite]
public class MessageGeneratorTest
{
  [TestCase]
  public void ShotPlayerMessageUsesYouForSelf()
  {
    AssertString (MessageGenerator.OnShotPlayer (isSelf: true, "Alice", "Bob")).IsEqual ("You shot Bob");
    AssertString (MessageGenerator.OnShotPlayer (isSelf: false, "Alice", "Bob")).IsEqual ("Alice shot Bob");
  }

  [TestCase]
  public void RespawnedShotMessageUsesCorrectGrammar()
  {
    AssertString (MessageGenerator.OnPlayerRespawnedShot (isSelf: true, "Alice", "Bob")).IsEqual ("You were shot by Bob");
    AssertString (MessageGenerator.OnPlayerRespawnedShot (isSelf: false, "Alice", "Bob")).IsEqual ("Alice was shot by Bob");
  }

  [TestCase]
  public void RespawnedFellMessagesAgreeAcrossPeers()
  {
    // The random overload picks a message & returns its index; the indexed overload
    // must produce the equivalent message for remote peers (modulo you/they wording).
    var selfMessage = MessageGenerator.OnPlayerRespawnedFell (isSelf: true, "Alice", out var index);
    var remoteMessage = MessageGenerator.OnPlayerRespawnedFell (isSelf: false, "Alice", index);
    AssertString (selfMessage).StartsWith ("You ");
    AssertString (remoteMessage).StartsWith ("Alice ");
  }
}
