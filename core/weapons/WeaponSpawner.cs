using System.Collections.Generic;
using System.Linq;
using com.forerunnergames.energyshot.core.world;
using com.forerunnergames.energyshot.players;
using com.forerunnergames.energyshot.utilities;
using Godot;

namespace com.forerunnergames.energyshot.weapons;

// Server-authoritative weapon lifecycle manager (issue #72): keeps at most 3 lasers,
// 1 banana, 1 boomerang (issue #98), 1 slingshot (issue #99), & exactly 1 paper
// airplane (issue #102) existing in the level (held + dropped + pickups + boomerang
// escrow + the airplane catch handoff + slingshot ammo + airplane hazards in
// progress), spawning pickups at the building-top & banana-platform spawn points.
// Spawns replicate to every peer through the World's MultiplayerSpawner, same as
// players.
//
// Bread is deliberately uncapped (issue #190): every life restocks a loaf, so the
// level's supply is however many players are alive plus whatever they dropped, & a
// dropped loaf simply expires like any other drop instead of respawning.
public partial class WeaponSpawner : Node3D
{
  [Export] public int MaxLasers = 3;
  [Export] public int MaxBananas = 1;
  [Export] public int MaxBoomerangs = 1;
  [Export] public int MaxSlingshots = 1;
  [Export] public int MaxPaperAirplanes = 1;
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
  // Playtest-only (#102): same idea for the paper airplane throw/catch phase, on
  // the opposite wall at z = -5.8: far enough from the +/-4 random spawn scatter
  // that no unlucky spawn can auto-claim it - only a deliberate walk reaches it,
  // which matters because the airplane pickup is capped at exactly one (#102).
  public static readonly Vector3 PlaytestAirplanePosition = new(0.0f, 31.1f, -5.8f);
  // Playtest-only (#169): the victim arms up here before the kill phase, so the death
  // drop has something to drop - RequestDrop's death path had no coverage at all,
  // which is how the #167 vanishing-weapon regression reached players. Down in the
  // empty arena rather than the spawn room: every spot in that small room is within
  // claim reach of the +/-4 random spawn scatter (observed: a joining peer grabbed
  // it seconds after spawning), & a stray banana in someone's hands would make the
  // death-drop phase's claim assert meaningless.
  public static readonly Vector3 PlaytestBananaPosition = new(0.0f, 0.9f, -40.0f);
  // The landmine phase (#191) needs no fixed spot of its own: the driver arms one by
  // throwing the airplane into the floor, & it comes down armed wherever it lands.
  private const float OccupiedRadius = 1.0f;
  // Cargo riding a boomerang home (issue #98): stolen & scooped weapons live here
  // between the grab & the thrower's catch, so the caps still count them.
  private readonly record struct BoomerangCargo (int OwnerId, HeldWeapon Type, string PreviousOwner);
  private readonly List <BoomerangCargo> _escrow = new();
  // Award->replication bridge (issue #154): between despawning a claimed pickup (or
  // delivering escrowed cargo) & the collector's replicated HeldWeapon showing the
  // weapon, the count dips below the cap - a reconcile pass in that window would
  // spawn a duplicate. Pending grants keep the weapon counted until the flag lands;
  // the timeout covers a collector that vanishes mid-grant (the caps then respawn it).
  private readonly record struct PendingGrant (int CollectorId, HeldWeapon Type, ulong ExpiresAtMs);
  private readonly List <PendingGrant> _pendingGrants = new();
  private const float PendingGrantTimeoutSeconds = 3.0f;
  // Universal slingshot ammo (issue #190): a world item loaded into a slingshot
  // exists nowhere else until it lands, so the caps count it here - exactly like
  // boomerang cargo. One nocked item per loader.
  private readonly record struct LoadedAmmo (int LoaderId, HeldWeapon Type, string PreviousOwner);
  private readonly List <LoadedAmmo> _ammoEscrow = new();
  // A paper airplane mid-hazard (issue #191): from the mine trigger (or a slingshot
  // strike) until the target pops, the airplane isn't a pickup & isn't ammo - this
  // flight record is what keeps the exactly-one invariant honest in between. The
  // timeout covers a target that disconnects mid-burn (the caps then respawn it).
  private readonly record struct AirplaneHazard (int TargetId, ulong ExpiresAtMs);
  private readonly List <AirplaneHazard> _airplaneHazards = new();
  private const float AirplaneHazardTimeoutSeconds = 15.0f;
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
  // Players count via HeldOrRecentlyHeld (CodeRabbit on #168): a weapon inside its
  // drop-grace window (cleared from HeldWeapon, drop RPC not yet processed) still
  // exists, & counting only current holds let a reconcile pass over-spawn it. The
  // union per player counts once - recently-held includes any still-held flags.
  // The paper airplane's catch handoff (issue #102) rides the same grace: between
  // the thrower's replicated clear & the catcher's replicated grant, the thrower
  // still counts it, so the exactly-one invariant holds across the handoff.
  // Pending grants bridge the award->replication window the same way (issue #154).
  // Loaded slingshot ammo (issue #190) & airplane hazards in progress (issue #191)
  // count the same way, so nothing over-spawns while an item is mid-transition.
  private int Count (HeldWeapon type, List <WeaponPickup> pickups, List <Player> players) => pickups.Count (pickup => pickup.Weapon == type) + players.Count (player => (player.HeldOrRecentlyHeld & type) != 0) + _escrow.Count (cargo => cargo.Type == type) + _pendingGrants.Count (grant => grant.Type == type) + _ammoEscrow.Count (ammo => ammo.Type == type) + (type == HeldWeapon.PaperAirplane ? _airplaneHazards.Count : 0);
  private void TrackPendingGrant (int collectorId, HeldWeapon type) => _pendingGrants.Add (new PendingGrant (collectorId, type, Time.GetTicksMsec() + (ulong)(PendingGrantTimeoutSeconds * 1000.0f))); // Issue #154.
  // A pending grant ends when the collector's replicated HeldWeapon shows the weapon
  // (it counts as held from then on) or the timeout passes (issue #154).
  private void PrunePendingGrants (List <Player> players) => _pendingGrants.RemoveAll (grant => Time.GetTicksMsec() > grant.ExpiresAtMs || players.Any (player => player.NetworkId == grant.CollectorId && player.Holds (grant.Type)));
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
    // Same for airplane flights whose thrower disconnected (CodeRabbit on #180),
    // ammo nocked by a player who left (issue #190), & airplane hazards whose target
    // left or whose burn sequence never reported back (issue #191).
    _airplaneFlights.RemoveWhere (throwerId => players.All (player => player.NetworkId != throwerId));
    _ammoEscrow.RemoveAll (ammo => players.All (player => player.NetworkId != ammo.LoaderId));
    _airplaneHazards.RemoveAll (hazard => Time.GetTicksMsec() > hazard.ExpiresAtMs || players.All (player => player.NetworkId != hazard.TargetId));
    PrunePendingGrants (players); // Delivered or expired award bridges stop counting (issue #154).
    var freePoints = _laserPoints.Where (point => IsFree (point, pickups)).ToList();
    var laserCount = Count (HeldWeapon.Laser, pickups, players);

    while (laserCount < MaxLasers && freePoints.Count > 0)
    {
      Spawn (HeldWeapon.Laser, TakeRandom (freePoints), expires: false);
      ++laserCount;
    }

    SpawnSpecialsIfMissing (pickups, players, freePoints);
    EnsurePlaytestPickups (pickups, players);
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
    // Exactly 1 airplane in the game (issue #102), refolded at a spawn point whenever
    // the level's only one is spent - a mine popped its target, or a thrown or slung
    // one ignited somebody (issue #191). A fresh spawn-point airplane is unarmed, so
    // it is a normal pickup; only one that has come down from flight is a mine.
    // In playtest mode the deterministic spawn-room pickup (EnsurePlaytestPickups) is
    // the airplane's ONLY spawn path, or the two paths together could mint a second
    // one (CodeRabbit on #180).
    if (!_isPlaytest && candidates.Count > 0 && Count (HeldWeapon.PaperAirplane, pickups, players) < MaxPaperAirplanes) Spawn (HeldWeapon.PaperAirplane, TakeRandom (candidates), expires: false);
  }

  // Playtest-only (#72 & #98): keeps deterministic pickups available in the spawn
  // room for the driver's collection, shooting, throw/catch, & death-drop phases.
  private void EnsurePlaytestPickups (List <WeaponPickup> pickups, List <Player> players)
  {
    if (!_isPlaytest) return;
    EnsurePlaytestPickup (HeldWeapon.Laser, PlaytestLaserPosition, pickups);
    EnsurePlaytestPickup (HeldWeapon.Boomerang, PlaytestBoomerangPosition, pickups);
    EnsurePlaytestPickup (HeldWeapon.Slingshot, PlaytestSlingshotPosition, pickups); // Issue #99.
    // The airplane respects its exactly-one cap even in playtest mode (CodeRabbit on
    // #180): while someone holds it (or it sits landed somewhere), no respawn here.
    if (Count (HeldWeapon.PaperAirplane, pickups, players) < MaxPaperAirplanes) EnsurePlaytestPickup (HeldWeapon.PaperAirplane, PlaytestAirplanePosition, pickups); // Issue #102.
    // Same cap guard for the banana (CodeRabbit on #180): respawning it here while
    // someone already carries one would put two in a level capped at one.
    if (Count (HeldWeapon.Banana, pickups, players) < MaxBananas) EnsurePlaytestPickup (HeldWeapon.Banana, PlaytestBananaPosition, pickups); // Issue #169.
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

  private void Spawn (HeldWeapon type, Vector3 position, bool expires, string previousOwner = "", bool armed = false)
  {
    var pickup = _pickupScene.Instantiate <WeaponPickup>();
    pickup.Name = $"WeaponPickup{++_nextPickupId}";
    pickup.Weapon = type;
    pickup.Position = position;
    pickup.Expires = expires;
    pickup.PreviousOwner = previousOwner; // For theft-revenge messages (issue #84).
    pickup.Armed = armed; // An airplane that came down from flight is a live landmine (issue #191).
    GetParent().AddChild (pickup); // The MultiplayerSpawner replicates the spawn to every peer.
    ServerLog.Event ($"weapon spawn: {type} pickup [{pickup.Name}] at {position}{(expires ? " (expiring drop)" : "")}{(armed ? " (armed)" : "")}");
  }

  // First request wins: a pickup that's already claimed or expired is simply gone.
  // Every claim/award/deny decision is logged server-side (issues #110 & #111).
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestPickup (string pickupName, int collectorId)
  {
    if (!Multiplayer.IsServer()) return;

    // Claims are always filed by the collecting player's own peer (CodeRabbit on
    // #184): a forged collectorId can't award (or deny) weapons to someone else.
    if (collectorId != SenderOrSelf())
    {
      ServerLog.Event (SenderOrSelf(), $"weapon deny: claim for another peer [{collectorId}]");
      return;
    }

    // A body mid-death-sequence is scenery (issue #152), so it can't claim anything
    // (CodeRabbit on #199): the collector's own peer already refuses, but the server
    // is what actually awards, so it enforces the rule too.
    var collector = Players().FirstOrDefault (player => player.NetworkId == collectorId);

    if (collector is { Fallen: true })
    {
      ServerLog.Event (collectorId, $"weapon deny: pickup [{pickupName}] claimed while lying dead");
      return;
    }

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
      GrantToSelf ((int)type, previousOwner); // Synchronous: the server's own HeldWeapon shows it immediately.
      return;
    }

    TrackPendingGrant (collectorId, type); // Bridge until the collector's HeldWeapon replicates back (issue #154).
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
    // A landing airplane ends its flight (CodeRabbit on #180): consume the sender's
    // flight record so a late or replayed catch request can't also grant it.
    if (((HeldWeapon)droppedMask & HeldWeapon.PaperAirplane) != 0 && _airplaneFlights.Remove (senderId)) ServerLog.Event (senderId, "airplane landing: flight consumed");
    // Current-or-recently-held (issue #167): the death-drop clear can replicate ahead
    // of this RPC, so a strict current-held check denied every kill's weapon drop.
    var dropped = (HeldWeapon)droppedMask & (dropper?.HeldOrRecentlyHeld ?? HeldWeapon.None);

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
    if (dropped.HasFlag (HeldWeapon.PaperAirplane)) Spawn (HeldWeapon.PaperAirplane, spot + Vector3.Forward * 0.8f, expires: true, dropperName); // Issue #102.
    // Death drops the uneaten loaf too (issue #190), & it expires like any other
    // drop so dropped bread can never pile up - respawns restock it anyway.
    if (dropped.HasFlag (HeldWeapon.Bread)) Spawn (HeldWeapon.Bread, spot + Vector3.Right * 1.6f, expires: true, dropperName);
  }

  // ------------------------------------------- slingshot universal ammo (issue #190)

  // Client -> server entry points; when this peer already is the server, skip the RPC.
  public void SendAmmoLoadRequest (string pickupName)
  {
    if (Multiplayer.IsServer()) { RequestAmmoLoad (pickupName); return; }
    RpcId (1, MethodName.RequestAmmoLoad, pickupName);
  }

  public void SendAmmoLandRequest (Vector3 position)
  {
    if (Multiplayer.IsServer()) { RequestAmmoLand (position); return; }
    RpcId (1, MethodName.RequestAmmoLand, position);
  }

  // A slingshot-equipped player walked onto a world item: despawn it for everyone &
  // hold it as that player's nocked ammo. First request wins, like RequestPickup, so
  // two players reaching the same item resolve to exactly one loader.
  // Server-side state check (the #145/#167/#184 convention): the sender's replicated
  // HeldWeapon must show a slingshot & their replicated SelectedWeapon must have it
  // out - a client can't claim to be equipped.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestAmmoLoad (string pickupName)
  {
    if (!Multiplayer.IsServer()) return;
    var loaderId = SenderOrSelf();
    var loader = Players().FirstOrDefault (player => player.NetworkId == loaderId);

    if (loader == null || (loader.HeldOrRecentlyHeld & HeldWeapon.Slingshot) == 0 || loader.SelectedWeapon != SelectedWeapon.Slingshot)
    {
      ServerLog.Event (loaderId, "ammo load deny: sender does not have a slingshot equipped");
      return;
    }

    if (_ammoEscrow.Any (ammo => ammo.LoaderId == loaderId))
    {
      ServerLog.Event (loaderId, "ammo load deny: slingshot is already loaded");
      return;
    }

    var pickup = GetParent().GetNodeOrNull <WeaponPickup> (pickupName);

    if (pickup == null || pickup.IsQueuedForDeletion())
    {
      ServerLog.Event (loaderId, $"ammo load deny: pickup [{pickupName}] is gone");
      return;
    }

    _ammoEscrow.Add (new LoadedAmmo (loaderId, pickup.Weapon, pickup.PreviousOwner));
    ServerLog.Event (loaderId, $"ammo load: {pickup.Weapon} from pickup [{pickupName}]");
    pickup.QueueFree(); // Despawns on every peer via the MultiplayerSpawner.
    if (loaderId == Multiplayer.GetUniqueId()) { LoadIntoSelf ((int)pickup.Weapon); return; }
    RpcId (loaderId, MethodName.ConfirmAmmoLoad, (int)pickup.Weapon);
  }

  private void LoadIntoSelf (int type) => (GetParent() as World)?.SelfPlayer?.LoadSlingshotAmmo ((HeldWeapon)type);
  [Rpc] private void ConfirmAmmoLoad (int type) => LoadIntoSelf (type);

  // A slung item came to rest (or its loader died holding it): it becomes a normal
  // world pickup again where it stopped, grounded onto the level below like any
  // drop (issue #151). Escrow is the server's own record, so a forged request from
  // a peer with nothing nocked simply finds nothing to spawn.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestAmmoLand (Vector3 position)
  {
    if (!Multiplayer.IsServer()) return;
    var loaderId = SenderOrSelf();
    var landed = TakeAmmoFor (loaderId);

    if (landed.Count == 0)
    {
      ServerLog.Event (loaderId, "ammo land deny: sender has nothing nocked");
      return;
    }

    foreach (var ammo in landed) LandAmmo (loaderId, ammo, position);
  }

  private List <LoadedAmmo> TakeAmmoFor (int loaderId)
  {
    var loaded = _ammoEscrow.Where (ammo => ammo.LoaderId == loaderId).ToList();
    _ammoEscrow.RemoveAll (ammo => ammo.LoaderId == loaderId);
    return loaded;
  }

  private void LandAmmo (int loaderId, LoadedAmmo ammo, Vector3 position)
  {
    // Cosmetic ammo (banana chunks) is scenery no cap tracks: it just splatters.
    if (ammo.Type == HeldWeapon.BananaChunk) return;

    // Nothing beneath it (over the void): skip the spawn like RequestDrop does & let
    // the caps put the item back at a spawn point instead of floating it out of reach.
    if (!TryFindGround (position, out var spot))
    {
      ServerLog.Event (loaderId, $"ammo land skip: no ground beneath {position}; [{ammo.Type}] returns via the caps");
      return;
    }

    ServerLog.Event (loaderId, $"ammo land: {ammo.Type} at {spot}");
    // A slung airplane that missed comes down ARMED (issue #191): it re-arms as the
    // landmine right where it fell & never expires, since it's the only one there is.
    var isAirplane = ammo.Type == HeldWeapon.PaperAirplane;
    Spawn (ammo.Type, spot, expires: !isAirplane, ammo.PreviousOwner, armed: isAirplane);
  }

  // ------------------------------------------- paper airplane hazard (issue #191)

  // Client -> server entry points; when this peer already is the server, skip the RPC.
  public void SendAirplaneLandRequest (Vector3 position)
  {
    if (Multiplayer.IsServer()) { RequestAirplaneLand (position); return; }
    RpcId (1, MethodName.RequestAirplaneLand, position);
  }

  public void SendMineTriggerRequest (string pickupName)
  {
    if (Multiplayer.IsServer()) { RequestMineTrigger (pickupName); return; }
    RpcId (1, MethodName.RequestMineTrigger, pickupName);
  }

  public void SendAirplaneStrikeRequest (int targetId)
  {
    if (Multiplayer.IsServer()) { RequestAirplaneStrike (targetId); return; }
    RpcId (1, MethodName.RequestAirplaneStrike, targetId);
  }

  public void SendAirplaneSpentRequest()
  {
    if (Multiplayer.IsServer()) { RequestAirplaneSpent(); return; }
    RpcId (1, MethodName.RequestAirplaneSpent);
  }

  // A glide ended without finding a player: the airplane comes down ARMED where it
  // stopped (issue #191) & waits there as a landmine. This replaces the plain drop
  // the landing used to file, so it consumes the thrower's flight record the same
  // way RequestDrop did - the single-use ticket a late catch would otherwise spend.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestAirplaneLand (Vector3 position)
  {
    if (!Multiplayer.IsServer()) return;
    var throwerId = SenderOrSelf();
    var thrower = Players().FirstOrDefault (player => player.NetworkId == throwerId);

    // Server-side state check (the #145/#167/#184 convention): only a sender whose
    // replicated hands show the airplane (current or drop-grace) can land one.
    if (thrower == null || (thrower.HeldOrRecentlyHeld & HeldWeapon.PaperAirplane) == 0)
    {
      ServerLog.Event (throwerId, "airplane land deny: sender's replicated hands show no paper airplane");
      return;
    }

    _airplaneFlights.Remove (throwerId); // Consumed: a late catch request can't also spend it.

    if (!TryFindGround (position, out var spot))
    {
      ServerLog.Event (throwerId, $"airplane land skip: no ground beneath {position}; it returns via the caps");
      return;
    }

    ServerLog.Event (throwerId, $"airplane land: armed as a landmine at {spot}");
    Spawn (HeldWeapon.PaperAirplane, spot, expires: false, thrower.DisplayName, armed: true);
  }

  // Somebody stepped on an armed, grounded airplane. Despawning the pickup here is
  // what makes simultaneous touches pick exactly ONE target: the second request finds
  // it already gone. The sender is the target by construction (peers only ever report
  // their own steps), & their replicated state has to allow it.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestMineTrigger (string pickupName)
  {
    if (!Multiplayer.IsServer()) return;
    var targetId = SenderOrSelf();
    var target = Players().FirstOrDefault (player => player.NetworkId == targetId);

    if (target == null || target.SpawnArmor || target.Fallen || target.Burning)
    {
      ServerLog.Event (targetId, "mine deny: sender is armored, already alight, or down");
      return;
    }

    var pickup = GetParent().GetNodeOrNull <WeaponPickup> (pickupName);

    if (pickup == null || pickup.IsQueuedForDeletion() || pickup.Weapon != HeldWeapon.PaperAirplane || !pickup.Armed)
    {
      ServerLog.Event (targetId, $"mine deny: pickup [{pickupName}] is not an armed airplane");
      return;
    }

    var spot = pickup.Position;
    pickup.QueueFree(); // Despawns on every peer via the MultiplayerSpawner.
    TrackAirplaneHazard (targetId);
    ServerLog.Event (targetId, $"mine trigger: armed paper airplane [{pickupName}] locked onto its stepper at {spot}");
    if (targetId == Multiplayer.GetUniqueId()) { TriggerMineOnSelf (spot); return; }
    RpcId (targetId, MethodName.ConfirmMineTrigger, spot);
  }

  private void TriggerMineOnSelf (Vector3 spot) => (GetParent() as World)?.SelfPlayer?.BeginMineFuse (spot);
  [Rpc] private void ConfirmMineTrigger (Vector3 spot) => TriggerMineOnSelf (spot);
  private void TrackAirplaneHazard (int targetId) => _airplaneHazards.Add (new AirplaneHazard (targetId, Time.GetTicksMsec() + (ulong)(AirplaneHazardTimeoutSeconds * 1000.0f)));

  // The airplane found a player - thrown & uncaught, or slung fast & straight
  // (issues #190 & #191). The attacker reports the contact, but only the server
  // decides whether it counts: they must really have had the airplane, either as a
  // registered flight (a throw) or nocked in their slingshot. Whichever ticket they
  // hold is consumed here, so one contact can only ever light up one player.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestAirplaneStrike (int targetId)
  {
    if (!Multiplayer.IsServer()) return;
    var attackerId = SenderOrSelf();
    var attacker = Players().FirstOrDefault (player => player.NetworkId == attackerId);
    var threw = _airplaneFlights.Remove (attackerId);
    var slung = _ammoEscrow.RemoveAll (ammo => ammo.LoaderId == attackerId && ammo.Type == HeldWeapon.PaperAirplane) > 0;

    if (attacker == null || (!threw && !slung))
    {
      ServerLog.Event (attackerId, "airplane strike deny: sender had no airplane in flight or nocked");
      return;
    }

    var target = Players().FirstOrDefault (player => player.NetworkId == targetId);

    if (target == null || target.SpawnArmor || target.Fallen || target.Burning)
    {
      ServerLog.Event (attackerId, $"airplane strike void: target [{targetId}] is armored, already alight, or gone; the airplane returns via the caps");
      return;
    }

    TrackAirplaneHazard (targetId);
    ServerLog.Event (attackerId, $"airplane strike: [{attacker.DisplayName}] lit up [{target.DisplayName}]");
    if (targetId == Multiplayer.GetUniqueId()) { IgniteSelf (attackerId, attacker.DisplayName); return; }
    RpcId (targetId, MethodName.ConfirmAirplaneStrike, attackerId, attacker.DisplayName);
  }

  private void IgniteSelf (int attackerId, string attackerName) => (GetParent() as World)?.SelfPlayer?.IgniteFromAirplane (attackerId, attackerName);
  [Rpc] private void ConfirmAirplaneStrike (int attackerId, string attackerName) => IgniteSelf (attackerId, attackerName);

  // The hazard finished (the target popped, or the burn was cut short): the record
  // drops & the caps fold a fresh airplane back into the level.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestAirplaneSpent()
  {
    if (!Multiplayer.IsServer()) return;
    var targetId = SenderOrSelf();
    if (_airplaneHazards.RemoveAll (hazard => hazard.TargetId == targetId) == 0) return;
    ServerLog.Event (targetId, "airplane spent: the caps will fold a new one");
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
    var thrower = Players().FirstOrDefault (player => player.NetworkId == throwerId);

    // Server-side state check (CodeRabbit on #184, the #145/#167 convention): only a
    // boomerang carrier can have one out flying to scoop with (the flag stays set
    // through the whole flight).
    if (thrower == null || (thrower.HeldOrRecentlyHeld & HeldWeapon.Boomerang) == 0)
    {
      ServerLog.Event (throwerId, "boomerang scoop deny: sender does not hold a boomerang");
      return;
    }

    var pickup = GetParent().GetNodeOrNull <WeaponPickup> (pickupName);

    if (pickup == null || pickup.IsQueuedForDeletion())
    {
      ServerLog.Event (throwerId, $"boomerang scoop deny: pickup [{pickupName}] is gone");
      return;
    }

    // An armed airplane is a hazard, not cargo (issue #191): a boomerang can't carry
    // a live landmine home. An unarmed one is an ordinary pickup & scoops fine.
    if (pickup.Weapon == HeldWeapon.PaperAirplane && pickup.Armed)
    {
      ServerLog.Event (throwerId, "boomerang scoop deny: the armed paper airplane is not cargo");
      return;
    }

    _escrow.Add (new BoomerangCargo (throwerId, pickup.Weapon, pickup.PreviousOwner));
    ServerLog.Event (throwerId, $"boomerang scoop: {pickup.Weapon} from pickup [{pickupName}]");
    pickup.QueueFree(); // Despawns on every peer via the MultiplayerSpawner.
  }

  // A boomerang hit stole the victim's held weapon. The victim reports its own loss
  // (it owns its replicated HeldWeapon), so the theft attribution for the revenge
  // messages (issue #84) comes from the RPC sender - never client-supplied text.
  // The surrendered type is validated the same way (CodeRabbit on #184, the
  // #145/#167 convention): it must show in the victim's replicated state (current
  // or drop-grace), reduced to a single flag so a forged multi-flag mask can't
  // conjure weapons into escrow.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestBoomerangEscrow (int throwerId, int type)
  {
    if (!Multiplayer.IsServer()) return;
    var victimId = SenderOrSelf();
    var victim = Players().FirstOrDefault (player => player.NetworkId == victimId);
    var surrendered = FirstFlag ((HeldWeapon)type & (victim?.HeldOrRecentlyHeld ?? HeldWeapon.None));

    if (surrendered == HeldWeapon.None)
    {
      ServerLog.Event (victimId, $"boomerang steal deny: mask [{(HeldWeapon)type}] not held by sender");
      return;
    }

    var victimName = victim!.DisplayName; // Non-null: the mask intersection above proved the sender exists.
    _escrow.Add (new BoomerangCargo (throwerId, surrendered, victimName));
    ServerLog.Event (victimId, $"boomerang steal: {surrendered} taken from [{victimName}] for peer {throwerId}");
  }

  // Escrow & pickups carry exactly one weapon each: reduce a validated mask to its
  // first flag so downstream Spawn/Deliver never see a multi-flag type (issue #184).
  // Bread is deliberately absent (issue #190): a boomerang steals weapons, not lunch.
  private static HeldWeapon FirstFlag (HeldWeapon mask)
  {
    foreach (var flag in new[] { HeldWeapon.Laser, HeldWeapon.Banana, HeldWeapon.Boomerang, HeldWeapon.Slingshot }) { if ((mask & flag) != 0) return flag; }
    return HeldWeapon.None;
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
      GrantToSelf ((int)cargo.Type, cargo.PreviousOwner); // Synchronous: the server's own HeldWeapon shows it immediately.
      return;
    }

    TrackPendingGrant (throwerId, cargo.Type); // Bridge until the thrower's HeldWeapon replicates back (issue #154).
    RpcId (throwerId, MethodName.ConfirmPickup, (int)cargo.Type, cargo.PreviousOwner);
  }

  // ------------------------------------------------ paper airplane catch (issue #102)

  // Active airplane flights by thrower id (CodeRabbit on #180): registered when the
  // throw starts, consumed exactly once - by the catch handoff OR by the landing
  // drop - so a replayed or forged catch request can never grant a second airplane.
  private readonly HashSet <int> _airplaneFlights = new();

  // Client -> server entry points; when this peer already is the server, skip the RPC.
  public void SendAirplaneThrowRequest()
  {
    if (Multiplayer.IsServer()) { RequestAirplaneThrow(); return; }
    RpcId (1, MethodName.RequestAirplaneThrow);
  }

  public void SendAirplaneCatchRequest (int catcherId)
  {
    if (Multiplayer.IsServer()) { RequestAirplaneCatch (catcherId); return; }
    RpcId (1, MethodName.RequestAirplaneCatch, catcherId);
  }

  // The throw starts a server-side flight record (CodeRabbit on #180): only a player
  // whose replicated hands show the airplane can open one, & each thrower has at
  // most one. The record is the single-use ticket the catch handoff consumes.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestAirplaneThrow()
  {
    if (!Multiplayer.IsServer()) return;
    var throwerId = SenderOrSelf();
    var thrower = Players().FirstOrDefault (player => player.NetworkId == throwerId);

    if (thrower == null || !thrower.Holds (HeldWeapon.PaperAirplane))
    {
      ServerLog.Event (throwerId, "airplane throw deny: sender's replicated hands show no paper airplane");
      return;
    }

    _airplaneFlights.Add (throwerId);
    ServerLog.Event (throwerId, "airplane throw: flight registered");
  }

  // Someone punched the thrower's airplane out of the air (issue #102): the thrower
  // (the flight's authority) reports the catch & the server hands the airplane to
  // the catcher through the ConfirmPickup path, so auto-equip (#128) & theft-revenge
  // (#84) apply. Single-use & server-authoritative (CodeRabbit on #180): the throw's
  // flight record is consumed atomically before any grant, so a duplicate, replayed,
  // or post-landing request finds no record & grants nothing. The catcher may be any
  // connected player - anyone in the flight path can punch-catch (issue #102); the
  // thrower's authority already validated the catch proximity against the live
  // flight before reporting it here.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestAirplaneCatch (int catcherId)
  {
    if (!Multiplayer.IsServer()) return;
    var throwerId = SenderOrSelf();
    var thrower = Players().FirstOrDefault (player => player.NetworkId == throwerId);

    if (thrower == null || !_airplaneFlights.Remove (throwerId))
    {
      ServerLog.Event (throwerId, "airplane catch deny: no active flight registered for sender");
      return;
    }

    // Belt & braces on top of the consumed record (issue #167): the sender's
    // replicated hands must still show the airplane, current-or-recently-held -
    // throwers send the catch BEFORE clearing (#145), but the clear delta can
    // beat this RPC to the server.
    if ((thrower.HeldOrRecentlyHeld & HeldWeapon.PaperAirplane) == 0)
    {
      ServerLog.Event (throwerId, "airplane catch deny: sender's replicated hands show no paper airplane");
      return;
    }

    var catcher = Players().FirstOrDefault (player => player.NetworkId == catcherId);
    if (catcher == null) return; // Catcher vanished mid-catch; the caps respawn it.
    ServerLog.Event (throwerId, $"airplane catch: handed to peer {catcherId}");
    // The announcement only goes out once the handoff has actually committed here
    // (CodeRabbit): the thrower used to announce its own predicted catch, which a
    // denied request (an impact, a landing, or a lost flight record) would have made
    // a lie. Told before the grant, so the thrower hears it even if the grant RPC
    // is the packet that goes missing.
    AnnounceCatch (throwerId, catcher.DisplayName);

    if (catcherId == Multiplayer.GetUniqueId())
    {
      GrantToSelf ((int)HeldWeapon.PaperAirplane, thrower.DisplayName);
      return;
    }

    TrackPendingGrant (catcherId, HeldWeapon.PaperAirplane); // Bridge until the catcher's HeldWeapon replicates back (issue #154).
    RpcId (catcherId, MethodName.ConfirmPickup, (int)HeldWeapon.PaperAirplane, thrower.DisplayName);
  }

  private void AnnounceCatch (int throwerId, string catcherName)
  {
    if (throwerId == Multiplayer.GetUniqueId()) { AnnounceCatchToSelf (catcherName); return; }
    RpcId (throwerId, MethodName.ConfirmAirplaneCatch, catcherName);
  }

  private void AnnounceCatchToSelf (string catcherName) => (GetParent() as World)?.SelfPlayer?.NotifyAirplaneCaught (catcherName);
  [Rpc] private void ConfirmAirplaneCatch (string catcherName) => AnnounceCatchToSelf (catcherName);

  // The boomerang dropped out of the sky (thrower zapped out mid-flight, or the
  // safety timeout): it & any cargo become expiring pickups where it was, settled
  // onto the ground below so nothing floats out of reach.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestBoomerangRelease (Vector3 position)
  {
    if (!Multiplayer.IsServer()) return;
    var throwerId = SenderOrSelf();
    var thrower = Players().FirstOrDefault (player => player.NetworkId == throwerId);
    // Server-side state check (CodeRabbit on #184, the #145/#167 convention): only a
    // sender whose replicated HeldWeapon shows a boomerang (current or drop-grace)
    // can release one - a forged request can't conjure pickups.
    if (thrower == null || (thrower.HeldOrRecentlyHeld & HeldWeapon.Boomerang) == 0)
    {
      ServerLog.Event (throwerId, "boomerang release deny: sender does not hold a boomerang");
      return;
    }

    // No ground beneath the release point (CodeRabbit on #184): skip the spawns like
    // RequestDrop does - draining the escrow lets the caps respawn boomerang & cargo
    // at spawn points instead of leaving unreachable floating pickups.
    if (!TryFindGround (position, out var spot))
    {
      TakeEscrowFor (throwerId);
      ServerLog.Event (throwerId, $"boomerang release skip: no ground beneath {position}; boomerang & cargo return via the caps");
      return;
    }

    ServerLog.Event (throwerId, $"boomerang release: dropped at {spot}");
    Spawn (HeldWeapon.Boomerang, spot, expires: true);
    var offset = 0;
    foreach (var cargo in TakeEscrowFor (throwerId)) Spawn (cargo.Type, spot + Vector3.Right * (0.8f * ++offset), expires: true, cargo.PreviousOwner);
  }

  // Level geometry only (collision layer 1): another player below isn't a shelf.
  private const uint WorldLayer = 1;
  // The kill boundary under the arena (y=-100) shares the world layer, & the ground
  // ray was treating it as a floor - spawning unreachable pickups at y=-99 (issue
  // #172). No real level surface sits below this, so anything deeper is the void.
  private const float MinGroundY = -50.0f;
  // A Player's origin sits at its feet, so the ground ray has to start above the
  // drop point to see the surface underfoot at all (issue #196); roughly chest
  // height clears the floor without reaching through a low ceiling.
  private const float GroundRayLiftMeters = 1.0f;

  // Finds the first level surface beneath the point (hover height above it); false
  // over the void, where there's nothing for a pickup to rest on (issue #151) - the
  // kill boundary below the arena doesn't count (issue #172).
  private bool TryFindGround (Vector3 position, out Vector3 spot)
  {
    // Start the cast above the drop point (issue #196): a Player's origin is at its
    // FEET, so a standing player's death drop began exactly on the surface it stood
    // on & the ray missed it - grounding spawn-room drops 30m below in the arena, &
    // skipping arena-floor drops entirely (nothing under them but the kill boundary
    // #172 rejects, so those weapons just vanished).
    var from = position + Vector3.Up * GroundRayLiftMeters;
    var query = PhysicsRayQueryParameters3D.Create (from, from + Vector3.Down * (100.0f + GroundRayLiftMeters), collisionMask: WorldLayer);
    var hit = GetWorld3D().DirectSpaceState.IntersectRay (query);
    var grounded = hit.Count > 0 && ((Vector3)hit["position"]).Y >= MinGroundY;
    spot = grounded ? (Vector3)hit["position"] + Vector3.Up * PickupHoverHeight : position;
    return grounded;
  }
}
