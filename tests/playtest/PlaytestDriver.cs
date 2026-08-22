using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using com.forerunnergames.energyshot.core.audio;
using com.forerunnergames.energyshot.core.world;
using com.forerunnergames.energyshot.players;
using com.forerunnergames.energyshot.utilities;
using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.playtest;

// Automated multiplayer playtest: three headless instances (host, shooter, victim)
// drive the real game end-to-end - join, replication, movement, charged laser kills,
// respawn, spawn armor, fire-rate cap & full-auto - and exit 0/1 for CI.
// Activated by launching with: godot --headless --path . -- --playtest <role> [--address a] [--port n]
public partial class PlaytestDriver : Node
{
  private const int DefaultPort = 55599;
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
  // Death-drop coverage (issue #169): the victim carries the deterministic playtest
  // banana to this fixed spot to be zapped, so the drop lands metres clear of every
  // playtest pickup spot - a drop search next to one of those could match it instead.
  private static readonly Vector3 KillSpot = new(4.0f, 31.3f, 0.0f);
  private static readonly Vector3 SpawnRoomCenter = new(0.0f, 31.3f, 0.0f);
  // Fixed marks for the airplane throw/catch (#102), out in the empty arena well
  // clear of the spawn room where the host idles: 8m apart, so the glider gets a
  // real flight & nobody else can wander into the throw's aim ray.
  private static readonly Vector3 CatchMark = new(40.0f, 1.0f, -40.0f);
  private static readonly Vector3 CatchThrowMark = new(40.0f, 1.0f, -32.0f);
  // The drop grounds straight down from the death spot (#151), so it stays in that
  // XZ column; the radius only has to cover RequestDrop's per-weapon side offsets.
  private const float DropSearchRadius = 2.0f;
  private World _world = null!;
  private string _role = string.Empty;
  private string _address = "127.0.0.1";
  // Overridable so parallel local runs don't collide on one port (--port n).
  private int _port = DefaultPort;
  private int _boltsSpawned;
  private int _boomerangsSpawned;
  private int _stonesSpawned;
  // Paper airplane flights seen locally (issue #102): the throw phase watches this
  // instead of the transient in-flight node, same trick as the boomerang count.
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
    // Sanity-check the override (CodeRabbit on #185): anything outside the sane
    // unprivileged range falls back to the default instead of failing the bind.
    _port = int.TryParse (ArgValue (args, "--port"), out var port) && port is >= 1024 and <= 65535 ? port : DefaultPort;
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
    _world.StartHostSession (HostName, difficulty: 2, _port, Password, HostColor);
    // Vote-memory transitions (#162): record every server-side tally change with
    // the authoritative all-time totals at that exact moment - votes only flow
    // through this instance, so hooking before any client joins misses nothing.
    var voteHistory = new List <(int Up, int Down, (int Up, int Down) AllTime)>();
    Music.VoteCountsChanged += (up, down) => voteHistory.Add ((up, down, Music.CurrentTrackAllTimeVotes));
    await WaitUntil (() => _world.GetPlayers().Count() == 3, 60, "all 3 players joined");
    // Chosen body colors (issue #43): own pick stuck & both clients' picks replicate to the host.
    Assert (Self.ColorIndex == HostColor, $"own chosen color is {HostColor}, got {Self.ColorIndex}");
    await WaitUntil (() => FindPlayer (ShooterName)?.ColorIndex == ShooterColor && FindPlayer (VictimName)?.ColorIndex == VictimColor, 30, "clients' chosen colors replicated to host (#43)");
    // Crown rules (issue #178): nobody wears the crown at 0-0 - it must be earned.
    await Task.Delay (3000); // Let a few 1s crown ticks pass before judging.
    Assert (_world.GetPlayers().All (player => !player.IsCrowned), "no crown at 0-0 (#178)");
    // Server-measured pings replicate back to every peer (issue #100).
    await WaitUntil (() => FindPlayer (ShooterName)?.PingMs >= 0, 30, "shooter's ping measured & replicated to host");
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
    await WaitUntil (() => !System.IO.File.Exists (adminFile), 30, "admin message file consumed after broadcast (#158)");
    // Shooter kills victim once (plus possibly the host itself in the line of
    // fire); wait to observe the replicated score.
    await WaitUntil (() => FindPlayer (ShooterName)?.Score >= 1, 120, "shooter's kill replicated to host");
    // Crown rules (issue #178): the first score puts the crown on the scorer - & on
    // nobody else. (A tie handover isn't cheaply reachable in this scenario's score
    // flow, so the incumbent rules beyond these are covered by the logic itself.)
    await WaitUntil (() => FindPlayer (ShooterName)?.IsCrowned == true, 30, "crown appeared on the first scorer (#178)");
    await WaitUntil (() => _world.GetPlayers().Count (player => player.IsCrowned) == 1, 30, "exactly one crown after the first score (#178)");
    // Victim respawns with armor visible to the host too (~5s later now, #152).
    await WaitUntil (() => FindPlayer (VictimName)?.SpawnArmor == true, 35, "victim respawn armor replicated to host");
    // The victim's fall at score 0 goes negative & replicates (issue #108).
    await WaitUntil (() => FindPlayer (VictimName)?.Score == -1, 60, "victim's fall penalty (-1) replicated to host");
    // Crown rules (issue #178): a lower score moving (the fall) never moves the crown.
    Assert (FindPlayer (ShooterName)?.IsCrowned == true, "crown stayed on the leader after the fall penalty (#178)");
    // Stay up until both clients have finished & disconnected (the shooter's solo
    // phases now end with the paper airplane throw & catch, issue #102).
    // 600s, not 180 (CodeRabbit): this single wait has to span every client phase
    // that follows - the shooter's ammo & airplane phases & the victim's catch and
    // landmine phases - & the sum of their per-step budgets already exceeds 180s. A
    // slow run that stays inside every inner budget must not fail out here.
    await WaitUntil (() => _world.GetPlayers().Count() == 1, 600, "clients disconnected");
    // The version line goes only to joining clients, never broadcast (#158), so the
    // host must never have seen one.
    Assert (_adminMessages.All (message => !message.Contains ("Running")), "version line was not broadcast to the host (#158)");
  }

  private async Task RunShooter()
  {
    _world.StartClientSession (ShooterName, difficulty: 1, _address, _port, Password, ShooterColor);
    // Snapshot the spawn state the moment our own player exists (#72 & #190). The
    // join wait below runs for seconds, & the spawn room's deterministic pickups sit
    // inside claim reach of the +/-4 random spawn scatter - an unlucky spawn
    // auto-claims one & rewrites what "spawned with" ever meant.
    await WaitUntil (() => _world.GetPlayers().Any (player => player.IsMultiplayerAuthority()), 60, "own player spawned");
    var spawnedUnarmed = Self.IsUnarmed;
    var spawnedWithBread = Self.Holds (HeldWeapon.Bread);
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
    await WaitUntil (() => victim.ColorIndex == VictimColor && host.ColorIndex == HostColor, 30, "victim's & host's chosen colors replicated to shooter (#43)");
    // Unarmed means no guns (issue #190): every life still starts with a loaf, & the
    // loaf now rides the HeldWeapon mask so death can drop it.
    Assert (spawnedUnarmed, "spawned unarmed (#72)");
    Assert (spawnedWithBread, "spawned carrying the one-per-life loaf (#190)");
    // The server measures our ping & tells us within a tick or two (issue #100).
    await WaitUntil (() => Self.PingMs >= 0, 30, "own ping measured by the server");

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
    await WaitUntil (() => !victim.SpawnArmor, 30, "victim's initial spawn armor expired");

    // Chosen color on the body (issue #43): with armor's white glow gone, the victim's
    // per-instance body material must show exactly its picked palette color.
    var victimBody = (StandardMaterial3D)victim.GetNode <MeshInstance3D> ("MeshInstance3D").GetSurfaceOverrideMaterial (0);
    Assert (victimBody.AlbedoColor.IsEqualApprox (PlayerColors.At (VictimColor)), "victim's body tinted with its chosen color (#43)");

    // Streak replication (#88): the victim simulates an active 3-streak; it must
    // replicate here so the on-fire glow & pulsing leaderboard entry appear.
    await WaitUntil (() => victim.ZapStreakCount == 3, 30, "victim's simulated 3-streak replicated to shooter");

    // Dance emote (#103): the victim started dancing after its armor expired; the
    // replicated state must animate this puppet copy too.
    await WaitUntil (() => victim.Dancing, 30, "victim's dance replicated to shooter (#103)");

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

    // Spawn armor absorbs punches outright (#48), & a victim that just respawned
    // still has ~5s of it - wait it out rather than spending swings on it.
    await WaitUntil (() => !victim.SpawnArmor, 30, "victim's spawn armor expired before the punch phase (#48)");

    // The punch stun is TRANSIENT - 3s from the last landed punch - & the dance-cancel
    // wait below can legitimately burn 5s before we ever look at it, so sample it
    // continuously from before the first swing instead of afterwards. Same watcher
    // idiom as the respawn-armor window further down; this exact assert timed out on
    // a loaded CI runner, having decayed back to zero before it was read.
    var stunSeen = false;
    WatchForPunchStun();

    async void WatchForPunchStun()
    {
      while (!stunSeen && IsInsideTree())
      {
        stunSeen = victim.StunFactor > 0.0f;
        await Task.Delay (50);
      }
    }

    // Punch on LEFT click (issue #164): injected as real mouse-button events so the
    // binding itself is under test, not just the action. Re-take the punching spot
    // every attempt - the victim is running its own phases & wanders off, & the
    // punch ray hits the FIRST body, so we stand on the side opposite the idle host
    // (an unlucky spawn once put it in front of the victim for all 10 attempts).
    for (var attempt = 0; attempt < 20 && victim.Health >= healthBeforePunch; ++attempt)
    {
      var awayFromHost = victim.GlobalPosition - host.GlobalPosition;
      var flatAway = new Vector3 (awayFromHost.X, 0.0f, awayFromHost.Z);
      Self.Position = victim.GlobalPosition + (flatAway.Length() > 0.5f ? flatAway.Normalized() : Vector3.Right) * 1.5f;
      await Task.Delay (150); // Settle before swinging.
      // Their position reaches us over the wire, so on a lagging runner it can be
      // stale by the time we arrive - & the victim is moving through its own phases.
      // Don't spend the swing on thin air; take the spot again next pass.
      if (Self.GlobalPosition.DistanceTo (victim.GlobalPosition) > Self.PunchRange * 0.75f) continue;
      // Fists again before EVERY swing (#78): re-taking the spot can walk us over a
      // spawn-room pickup, which auto-equips (#128) - & a left click with the paper
      // airplane in hand threw it point-blank, armed it at our feet, & we stepped on
      // our own mine mid-phase.
      PressAction ("weapon_1");
      await Task.Delay (50);
      ReleaseAction ("weapon_1");
      AimAt (victim.GlobalPosition + Vector3.Up);
      PressLeftClick();
      await Task.Delay (80);
      ReleaseLeftClick();
      await Task.Delay (700);
    }

    Assert (victim.Health < healthBeforePunch, $"punch damaged the victim ({healthBeforePunch} -> {victim.Health})");
    // Damage cancels the dance (#103) & the cancel must replicate back here too.
    await WaitUntil (() => !victim.Dancing, 30, "victim's dance cancel replicated to shooter (#103)");
    // Sync property index 14 (#88): the punch stun must replicate to the shooter's copy.
    await WaitUntil (() => stunSeen, 30, "victim's punch stun replicated to shooter");

    // Hit-flash restoration (#43): the punch just flashed this puppet's body dark
    // red; the flash must settle back onto the exact chosen palette color - the
    // riskiest base-color path, since it used to restore a hardcoded default.
    await WaitUntil (() => victimBody.AlbedoColor.IsEqualApprox (PlayerColors.At (VictimColor)), 15, "victim's body returned to its chosen color after the hit flash (#43)");

    await RunBreadEatCompletionPhase(); // Issues #209 & #192.

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

    // Death sequence (#152): the victim's body lies fallen at the death spot &
    // the replicated Fallen state must render the tip-over on this peer too.
    await WaitUntil (() => victim.Fallen, 30, "victim's fallen body replicated to shooter (#152)");
    await RunDeathDropPhase (victim);

    // Victim must come back armored in the spawn room (~5s later now, #152).
    await WaitUntil (() => respawnArmorSeen, 30, "victim respawned with spawn armor");
    await WaitUntil (() => !victim.Fallen, 30, "victim's body stood back up on respawn (#152)");

    // Streak glow bug (#88): the kill ended the victim's streak; the reset must
    // replicate here so the glow & pulsing leaderboard entry clear.
    await WaitUntil (() => victim.ZapStreakCount == 0, 30, "victim's streak reset replicated to shooter");

    await RunPunchTheEaterPhase (victim); // Issue #192.

    // Third-person view (#119): toggle mid-run so the fire-rate & full-auto phases
    // below prove bolts still spawn from the aim ray with the chase camera live.
    // Toggle-until-third-person (instead of a single press) absorbs a persisted
    // third-person preference (the view survives restarts by design): whatever the
    // starting view, the phases below must run in third person.
    var startedThirdPerson = Self.IsThirdPerson;
    await ToggleViewUntil (thirdPerson: true);
    Assert (Self.IsThirdPerson, "third-person view toggled on (#119)");

    // Over-the-shoulder framing (#187): the chase camera is offset to the RIGHT of the
    // head & sits near eye level, then aimed at the crosshair point on the HEAD
    // camera's ray - so the crosshair stays truthful to the shot line (bolts still
    // leave the head camera) & our own body hangs to the lower-LEFT instead of
    // squatting dead center. Aim into the open first & let the spring arm reach full
    // extension: a wall-clipped arm shortens the rig & is a different measurement.
    AimAt (Self.GlobalPosition + new Vector3 (0, 1, 10)); // Aim away from everyone; open room behind us.
    await Task.Delay (400);
    // The bound is TIGHT on purpose: the aim is derived from the arm's live length, so
    // a correct rig is exact (0.00) & anything looser stops discriminating - the whole
    // drift this can suffer is under a degree even fully collapsed (CodeRabbit).
    Assert (Self.ChaseViewCrosshairErrorDegrees < 0.1f, $"chase camera looks straight down the crosshair's line (#187), off by {Self.ChaseViewCrosshairErrorDegrees:0.00} degrees");
    var chaseBodyOffset = Self.ChaseViewBodyOffset;
    Assert (chaseBodyOffset.X < -0.2f && chaseBodyOffset.Y < -0.2f, $"own body framed to the lower-LEFT of the chase view (#187), camera-local offset {chaseBodyOffset}");

    // ...& it stays truthful when the spring arm CLIPS (#187, CodeRabbit): the arm
    // shortens against geometry, so an aim computed once from the FULL length drifts
    // off the shot line exactly when the camera is pulled in. Aiming steeply upward
    // swings the arm back & DOWN into the floor, which pulls it right in. The floor,
    // not a wall: the spawn room is fenced by waist-high parapets that the arm at
    // head height sails straight over, so they never clip it at all.
    Self.Position = SpawnRoomCenter;
    await Task.Delay (400); // Settle onto the floor.
    AimAt (Self.GlobalPosition + new Vector3 (0.0f, 8.0f, 5.0f)); // Steeply up: the arm goes back & down into the floor.
    await Task.Delay (400); // Let the arm find it & the re-aim follow.
    Assert (Self.ChaseViewArmLengthMeters < Self.ThirdPersonBackMeters - 0.3f, $"the floor really did clip the spring arm (#119/#187), length {Self.ChaseViewArmLengthMeters:0.00}m of {Self.ThirdPersonBackMeters}m");
    // Same tight bound, & this is the one that would have caught the precomputed aim:
    // at this clip depth an aim fixed to the full 2.8m is off by roughly 0.2 degrees.
    Assert (Self.ChaseViewCrosshairErrorDegrees < 0.1f, $"clipped chase camera still looks down the crosshair's line (#187), off by {Self.ChaseViewCrosshairErrorDegrees:0.00} degrees");

    // Fire-rate cap: spamming can spawn at most 1 bolt (cooldown blocks recharging).
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
    await WaitUntil (() => !Self.Sliding && Self.Crouching, 10, "crouch press canceled the slide into a crouch (#131)");
    ReleaseAction ("slide");
    await Task.Delay (200);
    Assert (!Self.Sliding, "canceled slide did not restart from the held key (#131)");
    PressAction ("crouch");
    await Task.Delay (100);
    ReleaseAction ("crouch");
    await WaitUntil (() => !Self.Crouching, 10, "stood back up after the canceled slide (#131)");

    // Dance emote (#103): G starts the groove & any movement input cancels it,
    // restoring the normal standing state.
    PressAction ("dance");
    await Task.Delay (100);
    ReleaseAction ("dance");
    await WaitUntil (() => Self.Dancing, 10, "own dance started on G (#103)");
    Input.ActionPress ("move_forward");
    await Task.Delay (300);
    Input.ActionRelease ("move_forward");
    await WaitUntil (() => !Self.Dancing, 10, "moving canceled the dance (#103)");

    await RunMovementBatchPhases();

    // Boomerang (#98): collect the deterministic spawn-room pickup, throw it (aimed
    // away from everyone so no incidental steals), & watch it fly back into the hand.
    await WaitUntil (() => WalkedTo (WeaponSpawner.PlaytestBoomerangPosition), 45, "walked to the playtest boomerang pickup");
    await WaitUntil (() => Self.Holds (HeldWeapon.Boomerang), 15, "collected the boomerang pickup (#98)");
    PressAction ("weapon_4");
    await Task.Delay (100);
    ReleaseAction ("weapon_4");
    Assert (Self.SelectedWeapon == SelectedWeapon.Boomerang, "boomerang selected in slot 4 (#98)");
    // Down the -Z lane, not +X (#197): the pickup moved into the room's +X/+Z corner,
    // where the east wall is now point-blank; this lane is 11m of empty room with no
    // pickup near enough for the flight to scoop (the airplane sits 5.5m off it).
    AimAt (Self.GlobalPosition + new Vector3 (0, 1, -10));
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
    // Step off the pickup spot before the stone phases (#190): the playtest restocks
    // a slingshot right where we're standing, & an equipped-but-empty slingshot now
    // LOADS whatever it walks onto - so standing there would sling slingshots, not
    // stones. Mid-wall, not the corner (#197): the deterministic pickups moved into
    // the corners, & this spot still keeps the wall at z=6 point-blank for the #163
    // phases while sitting 5.5m clear of every one of them.
    Self.Position = new Vector3 (0.0f, 31.3f, 5.0f);
    await Task.Delay (400);
    await EmptySlingshot();
    Assert (Self.SlingshotAmmo == HeldWeapon.None, "slingshot is empty for the stone phases (#190)");
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
    AimAt (new Vector3 (Self.GlobalPosition.X, 31.3f, 6.0f)); // Mid-height of the wall ahead.
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

    await RunUniversalAmmoPhases();

    // Paper airplane (#102): collect the deterministic spawn-room pickup, walk near
    // the victim, & throw with them locked under the crosshair; the victim
    // punch-catches the incoming glider & the handoff swaps it into their hands.
    // Holster the slingshot first (#190): an equipped, empty slingshot LOADS a world
    // item instead of collecting it, & the airplane is a world item like any other.
    PressAction ("weapon_2");
    await Task.Delay (100);
    ReleaseAction ("weapon_2");
    Assert (Self.SelectedWeapon == SelectedWeapon.Laser, "slingshot holstered so the airplane can be collected (#190)");
    await WaitUntil (() => WalkedTo (WeaponSpawner.PlaytestAirplanePosition), 45, "walked to the playtest paper airplane pickup");
    await WaitUntil (() => Self.Holds (HeldWeapon.PaperAirplane), 15, "collected the paper airplane pickup (#102)");
    PressAction ("weapon_6");
    await Task.Delay (100);
    ReleaseAction ("weapon_6");
    Assert (Self.SelectedWeapon == SelectedWeapon.PaperAirplane, "paper airplane selected in slot 6 (#102)");
    // The victim fell & respawned earlier; wait for it to be back in the spawn room,
    // then throw from close by so the host can't wander into the flight path.
    // Keep some distance: the glider needs a moment of flight for the catch to be
    // catchable at all - throwing from a few meters lands it before anyone can swing.
    // Both of us take fixed marks in the empty arena for this phase (#102): the
    // spawn room's three-bot traffic kept putting the idle host under the crosshair,
    // & the throw locks onto whoever the ray finds first. Here the line is ours, &
    // the 8m gap gives the glider enough flight to actually be catchable.
    Self.Position = CatchThrowMark;
    await Task.Delay (500); // Settle onto the ground.
    await WaitUntil (() => victim.GlobalPosition.DistanceTo (CatchMark) < 3.0f, 60, "victim took its mark for the airplane catch (#102)");
    // A genuine punch-catch fires our own AirplaneCaught signal when the handoff is
    // validated (CodeRabbit on #180): a landing must NOT pass this phase.
    var airplaneCaught = false;
    Self.AirplaneCaught += _ => airplaneCaught = true;
    var airplanesBefore = _airplanesSpawned;

    for (var attempt = 0; attempt < 10 && _airplanesSpawned == airplanesBefore; ++attempt)
    {
      // Wait a drifting bystander off the line WITHOUT burning a throw attempt
      // (CodeRabbit): the old `continue` spent all 10 attempts in ~2.5s, which is
      // exactly the flake this phase's fixed marks were meant to kill.
      await TryWaitUntil (() => IsVictimTheNearestTarget (victim), 15);
      AimAt (victim.GlobalPosition + Vector3.Up);
      if (!IsVictimTheNearestTarget (victim)) { --attempt; continue; } // Still blocked: not a throw.
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

  // The eater's half of the bread ritual (issues #209 & #192): bread is a real weapon
  // slot you spawn with, left click starts a 3s ritual you can't move during, & the
  // whole three seconds heals you to full.
  //
  // Self-contained on our own spawn loaf: knuckles vs. wall (#122) is the cheapest
  // way below full health, which the don't-waste-the-loaf rule (#160) requires before
  // an eat can start at all. Covers the slot key, the moving rejection, the rooting,
  // & the heal. (The victim's later phase covers the interruption, on ITS loaf: an
  // interrupted loaf is wasted, so spending this one twice isn't an option.)
  private async Task RunBreadEatCompletionPhase()
  {
    await TakeBreadEatingPosition();
    Assert (Self.Health < Self.MaxHealth, $"punching the wall dented our own health (#122), health {Self.Health}/{Self.MaxHealth}");
    Assert (Self.Holds (HeldWeapon.Bread), "still carrying this life's loaf (#190)");
    await SelectBreadSlot();
    await AssertEatingOnTheMoveIsRejected();
    await StartEating ("standing still with bread out");
    await AssertEatingRootsUsInPlace();
    await WaitUntil (() => !Self.Eating, 15, "the ritual ran its full three seconds (#192)");
    Assert (Self.Health == Self.MaxHealth, $"the completed eat healed to full (#62), health {Self.Health}/{Self.MaxHealth}");
    Assert (!Self.Holds (HeldWeapon.Bread), "the completed eat consumed the loaf (#190)");
    Assert (Self.SelectedWeapon == SelectedWeapon.Fists, "the emptied bread slot fell back to fists (#209)");
    Self.Position = SpawnRoomCenter; // Back where the phases below expect to run.
    await Task.Delay (300);
  }

  // The attacker's half (#192): a rooted, eating player must be VISIBLE to every peer
  // - that's the whole risk/reward of the ritual - & a hit must cancel it, wasting
  // the loaf & healing nothing. The victim's replicated bread slot is our only cue
  // that it's about to eat, which is exactly what a real opponent has to go on.
  private async Task RunPunchTheEaterPhase (Player victim)
  {
    await WaitUntil (() => victim.SelectedWeapon == SelectedWeapon.Bread, 120, "victim's bread slot replicated to shooter (#209)");
    // Spawn armor absorbs punches outright (#48), so a still-armored victim would eat
    // the whole loaf uninterrupted - the same guard the punch phase uses (#213).
    await WaitUntil (() => !victim.SpawnArmor, 30, "victim's spawn armor expired before the bread interrupt (#48)");
    // Take the spot BEFORE it starts eating: the victim holds its one-per-life loaf
    // until it can see us standing in punching range, so this is the handshake.
    Self.Position = victim.GlobalPosition + new Vector3 (0.0f, 0.0f, -2.0f); // Behind it, off the wall it punched.
    await Task.Delay (400); // Settle onto the floor.
    PressAction ("weapon_1"); // Fists, so the swing is a real punch (#82).
    await Task.Delay (100);
    ReleaseAction ("weapon_1");
    // The eating state replicates WHILE the ritual is in progress, not just after it.
    await WaitUntil (() => victim.Eating, 90, "victim's in-progress eating state replicated to shooter (#192)");
    await Task.Delay (700); // Leave the victim its own window to prove the ritual roots it.

    // The whole ritual is only 3s, so swing about as fast as the punch cooldown allows.
    // Re-take the punching spot every attempt & skip a swing rather than spend it on
    // thin air (#213): a rooted victim can't dodge, but its position still reaches us
    // over the wire & can be stale on a lagging runner.
    for (var attempt = 0; attempt < 20 && victim.Eating; ++attempt)
    {
      Self.Position = victim.GlobalPosition + new Vector3 (0.0f, 0.0f, -2.0f); // Behind it, off the wall it punched.
      await Task.Delay (100); // Settle before swinging.
      if (Self.GlobalPosition.DistanceTo (victim.GlobalPosition) > Self.PunchRange * 0.75f) continue;
      AimAt (victim.GlobalPosition + Vector3.Up);
      PressLeftClick();
      await Task.Delay (60);
      ReleaseLeftClick();
      // Outlast the 0.3s punch cooldown between swings (#78): at the old 290ms cadence
      // every other swing landed inside it & was swallowed, & on a slow runner the
      // whole 3s ritual could pass with no connected punch.
      await Task.Delay (300);
    }

    // 30s, not 5 (#213): the ritual's own 3s expiry ends it either way, & the health
    // assert below is what tells a canceled eat from a completed one.
    await WaitUntil (() => !victim.Eating, 30, "our punch canceled the victim's eat (#192)");
    Assert (victim.Health < victim.MaxHealth, $"the canceled eat healed nobody (#192), victim health {victim.Health}/{victim.MaxHealth}");
    Self.Position = SpawnRoomCenter;
    await Task.Delay (300);
    PressAction ("weapon_2"); // The bolt-counting phases below need the laser back.
    await Task.Delay (100);
    ReleaseAction ("weapon_2");
    Assert (Self.SelectedWeapon == SelectedWeapon.Laser, "laser re-selected after the bread interrupt phase (#192)");
  }

  // The bread mark: mid-wall against the spawn room's z=6 wall, point-blank, so a few
  // fist-fulls of wall get us below full health (#122) without touching anyone. Mid-
  // wall, not the corner (#197): the deterministic pickups moved into the corners to
  // get out of the respawn scatter's claim reach, & an eater parked in one would
  // simply auto-claim the pickup mid-ritual. From here every one of them is 5.5m off.
  private async Task TakeBreadEatingPosition()
  {
    Self.Position = new Vector3 (0.0f, 31.3f, 5.0f);
    await Task.Delay (400); // Settle onto the floor.
    PressAction ("weapon_1");
    await Task.Delay (100);
    ReleaseAction ("weapon_1");
    AimAt (new Vector3 (0.0f, 31.3f, 6.0f)); // Mid-height of the wall ahead.

    for (var attempt = 0; attempt < 12 && Self.Health >= Self.MaxHealth; ++attempt)
    {
      PressLeftClick();
      await Task.Delay (80);
      ReleaseLeftClick();
      await Task.Delay (350);
    }
  }

  private async Task SelectBreadSlot()
  {
    PressAction ("weapon_0");
    await Task.Delay (100);
    ReleaseAction ("weapon_0");
    Assert (Self.SelectedWeapon == SelectedWeapon.Bread, "bread is selectable in its own slot (#209)");
  }

  // Moving rejects the eat (#192): no ritual ever starts while we're walking, & the
  // refused attempt costs nothing.
  private async Task AssertEatingOnTheMoveIsRejected()
  {
    var mark = Self.Position;
    Input.ActionPress ("move_back"); // Away from the wall we've been punching.
    await Task.Delay (350);
    PressLeftClick();
    await Task.Delay (200);
    ReleaseLeftClick();
    Assert (!Self.Eating, "eating while moving was rejected (#192)");
    Assert (Self.Holds (HeldWeapon.Bread), "the rejected attempt cost us nothing (#192)");
    Input.ActionRelease ("move_back");
    Self.Position = mark; // Back on the mark, so an attacker's rendezvous still holds.
    await WaitUntil (() => new Vector2 (Self.Velocity.X, Self.Velocity.Z).Length() < 0.5f, 10, "came to a full stop before eating (#192)");
  }

  // Rooted (#192): movement & jump input produce no motion at all, & neither escapes
  // the ritual.
  private async Task AssertEatingRootsUsInPlace()
  {
    var rootedFrom = Self.GlobalPosition;
    Input.ActionPress ("move_forward");
    PressAction ("jump");
    await Task.Delay (400);
    ReleaseAction ("jump");
    Input.ActionRelease ("move_forward");
    Assert (Self.Eating, "no movement or jump input escaped the ritual (#192)");
    Assert (Self.GlobalPosition.DistanceTo (rootedFrom) < 0.3f, $"eating rooted us in place (#192), drifted {Self.GlobalPosition.DistanceTo (rootedFrom):0.00}m");
  }

  // Left click with the loaf out starts the ritual (#192), retried until it takes:
  // the stationary check is honest about settling velocity & knockback shoves, so a
  // rejected press just means "not quite still yet".
  private async Task StartEating (string description)
  {
    for (var attempt = 0; attempt < 15 && !Self.Eating; ++attempt)
    {
      PressLeftClick();
      await Task.Delay (60);
      ReleaseLeftClick();
      await Task.Delay (200);
    }

    Assert (Self.Eating, $"left click started the eating ritual (#192): {description}");
  }

  // Death-drop coverage (issue #169): the victim died holding the deterministic
  // playtest banana, so RequestDrop's death path must have left a real, claimable
  // pickup on the floor under the body. That path had zero playtest coverage until
  // now - which is how the #167 regression (killed players' weapons vanishing into
  // nothing) reached players.
  private async Task RunDeathDropPhase (Player victim)
  {
    var deathSpot = victim.GlobalPosition;
    await WaitUntil (() => DroppedNear (HeldWeapon.Banana, deathSpot) != null, 15, "victim's banana dropped in the death spot's column (#169)");
    var drop = DroppedNear (HeldWeapon.Banana, deathSpot)!;
    // Death drops EVERYTHING (#190): the uneaten loaf lands in the same column as a
    // world pickup of its own, so it can be scavenged like any other drop.
    await WaitUntil (() => DroppedNear (HeldWeapon.Bread, deathSpot) != null, 15, "victim's uneaten bread dropped in the death spot's column (#190)");
    // Ray-grounded AT the death spot (#151/#172/#196): on the surface the body was
    // standing on, not floating above it & not a storey below - the ground ray used
    // to start at the player's feet, miss the floor underfoot, & drop weapons into
    // the arena 30m down (or nowhere at all, out on the arena floor).
    Assert (drop.GlobalPosition.Y > deathSpot.Y - 2.0f && drop.GlobalPosition.Y < deathSpot.Y + 2.0f, $"dropped banana grounded at the death spot (#196), drop y {drop.GlobalPosition.Y:0.00} vs death y {deathSpot.Y:0.00}");
    // Claimable: take it through the real claim path, before the drop expires. The
    // only other banana in the level is the playtest one down in the arena, so
    // starting empty-handed is what makes the wait below mean "the drop was claimed".
    Assert (!Self.Holds (HeldWeapon.Banana), "reached the death-drop phase with no banana of our own (#169)");
    Self.Position = drop.GlobalPosition;
    await WaitUntil (() => Self.Holds (HeldWeapon.Banana), 30, "victim's dropped banana was claimable (#169)");
    Self.Position = SpawnRoomCenter; // Back where the phases below expect to run.
    await Task.Delay (300); // Settle onto the floor.
    // That pickup auto-equipped the banana (#128) & the phases below count laser
    // bolts, so put the laser back in hand first.
    PressAction ("weapon_2");
    await Task.Delay (100);
    ReleaseAction ("weapon_2");
    Assert (Self.SelectedWeapon == SelectedWeapon.Laser, "laser re-selected after the death-drop phase (#169)");
  }

  // The nearest live pickup of a type around a spot, measured flat: the drop grounds
  // onto whatever lies below, so its height is the one thing this search must not assume.
  private WeaponPickup? DroppedNear (HeldWeapon type, Vector3 spot) => _world.GetChildren().OfType <WeaponPickup>().FirstOrDefault (pickup => pickup.Weapon == type && !pickup.IsQueuedForDeletion() && FlatDistance (pickup.GlobalPosition, spot) < DropSearchRadius);
  private static float FlatDistance (Vector3 a, Vector3 b) => new Vector2 (a.X - b.X, a.Z - b.Z).Length();

  // Movement & death-feel batch (#171/#147/#148/#149/#150): crouch un-stick, the
  // hold-to-crouch setting, slide-jump chaining, & standing slide expiry. Runs on
  // the open arena ground - the paper-thin slab is the exact surface the #171
  // regression wedged on - then returns to the spawn room for the pickup phases
  // (teleport precedent: the victim's fall-penalty phase).
  private async Task RunMovementBatchPhases()
  {
    Self.Position = new Vector3 (40.0f, 1.0f, -40.0f); // Open corner: no buildings, pillars, or platforms nearby.
    await Task.Delay (300); // Settle onto the ground.

    // The crouch phases must not depend on whatever crouch mode the developer's
    // real settings.cfg persists (CodeRabbit on #185): force each mode explicitly
    // & restore the real preference at the end.
    var startedHoldToCrouch = Settings.HoldToCrouch;
    Settings.HoldToCrouch = false;
    Self.RefreshCrouchMode();

    // Crouch un-stick (#171): a plain toggle on the thin arena ground must go down
    // AND back up, with the feet staying planted - the old center-scale sank the
    // body ~0.4m, so the overhead probe started under the slab, saw its underside,
    // & wedged the toggle down.
    var yBeforeCrouch = Self.GlobalPosition.Y;
    PressAction ("crouch");
    await Task.Delay (100);
    ReleaseAction ("crouch");
    await WaitUntil (() => Self.Crouching, 10, "crouch toggled down (#171)");
    await Task.Delay (400); // Give any (wrongly) sinking body time to sink before probing.
    Assert (Mathf.Abs (Self.GlobalPosition.Y - yBeforeCrouch) < 0.2f, $"crouch kept the feet planted (#171), drifted {Self.GlobalPosition.Y - yBeforeCrouch:0.00}m");
    PressAction ("crouch");
    await Task.Delay (100);
    ReleaseAction ("crouch");
    await WaitUntil (() => !Self.Crouching, 10, "crouch toggled back up (#171)");

    // Hold-to-crouch (#147): switch to hold mode - hold = crouch, release = stand.
    Settings.HoldToCrouch = true;
    Self.RefreshCrouchMode();
    PressAction ("crouch");
    await WaitUntil (() => Self.Crouching, 10, "hold mode: crouched while held (#147)");
    ReleaseAction ("crouch");
    await WaitUntil (() => !Self.Crouching, 10, "hold mode: stood up on release (#147)");
    Settings.HoldToCrouch = startedHoldToCrouch; // The developer's real preference survives the run.
    Self.RefreshCrouchMode();

    // Slide-jump chaining (#149): jumping out of a slide keeps its momentum in the
    // air & cancels the cooldown, so a slide pressed right after landing chains
    // faster than base - capped so it can't diverge. Aimed down the open -X lane at
    // z=-40: ~15m of travel with nothing to hit.
    await WaitUntil (() => Self.SlideReadyFraction >= 1.0f, 15, "slide cooldown ready for the chain test (#149)");
    AimAt (Self.GlobalPosition + new Vector3 (-20.0f, 0.0f, 0.0f));
    Input.ActionPress ("move_forward"); // The carry needs real momentum.
    PressAction ("slide");
    await WaitUntil (() => Self.Sliding, 10, "slide started for the chain (#149)");
    await Task.Delay (200);
    await JumpOutOfSlide();
    Assert (!Self.Sliding, "jump ended the slide (#149)");
    ReleaseAction ("slide");
    Assert (Self.SlideReadyFraction >= 1.0f, "slide-jump canceled the slide cooldown (#149)");
    var airSpeed = new Vector3 (Self.Velocity.X, 0.0f, Self.Velocity.Z).Length();
    Assert (airSpeed >= Self.Speed * Self.SlideSpeedMultiplier - 0.5f, $"slide momentum carried into the air (#149), speed {airSpeed:0.0}");
    await WaitUntil (() => Self.IsOnFloor(), 10, "landed from the slide-jump (#149)");
    PressAction ("slide");
    await WaitUntil (() => Self.Sliding, 10, "chained slide started with no cooldown (#149)");
    Assert (Self.CurrentSlideSpeed > Self.Speed * Self.SlideSpeedMultiplier + 0.1f, $"chained slide runs faster than base (#149), speed {Self.CurrentSlideSpeed:0.0}");
    Assert (Self.CurrentSlideSpeed <= Self.Speed * Self.SlideSpeedMultiplier * Self.MaxChainedSlideSpeedScale + 0.01f, $"chained slide speed capped (#149), speed {Self.CurrentSlideSpeed:0.0}");
    ReleaseAction ("slide");
    Input.ActionRelease ("move_forward");
    await WaitUntil (() => !Self.Sliding, 10, "chained slide released");

    // Chain window expiry (#149): outliving the landing window forfeits the chain -
    // the next slide runs at base speed again (CodeRabbit on #185).
    await WaitUntil (() => Self.SlideReadyFraction >= 1.0f, 15, "slide cooldown ready for the window-expiry test (#149)");
    AimAt (Self.GlobalPosition + new Vector3 (-20.0f, 0.0f, 0.0f)); // Same clear -X lane.
    Input.ActionPress ("move_forward");
    PressAction ("slide");
    await WaitUntil (() => Self.Sliding, 10, "slide started for the window-expiry test (#149)");
    await Task.Delay (200);
    await JumpOutOfSlide();
    Assert (!Self.Sliding, "jump ended the window-expiry slide (#149)");
    // Only a slide-JUMP clears the cooldown (#149); a slide that merely ran out
    // leaves it recharging, so this is the evidence that the jump landed.
    Assert (Self.SlideReadyFraction >= 1.0f, "the window-expiry slide was ended by a jump, not by expiry (#149)");
    ReleaseAction ("slide");
    await WaitUntil (() => Self.IsOnFloor(), 10, "landed from the window-expiry slide-jump (#149)");
    Input.ActionRelease ("move_forward");
    await Task.Delay (2000); // Far past the 0.5s window; generous because it counts (slower) physics time.
    PressAction ("slide");
    await WaitUntil (() => Self.Sliding, 10, "post-window slide started (#149)");
    Assert (Self.CurrentSlideSpeed <= Self.Speed * Self.SlideSpeedMultiplier + 0.01f, $"expired chain window: slide back at base speed (#149), speed {Self.CurrentSlideSpeed:0.0}");
    ReleaseAction ("slide");
    await WaitUntil (() => !Self.Sliding, 10, "post-window slide released");

    // Slide TIMER expiry (#148/#150): the slide runs its full duration & ends STANDING
    // in the open - no more forced crouch on expiry. Stationary (no movement input):
    // the timer & end pose don't need travel. The duration itself is pinned to the
    // shortened 3-4s burst band (#148, reversing the earlier lengthening to 7s), which
    // is the assert that would catch the value drifting back up.
    Assert (Self.SlideDurationSeconds is >= 3.0f and <= 4.0f, $"slide is a short burst, not a long glide (#148), got {Self.SlideDurationSeconds}s");
    await WaitUntil (() => Self.SlideReadyFraction >= 1.0f, 15, "slide cooldown recovered for the expiry test (#150)");
    PressAction ("slide");
    await WaitUntil (() => Self.Sliding, 10, "slide started for the expiry test (#150)");
    var slideStartMs = Time.GetTicksMsec();
    await WaitUntil (() => !Self.Sliding, Self.SlideDurationSeconds + 8, "slide timer expired on its own (#148)");
    var slideMs = Time.GetTicksMsec() - slideStartMs;
    ReleaseAction ("slide");
    // Lower bound only: the timer counts PHYSICS time, which a loaded runner dilates
    // behind the wall clock this measures, so an upper bound would just be flaky.
    Assert (slideMs >= (ulong)(Self.SlideDurationSeconds * 1000.0f) - 500, $"slide lasted its full duration (#148), got {slideMs}ms");
    Assert (!Self.Crouching, "expired slide ended standing, not crouched (#150)");

    // Back to the spawn room for the boomerang & slingshot pickup phases.
    Self.Position = SpawnRoomCenter;
    await Task.Delay (300);
  }

  // Jumps out of the slide we're in, re-pressing until it actually ends: a single
  // injected press can land in a frame that physics skips under load, & one press was
  // enough to time out a run that had nothing wrong with its chaining.
  //
  // The WHOLE loop has to finish inside the slide's own lifetime (#148/#213). A slide
  // is a 3.5s burst now, so a fixed budget that outlives it would let a retry land
  // after the timer had already expired on its own - & the phase would then pass
  // having never slide-jumped at all, which is the exact hole the cooldown evidence
  // assert was added to close. Derived from SlideDurationSeconds rather than hardcoded
  // so it can't drift out of step if that value is ever retuned again: 5 tries inside
  // 60% of the slide, leaving the rest of the burst as margin.
  private async Task JumpOutOfSlide()
  {
    const int retries = 5;
    const float pressSeconds = 0.1f;
    var perRetrySeconds = Self.SlideDurationSeconds * 0.6f / retries - pressSeconds;

    for (var attempt = 0; attempt < retries && Self.Sliding; ++attempt)
    {
      PressAction ("jump");
      await Task.Delay ((int)(pressSeconds * 1000.0f));
      ReleaseAction ("jump");
      await TryWaitUntil (() => !Self.Sliding, perRetrySeconds);
    }
  }

  // Slingshot universal ammo (#190): with the slingshot equipped & empty, walking
  // onto a world item LOADS it instead of collecting it - here the deterministic
  // laser pickup, which we already hold, so a normal pickup could never fire. Then
  // slinging it must empty the slingshot & put the laser back into the world as an
  // ordinary pickup, so nothing duplicates & nothing vanishes.
  // Universal ammo (#190) means an equipped slingshot hoovers up whatever it walks
  // onto, & this run's earlier phases can genuinely wander over the spawn room's
  // pickups - so discard anything nocked before the stone phases, by slinging it up
  // & over the wall where it can't be walked back onto.
  private async Task EmptySlingshot()
  {
    if (Self.SlingshotAmmo == HeldWeapon.None) return;
    AimAt (Self.GetNode <Camera3D> ("Camera3D").GlobalPosition + new Vector3 (0.0f, 30.0f, 30.0f));
    await SlingAStone (drawMs: 1500, "slingshot-emptying shot (#190)");
    await TryWaitUntil (() => Self.SlingshotAmmo == HeldWeapon.None, 5);
  }

  private async Task RunUniversalAmmoPhases()
  {
    Assert (Self.Holds (HeldWeapon.Slingshot) && Self.SelectedWeapon == SelectedWeapon.Slingshot, "slingshot still equipped for the ammo phase (#190)");
    Assert (Self.SlingshotAmmo == HeldWeapon.None, "slingshot starts empty (#190)");
    // Approach the laser pickup straight down the empty -Z lane, so the only item we
    // can walk onto on the way is the one under test.
    Self.Position = new Vector3 (WeaponSpawner.PlaytestLaserPosition.X, 31.3f, 0.5f);
    await Task.Delay (400);
    // We already hold a laser, so a NORMAL pickup could never fire here: any load at
    // all proves the equipped slingshot changed what walking onto an item means.
    Assert (Self.Holds (HeldWeapon.Laser), "already holding a laser, so only an ammo load can happen (#190)");
    await WaitUntil (() => WalkedTo (WeaponSpawner.PlaytestLaserPosition), 45, "walked back onto the laser pickup with the slingshot equipped (#190)");
    await WaitUntil (() => Self.SlingshotAmmo == HeldWeapon.Laser, 20, "walking onto the laser LOADED it as slingshot ammo instead of collecting it (#190)");

    // Let the playtest spot restock BEFORE the landing check & then step off it, so
    // the only laser pickup that can appear afterwards is the one we sling.
    await WaitUntil (() => LaserPickupNames (WeaponSpawner.PlaytestLaserPosition).Any(), 20, "the playtest laser spot restocked (#72)");
    Self.Position = new Vector3 (WeaponSpawner.PlaytestLaserPosition.X, 31.3f, 0.5f);
    await Task.Delay (400);
    // Aimed down the empty -Z lane into the spawn-room floor, so the slung laser
    // comes to rest on real ground well clear of us (& can't be instantly reloaded).
    AimAt (Self.GlobalPosition + new Vector3 (0.0f, -1.0f, -6.0f));
    var lasersBefore = LaserPickupNames();
    var ammoStonesBefore = _stonesSpawned;
    // A soft lob, NOT a full draw (issue #272): now that the draw is engine-time
    // honest, a 900ms draw punches the stone clean through the paper-thin spawn-room
    // slab & it falls off-world - the server correctly skips the landing ("no ground
    // beneath") & returns the laser via the caps, & the landed-pickup wait times out.
    await SlingAStone (drawMs: 300, "loaded-ammo shot (#190)");
    Assert (_stonesSpawned > ammoStonesBefore, "fired the loaded laser out of the slingshot (#190)");
    await WaitUntil (() => Self.SlingshotAmmo == HeldWeapon.None, 10, "firing emptied the slingshot (#190)");
    // Nothing may vanish: the slung laser has to come back as an ordinary pickup.
    // Capture the name INSIDE the wait (CodeRabbit): re-querying afterwards could
    // find the pickup already claimed or expired & throw an opaque First() instead
    // of a named assertion failure.
    var landedName = string.Empty;
    await WaitUntil (() => (landedName = LaserPickupNames().Except (lasersBefore).FirstOrDefault() ?? string.Empty).Length > 0, 20, "the slung laser landed as a world pickup again (#190)");
    // ...& it has to rest on the floor it actually hit (the #151/#172 ray-grounding
    // conventions): the spawn-room slab is paper thin, & a ground ray starting
    // exactly on it used to fall through onto the arena 30m below.
    var landed = _world.GetNode <WeaponPickup> (landedName);
    Assert (landed.Position.Y > 20.0f, $"the slung laser rested on the spawn-room floor, not through it (#190), y={landed.Position.Y:0.0}");
  }

  private List <string> LaserPickupNames() => _world.GetChildren().OfType <WeaponPickup>().Where (pickup => pickup.Weapon == HeldWeapon.Laser).Select (pickup => pickup.Name.ToString()).ToList();
  private List <string> LaserPickupNames (Vector3 near) => _world.GetChildren().OfType <WeaponPickup>().Where (pickup => pickup.Weapon == HeldWeapon.Laser && pickup.Position.DistanceTo (near) < 1.0f).Select (pickup => pickup.Name.ToString()).ToList();

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
    _world.StartClientSession (VictimName, difficulty: 0, _address, _port, Password, VictimColor);
    await WaitUntil (() => _world.GetPlayers().Count() == 3, 60, "all 3 players visible");
    Assert (Self.MaxHealth == 400, $"own MaxHealth is Beginner 400, got {Self.MaxHealth}");
    // Chosen body colors (issue #43): own pick stuck & both peers' picks replicate to the victim.
    Assert (Self.ColorIndex == VictimColor, $"own chosen color is {VictimColor}, got {Self.ColorIndex}");
    await WaitUntil (() => FindPlayer (ShooterName)?.ColorIndex == ShooterColor && FindPlayer (HostName)?.ColorIndex == HostColor, 30, "shooter's & host's chosen colors replicated to victim (#43)");
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
    await WaitUntil (() => !Self.SpawnArmor, 30, "spawn armor expired on its own");
    // Streak replication (#88): simulate an active 3-streak on our own authority so
    // the shooter can verify it replicates - & that the death reset replicates too.
    Self.ZapStreakCount = 3;
    // Dance emote (#103): groove on G; the shooter verifies the replicated state on
    // its puppet copy, & the punch damage below must cancel the dance.
    PressAction ("dance");
    await Task.Delay (100);
    ReleaseAction ("dance");
    await WaitUntil (() => Self.Dancing, 10, "dance started on G (#103)");
    // The shooter opens fire once armor drops; verify damage & then a full respawn.
    await WaitUntil (() => Self.Health < Self.MaxHealth, 120, "took damage from shooter");
    Assert (!Self.Dancing, "taking damage canceled the dance (#103)");
    // One-hit-kill (#93): after the punch phase, the shooter only fires full-charge
    // shots, & a full-charge shot is lethal on any target - so no partial-damage
    // health value may ever appear between the punch & the respawn reset.
    var partialLaserHits = 0;
    var healthAfterPunch = Self.Health;
    Self.HealthChanged += value => partialLaserHits += value > 0 && value < healthAfterPunch ? 1 : 0;
    // Death-drop coverage (#169): arm up AFTER the punch (a punch has a drop chance of
    // its own, which would empty our hands again) & carry the banana to the fixed kill
    // spot, so the kill actually runs RequestDrop's death path. Teleporting to the
    // pickup & to the kill spot follows the fall-penalty phase's precedent below.
    Self.Position = WeaponSpawner.PlaytestBananaPosition; // Down in the empty arena, then straight back up.
    await WaitUntil (() => Self.Holds (HeldWeapon.Banana), 20, "collected the playtest banana before the kill (#169)");
    // Loaded-slingshot death drops (#212): take the spawn-room slingshot & nock the
    // deterministic laser in it, so the kill below has to drop BOTH - the slingshot
    // out of the held mask via RequestDrop, & the nocked laser out of the server's
    // ammo escrow (#190), which is the half that had nowhere to come from before.
    // Both playtest spots restock unconditionally in --playtest mode, so borrowing
    // them here can't starve the shooter's own boomerang/slingshot/ammo phases.
    Self.Position = WeaponSpawner.PlaytestSlingshotPosition;
    await WaitUntil (() => Self.Holds (HeldWeapon.Slingshot), 30, "collected the playtest slingshot before the kill (#212)");
    Assert (Self.SelectedWeapon == SelectedWeapon.Slingshot, "the slingshot pickup auto-equipped, so it can load ammo (#128/#190)");
    // An equipped, EMPTY slingshot loads a world item instead of collecting it (#190).
    // The BOOMERANG spot, not the laser's: the shooter walks to the laser corner in
    // the very phase that runs alongside this one, & two bodies converging on one
    // corner simply block each other out of claim reach (observed: the shooter's walk
    // timed out while we stood on its target). The shooter's own boomerang phase is
    // most of a run away from here, & every playtest spot restocks unconditionally,
    // so borrowing this one costs it nothing.
    Self.Position = WeaponSpawner.PlaytestBoomerangPosition;
    await WaitUntil (() => Self.SlingshotAmmo == HeldWeapon.Boomerang, 30, "nocked the playtest boomerang in the slingshot before the kill (#212)");
    Self.Position = KillSpot; // Straight off the pickup spot, before its restock can be collected too.
    await Task.Delay (400); // Settle onto the floor.
    // Pins the precondition the #212 asserts below rest on: the boomerang is NOCKED &
    // none is in hand, so a boomerang pickup at the death spot can only have come out
    // of the server's ammo escrow - never out of RequestDrop's held mask.
    Assert (Self.SlingshotAmmo == HeldWeapon.Boomerang && !Self.Holds (HeldWeapon.Boomerang), $"reached the kill spot with the boomerang nocked & none in hand (#212), nocked {Self.SlingshotAmmo}, held {Self.HeldWeapon}");
    // Death sequence (#152): the kill drops us where we stand for ~DeathSequenceSeconds
    // with the pulled-back death camera live, & only then the usual armored respawn.
    await WaitUntil (() => Self.Fallen, 120, "own death started the lie-down (#152)");
    var fallenStartMs = Time.GetTicksMsec();
    Assert (Self.IsDeathViewActive, "death camera pulled back over the death spot (#152)");
    // The death drop runs before the lie-down starts (#169), so our hands are already
    // empty here - the shooter asserts the pickups themselves landed & are claimable.
    // "Everything" now literally means everything (#190): the uneaten loaf rides the
    // same mask, so an empty mask here proves the bread went with the weapons.
    Assert (Self.HeldWeapon == HeldWeapon.None, $"death dropped every carried item, bread included (#169/#190), still holding {Self.HeldWeapon}");
    await AssertLoadedSlingshotDroppedBoth();
    await AssertDeadBodyIsRootedInPlace();
    await WaitUntil (() => Self.SpawnArmor && Self.Health == Self.MaxHealth, 120, "died & respawned with armor & full health");
    var lieDownMs = Time.GetTicksMsec() - fallenStartMs;
    Assert (lieDownMs >= (ulong)(Self.DeathSequenceSeconds * 1000.0f) - 500, $"lay at the death spot ~{Self.DeathSequenceSeconds}s before respawning (#152), got {lieDownMs}ms");
    Assert (!Self.Fallen && !Self.IsDeathViewActive, "respawn ended the lie-down & restored the normal view (#152)");
    Assert (partialLaserHits == 0, $"full-charge kill took exactly one hit (#93), saw {partialLaserHits} partial-damage hits");
    Assert (Self.GlobalPosition.Y > 20.0f, $"respawned up in the spawn room, y={Self.GlobalPosition.Y}");
    // >= 1: an incidental one-hit kill on the host in the line of fire also counts.
    await WaitUntil (() => FindPlayer (ShooterName)?.Score >= 1, 30, "shooter's score replicated to victim");
    // Streak glow (#77/#88): the shooter's kill streak must replicate to the victim's
    // copy of the shooter node, since that drives the glow & leaderboard pulsing here.
    await WaitUntil (() => FindPlayer (ShooterName)?.ZapStreakCount >= 1, 30, "shooter's streak replicated to victim");
    // The bread ritual (#192) runs on THIS life's fresh loaf, after the death-drop
    // phase has already proved the previous one landed as a pickup: an interrupted
    // eat wastes the loaf, so it can't be the one the death drop is counting on.
    await RunBreadInterruptedPhase();
    // Fall penalty goes negative (issue #108): step off the world at score 0 & verify -1.
    Assert (Self.Score == 0, $"own score is 0 before the fall, got {Self.Score}");
    Self.Position = new Vector3 (120.0f, 5.0f, 120.0f); // Beyond the arena: nothing below but the kill boundary.
    await WaitUntil (() => Self.Score == -1, 60, "fall at score 0 dropped own score to -1");
    // Respawned from the fall; the shooter's paper airplane phase needs us standing
    // in the spawn room (#102).
    await WaitUntil (() => Self.GlobalPosition.Y > 20.0f, 30, "respawned in the spawn room after the fall");
    await WaitForTheShooterToClearTheCatchMark();
    await DitchStraySlingshot(); // Before leaving the spawn room, so the drop stays here.
    // Take up a fixed mark out in the empty arena for the catch (#102): the three
    // bots milling about the spawn room made this phase a lottery - the idle host
    // kept wandering under the shooter's crosshair & stealing the airplane's target
    // lock. Down here the line between the two of us is ours alone.
    Self.Position = CatchMark;
    await Task.Delay (500); // Settle onto the ground.
    // The throw replicates (#102): the shooter's flying airplane must appear here as
    // a visual copy before there's anything to catch.
    // 300s, not 150 (CodeRabbit): the shooter still has its universal-ammo phase &
    // its own walk to the airplane pickup to get through before it can throw, & the
    // sum of those per-step budgets is already larger than the old bound.
    await WaitUntil (() => _world.GetChildren().OfType <PaperAirplaneProjectile>().Any(), 300, "shooter's thrown airplane replicated as a flying copy (#102)");
    // Targeted-only warning (#191): the airplane locked onto US, so our own ring
    // reads a live threat - & it must clear the moment the catch takes it away.
    await WaitUntil (() => Self.AirplaneThreatFraction > 0.0f, 30, "the incoming airplane raised our own warning ring (#191)");
    // The signature catch (#102): watch the shooter's incoming airplane & punch it
    // out of the air once it's in reach; the handoff must land in our own hands.
    // Catching still beats the hazard (#191): a caught airplane never ignites anyone.
    // Catching requires fists out - re-select in case a wandering auto-claim ever
    // auto-equipped something else (#128).
    PressAction ("weapon_1");
    await Task.Delay (100);
    ReleaseAction ("weapon_1");
    await PunchCatchAirplane();
    Assert (Self.Holds (HeldWeapon.PaperAirplane), "punch-caught the incoming paper airplane & it was granted (#102)");
    Assert (Self.SelectedWeapon == SelectedWeapon.PaperAirplane, "the caught paper airplane auto-equipped (#128)");
    Assert (!Self.Burning, "punch-catching the airplane never ignites the catcher (#191)");
    await WaitUntil (() => Self.AirplaneThreatFraction <= 0.0f, 10, "the warning ring cleared once the airplane was caught (#191)");
    // Give the shooter time to observe the handoff before the landmine scenario.
    await Task.Delay (3000);
    await RunLandminePhase();
    // The shooter's forged admin RPC must never have been relayed to us: the
    // server drops admin messages from any sender but peer 1 (#158).
    Assert (_adminMessages.All (message => !message.Contains ("FORGED")), "forged admin RPC never relayed to the victim (#158)");
  }

  // Loaded-slingshot death drops (issue #212): dying with something nocked has to
  // leave TWO separate world pickups at the death spot - the slingshot itself, out of
  // the held mask via RequestDrop, & the nocked item, released out of the server-side
  // ammo escrow (#190) that is the only place it exists. Neither may be folded into
  // the other, neither may vanish, & both ray-ground at the death spot per the
  // #151/#172/#196 conventions. Asserted from the dying peer during its own lie-down:
  // the drops all resolve at death time, before the body is even on the floor.
  private async Task AssertLoadedSlingshotDroppedBoth()
  {
    Assert (Self.SlingshotAmmo == HeldWeapon.None, $"death emptied the slingshot (#212), still nocking {Self.SlingshotAmmo}");
    var deathSpot = Self.GlobalPosition;
    await WaitUntil (() => DroppedNear (HeldWeapon.Slingshot, deathSpot) != null, 15, "the slingshot dropped at the death spot (#212)");
    await WaitUntil (() => DroppedNear (HeldWeapon.Boomerang, deathSpot) != null, 15, "the nocked boomerang dropped as its OWN pickup at the death spot (#212)");
    var slingshot = DroppedNear (HeldWeapon.Slingshot, deathSpot)!;
    var ammo = DroppedNear (HeldWeapon.Boomerang, deathSpot)!;
    var apart = slingshot.GlobalPosition.DistanceTo (ammo.GlobalPosition);
    // Separate pickups, not one pile: a single walk-over must never swallow both.
    Assert (apart > 0.5f, $"the slingshot & its nocked boomerang landed as two separate pickups (#212), {apart:0.00}m apart");
    // Grounded AT the death spot, like every other death drop (#151/#172/#196).
    Assert (Mathf.Abs (ammo.GlobalPosition.Y - deathSpot.Y) < 2.0f, $"the released ammo grounded at the death spot (#196/#212), drop y {ammo.GlobalPosition.Y:0.00} vs death y {deathSpot.Y:0.00}");
    Assert (Mathf.Abs (slingshot.GlobalPosition.Y - deathSpot.Y) < 2.0f, $"the dropped slingshot grounded at the death spot (#196/#212), drop y {slingshot.GlobalPosition.Y:0.00} vs death y {deathSpot.Y:0.00}");
  }

  // The interrupted half of the bread ritual (issue #192): YOU can't cancel the three
  // seconds, but an attacker can. The shooter watches our replicated bread slot &
  // eating state - the same cues a real opponent gets - & swings; the hit must end
  // the ritual, WASTE the loaf, heal nothing, & drop the empty slot back to fists.
  //
  // Runs at the bread mark (the spawn-room corner with the wall at z=6 point-blank):
  // a fresh life starts at full health, & the don't-waste-the-loaf rule (#160) refuses
  // an eat there, so a few knuckles vs. wall (#122) open the ritual up.
  // The walking-corpse regression (#216): the lie-down disables input, but Move()
  // used to read the keys directly & stroll the body away from the death spot.
  // Holding forward for a second of the lie-down must not move us at all.
  private async Task AssertDeadBodyIsRootedInPlace()
  {
    if (!Self.Fallen) return; // The lie-down already ended: nothing left to prove this run.
    var deathSpot = Self.Position;
    PressAction ("move_forward");
    await Task.Delay (1000);
    ReleaseAction ("move_forward");
    var drift = (Self.Position - deathSpot) with { Y = 0.0f };
    Assert (Self.Fallen, "still lying down after the rooted-movement window (#216)");
    Assert (drift.Length() < 0.1f, $"dead body stayed rooted despite held movement input (#216), drifted {drift.Length():F2}m");
  }

  private async Task RunBreadInterruptedPhase()
  {
    Assert (Self.Holds (HeldWeapon.Bread), "this life restocked the loaf (#62/#190)");
    await WaitUntil (() => !Self.SpawnArmor, 30, "spawn armor expired before the bread ritual (#192)");
    await TakeBreadEatingPosition();
    Assert (Self.Health < Self.MaxHealth, $"punching the wall dented our own health (#122), health {Self.Health}/{Self.MaxHealth}");
    await SelectBreadSlot(); // The shooter watches for this before coming over.
    await AssertEatingOnTheMoveIsRejected();
    // Only start once the shooter has taken its punching position: the loaf is
    // one-per-life, so a ritual nobody can reach would just be wasted.
    await WaitUntil (() => FindPlayer (ShooterName)?.GlobalPosition.DistanceTo (Self.GlobalPosition) < 3.5f, 120, "shooter took punching position (#192)");
    await StartEating ("standing still with bread out");
    await AssertEatingRootsUsInPlace();
    // The shooter is swinging now: the hit must end it. A completed eat would have
    // healed us to full instead, which is exactly what the health assert catches.
    await WaitUntil (() => !Self.Eating, 30, "the incoming punch canceled the eat (#192)");
    Assert (Self.Health < Self.MaxHealth, $"the canceled eat granted no heal (#192), health {Self.Health}/{Self.MaxHealth}");
    Assert (!Self.Holds (HeldWeapon.Bread), "the interrupted loaf was wasted (#192)");
    Assert (Self.SelectedWeapon == SelectedWeapon.Fists, "the emptied bread slot fell back to fists (#209)");
  }

  // Landing & landmine (#102 & #191): the caught airplane is thrown into the floor
  // with nobody under the crosshair, so the glide ends with no target - it comes down
  // ARMED as a grounded pickup, & walking onto it makes US the mine's one target.
  // Fastest beeping immediately, ignite about a second later, then the personal pop.
  private async Task RunLandminePhase()
  {
    Assert (Self.Holds (HeldWeapon.PaperAirplane), "holding the airplane to arm the landmine with (#191)");
    Assert (!Self.Holds (HeldWeapon.Slingshot), "no slingshot, so the grounded airplane is a mine & not ammo (#190/#191)");
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
    await WaitUntil (() => LandedAirplane() != null, 15, "landed airplane became a grounded pickup (#102)");
    Assert (LandedAirplane()!.Armed, "an airplane that came down from flight is ARMED (#191)");
    // Spawn armor would (rightly) refuse to set the mine off, so wait it out first.
    await WaitUntil (() => !Self.SpawnArmor, 20, "spawn armor expired before stepping on the mine (#191)");
    await WaitUntil (WalkedToLandedAirplane, 45, "walked onto the armed paper airplane (#191)");
    // Fastest beeping & blinking immediately: the ring is pinned at maximum for the
    // whole fuse, & only the stepper's own HUD ever sees it.
    await WaitUntil (() => Self.AirplaneThreatFraction >= 1.0f, 10, "the mine pinned our warning ring at maximum (#191)");
    await WaitUntil (() => Self.Burning, 20, "the mine's fuse set us alight (#191)");
    // Damage over time while burning, then the pop finishes the job.
    var healthWhileBurning = Self.Health;
    await WaitUntil (() => Self.Health < healthWhileBurning, 5, "burning damaged us over time (#191)");
    await WaitUntil (() => Self.Fallen, 15, "the airplane popped us (#191)");
    // Fallen & Burning clear in separate frames, so wait (CodeRabbit): asserting in
    // the same tick could read the stale burning state & fail a correct run.
    await WaitUntil (() => !Self.Burning, 5, "the fire went out with the pop (#191)");
    await WaitUntil (() => !Self.Fallen && Self.SpawnArmor, 30, "respawned armored after the landmine (#191)");
    Assert (!Self.Burning, "a fresh life never inherits the fire (#191)");
    // Exactly one airplane, always (#102/#191): the caps fold a new one straight away.
    await WaitUntil (() => AirplaneCount() == 1, 25, "exactly one paper airplane back in the level (#102/#191)");
  }

  // The shooter's movement batch (#148/#149/#150) teleports onto the very corner we
  // park on for the airplane catch (#102), & a body already standing there is
  // something to ride up & off: its slide leaves the floor, so the slide-jump that
  // ends it is swallowed (#149 needs IsOnFloor) & the chain phase wedges. Its trip
  // DOWN into the arena & back UP to the spawn room afterwards is the all-clear.
  // Waited out from a quiet spot away from the spawn room's pickups, so idling here
  // can't auto-claim one (#128).
  private async Task WaitForTheShooterToClearTheCatchMark()
  {
    Self.Position = new Vector3 (5.0f, 31.3f, -3.0f); // Clear of all four deterministic pickup spots.
    await Task.Delay (300); // Settle onto the floor.
    var wentDown = false;

    await WaitUntil (() =>
    {
      var shooter = FindPlayer (ShooterName);
      if (shooter == null) return false;
      wentDown |= shooter.GlobalPosition.Y < 20.0f;
      return wentDown && shooter.GlobalPosition.Y > 20.0f;
    }, 240, "shooter finished its arena phases & cleared the catch mark (#149)");
  }

  // The spawn room's deterministic pickups (#72) sit inside the +/-4 random spawn
  // scatter, so a respawn can land right on one & auto-claim it (#128) - the same
  // hazard the shooter's spawn snapshot guards against. A stray slingshot is the one
  // that breaks the landmine phase below: an equipped slingshot LOADS an armed
  // airplane as ammo instead of setting it off (#190). Dropped here, in the spawn
  // room, so the drop is left far behind when we take our mark in the arena.
  private async Task DitchStraySlingshot()
  {
    if (!Self.Holds (HeldWeapon.Slingshot)) return;
    PressAction ("weapon_5"); // DropHeldWeapon prefers the selected slot (#82).
    await Task.Delay (100);
    ReleaseAction ("weapon_5");
    Self.DropHeldWeapon();
    await WaitUntil (() => !Self.Holds (HeldWeapon.Slingshot), 30, "ditched a slingshot auto-claimed by an unlucky respawn (#128)"); // Server-side drop, so a wire budget (#213).
  }

  // Every airplane anywhere: pickups on the ground plus whatever is in someone's
  // hands, which together is what the exactly-one invariant is about (#102).
  private int AirplaneCount() =>
    _world.GetChildren().OfType <WeaponPickup>().Count (pickup => pickup.Weapon == HeldWeapon.PaperAirplane && !pickup.IsQueuedForDeletion())
    + _world.GetPlayers().Count (player => player.Holds (HeldWeapon.PaperAirplane));

  // The airplane we just landed (#102): near us & NOT the deterministic spawn-room
  // pickup, which belongs to the shooter's collection phase.
  private WeaponPickup? LandedAirplane() => _world.GetChildren().OfType <WeaponPickup>().FirstOrDefault (IsCatchRecoveryPickup);

  private bool WalkedToLandedAirplane()
  {
    var pickup = LandedAirplane();
    if (pickup == null) return Self.Burning || Self.AirplaneThreatFraction > 0.0f; // Already stepped on it.
    return WalkedTo (pickup.GlobalPosition);
  }

  // The airplane locks onto whoever the crosshair ray finds first (#102), so the
  // throw only reaches the victim while no one else is standing on the line to it.
  // Merely being closer to us doesn't matter - the idle host often parks nearby but
  // well off the line, & requiring it to be farther away than the victim never came
  // true. This is the condition the ray itself cares about.
  private bool IsVictimTheNearestTarget (Player victim) => FindThrowBlocker (victim) == null;

  private Player? FindThrowBlocker (Player victim)
  {
    var from = Self.GlobalPosition + Vector3.Up;
    var to = victim.GlobalPosition + Vector3.Up;
    return _world.GetPlayers().FirstOrDefault (player => player != Self && player != victim && DistanceToSegment (player.GlobalPosition + Vector3.Up, from, to) <= 2.0f);
  }

  private static float DistanceToSegment (Vector3 point, Vector3 from, Vector3 to)
  {
    var line = to - from;
    var lengthSquared = line.LengthSquared();
    if (lengthSquared < 0.001f) return point.DistanceTo (from);
    return point.DistanceTo (from + line * Mathf.Clamp ((point - from).Dot (line) / lengthSquared, 0.0f, 1.0f));
  }

  // Legacy-client check (issue #170): join the way a pre-#170 client does (the
  // 4-argument RequestPlayerSlot RPC with no version), expect the server to kick
  // us with the exact update-required reason old clients can already display
  // (#109), then wait out the disconnect so the next join starts clean.
  private async Task AssertLegacyJoinIsKicked()
  {
    var kickReason = string.Empty;
    _world.KickedFromServer += reason => kickReason = reason;
    _world.StartLegacyClientSession (VictimName, difficulty: 0, _address, _port, Password);
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
    _world.StartClientSession (VictimName, difficulty: 0, _address, _port, Password, version: "0.0.0-spoofed");
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
    _world.StartClientSession (VictimName, difficulty: 0, _address, _port, "wrong-" + Password);
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
    // Wait on OBSERVED draw, never wall time (issue #272): draw accumulates in
    // physics ticks, & a starved CI runner fits under 0.2s of engine time into a
    // 900ms wall-clock hold - the release lands sub-minimum & silently fires
    // nothing, every retry alike. Reading the player's own draw clock instead
    // makes the hold immune to frame starvation.
    var targetDrawSeconds = Mathf.Min (drawMs / 1000.0f, Self.SlingshotMaxDrawSeconds);

    for (var attempt = 0; attempt < 5; ++attempt)
    {
      // A pickup grant can land mid-phase & auto-equip itself (#128 is by design) -
      // an airplane did exactly that in CI & the shoot press THREW it, dropping the
      // shooter to fists for every retry (issue #272). Re-assert the selection, so a
      // selection stomp costs one loud reselect instead of the whole phase.
      if (Self.SelectedWeapon != SelectedWeapon.Slingshot)
      {
        GD.Print ($"SlingAStone: selection was stomped to {Self.SelectedWeapon} - reselecting the slingshot (issue #272)");
        PressAction ("weapon_5");
        var reselected = await TryWaitUntil (() => Self.SelectedWeapon == SelectedWeapon.Slingshot, 5);
        ReleaseAction ("weapon_5");
        if (!reselected) continue; // No slingshot in hand (lost to a death?) - a draw wait would just burn 15s (CodeRabbit).
      }

      _lastStone = null;
      PressAction ("shoot");
      await TryWaitUntil (() => Self.SlingshotDrawSeconds >= targetDrawSeconds, 15);
      // Diagnosis for issue #272: which link breaks - the press, the draw, or the fire.
      GD.Print ($"SlingAStone attempt {attempt}: pressed={Input.IsActionPressed ("shoot")} draw={Self.SlingshotDrawSeconds:0.00}/{targetDrawSeconds:0.00} selected={Self.SelectedWeapon} ammo={Self.SlingshotAmmo}");
      ReleaseAction ("shoot");
      // Only OUR live stone counts (CodeRabbit): TrackStone also catches remote
      // players' visual copies, which would false-pass the spawn assert.
      await TryWaitUntil (() => _lastStone != null && IsInstanceValid (_lastStone) && _lastStone.Shooter == Self, 3);
      if (_lastStone != null && IsInstanceValid (_lastStone) && _lastStone.Shooter == Self) return _lastStone;
      GD.Print ($"SlingAStone attempt {attempt}: released, no stone; draw now {Self.SlingshotDrawSeconds:0.00}");
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
