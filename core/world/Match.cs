using System.Collections.Generic;
using System.Linq;
using com.forerunnergames.energyshot.ui.hud.messages;

namespace com.forerunnergames.energyshot.core.world;

// Rounds (issue #153), the pure part: when a round is over, who earns which
// superlative, & the scoreboard text. No nodes, no network - unit-tested directly.
public readonly record struct RoundStats (string Name, string ColorHex, int Zaps, int ZapOuts, int Assists, int Falls);

public static class Match
{
  public const int DefaultRoundMinutes = 5;
  public const int DefaultZapLimit = 20;
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

  // BBCode for the end-of-round overlay: a table of everybody's numbers, then the
  // superlatives. titleFor renders one award line (the generator picks the template).
  public static string BuildScoreboard (IReadOnlyList <RoundStats> stats, List <(List <string> Pool, string Name)> awards, System.Func <List <string>, string, string> titleFor)
  {
    var rows = stats.Select (s => $"[cell][color=#{s.ColorHex}]{s.Name}[/color][/cell][cell]{s.Zaps}[/cell][cell]{s.ZapOuts}[/cell][cell]{s.Assists}[/cell][cell]{s.Falls}[/cell]");
    var table = $"[table=5][cell][b]Player[/b][/cell][cell][b]Zaps[/b][/cell][cell][b]Zap-outs[/b][/cell][cell][b]Assists[/b][/cell][cell][b]Falls[/b][/cell]{string.Concat (rows)}[/table]";
    var titles = string.Join ("\n", awards.Select (award => titleFor (award.Pool, award.Name)));
    return $"[center][b]ROUND OVER[/b]\n\n{table}\n\n{titles}[/center]";
  }
}
