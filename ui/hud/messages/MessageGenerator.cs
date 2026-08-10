using System.Collections.Generic;
using Godot;

namespace com.forerunnergames.energyshot.ui.hud;

public static class MessageGenerator
{
  private static readonly List <string> FallMessageTemplates = new()
  {
    "fell off the world",
    "found out the world was flat",
    "found out {youOrThey} couldn't fly",
    "didn't realize {youOrThey} were near the edge",
    "decided to respawn just for fun",
    "discovered gravity",
    "learned how to fall with style",
    "stepped confidently into the void",
    "forgot the floor was optional up there",
    "went to inspect the underside of the arena",
    "rage-quit the concept of standing"
  };

  // {v} = the player who got zapped out, {z} = the player who did the zapping.
  private static readonly List <string> ZappedTemplates = new()
  {
    "{v} got thoroughly zapped by {z}",
    "{z} sent {v} back to the drawing board",
    "{v} took an unscheduled vacation, courtesy of {z}",
    "{z} politely escorted {v} out of the arena",
    "{v} learned a valuable lesson about standing near {z}",
    "{z} turned {v} into a cautionary tale"
  };

  private static readonly List <string> FullChargeZappedTemplates = new()
  {
    "{v} caught the full brunt of {z}'s science project",
    "{z} charged that one up just for {v}. How thoughtful"
  };

  private static readonly List <string> ZapStreakTemplates = new()
  {
    "{z} is on a roll. Someone should probably do something",
    "{z} keeps winning & it's getting awkward for everyone else",
    "{z} has decided the arena belongs to them now"
  };

  private static readonly List <string> ZappedStreakTemplates = new()
  {
    "{v} is having one of those days",
    "{v} would like everyone to know they're just warming up",
    "{v} is generously boosting everyone else's confidence"
  };

  private static readonly List <string> FallStreakTemplates = new()
  {
    "{v} & gravity are becoming close personal friends",
    "{v} is speedrunning the falling tutorial"
  };

  // @formatter:off
  private static readonly RandomNumberGenerator Rng = new();
  static MessageGenerator() => Rng.Randomize();
  public static string OnPlayerRespawnedFell (bool isSelf, string playerName, out int randomMessageIndex) => $"{YouOrNameCapital (isSelf, playerName)} {GetRandomFallMessage (YouOrThey (isSelf, playerName), out randomMessageIndex)}";
  public static string OnPlayerRespawnedFell (bool isSelf, string playerName, int messageIndex) => $"{YouOrNameCapital (isSelf, playerName)} {GetFallMessage (YouOrThey (isSelf, playerName), messageIndex)}";
  public static string OnZapStreak (string zapperName) => Pick (ZapStreakTemplates).Replace ("{z}", zapperName);
  public static string OnZappedStreak (string victimName) => Pick (ZappedStreakTemplates).Replace ("{v}", victimName);
  public static string OnFallStreak (string victimName) => Pick (FallStreakTemplates).Replace ("{v}", victimName);
  private static string Pick (List <string> pool) => pool[Rng.RandiRange (0, pool.Count - 1)];
  private static string GetFallMessage (string youOrThey, int index) => FallMessageTemplates[index].Replace ("{youOrThey}", youOrThey);
  private static string YouOrNameCapital (bool isSelf, string playerName) => isSelf ? "You" : playerName;
  private static string YouOrThey (bool isSelf, string playerName) => isSelf ? "you" : "they";
  // @formatter:on

  // One random zapped-out message; "you" substituted for whichever role the local
  // player holds. Full-charge finishes get their own pool.
  public static string OnZapped (string victimName, string zapperName, bool selfIsVictim, bool selfIsZapper, bool fullCharge)
  {
    var template = Pick (fullCharge ? FullChargeZappedTemplates : ZappedTemplates);
    var message = template.Replace ("{v}", selfIsVictim ? "you" : victimName).Replace ("{z}", selfIsZapper ? "you" : zapperName);
    return char.ToUpper (message[0]) + message[1..];
  }

  private static string GetRandomFallMessage (string youOrThey, out int index)
  {
    index = Rng.RandiRange (0, FallMessageTemplates.Count - 1);
    return GetFallMessage (youOrThey, index);
  }
}
