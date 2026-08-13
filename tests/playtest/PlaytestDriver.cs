using System;
using System.Linq;
using System.Threading.Tasks;
using com.forerunnergames.energyshot.core.audio;
using com.forerunnergames.energyshot.core.world;
using com.forerunnergames.energyshot.players;
using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.playtest;

// Automated multiplayer playtest: three headless instances (host, shooter, victim)
// drive the real game end-to-end - join, replication, movement, charged laser kills,
// respawn, spawn armor, fire-rate cap & full-auto - and exit 0/1 for CI.
// Activated by launching with: godot --headless --path . -- --playtest <role> [--address a] [--port n]
public partial class PlaytestDriver : Node
{
  private const int Port = 55599;
  // Fixed, deterministic game password (issue #90): exercises the server-side check.
  private const string Password = "playtest-secret";
  private const string HostName = "Host";
  private const string ShooterName = "Shooter";
  private const string VictimName = "Victim";
  // Distinct chosen body colors per role (issue #43), asserted to replicate everywhere.
  private const int HostColor = 1;
  private const int ShooterColor = 3;
  private const int VictimColor = 5;
  private World _world = null!;
  private string _role = string.Empty;
  private string _address = "127.0.0.1";
  private int _boltsSpawned;
  private int _boomerangsSpawned;
  private int _stonesSpawned;
  private int _airplanesSpawned;
  private Player? _self;
  private Player Self => _self ??= _world.GetPlayers().First (player => player.IsMultiplayerAuthority());
  private MusicManager Music => _world.GetNode <MusicManager> ("MusicManager");
  private Player? FindPlayer (string name) => _world.GetPlayers().FirstOrDefault (player => player.DisplayName == name);

  public override void _Ready()
  {
    // Three instances share the CI runner; uncapped frame loops starve physics &
    // ENet, dilating in-game time far behind the wall clock this driver waits on.
    Engine.MaxFps = 30;
    _world = GetNode <World> ("/root/World");
    _world.ChildEnteredTree += node => _boltsSpawned += node is LaserBolt ? 1 : 0;
    _world.ChildEnteredTree += node => _boomerangsSpawned += node is BoomerangProjectile ? 1 : 0;
    _world.ChildEnteredTree += node => _stonesSpawned += node is SlingshotStone ? 1 : 0;
    _world.ChildEnteredTree += node => _airplanesSpawned += node is PaperAirplaneProjectile ? 1 : 0;
    var args = OS.GetCmdlineUserArgs();
    _role = ArgValue (args, "--playtest") ?? string.Empty;
    _address = ArgValue (args, "--address") ?? "127.0.0.1";
    GD.Print ($"PLAYTEST: starting role [{_role}]");
    RunScenario();
  }

  private static string? ArgValue (string[] args, string name)
  {
    var index = Array.IndexOf (args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
  }

  private async void RunScenario()
  {
    try
    {
      await Task.Delay (100); // Let the world finish _Ready wiring.

      switch (_role)
      {
        case "host":
          await RunHost();
          break;
        case "shooter":
          await RunShooter();
          break;
        case "victim":
          await RunVictim();
          break;
        default:
          Fail ($"Unknown playtest role [{_role}]");
          return;
      }

      GD.Print ($"PLAYTEST PASS [{_role}]");
      await Task.Delay (500); // Let final packets flush before quitting.
      GetTree().Quit (0);
    }
    catch (Exception e)
    {
      Fail (e.Message);
    }
  }

  private void Fail (string reason)
  {
    GD.PrintErr ($"PLAYTEST FAIL [{_role}]: {reason}");
    GetTree().Quit (1);
  }

  // ---------------------------------------------------------------- roles

  private async Task RunHost()
  {
    _world.StartHostSession (HostName, difficulty: 2, Port, Password, HostColor);
    await WaitUntil (() => _world.GetPlayers().Count() == 3, 60, "all 3 players joined");
    // Chosen body colors (issue #43): own pick stuck & both clients' picks replicate to the host.
    Assert (Self.ColorIndex == HostColor, $"own chosen color is {HostColor}, got {Self.ColorIndex}");
    await WaitUntil (() => FindPlayer (ShooterName)?.ColorIndex == ShooterColor && FindPlayer (VictimName)?.ColorIndex == VictimColor, 15, "clients' chosen colors replicated to host (#43)");
    // Exactly one player wears the crown even at 0-0 (issue #107).
    await WaitUntil (() => _world.GetPlayers().Count (player => player.IsCrowned) == 1, 10, "exactly one player crowned at 0-0");
    // Server-measured pings replicate back to every peer (issue #100).
    await WaitUntil (() => FindPlayer (ShooterName)?.PingMs >= 0, 15, "shooter's ping measured & replicated to host");
    // Synced music (issue #137): the server picked a track & the shooter's thumbs-up
    // vote came back through the server tally.
    await WaitUntil (() => Music.CurrentTrackTitle.Length > 0, 15, "music track started on the server");
    await WaitUntil (() => Music.CurrentUpVotes == 1, 30, "shooter's music vote tallied on host");
    // Shooter kills victim once (plus possibly the host itself in the line of
    // fire); wait to observe the replicated score.
    await WaitUntil (() => FindPlayer (ShooterName)?.Score >= 1, 120, "shooter's kill replicated to host");
    // Victim respawns with armor visible to the host too.
    await WaitUntil (() => FindPlayer (VictimName)?.SpawnArmor == true, 30, "victim respawn armor replicated to host");
    // The victim's fall at score 0 goes negative & replicates (issue #108).
    await WaitUntil (() => FindPlayer (VictimName)?.Score == -1, 60, "victim's fall penalty (-1) replicated to host");
    // Stay up until both clients have finished & disconnected (the shooter's solo
    // phases now end with the paper airplane throw & catch, issue #102).
    await WaitUntil (() => _world.GetPlayers().Count() == 1, 180, "clients disconnected");
  }

  private async Task RunShooter()
  {
    _world.StartClientSession (ShooterName, difficulty: 1, _address, Port, Password, ShooterColor);
    await WaitUntil (() => _world.GetPlayers().Count() == 3, 60, "all 3 players visible");
    // DisplayName is an on-change sync that can land a beat after the spawn itself
    // under CI load (issue #78): wait for both names before dereferencing the
    // lookups, or FindPlayer returns null right here.
    await WaitUntil (() => FindPlayer (VictimName) != null && FindPlayer (HostName) != null, 30, "victim's & host's names replicated to shooter");
    var victim = FindPlayer (VictimName)!;
    var host = FindPlayer (HostName)!;
    Assert (victim.MaxHealth == 400, $"victim MaxHealth replicated as Beginner 400, got {victim.MaxHealth}");
    Assert (host.MaxHealth == 200, $"host MaxHealth replicated as Expert 200, got {host.MaxHealth}");
    Assert (Self.MaxHealth == 300, $"own MaxHealth is Intermediate 300, got {Self.MaxHealth}");
    // Chosen body colors (issue #43): everyone's pick replicates to this peer.
    Assert (Self.ColorIndex == ShooterColor, $"own chosen color is {ShooterColor}, got {Self.ColorIndex}");
    await WaitUntil (() => victim.ColorIndex == VictimColor && host.ColorIndex == HostColor, 15, "victim's & host's chosen colors replicated to shooter (#43)");
    Assert (Self.HeldWeapon == HeldWeapon.None, "spawned unarmed (#72)");
    // The server measures our ping & tells us within a tick or two (issue #100).
    await WaitUntil (() => Self.PingMs >= 0, 15, "own ping measured by the server");

    // Synced music (issue #137): the server's track choice reached this client; a
    // thumbs-up vote here must show up on every other peer's tally.
    await WaitUntil (() => Music.CurrentTrackTitle.Length > 0, 15, "current music track synced from server");
    Music.SubmitVote (isUpVote: true);

    // Movement: hold forward briefly & verify we actually moved.
    var startPosition = Self.GlobalPosition;
    Input.ActionPress ("move_forward");
    await Task.Delay (600);
    Input.ActionRelease ("move_forward");
    Assert (Self.GlobalPosition.DistanceTo (startPosition) > 0.3f, "player moved with input");

    // Wait out everyone's initial spawn armor before the damage phase.
    await WaitUntil (() => !victim.SpawnArmor, 15, "victim's initial spawn armor expired");

    // Chosen color on the body (issue #43): with armor's white glow gone, the victim's
    // per-instance body material must show exactly its picked palette color.
    var victimBody = (StandardMaterial3D)victim.GetNode <MeshInstance3D> ("MeshInstance3D").GetSurfaceOverrideMaterial (0);
    Assert (victimBody.AlbedoColor.IsEqualApprox (PlayerColors.At (VictimColor)), "victim's body tinted with its chosen color (#43)");

    // Streak replication (#88): the victim simulates an active 3-streak; it must
    // replicate here so the on-fire glow & pulsing leaderboard entry appear.
    await WaitUntil (() => victim.ZapStreakCount == 3, 15, "victim's simulated 3-streak replicated to shooter");

    // Dance emote (#103): the victim started dancing after its armor expired; the
    // replicated state must animate this puppet copy too.
    await WaitUntil (() => victim.Dancing, 15, "victim's dance replicated to shooter (#103)");

    // Punch phase: walk up to the victim & punch them; verify melee damage lands.
    // Fists are weapon slot 1 & punching requires them selected (issue #82); unarmed
    // players already default to fists, so this press is just a defensive re-select.
    PressAction ("weapon_1");
    await Task.Delay (100);
    ReleaseAction ("weapon_1");
    var healthBeforePunch = victim.Health;
    await WaitUntil (() => ApproachedVictim (victim), 30, "walked into punch range of victim");

    for (var attempt = 0; attempt < 10 && victim.Health >= healthBeforePunch; ++attempt)
    {
      AimAt (victim.GlobalPosition + Vector3.Up);
      PressAction ("punch");
      await Task.Delay (80);
      ReleaseAction ("punch");
      await Task.Delay (700);
    }

    Assert (victim.Health < healthBeforePunch, $"punch damaged the victim ({healthBeforePunch} -> {victim.Health})");
    // Damage cancels the dance (#103) & the cancel must replicate back here too.
    await WaitUntil (() => !victim.Dancing, 5, "victim's dance cancel replicated to shooter (#103)");
    // Sync property index 14 (#88): the punch stun must replicate to the shooter's copy.
    await WaitUntil (() => victim.StunFactor > 0.0f, 2, "victim's punch stun replicated to shooter");

    // Hit-flash restoration (#43): the punch just flashed this puppet's body dark
    // red; the flash must settle back onto the exact chosen palette color - the
    // riskiest base-color path, since it used to restore a hardcoded default.
    await WaitUntil (() => victimBody.AlbedoColor.IsEqualApprox (PlayerColors.At (VictimColor)), 15, "victim's body returned to its chosen color after the hit flash (#43)");

    // Weapon lifecycle (#72): everyone spawns unarmed, so collect the deterministic
    // laser pickup the WeaponSpawner keeps in the spawn room in --playtest mode.
    await WaitUntil (() => WalkedTo (WeaponSpawner.PlaytestLaserPosition), 45, "walked to the playtest laser pickup");
    await WaitUntil (() => Self.Holds (HeldWeapon.Laser), 15, "collected the laser pickup");
    Assert (Self.SelectedWeapon == SelectedWeapon.Laser, "pickup auto-equipped the laser (#128)");

    // The laser is weapon slot 2 (issue #82); every pickup auto-equips (#128), so
    // this press is just a defensive re-select before the shooting phases.
    PressAction ("weapon_2");
    await Task.Delay (100);
    ReleaseAction ("weapon_2");

    // Full-charge shots until one lands: a full-charge hit is a one-hit kill (#93),
    // so the first bolt that connects scores (shooter is told via NotifyScored -> Score).
    // Retry until a bolt actually spawns each attempt - under CI load, physics time
    // (which drives the weapon cooldown) can lag the wall clock these delays use.
    // Count only kills of the designated victim: a full-charge bolt one-hit kills
    // ANY player (#93), so the host wandering into the line of fire must not end
    // the loop early & leave the victim alive.
    var kills = 0;
    Self.Scored += (_, shotPlayerName) => { if (shotPlayerName == victim.DisplayName) ++kills; };
    // The 5s respawn-armor window can elapse during the shot loop's waits, so watch
    // for it continuously instead of only checking after the loop.
    var respawnArmorSeen = false;
    WatchForRespawnArmor();

    async void WatchForRespawnArmor()
    {
      while (!respawnArmorSeen && IsInsideTree())
      {
        respawnArmorSeen = kills > 0 && victim.SpawnArmor;
        await Task.Delay (50);
      }
    }

    for (var attempt = 0; attempt < 25 && kills == 0; ++attempt)
    {
      AimAt (victim.GlobalPosition + Vector3.Up);
      var boltsBeforeShot = _boltsSpawned;
      await ChargeAndFire (chargeSeconds: 2.3f);
      await TryWaitUntil (() => _boltsSpawned > boltsBeforeShot || kills > 0, 3);
      GD.Print ($"PLAYTEST: shot attempt {attempt}: bolts fired {_boltsSpawned}, victim health {victim.Health}");
    }

    Assert (kills == 1, "scored a kill with charged shots");
    // >= 1: an incidental one-hit kill on the host in the line of fire also scores.
    Assert (Self.Score >= 1, $"own replicated Score is >= 1, got {Self.Score}");

    // Victim must come back armored in the spawn room.
    await WaitUntil (() => respawnArmorSeen, 15, "victim respawned with spawn armor");

    // Streak glow bug (#88): the kill ended the victim's streak; the reset must
    // replicate here so the glow & pulsing leaderboard entry clear.
    await WaitUntil (() => victim.ZapStreakCount == 0, 15, "victim's streak reset replicated to shooter");

    // Third-person view (#119): toggle mid-run so the fire-rate & full-auto phases
    // below prove bolts still spawn from the aim ray with the chase camera live.
    // Toggle-until-third-person (instead of a single press) absorbs a persisted
    // third-person preference (the view survives restarts by design): whatever the
    // starting view, the phases below must run in third person.
    var startedThirdPerson = Self.IsThirdPerson;
    await ToggleViewUntil (thirdPerson: true);
    Assert (Self.IsThirdPerson, "third-person view toggled on (#119)");

    // Fire-rate cap: spamming can spawn at most 1 bolt (cooldown blocks recharging).
    AimAt (Self.GlobalPosition + new Vector3 (0, 1, 10)); // Aim away from everyone.
    var boltsBefore = _boltsSpawned;

    for (var i = 0; i < 5; ++i)
    {
      await ChargeAndFire (chargeSeconds: 0.05f);
      await Task.Delay (50);
    }

    // 5 spam clicks in ~1.1s with a 0.5s cooldown legitimately allows up to ~3 shots;
    // the cap is broken only if most clicks got through.
    Assert (_boltsSpawned - boltsBefore <= 3, $"fire-rate cap held under spam, got {_boltsSpawned - boltsBefore} of 5 clicks through");

    // Full-auto: after cooldown, ability + held trigger must fire a burst of bolts.
    await Task.Delay (1200);
    boltsBefore = _boltsSpawned;
    PressAction ("ability");
    await Task.Delay (50);
    ReleaseAction ("ability");
    PressAction ("shoot");
    await Task.Delay (1300);
    ReleaseAction ("shoot");
    Assert (_boltsSpawned - boltsBefore >= 3, $"full-auto fired a burst, got {_boltsSpawned - boltsBefore} bolts");

    // Slide cancels (#131): with the slide key still held (simulating a wedged
    // pressed state), switch weapons mid-slide, then press crouch - the crouch must
    // cancel the slide into a crouch, the slide must not restart, & a final crouch
    // press must stand the player back up.
    for (var attempt = 0; attempt < 10 && !Self.Sliding; ++attempt)
    {
      ReleaseAction ("slide");
      await Task.Delay (100);
      PressAction ("slide");
      await Task.Delay (200);
    }

    Assert (Self.Sliding, "slide started (#131)");
    PressAction ("weapon_1");
    await Task.Delay (100);
    ReleaseAction ("weapon_1");
    Assert (Self.Sliding, "weapon switch mid-slide left the slide intact (#131)");
    PressAction ("crouch");
    await Task.Delay (100);
    ReleaseAction ("crouch");
    await WaitUntil (() => !Self.Sliding && Self.Crouching, 5, "crouch press canceled the slide into a crouch (#131)");
    ReleaseAction ("slide");
    await Task.Delay (200);
    Assert (!Self.Sliding, "canceled slide did not restart from the held key (#131)");
    PressAction ("crouch");
    await Task.Delay (100);
    ReleaseAction ("crouch");
    await WaitUntil (() => !Self.Crouching, 5, "stood back up after the canceled slide (#131)");

    // Dance emote (#103): G starts the groove & any movement input cancels it,
    // restoring the normal standing state.
    PressAction ("dance");
    await Task.Delay (100);
    ReleaseAction ("dance");
    await WaitUntil (() => Self.Dancing, 5, "own dance started on G (#103)");
    Input.ActionPress ("move_forward");
    await Task.Delay (300);
    Input.ActionRelease ("move_forward");
    await WaitUntil (() => !Self.Dancing, 5, "moving canceled the dance (#103)");

    // Boomerang (#98): collect the deterministic spawn-room pickup, throw it (aimed
    // away from everyone so no incidental steals), & watch it fly back into the hand.
    await WaitUntil (() => WalkedTo (WeaponSpawner.PlaytestBoomerangPosition), 45, "walked to the playtest boomerang pickup");
    await WaitUntil (() => Self.Holds (HeldWeapon.Boomerang), 15, "collected the boomerang pickup (#98)");
    PressAction ("weapon_4");
    await Task.Delay (100);
    ReleaseAction ("weapon_4");
    Assert (Self.SelectedWeapon == SelectedWeapon.Boomerang, "boomerang selected in slot 4 (#98)");
    AimAt (Self.GlobalPosition + new Vector3 (10, 1, 0));
    // The spawn-room round trip can finish faster than a poll, so count spawned
    // projectiles (like bolts) instead of sampling the transient in-flight state.
    var boomerangsBefore = _boomerangsSpawned;

    for (var attempt = 0; attempt < 10 && _boomerangsSpawned == boomerangsBefore; ++attempt)
    {
      PressAction ("shoot");
      await Task.Delay (80);
      ReleaseAction ("shoot");
      await Task.Delay (300);
    }

    Assert (_boomerangsSpawned > boomerangsBefore, "boomerang thrown (#98)");
    // Still held + no longer in flight = the return trip ended in an auto-catch; a
    // lost boomerang would have cleared the held flag instead (#98).
    await WaitUntil (() => Self.Holds (HeldWeapon.Boomerang) && !Self.IsBoomerangInFlight, 30, "boomerang returned & was auto-caught (#98)");

    // Slingshot (#99): collect the deterministic spawn-room pickup, then draw the
    // band (hold shoot) & release - a stone projectile must spawn.
    await WaitUntil (() => WalkedTo (WeaponSpawner.PlaytestSlingshotPosition), 45, "walked to the playtest slingshot pickup");
    await WaitUntil (() => Self.Holds (HeldWeapon.Slingshot), 15, "collected the slingshot pickup (#99)");
    PressAction ("weapon_5");
    await Task.Delay (100);
    ReleaseAction ("weapon_5");
    Assert (Self.SelectedWeapon == SelectedWeapon.Slingshot, "slingshot selected in slot 5 (#99)");
    AimAt (Self.GlobalPosition + new Vector3 (10, 1, 0)); // Aim away from everyone.
    var stonesBefore = _stonesSpawned;

    for (var attempt = 0; attempt < 10 && _stonesSpawned == stonesBefore; ++attempt)
    {
      PressAction ("shoot");
      await Task.Delay (600); // Hold to draw (#99); release slings the stone.
      ReleaseAction ("shoot");
      await Task.Delay (300);
    }

    Assert (_stonesSpawned > stonesBefore, "slingshot draw & release fired a stone (#99)");

    // Paper airplane (#102): collect the deterministic spawn-room pickup, walk near
    // the victim, & throw with them locked under the crosshair; the victim
    // punch-catches the incoming glider & the handoff swaps it into their hands.
    await WaitUntil (() => WalkedTo (WeaponSpawner.PlaytestAirplanePosition), 45, "walked to the playtest paper airplane pickup");
    await WaitUntil (() => Self.Holds (HeldWeapon.PaperAirplane), 15, "collected the paper airplane pickup (#102)");
    PressAction ("weapon_6");
    await Task.Delay (100);
    ReleaseAction ("weapon_6");
    Assert (Self.SelectedWeapon == SelectedWeapon.PaperAirplane, "paper airplane selected in slot 6 (#102)");
    // The victim fell & respawned earlier; wait for it to be back in the spawn room,
    // then throw from close by so the host can't wander into the flight path.
    await WaitUntil (() => victim.GlobalPosition.Y > 20.0f, 60, "victim back in the spawn room for the catch phase (#102)");
    await WaitUntil (() => WalkedTo (victim.GlobalPosition, reach: 6.0f), 45, "walked near the victim for the airplane throw (#102)");
    var airplanesBefore = _airplanesSpawned;

    for (var attempt = 0; attempt < 10 && _airplanesSpawned == airplanesBefore; ++attempt)
    {
      AimAt (victim.GlobalPosition + Vector3.Up);
      PressAction ("shoot");
      await Task.Delay (80);
      ReleaseAction ("shoot");
      await Task.Delay (300);
    }

    Assert (_airplanesSpawned > airplanesBefore, "paper airplane thrown at the victim (#102)");
    // The victim's punch-catch (or a landing beside them) hands the airplane over:
    // our replicated held flag clears & theirs sets (#102).
    await WaitUntil (() => !Self.Holds (HeldWeapon.PaperAirplane), 30, "airplane left our hands after the flight (#102)");
    await WaitUntil (() => victim.Holds (HeldWeapon.PaperAirplane), 60, "victim holds the caught paper airplane (#102)");

    // The toggle persists to the shared user settings (#119); restore the starting
    // view so a playtest run never flips the developer's real preference.
    await ToggleViewUntil (startedThirdPerson);
  }

  // Presses V until the view matches; the toggle only persists on a real key press,
  // so this exercises the exact input path a player uses (#119).
  private async Task ToggleViewUntil (bool thirdPerson)
  {
    for (var attempt = 0; attempt < 2 && Self.IsThirdPerson != thirdPerson; ++attempt)
    {
      PressAction ("toggle_view");
      await Task.Delay (100);
      ReleaseAction ("toggle_view");
      await Task.Delay (200);
    }
  }

  private async Task RunVictim()
  {
    // Password enforcement (issue #109): a wrong password must get kicked with
    // "Wrong password." before the real join succeeds.
    await AssertWrongPasswordIsKicked();
    _world.StartClientSession (VictimName, difficulty: 0, _address, Port, Password, VictimColor);
    await WaitUntil (() => _world.GetPlayers().Count() == 3, 60, "all 3 players visible");
    Assert (Self.MaxHealth == 400, $"own MaxHealth is Beginner 400, got {Self.MaxHealth}");
    // Chosen body colors (issue #43): own pick stuck & both peers' picks replicate to the victim.
    Assert (Self.ColorIndex == VictimColor, $"own chosen color is {VictimColor}, got {Self.ColorIndex}");
    await WaitUntil (() => FindPlayer (ShooterName)?.ColorIndex == ShooterColor && FindPlayer (HostName)?.ColorIndex == HostColor, 15, "shooter's & host's chosen colors replicated to victim (#43)");
    Assert (Self.SpawnArmor, "spawned with spawn armor");
    // Synced music (issue #137): same track as everyone & the shooter's vote
    // propagated here through the server broadcast.
    await WaitUntil (() => Music.CurrentTrackTitle.Length > 0, 15, "current music track synced from server");
    await WaitUntil (() => Music.CurrentUpVotes == 1, 30, "shooter's music vote visible to victim");
    await WaitUntil (() => !Self.SpawnArmor, 15, "spawn armor expired on its own");
    // Streak replication (#88): simulate an active 3-streak on our own authority so
    // the shooter can verify it replicates - & that the death reset replicates too.
    Self.ZapStreakCount = 3;
    // Dance emote (#103): groove on G; the shooter verifies the replicated state on
    // its puppet copy, & the punch damage below must cancel the dance.
    PressAction ("dance");
    await Task.Delay (100);
    ReleaseAction ("dance");
    await WaitUntil (() => Self.Dancing, 5, "dance started on G (#103)");
    // The shooter opens fire once armor drops; verify damage & then a full respawn.
    await WaitUntil (() => Self.Health < Self.MaxHealth, 120, "took damage from shooter");
    Assert (!Self.Dancing, "taking damage canceled the dance (#103)");
    // One-hit-kill (#93): after the punch phase, the shooter only fires full-charge
    // shots, & a full-charge shot is lethal on any target - so no partial-damage
    // health value may ever appear between the punch & the respawn reset.
    var partialLaserHits = 0;
    var healthAfterPunch = Self.Health;
    Self.HealthChanged += value => partialLaserHits += value > 0 && value < healthAfterPunch ? 1 : 0;
    await WaitUntil (() => Self.SpawnArmor && Self.Health == Self.MaxHealth, 120, "died & respawned with armor & full health");
    Assert (partialLaserHits == 0, $"full-charge kill took exactly one hit (#93), saw {partialLaserHits} partial-damage hits");
    Assert (Self.GlobalPosition.Y > 20.0f, $"respawned up in the spawn room, y={Self.GlobalPosition.Y}");
    // >= 1: an incidental one-hit kill on the host in the line of fire also counts.
    await WaitUntil (() => FindPlayer (ShooterName)?.Score >= 1, 30, "shooter's score replicated to victim");
    // Streak glow (#77/#88): the shooter's kill streak must replicate to the victim's
    // copy of the shooter node, since that drives the glow & leaderboard pulsing here.
    await WaitUntil (() => FindPlayer (ShooterName)?.ZapStreakCount >= 1, 15, "shooter's streak replicated to victim");
    // Fall penalty goes negative (issue #108): step off the world at score 0 & verify -1.
    Assert (Self.Score == 0, $"own score is 0 before the fall, got {Self.Score}");
    Self.Position = new Vector3 (120.0f, 5.0f, 120.0f); // Beyond the arena: nothing below but the kill boundary.
    await WaitUntil (() => Self.Score == -1, 60, "fall at score 0 dropped own score to -1");
    // Respawned from the fall; the shooter's paper airplane phase needs us standing
    // in the spawn room (#102).
    await WaitUntil (() => Self.GlobalPosition.Y > 20.0f, 30, "respawned in the spawn room after the fall");
    // The throw replicates (#102): the shooter's flying airplane must appear here as
    // a visual copy before there's anything to catch.
    await WaitUntil (() => _world.GetChildren().OfType <PaperAirplaneProjectile>().Any(), 150, "shooter's thrown airplane replicated as a flying copy (#102)");
    // The signature catch (#102): watch the shooter's incoming airplane & punch it
    // out of the air once it's in reach; the handoff must land in our own hands.
    await PunchCatchAirplane();
    Assert (Self.Holds (HeldWeapon.PaperAirplane), "punch-caught the incoming paper airplane & it was granted (#102)");
    Assert (Self.SelectedWeapon == SelectedWeapon.PaperAirplane, "the caught paper airplane auto-equipped (#128)");
    // Give the shooter time to observe the handoff before we vanish.
    await Task.Delay (3000);
  }

  // Poll the incoming airplane & punch once it's within catch reach (#102).
  // Recovery path: if a timing hiccup let it hit or land instead, it becomes a
  // pickup right beside us, so walking to it still ends the loop with it in hand -
  // but never the deterministic spawn-room pickup, which belongs to the shooter.
  private async Task PunchCatchAirplane()
  {
    var deadline = Time.GetTicksMsec() + 60_000;

    while (!Self.Holds (HeldWeapon.PaperAirplane) && Time.GetTicksMsec() < deadline)
    {
      var airplane = _world.GetChildren().OfType <PaperAirplaneProjectile>().FirstOrDefault();
      var pickup = _world.GetChildren().OfType <WeaponPickup>().FirstOrDefault (IsCatchRecoveryPickup);

      if (airplane != null && airplane.GlobalPosition.DistanceTo (Self.GlobalPosition + Vector3.Up) <= 2.2f)
      {
        PressAction ("punch");
        await Task.Delay (60);
        ReleaseAction ("punch");
      }
      else if (airplane == null && pickup != null) WalkedTo (pickup.GlobalPosition);

      await Task.Delay (40);
    }
  }

  private bool IsCatchRecoveryPickup (WeaponPickup pickup) =>
    pickup.Weapon == HeldWeapon.PaperAirplane
    && pickup.GlobalPosition.DistanceTo (Self.GlobalPosition) < 15.0f
    && pickup.GlobalPosition.DistanceTo (WeaponSpawner.PlaytestAirplanePosition) > 1.5f;

  // Negative password check (issue #109): join with a bogus password, expect the
  // server to kick us with exactly "Wrong password.", then wait out the disconnect
  // so the real join starts clean.
  private async Task AssertWrongPasswordIsKicked()
  {
    var kickReason = string.Empty;
    _world.KickedFromServer += reason => kickReason = reason;
    _world.StartClientSession (VictimName, difficulty: 0, _address, Port, "wrong-" + Password);
    await WaitUntil (() => kickReason.Length > 0, 30, "wrong-password join was kicked");
    Assert (kickReason == "Wrong password.", $"kick reason is \"Wrong password.\", got \"{kickReason}\"");
    await WaitUntil (() => Multiplayer.MultiplayerPeer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected, 15, "kicked connection fully closed");
    await Task.Delay (500); // Let the peer teardown settle before reconnecting.
  }

  // ---------------------------------------------------------------- helpers

  // One approach step per poll: aim at the victim & hold forward until within 2m.
  // Same stuck-strafe as WalkedTo: shoving straight into the victim (or a pillar
  // between us) otherwise stalls this phase forever under CI load.
  private bool ApproachedVictim (Player victim) => WalkedTo (victim.GlobalPosition, reach: 2.0f);

  // One walk step per poll toward a world position, releasing forward once in reach.
  private float _lastWalkDistance = float.MaxValue;
  private int _stuckPolls;

  private bool WalkedTo (Vector3 target, float reach = 0.8f)
  {
    var flatTarget = new Vector3 (target.X, Self.GlobalPosition.Y, target.Z);
    var distance = Self.GlobalPosition.DistanceTo (flatTarget);

    if (distance <= reach)
    {
      Input.ActionRelease ("move_forward");
      Input.ActionRelease ("move_left");
      _lastWalkDistance = float.MaxValue;
      _stuckPolls = 0;
      return true;
    }

    // Unstick: when blocked (e.g. shoving against the other player), strafe around.
    _stuckPolls = distance > _lastWalkDistance - 0.05f ? _stuckPolls + 1 : 0;
    _lastWalkDistance = distance;
    if (_stuckPolls > 8) Input.ActionPress ("move_left");
    else Input.ActionRelease ("move_left");
    AimAt (flatTarget);
    Input.ActionPress ("move_forward");
    return false;
  }

  private void AimAt (Vector3 target)
  {
    var self = Self;
    var flatTarget = new Vector3 (target.X, self.GlobalPosition.Y, target.Z);
    if (flatTarget.DistanceSquaredTo (self.GlobalPosition) > 0.01f) self.LookAt (flatTarget, Vector3.Up); // -Z (forward) faces the target.
    var camera = self.GetNode <Camera3D> ("Camera3D");
    if (!target.IsEqualApprox (camera.GlobalPosition)) camera.LookAt (target, Vector3.Up);
  }

  private async Task ChargeAndFire (float chargeSeconds)
  {
    PressAction ("shoot");
    await Task.Delay ((int)(chargeSeconds * 1000));
    ReleaseAction ("shoot");
    await Task.Delay (100);
  }

  private static void PressAction (string action) => Input.ParseInputEvent (new InputEventAction { Action = action, Pressed = true });
  private static void ReleaseAction (string action) => Input.ParseInputEvent (new InputEventAction { Action = action, Pressed = false });

  private static void Assert (bool condition, string description)
  {
    if (condition)
    {
      GD.Print ($"PLAYTEST OK: {description}");
      return;
    }

    throw new Exception ($"assertion failed: {description}");
  }

  private async Task WaitUntil (Func <bool> condition, float timeoutSeconds, string description)
  {
    if (await TryWaitUntil (condition, timeoutSeconds))
    {
      GD.Print ($"PLAYTEST OK: {description}");
      return;
    }

    throw new Exception ($"timed out after {timeoutSeconds}s waiting for: {description}");
  }

  // Non-throwing wait; a throwing condition (e.g. nodes vanishing mid-disconnect)
  // counts as false so the caller gets a readable timeout instead of a raw exception.
  private static async Task <bool> TryWaitUntil (Func <bool> condition, float timeoutSeconds)
  {
    var deadline = Time.GetTicksMsec() + (ulong)(timeoutSeconds * 1000);

    while (Time.GetTicksMsec() < deadline)
    {
      try
      {
        if (condition()) return true;
      }
      catch (Exception e)
      {
        // Treat as not-yet-true; the timeout surfaces the failure & the log says why.
        GD.Print ($"PLAYTEST: wait condition threw: {e.Message}");
      }

      await Task.Delay (100);
    }

    return false;
  }
}
