using com.forerunnergames.energyshot.core.audio;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// Own-vote memory transitions (issue #162): the all-time totals hold one vote per
// player per track - a fresh vote adds it, switching stance moves it, & a repeat
// of the same vote changes nothing, so nothing ever double-counts.
[TestSuite]
public class MusicVoteMemoryTest
{
  [TestCase]
  public void FirstUpVoteAddsOneUp() => AssertBool (MusicManager.AdjustTotals ((0, 0), previousVote: 0, newVote: 1) == (1, 0)).IsTrue();

  [TestCase]
  public void FirstDownVoteAddsOneDown() => AssertBool (MusicManager.AdjustTotals ((0, 0), previousVote: 0, newVote: -1) == (0, 1)).IsTrue();

  [TestCase]
  public void UpToDownSwitchMovesTheVote() => AssertBool (MusicManager.AdjustTotals ((3, 2), previousVote: 1, newVote: -1) == (2, 3)).IsTrue();

  [TestCase]
  public void DownToUpSwitchMovesTheVote() => AssertBool (MusicManager.AdjustTotals ((3, 2), previousVote: -1, newVote: 1) == (4, 1)).IsTrue();

  [TestCase]
  public void RepeatedUpVoteChangesNothing() => AssertBool (MusicManager.AdjustTotals ((3, 2), previousVote: 1, newVote: 1) == (3, 2)).IsTrue();

  [TestCase]
  public void RepeatedDownVoteChangesNothing() => AssertBool (MusicManager.AdjustTotals ((3, 2), previousVote: -1, newVote: -1) == (3, 2)).IsTrue();
}
