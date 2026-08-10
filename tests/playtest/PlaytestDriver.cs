using System;
using System.Linq;
using System.Threading.Tasks;
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
  private const string HostName = "Host";
  private const string ShooterName = "Shooter";
  private const string VictimName = "Victim";
  private World _world = null!;
  private string _role = string.Empty;
  private string _address = "127.0.0.1";
  private int _boltsSpawned;
  private Player? _self;
  private Player Self => _self ??= _world.GetPlayers().First (player => player.IsMultiplayerAuthority());
  private Player? FindPlayer (string name) => _world.GetPlayers().FirstOrDefault (player => player.DisplayName == name);

  public override void _Ready()
  {
    // Three instances share the CI runner; uncapped frame loops starve physics &
    // ENet, dilating in-game time far behind the wall clock this driver waits on.
    Engine.MaxFps = 30;
    _world = GetNode <World> ("/root/World");
    _world.ChildEnteredTree += node => _boltsSpawned += node is LaserBolt ? 1 : 0;
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
    _world.StartHostSession (HostName, difficulty: 2, Port);
    await WaitUntil (() => _world.GetPlayers().Count() == 3, 60, "all 3 players joined");
    // Shooter kills victim once; wait to observe the replicated score.
    await WaitUntil (() => FindPlayer (ShooterName)?.Score == 1, 120, "shooter's kill replicated to host");
    // Victim respawns with armor visible to the host too.
    await WaitUntil (() => FindPlayer (VictimName)?.SpawnArmor == true, 30, "victim respawn armor replicated to host");
    // Stay up until both clients have finished & disconnected.
    await WaitUntil (() => _world.GetPlayers().Count() == 1, 120, "clients disconnected");
  }

  private async Task RunShooter()
  {
    _world.StartClientSession (ShooterName, difficulty: 1, _address, Port);
    await WaitUntil (() => _world.GetPlayers().Count() == 3, 60, "all 3 players visible");
    var victim = FindPlayer (VictimName)!;
    var host = FindPlayer (HostName)!;
    Assert (victim.MaxHealth == 400, $"victim MaxHealth replicated as Beginner 400, got {victim.MaxHealth}");
    Assert (host.MaxHealth == 200, $"host MaxHealth replicated as Expert 200, got {host.MaxHealth}");
    Assert (Self.MaxHealth == 300, $"own MaxHealth is Intermediate 300, got {Self.MaxHealth}");

    // Movement: hold forward briefly & verify we actually moved.
    var startPosition = Self.GlobalPosition;
    Input.ActionPress ("move_forward");
    await Task.Delay (600);
    Input.ActionRelease ("move_forward");
    Assert (Self.GlobalPosition.DistanceTo (startPosition) > 0.3f, "player moved with input");

    // Wait out everyone's initial spawn armor before the damage phase.
    await WaitUntil (() => !victim.SpawnArmor, 15, "victim's initial spawn armor expired");

    // Punch phase: walk up to the victim & punch them; verify melee damage lands.
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

    // Charged shots until the victim dies (shooter is told via NotifyScored -> Score).
    // Retry until a bolt actually spawns each attempt - under CI load, physics time
    // (which drives the weapon cooldown) can lag the wall clock these delays use.
    var kills = 0;
    Self.Scored += (_, _) => ++kills;

    for (var attempt = 0; attempt < 25 && kills == 0; ++attempt)
    {
      AimAt (victim.GlobalPosition + Vector3.Up);
      var boltsBeforeShot = _boltsSpawned;
      await ChargeAndFire (chargeSeconds: 2.3f);
      await TryWaitUntil (() => _boltsSpawned > boltsBeforeShot, 3);
      GD.Print ($"PLAYTEST: shot attempt {attempt}: bolts fired {_boltsSpawned}, victim health {victim.Health}");
    }

    Assert (kills == 1, "scored a kill with charged shots");
    Assert (Self.Score == 1, $"own replicated Score is 1, got {Self.Score}");

    // Victim must come back armored in the spawn room.
    await WaitUntil (() => victim.SpawnArmor, 15, "victim respawned with spawn armor");

    // Fire-rate cap: spamming can spawn at most 1 bolt (cooldown blocks recharging).
    AimAt (Self.GlobalPosition + new Vector3 (0, 1, 10)); // Aim away from everyone.
    var boltsBefore = _boltsSpawned;

    for (var i = 0; i < 5; ++i)
    {
      await ChargeAndFire (chargeSeconds: 0.05f);
      await Task.Delay (50);
    }

    Assert (_boltsSpawned - boltsBefore <= 1, $"fire-rate cap held under spam, got {_boltsSpawned - boltsBefore} bolts");

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
  }

  private async Task RunVictim()
  {
    _world.StartClientSession (VictimName, difficulty: 0, _address, Port);
    await WaitUntil (() => _world.GetPlayers().Count() == 3, 60, "all 3 players visible");
    Assert (Self.MaxHealth == 400, $"own MaxHealth is Beginner 400, got {Self.MaxHealth}");
    Assert (Self.SpawnArmor, "spawned with spawn armor");
    await WaitUntil (() => !Self.SpawnArmor, 15, "spawn armor expired on its own");
    // The shooter opens fire once armor drops; verify damage & then a full respawn.
    await WaitUntil (() => Self.Health < Self.MaxHealth, 120, "took damage from shooter");
    await WaitUntil (() => Self.SpawnArmor && Self.Health == Self.MaxHealth, 120, "died & respawned with armor & full health");
    Assert (Self.GlobalPosition.Y > 20.0f, $"respawned up in the spawn room, y={Self.GlobalPosition.Y}");
    await WaitUntil (() => FindPlayer (ShooterName)?.Score == 1, 30, "shooter's score replicated to victim");
    // Give the shooter time to finish its solo phases (fire-rate & full-auto) before we vanish.
    await Task.Delay (8000);
  }

  // ---------------------------------------------------------------- helpers

  // One approach step per poll: aim at the victim & hold forward until within 2m.
  private bool ApproachedVictim (Player victim)
  {
    var distance = Self.GlobalPosition.DistanceTo (victim.GlobalPosition);

    if (distance <= 2.0f)
    {
      Input.ActionRelease ("move_forward");
      return true;
    }

    AimAt (victim.GlobalPosition + Vector3.Up);
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
      catch (Exception)
      {
        // Treat as not-yet-true; the timeout will surface the failure.
      }

      await Task.Delay (100);
    }

    return false;
  }
}
