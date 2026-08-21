using com.forerunnergames.energyshot.core.world;
using com.forerunnergames.energyshot.ui.hud.messages;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// Player chat (issue #188): the server's sanitizer & the HUD's BBCode escape are the
// two pure seams between a typing player & everyone's screen.
[TestSuite]
public class ChatTest
{
  [TestCase]
  public void SanitizerCapsAtOneHundredTwentyChars() => AssertInt (World.SanitizeChat (new string ('x', 500)).Length).IsEqual (World.MaxChatChars);

  [TestCase]
  public void SanitizerFlattensNewlinesAndTrims() => AssertString (World.SanitizeChat ("  hi\nthere\r\n ")).IsEqual ("hi there");

  [TestCase]
  public void SanitizerLeavesShortTextAlone() => AssertString (World.SanitizeChat ("gg")).IsEqual ("gg");

  [TestCase]
  public void EscapeNeutralizesSmuggledTags() => AssertString (ChatBox.EscapeBbcode ("[img]x[/img]")).IsEqual ("[lb]img]x[lb]/img]");
}
