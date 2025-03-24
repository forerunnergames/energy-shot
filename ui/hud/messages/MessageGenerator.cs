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
    "learned how to fall with style"
  };

  // @formatter:off
  private static readonly RandomNumberGenerator Rng = new();
  static MessageGenerator() => Rng.Randomize();
  public static string OnShotPlayer (bool isSelf, string playerName, string shotPlayerName) => $"{YouOrNameCapital (isSelf, playerName)} shot {shotPlayerName}";
  public static string OnPlayerRespawnedShot (bool isSelf, string playerName, string shotByPlayerName) => $"{YouOrNameCapital (isSelf, playerName)} {WasOrWere (isSelf, playerName)} shot by {shotByPlayerName}";
  public static string OnPlayerRespawnedFell (bool isSelf, string playerName, out int randomMessageIndex) => $"{YouOrNameCapital (isSelf, playerName)} {GetRandomFallMessage (YouOrThey (isSelf, playerName), out randomMessageIndex)}";
  public static string OnPlayerRespawnedFell (bool isSelf, string playerName, int messageIndex) => $"{YouOrNameCapital (isSelf, playerName)} {GetFallMessage (YouOrThey (isSelf, playerName), messageIndex)}";
  private static string GetFallMessage (string youOrThey, int index) => FallMessageTemplates[index].Replace ("{youOrThey}", youOrThey);
  private static string YouOrName (bool isSelf, string playerName) => isSelf ? "you" : playerName;
  private static string YouOrNameCapital (bool isSelf, string playerName) => isSelf ? "You" : playerName;
  private static string YouOrThey (bool isSelf, string playerName) => isSelf ? "you" : "they";
  private static string YouOrTheyCapital (bool isSelf, string playerName) => isSelf ? "You" : "They";
  private static string WasOrWere (bool isSelf, string playerName) => isSelf ? "were" : "was";
  // @formatter:on

  private static string GetRandomFallMessage (string youOrThey, out int index)
  {
    index = Rng.RandiRange (0, FallMessageTemplates.Count - 1);
    return GetFallMessage (youOrThey, index);
  }
}
