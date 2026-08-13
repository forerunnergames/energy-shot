using System.Collections.Generic;
using System.Linq;
using com.forerunnergames.energyshot.core.world;
using com.forerunnergames.energyshot.players;
using com.forerunnergames.energyshot.utilities;
using Godot;

namespace com.forerunnergames.energyshot.weapons;

// Server-authoritative weapon lifecycle manager (issue #72): keeps at most 3 lasers,
// 1 banana, 1 boomerang (issue #98), & 1 slingshot (issue #99) existing in the level
// (held + dropped + pickups + boomerang escrow), spawning pickups at the building-top
// & banana-platform spawn points. Spawns replicate to every peer through the World's MultiplayerSpawner,
// same as players.
public partial class WeaponSpawner : Node3D
{
  [Export] public int MaxLasers = 3;
  [Export] public int MaxBananas = 1;
  [Export] public int MaxBoomerangs = 1;
  [Export] public int MaxSlingshots = 1;
  [Export] public float ReconcileIntervalSeconds = 1.0f;
  [Export] public float PickupHoverHeight = 0.9f;
  // Playtest-only (#72): a laser pickup is kept at this fixed spawn-room spot so the
  // playtest driver can walk to it deterministically; z = 5 keeps it clear of the
  // +/-4 random spawn scatter. Respawned by Reconcile if anyone claims it.
  public static readonly Vector3 PlaytestLaserPosition = new(0.0f, 31.1f, 5.0f);
  // Playtest-only (#98): same idea for the boomerang throw/catch phase.
  public static readonly Vector3 PlaytestBoomerangPosition = new(3.0f, 31.1f, 5.0f);
  // Playtest-only (#99): same idea for the slingshot draw/release phase.
  public static readonly Vector3 PlaytestSlingshotPosition = new(-3.0f, 31.1f, 5.0f);
  private const float OccupiedRadius = 1.0f;
  // Cargo riding a boomerang home (issue #98): stolen & scooped weapons live here
  // between the grab & the thrower's catch, so the caps still count them.
  private readonly record struct BoomerangCargo (int OwnerId, HeldWeapon Type, string PreviousOwner);
  private readonly List <BoomerangCargo> _escrow = new();
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
  // Escrowed boomerang cargo counts too (issue #98), or a reconcile pass mid-flight
  // would over-spawn the weapon the boomerang is carrying.
  private int Count (HeldWeapon type, List <WeaponPickup> pickups, List <Player> players) => pickups.Count (pickup => pickup.Weapon == type) + players.Count (player => player.Holds (type)) + _escrow.Count (cargo => cargo.Type == type);
  private void GrantToSelf (int type, string previousOwner) => (GetParent() as World)?.SelfPlayer?.GrantWeapon ((HeldWeapon)type, previousOwner);
  [Rpc] private void ConfirmPickup (int type, string previousOwner) => GrantToSelf (type, previousOwner);
  // A direct (non-RPC) call means the host itself sent it, so there's no remote sender.
  private int SenderOrSelf() => Multiplayer.GetRemoteSenderId() == 0 ? Multiplayer.GetUniqueId() : Multiplayer.GetRemoteSenderId();

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
    // Cargo whose thrower vanished mid-flight goes back to the spawn pool via the caps (issue #98).
    _escrow.RemoveAll (cargo => players.All (player => player.NetworkId != cargo.OwnerId));
    var freePoints = _laserPoints.Where (point => IsFree (point, pickups)).ToList();
    var laserCount = Count (HeldWeapon.Laser, pickups, players);

    while (laserCount < MaxLasers && freePoints.Count > 0)
    {
      Spawn (HeldWeapon.Laser, TakeRandom (freePoints), expires: false);
      ++laserCount;
    }

    SpawnSpecialsIfMissing (pickups, players, freePoints);
    EnsurePlaytestPickups (pickups);
  }

  // The banana, boomerang (issue #98), & slingshot (issue #99) respawn at random free
  // points: the high platform + whatever laser points the lasers didn't claim (laser
  // precedence when contested); a shared candidate list keeps them from stacking.
  private void SpawnSpecialsIfMissing (List <WeaponPickup> pickups, List <Player> players, List <Vector3> freePoints)
  {
    var candidates = new List <Vector3> (freePoints);
    if (IsFree (_bananaPoint, pickups)) candidates.Add (_bananaPoint);
    if (candidates.Count > 0 && Count (HeldWeapon.Banana, pickups, players) < MaxBananas) Spawn (HeldWeapon.Banana, TakeRandom (candidates), expires: false);
    if (candidates.Count > 0 && Count (HeldWeapon.Boomerang, pickups, players) < MaxBoomerangs) Spawn (HeldWeapon.Boomerang, TakeRandom (candidates), expires: false);
    if (candidates.Count > 0 && Count (HeldWeapon.Slingshot, pickups, players) < MaxSlingshots) Spawn (HeldWeapon.Slingshot, TakeRandom (candidates), expires: false);
  }

  // Playtest-only (#72 & #98): keeps deterministic pickups available in the spawn
  // room for the driver's collection, shooting, & throw/catch phases.
  private void EnsurePlaytestPickups (List <WeaponPickup> pickups)
  {
    if (!_isPlaytest) return;
    EnsurePlaytestPickup (HeldWeapon.Laser, PlaytestLaserPosition, pickups);
    EnsurePlaytestPickup (HeldWeapon.Boomerang, PlaytestBoomerangPosition, pickups);
    EnsurePlaytestPickup (HeldWeapon.Slingshot, PlaytestSlingshotPosition, pickups); // Issue #99.
  }

  private void EnsurePlaytestPickup (HeldWeapon type, Vector3 position, List <WeaponPickup> pickups)
  {
    if (pickups.Any (pickup => pickup.Position.DistanceTo (position) < OccupiedRadius)) return;
    Spawn (type, position, expires: false);
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
    ServerLog.Event ($"weapon spawn: {type} pickup [{pickup.Name}] at {position}{(expires ? " (expiring drop)" : "")}");
  }

  // First request wins: a pickup that's already claimed or expired is simply gone.
  // Every claim/award/deny decision is logged server-side (issues #110 & #111).
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestPickup (string pickupName, int collectorId)
  {
    if (!Multiplayer.IsServer()) return;
    var pickup = GetParent().GetNodeOrNull <WeaponPickup> (pickupName);
    ServerLog.Event (collectorId, $"weapon claim: pickup [{pickupName}]");

    if (pickup == null || pickup.IsQueuedForDeletion())
    {
      ServerLog.Event (collectorId, $"weapon deny: pickup [{pickupName}] {(pickup == null ? "no longer exists" : "is already claimed")}");
      return;
    }

    var type = pickup.Weapon;
    var previousOwner = pickup.PreviousOwner;
    pickup.QueueFree(); // Despawns on every peer via the MultiplayerSpawner.
    ServerLog.Event (collectorId, $"weapon award: {type} from pickup [{pickupName}]");

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
  // The mask & position are validated too (CodeRabbit on #145): only weapons the
  // sender's replicated HeldWeapon shows can drop - droppers send the request BEFORE
  // clearing their local flags, so the server's view still holds them on arrival -
  // & the drop grounds onto the level beneath the spot (issue #151).
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestDrop (Vector3 position, int droppedMask)
  {
    if (!Multiplayer.IsServer()) return;
    var senderId = SenderOrSelf();
    var dropper = Players().FirstOrDefault (player => player.NetworkId == senderId);
    var dropped = (HeldWeapon)droppedMask & (dropper?.HeldWeapon ?? HeldWeapon.None);

    if (dropped == HeldWeapon.None)
    {
      ServerLog.Event (senderId, $"weapon drop deny: mask [{(HeldWeapon)droppedMask}] not held by sender");
      return;
    }

    // Mid-air drops settle onto the level below (issue #151); over the void there's
    // nothing to rest on, so the spawn is skipped & the caps respawn the weapons at
    // spawn points instead of leaving unreachable floating pickups.
    if (!TryFindGround (position, out var spot))
    {
      ServerLog.Event (senderId, $"weapon drop skip: no ground beneath {position}; [{dropped}] returns via the caps");
      return;
    }

    ServerLog.Event (senderId, $"weapon drop: {dropped} at {spot}");
    var dropperName = dropper!.DisplayName; // Non-null: the mask intersection above proved the sender exists.
    if (dropped.HasFlag (HeldWeapon.Laser)) Spawn (HeldWeapon.Laser, spot, expires: true, dropperName);
    if (dropped.HasFlag (HeldWeapon.Banana)) Spawn (HeldWeapon.Banana, spot + Vector3.Right * 0.8f, expires: true, dropperName);
    if (dropped.HasFlag (HeldWeapon.Boomerang)) Spawn (HeldWeapon.Boomerang, spot + Vector3.Left * 0.8f, expires: true, dropperName); // Issue #98.
    if (dropped.HasFlag (HeldWeapon.Slingshot)) Spawn (HeldWeapon.Slingshot, spot + Vector3.Back * 0.8f, expires: true, dropperName); // Issue #99.
  }

  // ------------------------------------------------ boomerang cargo (issue #98)

  // Client -> server entry points; when this peer already is the server, skip the RPC.
  public void SendScoopRequest (string pickupName)
  {
    if (Multiplayer.IsServer()) { RequestBoomerangScoop (pickupName); return; }
    RpcId (1, MethodName.RequestBoomerangScoop, pickupName);
  }

  public void SendStolenEscrowRequest (int throwerId, HeldWeapon type)
  {
    if (Multiplayer.IsServer()) { RequestBoomerangEscrow (throwerId, (int)type); return; }
    RpcId (1, MethodName.RequestBoomerangEscrow, throwerId, (int)type);
  }

  public void SendBoomerangCatchRequest()
  {
    if (Multiplayer.IsServer()) { RequestBoomerangCatch(); return; }
    RpcId (1, MethodName.RequestBoomerangCatch);
  }

  public void SendBoomerangReleaseRequest (Vector3 position)
  {
    if (Multiplayer.IsServer()) { RequestBoomerangRelease (position); return; }
    RpcId (1, MethodName.RequestBoomerangRelease, position);
  }

  // A flying boomerang scooped a world pickup: despawn it for everyone & hold the
  // weapon in escrow until the thrower's catch. First scoop wins, like RequestPickup.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestBoomerangScoop (string pickupName)
  {
    if (!Multiplayer.IsServer()) return;
    var throwerId = SenderOrSelf();
    var pickup = GetParent().GetNodeOrNull <WeaponPickup> (pickupName);

    if (pickup == null || pickup.IsQueuedForDeletion())
    {
      ServerLog.Event (throwerId, $"boomerang scoop deny: pickup [{pickupName}] is gone");
      return;
    }

    _escrow.Add (new BoomerangCargo (throwerId, pickup.Weapon, pickup.PreviousOwner));
    ServerLog.Event (throwerId, $"boomerang scoop: {pickup.Weapon} from pickup [{pickupName}]");
    pickup.QueueFree(); // Despawns on every peer via the MultiplayerSpawner.
  }

  // A boomerang hit stole the victim's held weapon. The victim reports its own loss
  // (it owns its replicated HeldWeapon), so the theft attribution for the revenge
  // messages (issue #84) comes from the RPC sender - never client-supplied text.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestBoomerangEscrow (int throwerId, int type)
  {
    if (!Multiplayer.IsServer()) return;
    var victimId = SenderOrSelf();
    var victimName = Players().FirstOrDefault (player => player.NetworkId == victimId)?.DisplayName ?? string.Empty;
    _escrow.Add (new BoomerangCargo (throwerId, (HeldWeapon)type, victimName));
    ServerLog.Event (victimId, $"boomerang steal: {(HeldWeapon)type} taken from [{victimName}] for peer {throwerId}");
  }

  // The thrower caught the boomerang: deliver all escrowed cargo. Grants reuse the
  // ConfirmPickup path so auto-equip (#128) & theft-revenge (#84) apply; a type the
  // thrower already holds drops beside them as an expiring pickup instead.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestBoomerangCatch()
  {
    if (!Multiplayer.IsServer()) return;
    var throwerId = SenderOrSelf();
    var thrower = Players().FirstOrDefault (player => player.NetworkId == throwerId);
    foreach (var cargo in TakeEscrowFor (throwerId)) Deliver (throwerId, thrower, cargo);
  }

  private List <BoomerangCargo> TakeEscrowFor (int ownerId)
  {
    var cargo = _escrow.Where (item => item.OwnerId == ownerId).ToList();
    _escrow.RemoveAll (item => item.OwnerId == ownerId);
    return cargo;
  }

  private void Deliver (int throwerId, Player? thrower, BoomerangCargo cargo)
  {
    if (thrower == null) return; // Mid-disconnect; the caps respawn the weapon.
    ServerLog.Event (throwerId, $"boomerang deliver: {cargo.Type}{(cargo.PreviousOwner.Length > 0 ? $" (from [{cargo.PreviousOwner}])" : "")}");

    if (thrower.Holds (cargo.Type))
    {
      Spawn (cargo.Type, thrower.GlobalPosition + Vector3.Up * PickupHoverHeight, expires: true, cargo.PreviousOwner);
      return;
    }

    if (throwerId == Multiplayer.GetUniqueId())
    {
      GrantToSelf ((int)cargo.Type, cargo.PreviousOwner);
      return;
    }

    RpcId (throwerId, MethodName.ConfirmPickup, (int)cargo.Type, cargo.PreviousOwner);
  }

  // The boomerang dropped out of the sky (thrower zapped out mid-flight, or the
  // safety timeout): it & any cargo become expiring pickups where it was, settled
  // onto the ground below so nothing floats out of reach.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestBoomerangRelease (Vector3 position)
  {
    if (!Multiplayer.IsServer()) return;
    var throwerId = SenderOrSelf();
    var spot = GroundedSpot (position);
    ServerLog.Event (throwerId, $"boomerang release: dropped at {spot}");
    Spawn (HeldWeapon.Boomerang, spot, expires: true);
    var offset = 0;
    foreach (var cargo in TakeEscrowFor (throwerId)) Spawn (cargo.Type, spot + Vector3.Right * (0.8f * ++offset), expires: true, cargo.PreviousOwner);
  }

  private Vector3 GroundedSpot (Vector3 position)
  {
    TryFindGround (position, out var spot);
    return spot; // Over the void this keeps the raw position; the pickup expires & the caps respawn it.
  }

  // Level geometry only (collision layer 1): another player below isn't a shelf.
  private const uint WorldLayer = 1;

  // Finds the first level surface beneath the point (hover height above it); false
  // over the void, where there's nothing for a pickup to rest on (issue #151).
  private bool TryFindGround (Vector3 position, out Vector3 spot)
  {
    var query = PhysicsRayQueryParameters3D.Create (position, position + Vector3.Down * 100.0f, collisionMask: WorldLayer);
    var hit = GetWorld3D().DirectSpaceState.IntersectRay (query);
    spot = hit.Count == 0 ? position : (Vector3)hit["position"] + Vector3.Up * PickupHoverHeight;
    return hit.Count > 0;
  }
}
