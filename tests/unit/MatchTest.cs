using System.Collections.Generic;
using com.forerunnergames.energyshot.core.world;
using com.forerunnergames.energyshot.ui.hud;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// Rounds (issue #153): the end condition & the superlative rules are pure, so they
// get pinned here - a limit of 0 disables that axis, awards need a real max, ties go
// to leaderboard order.
[TestSuite]
public class MatchTest
{
  private static RoundStats Stats (string name, int zaps, int zapOuts, int assists, int falls) => new(name, "ffffff", zaps, zapOuts, assists, falls);

  [TestCase]
  public void TimeLimitEndsTheRound() => AssertBool (Match.IsOver (300.0f, 300, 3, 20)).IsTrue();

  [TestCase]
  public void ZapLimitEndsTheRound() => AssertBool (Match.IsOver (10.0f, 300, 20, 20)).IsTrue();

  [TestCase]
  public void ZeroLimitsMeanNoEnd() => AssertBool (Match.IsOver (99999.0f, 0, 999, 0)).IsFalse();

  [TestCase]
  public void NoAwardWithoutADeed()
  {
    var awards = Match.AwardTitles (new List <RoundStats> { Stats ("a", 0, 0, 0, 0), Stats ("b", 0, 0, 0, 0) });
    AssertInt (awards.Count).IsEqual (0);
  }

  [TestCase]
  public void EveryCategoryGoesToItsLeaderAndTiesToListOrder()
  {
    var awards = Match.AwardTitles (new List <RoundStats> { Stats ("a", 5, 2, 1, 1), Stats ("b", 5, 7, 3, 1) });
    AssertInt (awards.Count).IsEqual (4);
    AssertObject (awards[0].Pool).IsSame (MessagePools.TitleMostZaps);
    AssertString (awards[0].Name).IsEqual ("a"); // Tie on zaps: first listed.
    AssertString (awards[1].Name).IsEqual ("b"); // Most zap-outs.
    AssertString (awards[2].Name).IsEqual ("b"); // Most assists.
    AssertString (awards[3].Name).IsEqual ("a"); // Tie on falls: first listed.
  }

  [TestCase]
  public void ScoreboardEscapesSmuggledTagsInNames()
  {
    var stats = new List <RoundStats> { Stats ("[img]x[/img]", 1, 0, 0, 0) };
    AssertBool (Match.BuildScoreboard (stats, Match.AwardTitles (stats), (pool, name) => name).Contains ("[img]")).IsFalse();
  }

  [TestCase]
  public void ScoreboardCarriesEveryPlayerAndTitle()
  {
    var stats = new List <RoundStats> { Stats ("alpha", 3, 1, 0, 0), Stats ("beta", 1, 3, 0, 2) };
    var board = Match.BuildScoreboard (stats, Match.AwardTitles (stats), (pool, name) => $"TITLE:{name}");
    AssertBool (board.Contains ("alpha") && board.Contains ("beta") && board.Contains ("TITLE:alpha") && board.Contains ("TITLE:beta") && board.Contains ("[table=5]")).IsTrue();
  }
}
