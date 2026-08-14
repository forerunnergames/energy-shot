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
  // Grace period so a dropper doesn't instantly re-collect their own drop.
  private const float ClaimDelaySeconds = 0.75f;
  private const float RetryCooldownSeconds = 1.0f;
  private static readonly Color BananaYellow = new(0.92f, 0.78f, 0.12f);
  private HeldWeapon _weapon = HeldWeapon.Laser;
  private Node3D _visual = null!;
  private Node3D _laserVisual = null!;
  private MeshInstance3D _bananaVisual = null!;
  private Node3D _boomerangVisual = null!;
  private Node3D _slingshotVisual = null!;
  private Node3D _breadVisual = null!;
  private Node3D _airplaneVisual = null!;
  private WeaponSpawner _spawner = null!;
  // How fast the grounded airplane's arming LED blinks while it waits (issue #191).
  private const float ArmedBlinksPerSecond = 0.8f;
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
    _airplaneVisual = PaperAirplane.CreateVisual(); // Grounded = armed landmine (issue #191).
    _visual.AddChild (_airplaneVisual);
    _spawner = GetNode <WeaponSpawner> ("/root/World/WeaponSpawner");
    _expiryLeft = ExpirySeconds;
    UpdateVisuals();
  }

  // Cosmetic float & spin, animated locally on every peer around the replicated base position.
  public override void _Process (double delta)
  {
    _ageSeconds += (float)delta;
    _visual.RotateY (Mathf.Tau * RotationsPerSecond * (float)delta);
    _visual.Position = Vector3.Up * (BobHeight * Mathf.Sin (Mathf.Tau * BobsPerSecond * _ageSeconds));
    // A grounded airplane is an armed mine, so it winks at everyone (issue #191).
    if (Weapon == HeldWeapon.Airplane) PaperAirplane.BlinkLed (_airplaneVisual, _ageSeconds, ArmedBlinksPerSecond);
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
    if (_ageSeconds < ClaimDelaySeconds || _retryCooldownLeft > 0.0f) return;
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
    if (Weapon == HeldWeapon.Airplane) { _spawner.SendMineTriggerRequest (Name); return; }
    _spawner.SendPickupRequest (Name, collector.NetworkId);
  }

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

  // A slingshot-equipped player loads ANY world item (issue #190), so the
  // already-holds rule doesn't apply to them; the grounded airplane is a mine
  // anyone can set off (issue #191), except while armored, already alight, or lying
  // through a death sequence - none of which should hand out a free detonation.
  private bool IsEligibleCollector (Player player)
  {
    if (!player.IsMultiplayerAuthority() || player.Fallen) return false;
    if (player.IsLoadingAmmo) return true;
    if (Weapon == HeldWeapon.Airplane) return !player.SpawnArmor && !player.Burning;
    return !player.Holds (Weapon);
  }

  private void UpdateVisuals()
  {
    if (_laserVisual == null || _boomerangVisual == null || _slingshotVisual == null || _breadVisual == null || _airplaneVisual == null) return;
    _laserVisual.Visible = Weapon == HeldWeapon.Laser;
    _bananaVisual.Visible = Weapon == HeldWeapon.Banana;
    _boomerangVisual.Visible = Weapon == HeldWeapon.Boomerang;
    _slingshotVisual.Visible = Weapon == HeldWeapon.Slingshot; // Issue #99.
    _breadVisual.Visible = Weapon == HeldWeapon.Bread; // Issue #190.
    _airplaneVisual.Visible = Weapon == HeldWeapon.Airplane; // Issue #191.
  }
}
