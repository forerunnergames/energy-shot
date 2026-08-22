using System.Linq;
using com.forerunnergames.energyshot.items;
using com.forerunnergames.energyshot.players;
using com.forerunnergames.energyshot.utilities;
using Godot;

namespace com.forerunnergames.energyshot.weapons;

// Floating, slowly rotating weapon pickup (issue #72), claimed by walking into it.
// The walking player's own peer detects the overlap & asks the server-side
// WeaponSpawner to award the weapon & despawn the pickup for everyone.
//
// Walking into one now means three different things (issues #190 & #191): a
// slingshot-equipped player LOADS it as ammo, anyone else stepping on the grounded
// paper airplane TRIGGERS its landmine, & everything else is a normal pickup.
public partial class WeaponPickup : Area3D
{
  // Replicated at spawn so every peer shows the right weapon model.
  [Export]
  public HeldWeapon Weapon
  {
    get => _weapon;
    set
    {
      _weapon = value;
      UpdateVisuals();
    }
  }

  // Armed (issue #191): true only for a paper airplane that came down FROM FLIGHT -
  // a glide that never found anyone, or a slung one that missed. Those are live
  // landmines: walking onto one targets you instead of collecting it. A fresh
  // spawn-point airplane (& one dropped from a dead player's hands) is unarmed &
  // collects normally, which is how anyone gets it into slot 6 in the first place.
  // Replicated at spawn so every peer renders & treats it the same way.
  [Export]
  public bool Armed
  {
    get => _armed;
    set
    {
      _armed = value;
      UpdateArmedLight();
    }
  }

  [Export] public float RotationsPerSecond = 0.25f;
  [Export] public float BobHeight = 0.15f;
  [Export] public float BobsPerSecond = 0.5f;
  // Dropped weapons despawn if unclaimed; spawn-point pickups never expire.
  // Server-side only - clients never free spawned nodes themselves.
  public bool Expires { get; set; }
  // Who dropped this weapon (issue #84), for theft-revenge messages. Server-side
  // only - the server reads it when awarding the pickup; empty for spawn-point pickups.
  public string PreviousOwner { get; set; } = string.Empty;
  [Export] public float ExpirySeconds = 5.0f;

  // Where a punched-loose item left the victim's hands (issue #193); zero = a normal
  // drop. Replicated at spawn so every peer plays the same fly-out arc.
  [Export] public Vector3 TossFrom { get; set; }
  // Grace period so a dropper doesn't instantly re-collect their own drop.
  private const float ClaimDelaySeconds = 0.75f;
  // The tiny cooldown on a fresh punched-loose drop (issue #193): long enough that
  // the victim can't stand still & instantly re-grab what just flew out of them.
  private const float PunchedClaimDelaySeconds = 1.5f;
  private const float RetryCooldownSeconds = 1.0f;
  private float ClaimDelay => TossFrom == Vector3.Zero ? ClaimDelaySeconds : PunchedClaimDelaySeconds;
  private const float TossSeconds = 0.4f;
  private const float TossArcHeight = 0.8f;
  private float _tossSecondsLeft;
  private static readonly Color BananaYellow = new(0.92f, 0.78f, 0.12f);
  private HeldWeapon _weapon = HeldWeapon.Laser;
  private Node3D _visual = null!;
  private Node3D _laserVisual = null!;
  private MeshInstance3D _bananaVisual = null!;
  private Node3D _boomerangVisual = null!;
  private Node3D _slingshotVisual = null!;
  private Node3D _breadVisual = null!;
  private Node3D _airplaneVisual = null!;
  private Node3D _blowgunVisual = null!;
  private Node3D _dartVisual = null!;
  private OmniLight3D? _armedLight;
  private bool _armed;
  private WeaponSpawner _spawner = null!;
  // No spawned item touches the floor (issue #278, thepro): floating & spinning
  // means safe & pickupable, flat on the ground means a landed hazard. A dart-sized
  // mesh needs real height for the difference to read; everything else already does.
  public const float DartHoverMeters = 0.6f;
  public static float HoverBaseline (HeldWeapon weapon, bool armed) => weapon == HeldWeapon.PoisonDart && !armed ? DartHoverMeters : 0.0f;

  // How fast an armed airplane's warning light blinks while it waits (issue #191).
  private const float ArmedBlinksPerSecond = 1.4f;
  private static readonly Color ArmedRed = new(1.0f, 0.15f, 0.12f);
  private float _ageSeconds;
  private float _retryCooldownLeft;
  private float _expiryLeft;
  // Same session-teardown guard as Player (see issue #22).
  private bool IsMultiplayerActive() => Multiplayer.MultiplayerPeer != null && Multiplayer.MultiplayerPeer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;

  public override void _Ready()
  {
    _visual = GetNode <Node3D> ("Visual");
    _laserVisual = GetNode <Node3D> ("Visual/Laser");
    _bananaVisual = GetNode <MeshInstance3D> ("Visual/Banana");
    _bananaVisual.Mesh = ResourceLoader.Load <Mesh> ("res://assets/weapons/Banana_Rifle.obj");
    _bananaVisual.MaterialOverride = new StandardMaterial3D { AlbedoColor = BananaYellow, Roughness = 0.6f };
    _boomerangVisual = BoomerangProjectile.CreateVisual(); // Code-built, shared with the projectile (issue #98).
    _visual.AddChild (_boomerangVisual);
    _slingshotVisual = SlingshotStone.CreateSlingshotVisual(); // Code-built, shared with the held model (issue #99).
    _visual.AddChild (_slingshotVisual);
    _breadVisual = Bread.CreateVisual(); // Death drops the loaf too (issue #190).
    _visual.AddChild (_breadVisual);
    _airplaneVisual = PaperAirplaneProjectile.CreateVisual(); // Code-built, shared with the projectile (issue #102).
    _visual.AddChild (_airplaneVisual);
    _blowgunVisual = BlowgunDart.CreateBlowgunVisual(); // Code-built, shared with the held model (issue #194).
    _visual.AddChild (_blowgunVisual);
    _dartVisual = BlowgunDart.CreateDartVisual(); // A death-scattered dart (issue #194).
    _visual.AddChild (_dartVisual);
    _spawner = GetNode <WeaponSpawner> ("/root/World/WeaponSpawner");
    _expiryLeft = ExpirySeconds;
    if (TossFrom != Vector3.Zero) _tossSecondsLeft = TossSeconds; // Fly-out arc (issue #193).
    UpdateVisuals();
    UpdateArmedLight(); // Spawn-state sync ran before this, when the node refs were null.
  }

  // An armed airplane advertises itself (issue #191): a small red light so a landmine
  // is never an invisible trap - you can always see what you're about to step on.
  private void UpdateArmedLight()
  {
    if (!IsInsideTree()) return;
    if (_armed && _armedLight == null) { _armedLight = new OmniLight3D { LightColor = ArmedRed, LightEnergy = 3.0f, OmniRange = 4.0f }; AddChild (_armedLight); return; }
    if (!_armed && _armedLight != null) { _armedLight.QueueFree(); _armedLight = null; }
  }

  // Cosmetic float & spin, animated locally on every peer around the replicated base position.
  public override void _Process (double delta)
  {
    _ageSeconds += (float)delta;

    if (_tossSecondsLeft > 0.0f) { UpdateToss (delta); return; }

    // An armed mine LIES on the ground (issue #204): it spawns at the standard
    // pickup hover like every other pickup, which left the airplane bobbing in
    // mid-air instead of sitting where it came down, waiting to be stepped on.
    if (IsLandedDart)
    {
      _visual.Position = Vector3.Down * MineRestDrop;
      _visual.Rotation = new Vector3 (0.0f, _visual.Rotation.Y, Mathf.DegToRad (90.0f)); // On its side, stuck in the ground.
      return;
    }

    if (IsArmedMine)
    {
      _visual.Position = Vector3.Down * MineRestDrop;
      if (_armedLight != null) _armedLight.Visible = Mathf.PosMod (_ageSeconds * ArmedBlinksPerSecond, 1.0f) < 0.5f;
      return;
    }

    _visual.RotateY (Mathf.Tau * RotationsPerSecond * (float)delta);
    _visual.Position = Vector3.Up * (HoverBaseline (Weapon, Armed) + BobHeight * Mathf.Sin (Mathf.Tau * BobsPerSecond * _ageSeconds));
    // An armed airplane winks at everyone while it waits (issue #191).
    if (_armedLight != null) _armedLight.Visible = Mathf.PosMod (_ageSeconds * ArmedBlinksPerSecond, 1.0f) < 0.5f;
  }

  // Punched-loose fly-out (issue #193): a short hand-animated arc from the victim's
  // hands down to the resting spot, with a tumble spin. Local cosmetic on every peer;
  // claims & expiry live on the root, which sits at the spot the whole time.
  private void UpdateToss (double delta)
  {
    _tossSecondsLeft = Mathf.Max (0.0f, _tossSecondsLeft - (float)delta);
    var progress = 1.0f - _tossSecondsLeft / TossSeconds;
    var start = TossFrom - GlobalPosition; // The root never rotates, so this is local space.
    _visual.Position = start.Lerp (Vector3.Zero, progress) + Vector3.Up * (TossArcHeight * 4.0f * progress * (1.0f - progress));
    _visual.RotateY (Mathf.Tau * 2.0f * (float)delta);
  }

  public override void _PhysicsProcess (double delta)
  {
    if (!IsMultiplayerActive()) return;
    UpdateExpiry (delta);
    UpdateClaim (delta);
  }

  private void UpdateExpiry (double delta)
  {
    if (!Multiplayer.IsServer() || !Expires) return;
    _expiryLeft -= (float)delta;
    if (_expiryLeft > 0.0f) return;
    ServerLog.Event ($"weapon despawn: unclaimed dropped {Weapon} pickup [{Name}] expired");
    QueueFree(); // The MultiplayerSpawner despawns it on every peer; the WeaponSpawner respawns the weapon at a free spawn point.
  }

  // Authority-side detection: only the local player's own peer requests the pickup.
  private void UpdateClaim (double delta)
  {
    _retryCooldownLeft = Mathf.Max (0.0f, _retryCooldownLeft - (float)delta);
    if (_ageSeconds < ClaimDelay || _retryCooldownLeft > 0.0f) return;
    var collector = FindLocalCollector();
    if (collector == null) return;
    _retryCooldownLeft = RetryCooldownSeconds;
    SendClaim (collector);
  }

  // Which of the three claims this walk-in is (issues #190 & #191). Every branch is
  // just a request: the server decides & first request wins, so two players reaching
  // the same item (or the same mine) in the same tick still resolve to exactly one.
  private void SendClaim (Player collector)
  {
    if (collector.IsLoadingAmmo) { _spawner.SendAmmoLoadRequest (Name); return; }
    if (IsArmedMine) { _spawner.SendMineTriggerRequest (Name); return; }
    // Darts (issues #236 & #248): ammo for a blowgun holder; a landed one is a hazard
    // to anyone else; a floating one is nothing to anyone else (IsEligibleCollector).
    if (Weapon == HeldWeapon.PoisonDart && collector.HasBlowgun) { _spawner.SendDartAmmoRequest (Name); return; }
    if (Weapon == HeldWeapon.PoisonDart) { _spawner.SendDartStepRequest (Name); return; }
    _spawner.SendPickupRequest (Name, collector.NetworkId);
  }

  // An airplane that came down from flight (issue #191). Anything else - including a
  // fresh spawn-point airplane - is an ordinary pickup you can put in slot 6 (#102).
  private bool IsArmedMine => Weapon == HeldWeapon.PaperAirplane && Armed;
  // A landed dart lies flat on the ground like a mine (issue #248) - the visual that
  // says 'hazard', vs. the floating spin that says 'pickup'.
  private bool IsLandedDart => Weapon == HeldWeapon.PoisonDart && Armed;
  // Pickups spawn a hover height up so they're reachable; a mine drops back to the
  // surface it landed on (issue #204).
  private const float MineRestDrop = 0.85f;

  // Sphere radius (1.2) + the player capsule's reach, against the player's center.
  private const float ClaimRangeMeters = 1.7f;

  // Can't pick up a weapon type you already hold. Area3D overlap alone can miss a
  // player who's inside the area but hasn't generated fresh contacts (issue #110), so
  // a plain distance check on the local player backs it up.
  private Player? FindLocalCollector()
  {
    var collector = GetOverlappingBodies().OfType <Player>().FirstOrDefault (IsEligibleCollector);
    if (collector != null) return collector;
    var local = Player.Local;
    if (local == null || !IsEligibleCollector (local)) return null;
    return (local.GlobalPosition + Vector3.Up).DistanceTo (GlobalPosition) <= ClaimRangeMeters ? local : null;
  }

  // A body mid-death-sequence is scenery (issues #152 & #196), so it can't go
  // shopping - or step on mines: dying on top of your own drop used to hand it
  // straight back a second later, undoing the whole point of dropping what you
  // carried. Beyond that, a slingshot-equipped player loads ANY world item (issue
  // #190), so the already-holds rule doesn't apply to them; & an armed airplane is a
  // mine anyone can set off (issue #191), except while armored or already alight -
  // neither of which should hand out a free detonation.
  private bool IsEligibleCollector (Player player)
  {
    if (!player.IsMultiplayerAuthority() || player.Fallen) return false;
    if (player.IsLoadingAmmo) return true;
    // Darts (issues #236 & #248): a blowgun holder collects any ground dart as ammo; a
    // LANDED (armed) dart is a hazard anyone else can step on; a floating one is
    // nothing to anyone else - you walk through it.
    if (Weapon == HeldWeapon.PoisonDart) return player.HasBlowgun || (Armed && !player.SpawnArmor);
    if (IsArmedMine) return !player.SpawnArmor && !player.Burning;
    return !player.Holds (Weapon);
  }

  private void UpdateVisuals()
  {
    if (_laserVisual == null || _boomerangVisual == null || _slingshotVisual == null || _breadVisual == null || _airplaneVisual == null || _blowgunVisual == null || _dartVisual == null) return;
    _laserVisual.Visible = Weapon == HeldWeapon.Laser;
    _bananaVisual.Visible = Weapon == HeldWeapon.Banana;
    _boomerangVisual.Visible = Weapon == HeldWeapon.Boomerang;
    _slingshotVisual.Visible = Weapon == HeldWeapon.Slingshot; // Issue #99.
    _breadVisual.Visible = Weapon == HeldWeapon.Bread; // Issue #190.
    _airplaneVisual.Visible = Weapon == HeldWeapon.PaperAirplane; // Issue #102.
    _blowgunVisual.Visible = Weapon == HeldWeapon.Blowgun; // Issue #194.
    _dartVisual.Visible = Weapon == HeldWeapon.PoisonDart; // Issue #194.
  }
}
