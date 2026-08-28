using System.Collections.Generic;
using System.Linq;
using com.forerunnergames.energyshot.ui.hud;

namespace com.forerunnergames.energyshot.core.world;

// Rounds (issue #153), the pure part: when a round is over, who earns which
// superlative, & the scoreboard text. No nodes, no network - unit-tested directly.
public readonly record struct RoundStats (string Name, string ColorHex, int Zaps, int ZapOuts, int Assists, int Falls);

// Zaps = classic deathmatch scoring; KingOfTheHill = points per second of sole hill
// occupancy (issue #44). Both share the round clock, the limits, & the scoreboard.
public enum GameMode { Zaps = 0, KingOfTheHill = 1 }

public static class Match
{
  public const int DefaultRoundMinutes = 5;
  public const int DefaultZapLimit = 20;
  // King of the Hill scores a point per second held, so 20 would end a round in
  // twenty seconds: 100 points is a real hold (Aaron, 2026-08-24).
  public const int DefaultHillPointLimit = 100;
  // Emptying the hill from OUTSIDE it pays a bounty (Aaron, 2026-08-28, issue #420):
  // every time your zap removes the LAST player standing in the zone while you are
  // not in it yourself, you earn this. Zaps only - knocking someone out of the zone,
  // or the last occupant wandering off, pays nobody. Winning without ever zoning is
  // a real strategy, by design.
  public const int HillClearBonusPoints = 5;

  public static bool IsHillClearBonus (bool victimInHill, bool attackerInHill, int othersStillInHill) => victimInHill && !attackerInHill && othersStillInHill == 0;

  // The hill ROTATES on its own clock, not just per round (Aaron, 2026-08-24):
  // hold it for a minute & it moves out from under you.
  public const int HillRotateSeconds = 60;
  public static int DefaultPointLimit (GameMode mode) => mode == GameMode.KingOfTheHill ? DefaultHillPointLimit : DefaultZapLimit;
  // The next hill is never the current one - a "rotation" that lands in place is
  // no rotation at all. roll is any non-negative random number.
  public static int NextSpotIndex (int current, int spotCount, int roll)
  {
    if (spotCount <= 1) return current;
    var span = spotCount - 1;
    var step = ((roll % span) + span) % span; // Non-negative even for a negative roll (C# % keeps the sign).
    return (current + 1 + step) % spotCount;
  }
  public const int MaxRoundMinutes = 60;
  public const int MaxZapLimit = 200;
  public const float IntermissionSeconds = 10.0f;

  // Either limit ends it; a limit of 0 means "no limit" on that axis.
  public static bool IsOver (float elapsedSeconds, int roundSeconds, int topScore, int zapLimit) => (roundSeconds > 0 && elapsedSeconds >= roundSeconds) || (zapLimit > 0 && topScore >= zapLimit);

  // One award per category, only when somebody actually did the thing (max > 0);
  // ties go to whoever is listed first (the caller passes leaderboard order).
  public static List <(List <string> Pool, string Name)> AwardTitles (IReadOnlyList <RoundStats> stats)
  {
    var awards = new List <(List <string>, string)>();
    Award (awards, MessagePools.TitleMostZaps, stats, s => s.Zaps);
    Award (awards, MessagePools.TitleMostZapOuts, stats, s => s.ZapOuts);
    Award (awards, MessagePools.TitleMostAssists, stats, s => s.Assists);
    Award (awards, MessagePools.TitleMostFalls, stats, s => s.Falls);
    return awards;
  }

  private static void Award (List <(List <string>, string)> awards, List <string> pool, IReadOnlyList <RoundStats> stats, System.Func <RoundStats, int> metric)
  {
    if (stats.Count == 0) return;
    var best = stats.MaxBy (metric);
    if (metric (best) <= 0) return;
    awards.Add ((pool, best.Name));
  }

  // Player names are player-typed & land in a BBCode label (CodeRabbit on #226): an
  // opening bracket renders as itself, never as a smuggled tag.
  public static string EscapeBbcode (string text) => text.Replace ("[", "[lb]");

  // BBCode for the end-of-round overlay: a table of everybody's numbers, then the
  // superlatives. titleFor renders one award line (the generator picks the template).
  public static string ScoreColumnLabel (GameMode mode) => mode == GameMode.KingOfTheHill ? "Hill pts" : "Zaps";

  public static string BuildScoreboard (IReadOnlyList <RoundStats> stats, List <(List <string> Pool, string Name)> awards, System.Func <List <string>, string, string> titleFor, GameMode mode = GameMode.Zaps)
  {
    var rows = stats.Select (s => $"[cell][color=#{s.ColorHex}]{EscapeBbcode (s.Name)}[/color]      [/cell][cell]{s.Zaps}      [/cell][cell]{s.ZapOuts}      [/cell][cell]{s.Assists}      [/cell][cell]{s.Falls}[/cell]");
    var table = $"[table=5][cell][b]Player[/b]      [/cell][cell][b]{ScoreColumnLabel (mode)}[/b]      [/cell][cell][b]Zap-outs[/b]      [/cell][cell][b]Assists[/b]      [/cell][cell][b]Falls[/b][/cell]{string.Concat (rows)}[/table]";
    var titles = string.Join ("\n", awards.Select (award => titleFor (award.Pool, EscapeBbcode (award.Name))));
    // Padded cells (Aaron, 2026-08-22): the bare table squashed its headers. Award
    // lines LEFT-align - the board label is already a centered block, & centering
    // the text again made it ragged & hard to read.
    return $"[center][b]ROUND OVER[/b][/center]\n\n{table}\n\n{titles}";
  }
}
