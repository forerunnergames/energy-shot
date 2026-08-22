using com.forerunnergames.energyshot.players;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// Issue #290: hold mode tracks the button exactly; toggle mode flips per fresh
// press; losing the gun always unscopes.
[TestSuite]
public class ScopeHoldTest
{
  [TestCase]
  public void HoldModeTracksTheButton()
  {
    AssertBool (Player.NextScoped (canScope: true, holdMode: true, pressed: true, justPressed: true, current: false)).IsTrue();
    AssertBool (Player.NextScoped (canScope: true, holdMode: true, pressed: false, justPressed: false, current: true)).IsFalse();
  }

  [TestCase]
  public void ToggleModeFlipsPerPress()
  {
    AssertBool (Player.NextScoped (canScope: true, holdMode: false, pressed: true, justPressed: true, current: false)).IsTrue();
    AssertBool (Player.NextScoped (canScope: true, holdMode: false, pressed: true, justPressed: true, current: true)).IsFalse();
    AssertBool (Player.NextScoped (canScope: true, holdMode: false, pressed: false, justPressed: false, current: true)).IsTrue(); // Held state persists.
  }

  [TestCase]
  public void LosingTheGunUnscopes() => AssertBool (Player.NextScoped (canScope: false, holdMode: true, pressed: true, justPressed: false, current: true)).IsFalse();
}
