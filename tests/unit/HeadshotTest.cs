using com.forerunnergames.energyshot.players;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// Headshots (issue #179): the flat dome damage must one-shot Expert (200) &
// Intermediate (300) & take exactly two on a Beginner (400) - pinned against the
// real health tiers so a retune of either side can't silently break the promise.
[TestSuite]
public class HeadshotTest
{
  [TestCase]
  public void OneHeadshotZapsExpertAndIntermediate()
  {
    AssertBool (Player.HeadshotDamage >= Player.MaxHealthFor (2)).IsTrue();
    AssertBool (Player.HeadshotDamage >= Player.MaxHealthFor (1)).IsTrue();
  }

  [TestCase]
  public void BeginnerSurvivesOneHeadshotButNotTwo()
  {
    AssertBool (Player.HeadshotDamage < Player.MaxHealthFor (0)).IsTrue();
    AssertBool (2 * Player.HeadshotDamage >= Player.MaxHealthFor (0)).IsTrue();
  }

  [TestCase]
  public void HeadHitboxSitsAboveTheBody() => AssertFloat (HeadHitbox.LocalOffset.Y - HeadHitbox.Radius).IsGreater (2.0f);
}
