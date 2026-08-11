using System.Collections.Generic;
using System.Linq;
using com.forerunnergames.energyshot.core.world;
using com.forerunnergames.energyshot.players;
using Godot;

namespace com.forerunnergames.energyshot.weapons;

// Server-authoritative weapon lifecycle manager (issue #72): keeps at most 3 lasers &
// exactly 1 banana existing in the level (held + dropped + pickups), spawning pickups
// at the building-top & banana-platform spawn points. Spawns replicate to every peer
// through the World's MultiplayerSpawner, same as players.
public partial class WeaponSpawner : Node3D
{
  [Export] public int MaxLasers = 3;
  [Export] public int MaxBananas = 1;
  [Export] public float ReconcileIntervalSeconds = 1.0f;
  [Export] public float PickupHoverHeight = 0.9f;
  // Playtest-only (#72): a laser pickup is kept at this fixed spawn-room spot so the
  // playtest driver can walk to it deterministically; z = 5 keeps it clear of the
  // +/-4 random spawn scatter. Respawned by Reconcile if anyone claims it.
  public static readonly Vector3 PlaytestLaserPosition = new(0.0f, 31.1f, 5.0f);
  private const float OccupiedRadius = 1.0f;
  private readonly RandomNumberGenerator _rng = new();
  private readonly List <Vector3> _laserPoints = new();
  private PackedScene _pickupScene = null!;
  private Vector3 _bananaPoint;
  private float _reconcileIn;
  private int _nextPickupId;
  private bool _isPlaytest;
  // Only a live ENet server session counts: the engine's default OfflineMultiplayerPeer
  // also reports Connected & IsServer, which would make every instance (even clients
  // before joining) spawn its own local pickups & corrupt replication.
  private bool IsActiveServer() => Multiplayer.MultiplayerPeer is ENetMultiplayerPeer && Multiplayer.MultiplayerPeer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected && Multiplayer.IsServer();
  private static Vector3 TopOf (CsgBox3D box, float hover) => box.Position + Vector3.Up * (box.Size.Y * 0.5f + hover);
  private IEnumerable <WeaponPickup> Pickups() => GetParent().GetChildren().OfType <WeaponPickup>();
  private IEnumerable <Player> Players() => GetParent().GetChildren().OfType <Player>();
  private static bool IsFree (Vector3 point, IEnumerable <WeaponPickup> pickups) => pickups.All (pickup => pickup.Position.DistanceTo (point) > OccupiedRadius);
  private static int Count (HeldWeapon type, List <WeaponPickup> pickups, List <Player> players) => pickups.Count (pickup => pickup.Weapon == type) + players.Count (player => player.Holds (type));
  private void GrantToSelf (int type, string previousOwner) => (GetParent() as World)?.SelfPlayer?.GrantWeapon ((HeldWeapon)type, previousOwner);
  [Rpc] private void ConfirmPickup (int type, string previousOwner) => GrantToSelf (type, previousOwner);

  public override void _Ready()
  {
    _rng.Randomize();
    _pickupScene = ResourceLoader.Load <PackedScene> ("res://core/weapons/WeaponPickup.tscn");
    // Laser spawn points: on top of the 5 low buildings; banana: the high platform.
    for (var i = 1; i <= 5; ++i) _laserPoints.Add (TopOf (GetNode <CsgBox3D> ($"../Building{i}"), PickupHoverHeight));
    _bananaPoint = TopOf (GetNode <CsgBox3D> ("../BananaPlatform"), PickupHoverHeight);
    _isPlaytest = OS.GetCmdlineUserArgs().Contains ("--playtest");
  }

  public override void _PhysicsProcess (double delta)
  {
    if (!IsActiveServer()) return;
    _reconcileIn -= (float)delta;
    if (_reconcileIn > 0.0f) return;
    _reconcileIn = ReconcileIntervalSeconds;
    Reconcile();
  }

  // Client -> server entry point; when this peer already is the server, skip the RPC.
  public void SendPickupRequest (string pickupName, int collectorId)
  {
    if (Multiplayer.IsServer())
    {
      RequestPickup (pickupName, collectorId);
      return;
    }

    RpcId (1, MethodName.RequestPickup, pickupName, collectorId);
  }

  // Client -> server entry point; when this peer already is the server, skip the RPC.
  public void SendDropRequest (Vector3 position, HeldWeapon dropped)
  {
    if (Multiplayer.IsServer())
    {
      RequestDrop (position, (int)dropped);
      return;
    }

    RpcId (1, MethodName.RequestDrop, position, (int)dropped);
  }

  // The level restocks lazily: whenever a weapon leaves it (an expired drop or a
  // holder disconnecting), the count dips below its cap & the next reconcile pass
  // respawns it at a free spawn point.
  private void Reconcile()
  {
    var pickups = Pickups().ToList();
    var players = Players().ToList();
    var freePoints = _laserPoints.Where (point => IsFree (point, pickups)).ToList();
    var laserCount = Count (HeldWeapon.Laser, pickups, players);

    while (laserCount < MaxLasers && freePoints.Count > 0)
    {
      Spawn (HeldWeapon.Laser, TakeRandom (freePoints), expires: false);
      ++laserCount;
    }

    SpawnBananaIfMissing (pickups, players, freePoints);
    EnsurePlaytestPickup (pickups);
  }

  // The banana respawns at a random free point: its own high platform + whatever
  // laser points the lasers didn't claim (laser precedence when contested).
  private void SpawnBananaIfMissing (List <WeaponPickup> pickups, List <Player> players, List <Vector3> freePoints)
  {
    if (Count (HeldWeapon.Banana, pickups, players) >= MaxBananas) return;
    var candidates = new List <Vector3> (freePoints);
    if (IsFree (_bananaPoint, pickups)) candidates.Add (_bananaPoint);
    if (candidates.Count == 0) return;
    Spawn (HeldWeapon.Banana, TakeRandom (candidates), expires: false);
  }

  // Playtest-only (#72): keeps a laser pickup available in the spawn room for the driver.
  private void EnsurePlaytestPickup (List <WeaponPickup> pickups)
  {
    if (!_isPlaytest) return;
    if (pickups.Any (pickup => pickup.Position.DistanceTo (PlaytestLaserPosition) < OccupiedRadius)) return;
    Spawn (HeldWeapon.Laser, PlaytestLaserPosition, expires: false);
  }

  private Vector3 TakeRandom (List <Vector3> points)
  {
    var index = _rng.RandiRange (0, points.Count - 1);
    var point = points[index];
    points.RemoveAt (index);
    return point;
  }

  private void Spawn (HeldWeapon type, Vector3 position, bool expires, string previousOwner = "")
  {
    var pickup = _pickupScene.Instantiate <WeaponPickup>();
    pickup.Name = $"WeaponPickup{++_nextPickupId}";
    pickup.Weapon = type;
    pickup.Position = position;
    pickup.Expires = expires;
    pickup.PreviousOwner = previousOwner; // For theft-revenge messages (issue #84).
    GetParent().AddChild (pickup); // The MultiplayerSpawner replicates the spawn to every peer.
    GD.Print ($"Server: Spawned {type} pickup at {position}");
  }

  // First request wins: a pickup that's already claimed or expired is simply gone.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestPickup (string pickupName, int collectorId)
  {
    if (!Multiplayer.IsServer()) return;
    var pickup = GetParent().GetNodeOrNull <WeaponPickup> (pickupName);
    if (pickup == null || pickup.IsQueuedForDeletion()) return;
    var type = pickup.Weapon;
    var previousOwner = pickup.PreviousOwner;
    pickup.QueueFree(); // Despawns on every peer via the MultiplayerSpawner.
    GD.Print ($"Server: Player [{collectorId}] picked up the {type}");

    if (collectorId == Multiplayer.GetUniqueId())
    {
      GrantToSelf ((int)type, previousOwner);
      return;
    }

    RpcId (collectorId, MethodName.ConfirmPickup, (int)type, previousOwner);
  }

  // Dropped weapons become expiring pickups at the drop spot (side by side when both drop at once).
  // The dropper's identity comes from the RPC sender, never from client-supplied data
  // (CodeRabbit on #96): a forged name could plant false theft-revenge attribution.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestDrop (Vector3 position, int droppedMask)
  {
    if (!Multiplayer.IsServer()) return;
    var senderId = Multiplayer.GetRemoteSenderId();
    if (senderId == 0) senderId = Multiplayer.GetUniqueId(); // Direct local call: the host player itself.
    var dropperName = Players().FirstOrDefault (player => player.NetworkId == senderId)?.DisplayName ?? string.Empty;
    var dropped = (HeldWeapon)droppedMask;
    var spot = position + Vector3.Up * PickupHoverHeight;
    if (dropped.HasFlag (HeldWeapon.Laser)) Spawn (HeldWeapon.Laser, spot, expires: true, dropperName);
    if (dropped.HasFlag (HeldWeapon.Banana)) Spawn (HeldWeapon.Banana, spot + Vector3.Right * 0.8f, expires: true, dropperName);
  }
}
