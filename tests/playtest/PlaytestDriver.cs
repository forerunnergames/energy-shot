using System;
using System.Collections.Generic;
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
  // Admin messages (issue #158): announcement text written into the host's operator
  // file mid-run; the version must match run-playtest.sh's version file content.
  private const string AdminAnnouncement = "Playtest admin announcement";
  private const string ServerVersion = "v9.9.9-playtest";
  private World _world = null!;
  private string _role = string.Empty;
  private string _address = "127.0.0.1";
  private int _boltsSpawned;
  private int _boomerangsSpawned;
  private int _stonesSpawned;
  private int _airplanesSpawned;
  // The most recent stone & how far along +Z it got, sampled every frame (issue
  // #163): the wall-block assert needs the flight path, not just the spawn count.
  private SlingshotStone? _lastStone;
  private float _lastStoneMaxZ = float.MinValue;
  private Player? _self;
  private readonly List <string> _adminMessages = new();
  private Player Self => _self ??= _world.GetPlayers().First (player => player.IsMultiplayerAuthority());
  private MusicManager Music => _world.GetNode <MusicManager> ("MusicManager");
  private Player? FindPlayer (string name) => _world.GetPlayers().FirstOrDefault (player => player.DisplayName == name);

  public override void _Ready()
  {
    // Three instances share the CI runner; uncapped frame loops starve physics &
    // ENet, dilating in-game time far behind the wall clock this driver waits on.
    Engine.MaxFps = 30;
    _world = GetNode <World> ("/root/World");
    _world.AdminMessageReceived += message => _adminMessages.Add (message);
    _world.ChildEnteredTree += node => _boltsSpawned += node is LaserBolt ? 1 : 0;
    _world.ChildEnteredTree += node => _boomerangsSpawned += node is BoomerangProjectile ? 1 : 0;
    _world.ChildEnteredTree += node => _stonesSpawned += node is SlingshotStone ? 1 : 0;
    _world.ChildEnteredTree += node => _airplanesSpawned += node is PaperAirplaneProjectile ? 1 : 0;
    _world.ChildEnteredTree += node => { if (node is SlingshotStone stone) TrackStone (stone); };
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
    // Vote-memory transitions (#162): record every server-side tally change with
    // the authoritative all-time totals at that exact moment - votes only flow
    // through this instance, so hooking before any client joins misses nothing.
    var voteHistory = new List <(int Up, int Down, (int Up, int Down) AllTime)>();
    Music.VoteCountsChanged += (up, down) => voteHistory.Add ((up, down, Music.CurrentTrackAllTimeVotes));
    await WaitUntil (() => _world.GetPlayers().Count() == 3, 60, "all 3 players joined");
    // Chosen body colors (issue #43): own pick stuck & both clients' picks replicate to the host.
    Assert (Self.ColorIndex == HostColor, $"own chosen color is {HostColor}, got {Self.ColorIndex}");
    await WaitUntil (() => FindPlayer (ShooterName)?.ColorIndex == ShooterColor && FindPlayer (VictimName)?.ColorIndex == VictimColor, 15, "clients' chosen colors replicated to host (#43)");
    // Crown rules (issue #178): nobody wears the crown at 0-0 - it must be earned.
    await Task.Delay (3000); // Let a few 1s crown ticks pass before judging.
    Assert (_world.GetPlayers().All (player => !player.IsCrowned), "no crown at 0-0 (#178)");
    // Server-measured pings replicate back to every peer (issue #100).
    await WaitUntil (() => FindPlayer (ShooterName)?.PingMs >= 0, 15, "shooter's ping measured & replicated to host");
    // Synced music (issue #137): the server picked a track & the shooter's thumbs-up
    // vote came back through the server tally.
    await WaitUntil (() => Music.CurrentTrackTitle.Length > 0, 15, "music track started on the server");
    await WaitUntil (() => voteHistory.Any (entry => entry.Up == 1 && entry.Down == 0), 30, "shooter's up-vote tallied on host");
    // The shooter re-votes up (a no-op) & then switches to down (#162): the same
    // peer's reliable RPCs arrive in order, so once the switch lands both up
    // entries are recorded - they must share identical totals (no double-count),
    // & the switch must have moved exactly one all-time vote across.
    await WaitUntil (() => voteHistory.Any (entry => entry.Up == 0 && entry.Down == 1), 60, "shooter's up-to-down switch tallied on host (#162)");
    var upEntries = voteHistory.Where (entry => entry.Up == 1 && entry.Down == 0).ToList();
    var downEntry = voteHistory.First (entry => entry.Up == 0 && entry.Down == 1);
    Assert (upEntries.Count >= 2 && upEntries.All (entry => entry.AllTime == upEntries[0].AllTime), "repeated up-vote never double-counted the all-time totals (#162)");
    Assert (downEntry.AllTime == (upEntries[0].AllTime.Up - 1, upEntries[0].AllTime.Down + 1), $"up-to-down switch moved one all-time vote (#162), got {upEntries[0].AllTime} -> {downEntry.AllTime}");
    // Admin messages (issue #158): drop an announcement into the operator file; the
    // 1s poller must broadcast it to every peer (host included) & consume the file
    // (claimed atomically by rename) so the same text can be re-sent later.
    var adminFile = ArgValue (OS.GetCmdlineUserArgs(), "--admin-message-file")!;
    System.IO.File.WriteAllText (adminFile, AdminAnnouncement);
    await WaitUntil (() => _adminMessages.Contains (AdminAnnouncement), 15, "admin announcement broadcast from the message file (#158)");
    await WaitUntil (() => !System.IO.File.Exists (adminFile), 10, "admin message file consumed after broadcast (#158)");
    // Shooter kills victim once (plus possibly the host itself in the line of
    // fire); wait to observe the replicated score.
    await WaitUntil (() => FindPlayer (ShooterName)?.Score >= 1, 120, "shooter's kill replicated to host");
    // Crown rules (issue #178): the first score puts the crown on the scorer - & on
    // nobody else. (A tie handover isn't cheaply reachable in this scenario's score
    // flow, so the incumbent rules beyond these are covered by the logic itself.)
    await WaitUntil (() => FindPlayer (ShooterName)?.IsCrowned == true, 10, "crown appeared on the first scorer (#178)");
    await WaitUntil (() => _world.GetPlayers().Count (player => player.IsCrowned) == 1, 10, "exactly one crown after the first score (#178)");
    // Victim respawns with armor visible to the host too.
    await WaitUntil (() => FindPlayer (VictimName)?.SpawnArmor == true, 30, "victim respawn armor replicated to host");
    // The victim's fall at score 0 goes negative & replicates (issue #108).
    await WaitUntil (() => FindPlayer (VictimName)?.Score == -1, 60, "victim's fall penalty (-1) replicated to host");
    // Crown rules (issue #178): a lower score moving (the fall) never moves the crown.
    Assert (FindPlayer (ShooterName)?.IsCrowned == true, "crown stayed on the leader after the fall penalty (#178)");
    // Stay up until both clients have finished & disconnected (the shooter's solo
    // phases now end with the paper airplane throw & catch, issue #102).
    await WaitUntil (() => _world.GetPlayers().Count() == 1, 180, "clients disconnected");
    // The version line goes only to joining clients, never broadcast (#158), so the
    // host must never have seen one.
    Assert (_adminMessages.All (message => !message.Contains ("Running")), "version line was not broadcast to the host (#158)");
  }

  private async Task RunShooter()
  {
    _world.StartClientSession (ShooterName, difficulty: 1, _address, Port, Password, ShooterColor);
    await WaitUntil (() => _world.GetPlayers().Count() == 3, 60, "all 3 players visible");
    // Forged admin RPC (issue #158): a client impersonating the server must be
    // dropped by the server's peer-1 check; every role asserts the text never
    // arrives. Sent by name: the RPC is private to NetworkManager on purpose.
    _world.GetNode <Node> ("NetworkManager").Rpc ("OnAdminMessageReceived", "FORGED announcement");
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
    // Own-vote memory (issue #162): the server confirms the vote back to the voter,
    // which is what drives the mini player's pressed-thumb highlight.
    await WaitUntil (() => Music.CurrentOwnVote == 1 && Music.CurrentUpVotes == 1, 15, "own up-vote confirmed back by the server (#162)");
    // Vote-memory transitions (#162): a repeated identical vote must change nothing
    // (the host asserts the authoritative totals stayed put), then an up-to-down
    // switch must press the other thumb & move the play tally by exactly one.
    Music.SubmitVote (isUpVote: true);
    await Task.Delay (1000);
    Assert (Music.CurrentOwnVote == 1 && Music.CurrentUpVotes == 1 && Music.CurrentDownVotes == 0, "repeated up-vote was a no-op (#162)");
    Music.SubmitVote (isUpVote: false);
    await WaitUntil (() => Music.CurrentOwnVote == -1 && Music.CurrentUpVotes == 0 && Music.CurrentDownVotes == 1, 15, "up-to-down switch confirmed & re-tallied (#162)");

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
    var healthBeforePunch = victim.Health;
    await WaitUntil (() => ApproachedVictim (victim), 30, "walked into punch range of victim");
    // Fists are weapon slot 1 & punching requires them selected (issue #82). The
    // re-select happens AFTER the walk: a pickup auto-claimed en route auto-equips
    // itself (#128) & would otherwise deselect the fists (seen with the paper
    // airplane pickup, #102).
    PressAction ("weapon_1");
    await Task.Delay (100);
    ReleaseAction ("weapon_1");

    // Punch on LEFT click (issue #164): injected as real mouse-button events so the
    // binding itself is under test, not just the action.
    for (var attempt = 0; attempt < 10 && victim.Health >= healthBeforePunch; ++attempt)
    {
      AimAt (victim.GlobalPosition + Vector3.Up);
      PressLeftClick();
      await Task.Delay (80);
      ReleaseLeftClick();
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
      await Task.Delay (900); // Hold to draw (#99), past the minimum draw (#163); release slings the stone.
      ReleaseAction ("shoot");
      await Task.Delay (300);
    }

    Assert (_stonesSpawned > stonesBefore, "slingshot draw & release fired a stone (#99)");

    // Fire-rate cap (#163): sub-minimum taps just relax the band - no stones.
    await Task.Delay (800); // Let the previous shot's cooldown lapse so only the taps are under test.
    var stonesBeforeSpam = _stonesSpawned;

    for (var i = 0; i < 4; ++i)
    {
      PressAction ("shoot");
      await Task.Delay (80);
      ReleaseAction ("shoot");
      await Task.Delay (120);
    }

    Assert (_stonesSpawned == stonesBeforeSpam, $"sub-minimum taps released no stones (#163), got {_stonesSpawned - stonesBeforeSpam}");

    // Wall blocking (#163): point-blank into the spawn-room wall (the wall face is
    // about as close as the muzzle offset from here), so the first-frame camera
    // sweep is what stops the stone - it must never travel past the wall at z=6.
    AimAt (new Vector3 (Self.GlobalPosition.X, 31.0f, 6.0f)); // Mid-height of the wall ahead.
    var wallStone = await SlingAStone (drawMs: 1500, "wall-test stone (#163)");
    await TryWaitUntil (() => !IsInstanceValid (wallStone) || !wallStone.IsInsideTree(), 5);
    Assert (!IsInstanceValid (wallStone) || !wallStone.IsInsideTree(), "the wall stopped the stone (#163)");
    Assert (_lastStoneMaxZ < 6.5f, $"stone never passed the wall at z=6 (#163), max z {_lastStoneMaxZ:0.00}");

    // Long flight (#163): a full-draw stone lobbed high over the walls must still be
    // flying seconds later & have covered real distance - no premature despawn.
    var launchPosition = Self.GlobalPosition;
    AimAt (Self.GetNode <Camera3D> ("Camera3D").GlobalPosition + new Vector3 (0.0f, 30.0f, 30.0f)); // ~45 degrees up, over the wall, away from everyone.
    var flightStone = await SlingAStone (drawMs: 3000, "long-flight stone (#163)");
    await Task.Delay (4000);
    Assert (IsInstanceValid (flightStone) && flightStone.IsInsideTree(), "full-draw stone still flying after 4s (#163)");
    var flightDistance = new Vector2 (flightStone.GlobalPosition.X - launchPosition.X, flightStone.GlobalPosition.Z - launchPosition.Z).Length();
    Assert (flightDistance > 40.0f, $"full-draw stone covered real range (#163), got {flightDistance:0.0}m");

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
    // Keep some distance: the glider needs a moment of flight for the catch to be
    // catchable at all - throwing from a few meters lands it before anyone can swing.
    await WaitUntil (() => WalkedTo (victim.GlobalPosition, reach: 6.0f), 45, "walked near the victim for the airplane throw (#102)");
    // The throw locks onto whoever is under the crosshair, & the idling host has
    // wandered into the ray & stolen the lock - wait until the line is the victim's.
    await WaitUntil (() => IsVictimTheNearestTarget (victim), 30, "clear line to the victim for the airplane throw (#102)");
    // A genuine punch-catch fires our own AirplaneCaught signal when the handoff is
    // validated (CodeRabbit on #180): a landing must NOT pass this phase.
    var airplaneCaught = false;
    Self.AirplaneCaught += _ => airplaneCaught = true;
    var airplanesBefore = _airplanesSpawned;

    for (var attempt = 0; attempt < 10 && _airplanesSpawned == airplanesBefore; ++attempt)
    {
      AimAt (victim.GlobalPosition + Vector3.Up);
      if (!IsVictimTheNearestTarget (victim)) { await Task.Delay (250); continue; } // Host drifted into the ray again.
      PressAction ("shoot");
      await Task.Delay (80);
      ReleaseAction ("shoot");
      await Task.Delay (300);
    }

    Assert (_airplanesSpawned > airplanesBefore, "paper airplane thrown at the victim (#102)");
    // The victim punch-catches it mid-air: the thrower-side catch signal is the
    // observable handoff transition - a landing would never fire it (#102).
    await WaitUntil (() => airplaneCaught, 30, "victim's punch-catch confirmed by own catch signal (#102)");
    await WaitUntil (() => !Self.Holds (HeldWeapon.PaperAirplane), 15, "caught airplane left our hands (#102)");
    await WaitUntil (() => victim.Holds (HeldWeapon.PaperAirplane), 30, "victim holds the caught paper airplane (#102)");

    // The toggle persists to the shared user settings (#119); restore the starting
    // view so a playtest run never flips the developer's real preference.
    await ToggleViewUntil (startedThirdPerson);

    // Admin messages (issue #158), asserted here so the waits don't stall the
    // timing-sensitive phases above: the join-time version line reached only us
    // (targeted send) & the host's file-driven announcement reached everyone.
    await WaitUntil (() => _adminMessages.Contains ($"Running {ServerVersion}"), 30, "version line received on join (#158)");
    await WaitUntil (() => _adminMessages.Contains (AdminAnnouncement), 30, "admin announcement received from the server (#158)");
    // Our forged admin RPC from the start of the run was dropped by the server:
    // it never echoed back here through any relay (#158).
    Assert (_adminMessages.All (message => !message.Contains ("FORGED")), "forged admin RPC was rejected (#158)");
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
    // Version enforcement (issue #170): a pre-#170 client joining via the legacy
    // versionless RPC, & a client with a mismatched version, must each get kicked
    // with the update-required reason before anything else is checked.
    await AssertLegacyJoinIsKicked();
    await AssertWrongVersionIsKicked();
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
    // The shooter's settled stance after its #162 transitions (up, repeat, down):
    // waiting on the final state instead of the transient up-vote avoids racing
    // the quick up-to-down switch.
    await WaitUntil (() => Music.CurrentUpVotes == 0 && Music.CurrentDownVotes == 1, 60, "shooter's settled music vote (down after switch) visible to victim (#162)");
    // Admin messages (issue #158): the join-time version line & the host's
    // file-driven announcement both arrive as admin messages here too.
    await WaitUntil (() => _adminMessages.Contains ($"Running {ServerVersion}"), 30, "version line received on join (#158)");
    await WaitUntil (() => _adminMessages.Contains (AdminAnnouncement), 30, "admin announcement received from the server (#158)");
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
    // Catching requires fists out - re-select in case a wandering auto-claim ever
    // auto-equipped something else (#128).
    PressAction ("weapon_1");
    await Task.Delay (100);
    ReleaseAction ("weapon_1");
    await PunchCatchAirplane();
    Assert (Self.Holds (HeldWeapon.PaperAirplane), "punch-caught the incoming paper airplane & it was granted (#102)");
    Assert (Self.SelectedWeapon == SelectedWeapon.PaperAirplane, "the caught paper airplane auto-equipped (#128)");
    // Give the shooter time to observe the handoff before the recovery scenario.
    await Task.Delay (3000);
    // Landing & recovery (#102), a separate scenario from the catch (CodeRabbit on
    // #180): throw the caught airplane into the floor nearby (nobody under the
    // crosshair), let it land as a ray-grounded expiring pickup, & reclaim it.
    await RecoverLandedAirplane();
    // The shooter's forged admin RPC must never have been relayed to us: the
    // server drops admin messages from any sender but peer 1 (#158).
    Assert (_adminMessages.All (message => !message.Contains ("FORGED")), "forged admin RPC never relayed to the victim (#158)");
  }

  // Landing lifecycle (#102): the airplane glides into the floor, becomes a grounded
  // pickup where it stopped, & walking over reclaims it before the 5s expiry.
  private async Task RecoverLandedAirplane()
  {
    var shooterPlayer = FindPlayer (ShooterName);
    var awayFromShooter = shooterPlayer == null ? Vector3.Right : (Self.GlobalPosition - shooterPlayer.GlobalPosition).Normalized();
    AimAt (Self.GlobalPosition + awayFromShooter * 3.0f + Vector3.Down * 1.0f); // Floor a few meters away, aimed at nobody.

    for (var attempt = 0; attempt < 10 && !Self.IsAirplaneInFlight && Self.Holds (HeldWeapon.PaperAirplane); ++attempt)
    {
      PressAction ("shoot");
      await Task.Delay (80);
      ReleaseAction ("shoot");
      await Task.Delay (200);
    }

    await WaitUntil (() => !Self.Holds (HeldWeapon.PaperAirplane), 15, "thrown airplane landed & left our hands (#102)");
    await WaitUntil (() => _world.GetChildren().OfType <WeaponPickup>().Any (IsCatchRecoveryPickup), 15, "landed airplane became a grounded pickup (#102)");
    await WaitUntil (WalkedToRecoveryPickup, 30, "landed airplane pickup was reclaimed by a player (#102)");
  }

  // We walk to the landed pickup, but any player claiming it proves the same thing:
  // the landing produced a real, claimable pickup. There's exactly one airplane in
  // the game (#102), so whoever is nearest legitimately wins the race - a bystander
  // beating us to it must not fail the phase.
  // The airplane locks onto whoever the crosshair ray finds first (#102), so the
  // throw is only aimed at the victim if no other player sits nearer along it.
  private bool IsVictimTheNearestTarget (Player victim)
  {
    var toVictim = Self.GlobalPosition.DistanceTo (victim.GlobalPosition);
    return _world.GetPlayers().All (player => player == Self || player == victim || player.GlobalPosition.DistanceTo (Self.GlobalPosition) > toVictim);
  }

  private bool WalkedToRecoveryPickup()
  {
    if (_world.GetPlayers().Any (player => player.Holds (HeldWeapon.PaperAirplane))) return true;
    var pickup = _world.GetChildren().OfType <WeaponPickup>().FirstOrDefault (IsCatchRecoveryPickup);
    if (pickup != null) WalkedTo (pickup.GlobalPosition);
    return false;
  }

  // Legacy-client check (issue #170): join the way a pre-#170 client does (the
  // 4-argument RequestPlayerSlot RPC with no version), expect the server to kick
  // us with the exact update-required reason old clients can already display
  // (#109), then wait out the disconnect so the next join starts clean.
  private async Task AssertLegacyJoinIsKicked()
  {
    var kickReason = string.Empty;
    _world.KickedFromServer += reason => kickReason = reason;
    _world.StartLegacyClientSession (VictimName, difficulty: 0, _address, Port, Password);
    await WaitUntil (() => kickReason.Length > 0, 30, "legacy versionless join was kicked");
    var expected = $"Update required: server is v{World.GameVersion}, you have an older version.";
    Assert (kickReason == expected, $"kick reason is \"{expected}\", got \"{kickReason}\"");
    await WaitUntil (() => Multiplayer.MultiplayerPeer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected, 15, "kicked connection fully closed");
    await Task.Delay (500); // Let the peer teardown settle before reconnecting.
  }

  // Negative version check (issue #170): join with a spoofed version & the right
  // password, expect the server to kick us with the exact update-required reason,
  // then wait out the disconnect so the next join starts clean.
  private async Task AssertWrongVersionIsKicked()
  {
    var kickReason = string.Empty;
    _world.KickedFromServer += reason => kickReason = reason;
    _world.StartClientSession (VictimName, difficulty: 0, _address, Port, Password, version: "0.0.0-spoofed");
    await WaitUntil (() => kickReason.Length > 0, 30, "wrong-version join was kicked");
    var expected = $"Update required: server is v{World.GameVersion}, you have v0.0.0-spoofed.";
    Assert (kickReason == expected, $"kick reason is \"{expected}\", got \"{kickReason}\"");
    await WaitUntil (() => Multiplayer.MultiplayerPeer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected, 15, "kicked connection fully closed");
    await Task.Delay (500); // Let the peer teardown settle before reconnecting.
  }

  // Poll the incoming airplane & punch once it's within catch reach (#102). Strict
  // catch-only coverage (CodeRabbit on #180): a landed airplane (visible as a fresh
  // grounded pickup) fails this phase immediately instead of masquerading as a
  // catch - the landing lifecycle has its own recovery scenario.
  private async Task PunchCatchAirplane()
  {
    var deadline = Time.GetTicksMsec() + 60_000;

    while (!Self.Holds (HeldWeapon.PaperAirplane) && Time.GetTicksMsec() < deadline)
    {
      if (_world.GetChildren().OfType <WeaponPickup>().Any (IsCatchRecoveryPickup)) throw new Exception ("assertion failed: the airplane landed instead of being punch-caught (#102)");
      var airplane = _world.GetChildren().OfType <PaperAirplaneProjectile>().FirstOrDefault();

      // Punch just outside the catch radius: input processing eats a frame or two
      // while the glider closes ~0.4m/frame, & the catch RPC still needs a few
      // more frames to reach the thrower before the hit lands.
      if (airplane != null && airplane.GlobalPosition.DistanceTo (Self.GlobalPosition + Vector3.Up) <= 4.4f)
      {
        PressAction ("punch");
        await Task.Delay (60);
        ReleaseAction ("punch");
      }

      await Task.Delay (25);
    }
  }

  // A freshly landed airplane pickup (#102): near us & NOT the deterministic
  // spawn-room pickup, which belongs to the shooter's collection phase.
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
  // Real left-mouse events, not action injections (issue #164): punching must work
  // through the actual left-click binding while fists are the selected weapon.
  private static void PressLeftClick() => Input.ParseInputEvent (new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
  private static void ReleaseLeftClick() => Input.ParseInputEvent (new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });

  // Draws (holding well past the minimum draw time, #163) & releases until a stone
  // spawns; retries absorb CI physics-time dilation eating into the cooldown & draw.
  private async Task <SlingshotStone> SlingAStone (int drawMs, string description)
  {
    for (var attempt = 0; attempt < 5; ++attempt)
    {
      _lastStone = null;
      PressAction ("shoot");
      await Task.Delay (drawMs);
      ReleaseAction ("shoot");
      await TryWaitUntil (() => _lastStone != null, 2);
      if (_lastStone != null) return _lastStone;
    }

    throw new Exception ($"no stone spawned: {description}");
  }

  // Samples the newest stone's +Z progress every frame until it despawns (issue
  // #163): flight paths outlive any 100ms poll, so the wall assert needs per-frame data.
  private async void TrackStone (SlingshotStone stone)
  {
    _lastStone = stone;
    _lastStoneMaxZ = float.MinValue;

    // Stop sampling once a newer stone takes over, so an earlier stone still in
    // flight can't pollute the newer stone's measurements.
    while (_lastStone == stone && IsInstanceValid (stone) && stone.IsInsideTree() && IsInsideTree())
    {
      _lastStoneMaxZ = Mathf.Max (_lastStoneMaxZ, stone.GlobalPosition.Z);
      await ToSignal (GetTree(), SceneTree.SignalName.ProcessFrame);
    }
  }

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
