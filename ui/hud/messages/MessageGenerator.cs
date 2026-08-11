using System.Collections.Generic;
using com.forerunnergames.energyshot.players;
using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.ui.hud;

// Pure, static message picker (issues #16, #53, & #84): templates live in
// MessagePools; this class only selects a pool from the death context & fills in
// the names, so it stays unit-testable.
public static class MessageGenerator
{
  public const float FullChargeThreshold = EnergyWeapon.FullChargeEnergyThreshold;
  public const float BananaDirectThreshold = 0.85f;

  // @formatter:off
  private static readonly RandomNumberGenerator Rng = new();
  static MessageGenerator() => Rng.Randomize();
  public static string OnPlayerRespawnedFell (bool isSelf, string playerName, out int randomMessageIndex) => $"{YouOrNameCapital (isSelf, playerName)} {GetRandomFallMessage (YouOrThey (isSelf, playerName), out randomMessageIndex)}";
  public static string OnPlayerRespawnedFell (bool isSelf, string playerName, int messageIndex) => $"{YouOrNameCapital (isSelf, playerName)} {GetFallMessage (YouOrThey (isSelf, playerName), messageIndex)}";
  public static string OnZapStreak (string zapperName, int streak) => Fill (Pick (SelectZapStreakPool (streak)), "", zapperName);
  public static string OnZappedStreak (string victimName) => Fill (Pick (MessagePools.ZappedStreak), victimName, "");
  public static string OnFallStreak (string victimName) => Fill (Pick (MessagePools.FallStreak), victimName, "");
  public static string OnTheftRevenge (string victimName, string zapperName) => Fill (Pick (MessagePools.TheftRevenge), victimName, zapperName);
  public static string OnZapped (string victimName, string zapperName, DeathContext context) => Fill (Pick (SelectZappedPool (context)), victimName, zapperName);
  private static string Fill (string template, string victimName, string zapperName) => Capitalize (template.Replace ("{v}", victimName).Replace ("{z}", zapperName));
  private static string Capitalize (string message) => char.ToUpper (message[0]) + message[1..];
  private static string Pick (List <string> pool) => pool[Rng.RandiRange (0, pool.Count - 1)];
  private static string GetFallMessage (string youOrThey, int index) => MessagePools.Fall[index].Replace ("{youOrThey}", youOrThey);
  private static string YouOrNameCapital (bool isSelf, string playerName) => isSelf ? "You" : playerName;
  private static string YouOrThey (bool isSelf, string playerName) => isSelf ? "you" : "they";
  // @formatter:on

  // Streak announcements get spicier at 5 & 7+ (issue #84).
  public static List <string> SelectZapStreakPool (int streak)
  {
    if (streak >= 7) return MessagePools.ZapStreakTier7;
    if (streak >= 5) return MessagePools.ZapStreakTier5;
    return MessagePools.ZapStreakTier3;
  }

  // Most-specific-scenario-first (issue #84); public so the unit tests can verify
  // the selection directly.
  public static List <string> SelectZappedPool (DeathContext context)
  {
    if (context.VictimLostStreak >= 5) return MessagePools.StreakEnded;
    if (context.VictimLostStreak >= 3) return MessagePools.StreakLost;
    if (context.SplatterActive && context.BlurActive) return MessagePools.ComboSplatterPunch;
    if (context.Kind == DamageKind.Banana && context.Energy >= BananaDirectThreshold) return MessagePools.BananaDirect;
    if (context.Kind == DamageKind.Banana) return MessagePools.BananaBlast;
    if (context.Kind == DamageKind.Punch && context.VictimArmed) return MessagePools.PunchedOutArmed;
    if (context.Kind == DamageKind.Punch && context.KillerUnarmed) return MessagePools.FistsVsFists;
    if (context.Kind == DamageKind.Punch) return MessagePools.Punch;
    if (context.VictimHeldBananaGun) return MessagePools.HoldingBananaGun;
    if (context.Kind == DamageKind.Laser && context.ThroughBarrier) return MessagePools.ThroughWall; // Pierced a wall/floor first (issue #94).
    if (context.KillerSliding) return MessagePools.SlideShotKiller;
    if (context.VictimSliding) return MessagePools.SlideShotVictim;
    if (context.KillerAirborne) return MessagePools.JumpShot;
    if (context.Kind == DamageKind.FullAuto) return MessagePools.FullAuto;
    if (context.Kind == DamageKind.Laser && context.Energy >= FullChargeThreshold) return MessagePools.FullCharge;
    return MessagePools.Zapped;
  }

  private static string GetRandomFallMessage (string youOrThey, out int index)
  {
    index = Rng.RandiRange (0, MessagePools.Fall.Count - 1);
    return GetFallMessage (youOrThey, index);
  }
}
