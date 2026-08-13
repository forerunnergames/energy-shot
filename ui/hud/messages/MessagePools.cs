using System.Collections.Generic;

namespace com.forerunnergames.energyshot.ui.hud;

// The unique message templates (issue #84), grouped into scenario pools.
// {v} = the player who got zapped out, {z} = the player who did the zapping,
// {youOrThey} = pronoun in fall messages. All short, sarcastic, & non-violent.
public static class MessagePools
{
  public static readonly List <string> Fall = new()
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

  public static readonly List <string> FallStreak = new()
  {
    "{v} & gravity are becoming close personal friends",
    "{v} is speedrunning the falling tutorial",
    "gravity has {v} on speed dial now"
  };

  public static readonly List <string> Zapped = new()
  {
    "{v} got thoroughly zapped by {z}",
    "{z} sent {v} back to the drawing board",
    "{v} took an unscheduled vacation, courtesy of {z}",
    "{z} politely escorted {v} out of the arena",
    "{v} learned a valuable lesson about standing near {z}",
    "{z} turned {v} into a cautionary tale",
    "{z} restarted {v} from the last checkpoint",
    "{v} has been recalled to the respawn factory, sincerely {z}"
  };

  // Full-charge zap-outs that pierced a wall or floor on the way (issue #94).
  public static readonly List <string> ThroughWall = new()
  {
    "{z} zapped {v} right through the wall. Rude",
    "{v} thought the wall would stop {z}. The wall had other plans",
    "{z} doesn't believe in walls. {v} believes in respawning now"
  };

  public static readonly List <string> FullCharge = new()
  {
    "{v} caught the full brunt of {z}'s science project",
    "{z} charged that one up just for {v}. How thoughtful",
    "{z} held that charge just long enough to ruin {v}'s afternoon",
    "{v} met the business end of {z}'s fully charged opinion",
    "{z} saved up all week & spent it all on {v}"
  };

  public static readonly List <string> FullAuto = new()
  {
    "{z} held the trigger down & {v} held the consequences",
    "{v} caught the spray & sadly also the pray",
    "{z} shredded {v} on full auto. Very economical",
    "{z} turned on the laser sprinkler & {v} forgot an umbrella",
    "{v} was reduced to a rounding error by {z}'s zap hose"
  };

  public static readonly List <string> Punch = new()
  {
    "{z} introduced {v} to the classics: bare knuckles",
    "{v} got respectfully decked by {z}",
    "{z} solved {v} with hands",
    "{v} ran out of health & {z} ran out of patience"
  };

  // The victim was holding a weapon & still got punched out.
  public static readonly List <string> PunchedOutArmed = new()
  {
    "{v} brought a laser to a fist fight & still lost",
    "{z} punched out {v}, weapon & all",
    "{v} was fully armed. {z} was fully unimpressed",
    "{z} disarmed {v} the old-fashioned way",
    "security escorted {v} out by hand. Security was {z}"
  };

  public static readonly List <string> FistsVsFists = new()
  {
    "{v} & {z} settled it like gentlemen. {v} lost",
    "no weapons, no problem: {z} out-boxed {v}",
    "{v} lost the arena's most polite brawl to {z}",
    "{z} won the fist symposium; {v} left early"
  };

  public static readonly List <string> BananaBlast = new()
  {
    "{v} was caught in {z}'s balanced breakfast",
    "{z} turned {v} into a banana split",
    "{v} slipped on {z}'s potassium special",
    "collateral fruit damage: {v}, courtesy of {z}"
  };

  // Direct sticky hit - the banana found its face.
  public static readonly List <string> BananaDirect = new()
  {
    "{v} took {z}'s banana straight to the face. Splat",
    "{z} stuck the banana landing. {v} did not",
    "direct hit: {v} is now 20% smoothie",
    "{v} caught {z}'s banana with their forehead",
    "{z}'s banana found {v} ripe for the picking",
    "airmail from {z}, signed for by {v}'s face"
  };

  public static readonly List <string> HoldingBananaGun = new()
  {
    "{v} went out clutching the banana gun. Priorities",
    "{v} respawned; the banana gun stayed loyal to the floor",
    "{v} had the banana gun & big plans. {z} had other ones",
    "{v} will be remembered for holding the banana gun really well"
  };

  // Banana splatter & punch blur on screen at the same time.
  public static readonly List <string> ComboSplatterPunch = new()
  {
    "{v} got the full spa treatment: banana peel & knuckle scrub",
    "splattered, punched, & respawned: {v} had a busy second",
    "{z} finished what the banana started on {v}",
    "{v} couldn't even see that punch coming. Banana in the eyes"
  };

  public static readonly List <string> JumpShot = new()
  {
    "{z} zapped {v} from mid-air. Showoff",
    "{z} rained on {v} from a personal altitude",
    "gravity was optional for {z}, unfortunately for {v}",
    "{z} hit the jump shot; {v} hit the respawn button"
  };

  public static readonly List <string> SlideShotKiller = new()
  {
    "{z} slid by & took {v}'s dignity with them",
    "{z} zapped {v} mid-slide. Style points awarded",
    "drive-by on knees: {z} claims {v}",
    "{z} power-slid into {v}'s evening & canceled it"
  };

  public static readonly List <string> SlideShotVictim = new()
  {
    "{v} slid gracefully into {z}'s line of fire",
    "{v}'s slide looked cool right up until {z}",
    "sliding didn't save {v}. {z} sends regards",
    "{v} was mid-slide, mid-zap, & is now mid-respawn"
  };

  public static readonly List <string> ZapStreakTier3 = new()
  {
    "{z} is on a roll. Someone should probably do something",
    "{z} keeps winning & it's getting awkward for everyone else",
    "{z} has decided the arena belongs to them now"
  };

  public static readonly List <string> ZapStreakTier5 = new()
  {
    "{z} is at five & counting. Form a committee",
    "five in a row: {z} is basically a weather event now",
    "{z} would like to thank everyone for their participation"
  };

  public static readonly List <string> ZapStreakTier7 = new()
  {
    "{z} has ascended. Please respawn accordingly",
    "seven & climbing: {z} pays rent on this arena now",
    "at this point the arena is just {z}'s screensaver"
  };

  // A big (5+) streak got ended - credit the ender.
  public static readonly List <string> StreakEnded = new()
  {
    "{z} unplugged {v}'s whole highlight reel",
    "{v}'s streak had a good run. {z} had a better shot",
    "breaking news: {z} put out the fire that was {v}",
    "{z} canceled {v}'s victory tour mid-encore"
  };

  // A modest (3-4) streak fizzled - mock the loser.
  public static readonly List <string> StreakLost = new()
  {
    "{v} was on fire. Was",
    "{v}'s streak has been returned to the manufacturer",
    "{v}'s streak is now a cautionary slideshow",
    "{v} dropped the streak. {z} helped"
  };

  public static readonly List <string> ZappedStreak = new()
  {
    "{v} is having one of those days",
    "{v} would like everyone to know they're just warming up",
    "{v} is generously boosting everyone else's confidence",
    "{v} is farming respawns at an industrial scale"
  };

  // Boomerang zap-outs (issue #98).
  public static readonly List <string> Boomerang = new()
  {
    "{z}'s boomerang made a round trip through {v}",
    "{v} stood on the flight path & {z}'s boomerang kept the schedule",
    "{z} threw it away & it still came back with {v}'s dignity",
    "{v} got clipped by {z}'s frisbee with commitment issues"
  };

  // Slingshot zap-outs (issue #99).
  public static readonly List <string> Slingshot = new()
  {
    "{z} slingshotted {v} straight back to the stone age",
    "{v} was zapped out by a pebble. {z} is very proud",
    "{z} drew, released, & {v}'s day unraveled",
    "physics homework by {z}: the arc ended on {v}"
  };

  // {z} zapped {v} with the weapon {v} dropped earlier.
  public static readonly List <string> TheftRevenge = new()
  {
    "{z} zapped {v} with {v}'s own hardware. Rude",
    "{v} donated a weapon & {z} returned it, hot",
    "{z} test-fired {v}'s old weapon on {v}. It works",
    "return to sender: {z} gave {v}'s weapon back the fun way"
  };

  // Registry so the unit test can verify all templates stay unique (issue #84).
  public static readonly IReadOnlyList <List <string>> All = new List <List <string>>
  {
    Fall, FallStreak, Zapped, ThroughWall, FullCharge, FullAuto, Punch, PunchedOutArmed, FistsVsFists,
    BananaBlast, BananaDirect, HoldingBananaGun, ComboSplatterPunch, JumpShot, SlideShotKiller,
    SlideShotVictim, ZapStreakTier3, ZapStreakTier5, ZapStreakTier7, StreakEnded, StreakLost,
    ZappedStreak, Boomerang, Slingshot, TheftRevenge
  };
}
