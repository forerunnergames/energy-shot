using System.Linq;
using com.forerunnergames.energyshot.players;
using Godot;

namespace com.forerunnergames.energyshot.weapons;

// Floating, slowly rotating weapon pickup (issue #72), claimed by walking into it.
// The walking player's own peer detects the overlap & asks the server-side
// WeaponSpawner to award the weapon & despawn the pickup for everyone.
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
  [Export] public float ExpirySeconds = 5.0f;
  // Grace period so a dropper doesn't instantly re-collect their own drop.
  private const float ClaimDelaySeconds = 0.75f;
  private const float RetryCooldownSeconds = 1.0f;
  private static readonly Color BananaYellow = new(0.92f, 0.78f, 0.12f);
  private HeldWeapon _weapon = HeldWeapon.Laser;
  private Node3D _visual = null!;
  private Node3D _laserVisual = null!;
  private MeshInstance3D _bananaVisual = null!;
  private WeaponSpawner _spawner = null!;
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
    GD.Print ($"Server: Unclaimed dropped {Weapon} pickup expired");
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
    _spawner.SendPickupRequest (Name, collector.NetworkId);
  }

  // Can't pick up a weapon type you already hold.
  private Player? FindLocalCollector() => GetOverlappingBodies().OfType <Player>().FirstOrDefault (player => player.IsMultiplayerAuthority() && !player.Holds (Weapon));

  private void UpdateVisuals()
  {
    if (_laserVisual == null) return;
    _laserVisual.Visible = Weapon == HeldWeapon.Laser;
    _bananaVisual.Visible = Weapon == HeldWeapon.Banana;
  }
}
