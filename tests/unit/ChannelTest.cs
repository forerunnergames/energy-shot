using com.forerunnergames.energyshot.core.world;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// Release channels (issue #415): the version suffix IS the channel, the channel picks
// the default port, & the version kick explains itself in player words. All pure
// statics, every branch pinned here.
[TestSuite]
public class ChannelTest
{
  [TestCase]
  public void CleanVersionsAreStable()
  {
    AssertBool (World.IsDevChannel ("0.8.126")).IsFalse();
    AssertBool (World.IsDevChannel ("1.0.0")).IsFalse();
  }

  [TestCase]
  public void SuffixedVersionsAreDev()
  {
    AssertBool (World.IsDevChannel ("0.8.127-dev.1")).IsTrue();
    AssertBool (World.IsDevChannel ("0.8.15-dev")).IsTrue(); // The editor's own project version.
    AssertBool (World.IsDevChannel ("0.0.0-spoofed")).IsTrue(); // The playtest's spoof probe.
  }

  [TestCase]
  public void EachChannelDefaultsToItsOwnPort()
  {
    AssertInt (World.DefaultPortFor ("0.8.126")).IsEqual (55556);
    AssertInt (World.DefaultPortFor ("0.8.127-dev.1")).IsEqual (World.DevServerPort);
  }

  [TestCase]
  public void TestBuildOnMainServerIsToldWhereItPlays()
  {
    var message = World.VersionKickMessage ("0.8.126", "0.8.127-dev.1");
    AssertBool (message.Contains ("test build")).IsTrue();
    AssertBool (message.Contains ("0.8.127-dev.1")).IsTrue();
  }

  [TestCase]
  public void RegularGameOnTestServerIsToldWhereItPlays()
  {
    var message = World.VersionKickMessage ("0.8.127-dev.1", "0.8.126");
    AssertBool (message.Contains ("test server")).IsTrue();
    AssertBool (message.Contains ("0.8.127-dev.1")).IsTrue();
  }

  [TestCase]
  public void SameChannelMismatchAsksForTheNewestVersion()
  {
    var message = World.VersionKickMessage ("0.8.126", "0.8.125");
    AssertBool (message.Contains ("0.8.126")).IsTrue();
    AssertBool (message.Contains ("0.8.125")).IsTrue();
    AssertBool (message.Contains ("newest")).IsTrue();
  }

  [TestCase]
  public void LegacyClientIsAskedForTheNewestVersion()
  {
    var message = World.LegacyVersionKickMessage ("0.8.126");
    AssertBool (message.Contains ("0.8.126")).IsTrue();
    AssertBool (message.Contains ("newest")).IsTrue();
  }

  [TestCase]
  public void DevToDevMismatchIsAnUpdatePromptToo()
  {
    var message = World.VersionKickMessage ("0.8.127-dev.2", "0.8.127-dev.1");
    AssertBool (message.Contains ("newest")).IsTrue();
  }
}
