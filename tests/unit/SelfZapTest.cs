using com.forerunnergames.energyshot.players;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// A slung gun's spree hits its own slinger (issue #287) - fair & funny, but never a
// point: only somebody ELSE's zap scores.
[TestSuite]
public class SelfZapTest
{
  [TestCase]
  public void ZappingYourselfNeverScores() => AssertBool (Player.ScoresZap (7, 7)).IsFalse();

  [TestCase]
  public void ZappingSomeoneElseScores() => AssertBool (Player.ScoresZap (7, 9)).IsTrue();
}
