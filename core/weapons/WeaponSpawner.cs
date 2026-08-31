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
  [Export] public int MaxBlowguns = 1; // The stealth weapon (issue #194).
  // The dart economy (issue #236): exactly this many darts exist, ever - in blowguns,
  // embedded in players, nocked in slingshots, flying, or on the ground. The census
  // below spawns floating pickups until the count is whole again (the void eats some).
  // 30 darts in 10 CLUSTERS of 3 (Aaron, 2026-08-24: "each dart spawn point has a
  // cluster of 3 darts instead of 1") - finding a stash is a real reload, not one
  // shot. The cap counts darts, embedded & loaded ones included.
  [Export] public int MaxDarts = 60; // Room for preloaded guns AND real stashes (issue #421).
  [Export] public int DartsPerCluster = 10; // Stashes of 10, no more 2s & 3s (Aaron, 2026-08-28, issue #421).
  [Export] public int DartsPerGunPreload = 10; // A fresh spawner blowgun comes loaded (issue #421).
  [Export] public float PickupClaimRangeMeters = 8.0f; // Server-enforced reach on claims (#430): walk-over is ~2m; the slack absorbs steady-state replication lag.
  private const float ClaimRecheckSeconds = 0.4f; // A teleport's replication gap: re-read once before denying reach (#430).
  private const float BoomerangScoopRangeMeters = 40.0f; // OutboundMeters (25) + curve drift + thrower movement during flight (#430).
  private const float DartFlightGraceSeconds = 7.0f; // A fired dart counts until it lands or hits (max lifetime + margin).
  private readonly List <ulong> _dartFlightsUntilMs = new();
  // Sanity bound on a death's dart scatter (issue #194): more embedded darts than
  // this is already a story, & the pickups expire in 5s anyway.
  private const int MaxScatteredDarts = 8;
  [Export] public float ReconcileIntervalSeconds = 1.0f;
  [Export] public float PickupHoverHeight = 0.9f;
  // Playtest-only (#72 & #197): deterministic pickups the driver can walk to, parked
  // in the spawn room's CORNERS. Respawns scatter over +/-4 in x & z, & a pickup is
  // claimable from 1.7m measured against the player's center - so the old mid-wall
  // spots at z = 5 sat ~1m from the nearest possible spawn & a joining peer could
  // auto-claim one seconds after landing, randomly failing the "spawned unarmed"
  // (#72) & auto-equip (#128) asserts. A corner is the only place in this 12x12 room
  // that is genuinely out of reach: 5.5 in BOTH axes is 2.12m from the nearest corner
  // of the scatter square, comfortably past the claim radius, so only a deliberate
  // walk gets there - the same reasoning that moved the banana out to the arena.
  public static readonly Vector3 PlaytestLaserPosition = new(-5.5f, 31.1f, 5.5f);
  // Playtest-only (#98): same idea for the boomerang throw/catch phase.
  public static readonly Vector3 PlaytestBoomerangPosition = new(5.5f, 31.1f, 5.5f);
  // Playtest-only (#99): same idea for the slingshot draw/release phase.
  public static readonly Vector3 PlaytestSlingshotPosition = new(-5.5f, 31.1f, -5.5f);
  // Playtest-only (#102): same idea for the paper airplane throw/catch phase, hard
  // against the far wall at z = -5.8, which already puts it 1.8m from the nearest
  // possible spawn - past the claim radius, so no unlucky spawn can auto-claim it
  // (#197) & only a deliberate walk reaches it. That matters most of all here, since
  // the airplane pickup is capped at exactly one (#102). It keeps this spot rather
  // than a corner so the boomerang phase's throw lane stays empty of scoopable items.
  public static readonly Vector3 PlaytestAirplanePosition = new(0.0f, 31.1f, -5.8f);
  // Playtest-only (#169): the victim arms up here before the kill phase, so the death
  // drop has something to drop - RequestDrop's death path had no coverage at all,
  // which is how the #167 vanishing-weapon regression reached players. Down in the
  // empty arena rather than the spawn room: every spot in that small room is within
  // claim reach of the +/-4 random spawn scatter (observed: a joining peer grabbed
  // it seconds after spawning), & a stray banana in someone's hands would make the
  // death-drop phase's claim assert meaningless.
  public static readonly Vector3 PlaytestBananaPosition = new(0.0f, 0.9f, -40.0f);
  // The blowgun takes the last free corner & a floating dart sits beside it (issue
  // #236): darts are harmless to anyone without the blowgun, so no auto-claim risk.
  public static readonly Vector3 PlaytestBlowgunPosition = new(5.5f, 31.1f, -5.5f);
  public static readonly Vector3 PlaytestDartPosition = new(3.0f, 31.1f, -5.5f);
  // The landmine phase (#191) needs no fixed spot of its own: the driver arms one by
  // throwing the airplane into the floor, & it comes down armed wherever it lands.
  private const float OccupiedRadius = 1.0f;
  // Cargo riding a boomerang home (issue #98): stolen & scooped weapons live here
  // between the grab & the thrower's catch, so the caps still count them.
  private readonly record struct BoomerangCargo (int OwnerId, HeldWeapon Type, string PreviousOwner, int DartPayload = 0);
  private readonly List <BoomerangCargo> _escrow = new();
  // Award->replication bridge (issue #154): between despawning a claimed pickup (or
  // delivering escrowed cargo) & the collector's replicated HeldWeapon showing the
  // weapon, the count dips below the cap - a reconcile pass in that window would
  // spawn a duplicate. Pending grants keep the weapon counted until the flag lands;
  // the timeout covers a collector that vanishes mid-grant (the caps then respawn it).
  private readonly record struct PendingGrant (int CollectorId, HeldWeapon Type, ulong ExpiresAtMs, int DartPayload = 0);
  private readonly List <PendingGrant> _pendingGrants = new();
  private const float PendingGrantTimeoutSeconds = 3.0f;
  // Universal slingshot ammo (issue #190): a world item loaded into a slingshot
  // exists nowhere else until it lands, so the caps count it here - exactly like
  // boomerang cargo. One nocked item per loader.
  private readonly record struct LoadedAmmo (int LoaderId, HeldWeapon Type, string PreviousOwner, int DartPayload = 0);
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
  private void TrackPendingGrant (int collectorId, HeldWeapon type, int dartPayload = 0) => _pendingGrants.Add (new PendingGrant (collectorId, type, Time.GetTicksMsec() + (ulong)(PendingGrantTimeoutSeconds * 1000.0f), dartPayload)); // Issue #154; the payload keeps counting through the bridge (CodeRabbit on #430).
  // A pending grant ends when the collector's replicated HeldWeapon shows the weapon
  // (it counts as held from then on) or the timeout passes (issue #154).
  // A blowgun grant's payload bridge holds until the AMMO replicates too (CodeRabbit
  // on #430): the weapon flag & the dart count are separate replicated properties, &
  // pruning on the flag alone drops the payload from the census for the gap between
  // them. BlowgunDarts > 0 is the confirmation (the 1.5s fire cooldown means at most
  // ~2 darts can be spent inside the 3s timeout, so a real preload can't hit zero
  // first); a zero-payload grant - a dropped, empty gun - keeps the old flag-only
  // prune via the timeout at worst, which over-counts briefly in the SAFE direction.
  private void PrunePendingGrants (List <Player> players) => _pendingGrants.RemoveAll (grant => Time.GetTicksMsec() > grant.ExpiresAtMs || players.Any (player => player.NetworkId == grant.CollectorId && player.Holds (grant.Type) && (grant.DartPayload == 0 || player.BlowgunDarts > 0)));
  private void GrantToSelf (int type, string previousOwner, int dartPayload = 0) => (GetParent() as World)?.SelfPlayer?.GrantWeapon ((HeldWeapon)type, previousOwner, dartPayload);
  [Rpc] private void ConfirmPickup (int type, string previousOwner, int dartPayload) => GrantToSelf (type, previousOwner, dartPayload);
  // A direct (non-RPC) call means the host itself sent it, so there's no remote sender.
  private int SenderOrSelf() => Multiplayer.GetRemoteSenderId() == 0 ? Multiplayer.GetUniqueId() : Multiplayer.GetRemoteSenderId();

  // Leaving a session frees the pickup & player NODES, but the caps also count
  // weapons that exist only as a ledger entry: boomerang cargo, a pending grant,
  // nocked ammo, an armed airplane, a dart in flight. Those records have no node
  // to free, so leaving mid-flight strands them - & since World (& this spawner)
  // survive the trip to the menu, every later hosted game in the same process
  // counts them forever & spawns one fewer of that weapon, permanently. Clear
  // what belongs to the session; _laserPoints is spawn geometry & stays.
  public void ResetSessionState()
  {
    _escrow.Clear();
    _pendingGrants.Clear();
    _ammoEscrow.Clear();
    _airplaneHazards.Clear();
    _airplaneFlights.Clear();
    _dartFlightsUntilMs.Clear();
    // The reconcile countdown too (CodeRabbit on #408): teardown now removes every
    // pickup, so reconcile is what refills the arena - & a session left mid-countdown
    // makes the NEXT game wait out that leftover interval with nothing to pick up.
    // Zero means the first server tick restocks. This only started mattering when the
    // pickups began being cleared; before that they simply persisted.
    _reconcileIn = 0.0f;
  }

  public override void _Ready()
  {
    _rng.Randomize();
    _pickupScene = ResourceLoader.Load <PackedScene> ("res://core/weapons/WeaponPickup.tscn");
    // Laser spawn points: on top of EVERY low building - new buildings become spawn
    // points automatically (issue #293); banana: the high platform.
    for (var i = 1; GetNodeOrNull <CsgBox3D> ($"../Building{i}") is { } building; ++i) _laserPoints.Add (TopOf (building, PickupHoverHeight));
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
  // Punch theft entry point (issue #193); when this peer already is the server, skip the RPC.
  public void SendPunchTheftRequest (int puncherId, Vector3 position, HeldWeapon lost, bool steal)
  {
    if (Multiplayer.IsServer()) { RequestPunchTheft (puncherId, position, (int)lost, steal); return; }
    RpcId (1, MethodName.RequestPunchTheft, puncherId, position, (int)lost, steal);
  }

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
    // FRESH snapshot for the dart census (CodeRabbit on #430): specials may have just
    // spawned a loaded blowgun, & counting the stale list overshoots by its payload.
    SpawnDartsIfMissing (Pickups().ToList(), players); // Issue #236.
    EnsurePlaytestPickups (pickups, players);
  }

  // ------------------------------------------------ dart census (issue #236)

  private int CountDarts (List <WeaponPickup> pickups, List <Player> players)
  {
    _dartFlightsUntilMs.RemoveAll (until => Time.GetTicksMsec() > until);
    return pickups.Count (pickup => pickup.Weapon == HeldWeapon.PoisonDart) + pickups.Where (pickup => pickup.Weapon == HeldWeapon.Blowgun).Sum (pickup => pickup.DartPayload) + players.Sum (player => player.BlowgunDarts + player.PoisonDarts) + _ammoEscrow.Count (ammo => ammo.Type == HeldWeapon.PoisonDart) + _ammoEscrow.Sum (ammo => ammo.DartPayload) + _escrow.Count (cargo => cargo.Type == HeldWeapon.PoisonDart) + _escrow.Sum (cargo => cargo.DartPayload) + _pendingGrants.Sum (grant => grant.DartPayload) + _dartFlightsUntilMs.Count;
  }

  // Floating (spawned) darts scatter over the arena floor, not the weapon points: only
  // a blowgun holder can collect them, so they're ammo to find, not loot to camp.
  // Not in playtest runs: the harness asserts its pickups by position (CodeRabbit on #180).
  private void SpawnDartsIfMissing (List <WeaponPickup> pickups, List <Player> players)
  {
    if (_isPlaytest) return;
    var missing = MaxDarts - CountDarts (pickups, players);
    var perCluster = Mathf.Max (1, DartsPerCluster); // One validated size for divisor & ring (CodeRabbit on #395).
    var clusters = Mathf.CeilToInt (missing / (float)perCluster);

    for (var cluster = 0; cluster < clusters && missing > 0; ++cluster)
    {
      var target = new Vector3 (_rng.RandfRange (-85.0f, 85.0f), 0.0f, _rng.RandfRange (-85.0f, 85.0f)); // The doubled floor (issue #293), with an edge margin.
      if (!TryFindGround (target, out var spot)) continue; // The next census retries this one.

      // A little ring, so the three read as a stash instead of one fat dart. An
      // offset with no ground under it seats on the cluster's own grounded
      // center instead - never at an unseated height (CodeRabbit on #395).
      for (var inCluster = 0; inCluster < perCluster && missing > 0; ++inCluster, --missing)
      {
        var angle = Mathf.Tau * inCluster / perCluster;
        var offset = new Vector3 (Mathf.Cos (angle), 0.0f, Mathf.Sin (angle)) * 0.45f;
        Spawn (HeldWeapon.PoisonDart, TryFindGround (spot + offset, out var seated) ? seated : spot, expires: false);
      }
    }
  }

  // The banana, boomerang (issue #98), & slingshot (issue #99) respawn at random free
  // points: the high platform + whatever laser points the lasers didn't claim (laser
  // precedence when contested); a shared candidate list keeps them from stacking.
  private void SpawnSpecialsIfMissing (List <WeaponPickup> pickups, List <Player> players, List <Vector3> freePoints)
  {
    // In playtest mode the deterministic fixtures (EnsurePlaytestPickups) are the ONLY
    // spawn path for every special - a random spawn here would go unseen by the stale
    // pickups snapshot, slip past the fixture cap checks, & mint doubles of capped
    // items or leave a fixture spot empty (CodeRabbit on #258, extending #180).
    if (_isPlaytest) return;
    var candidates = new List <Vector3> (freePoints);
    if (IsFree (_bananaPoint, pickups)) candidates.Add (_bananaPoint);
    if (candidates.Count > 0 && Count (HeldWeapon.Banana, pickups, players) < MaxBananas) Spawn (HeldWeapon.Banana, TakeRandom (candidates), expires: false);
    if (candidates.Count > 0 && Count (HeldWeapon.Boomerang, pickups, players) < MaxBoomerangs) Spawn (HeldWeapon.Boomerang, TakeRandom (candidates), expires: false);
    if (candidates.Count > 0 && Count (HeldWeapon.Slingshot, pickups, players) < MaxSlingshots) Spawn (HeldWeapon.Slingshot, TakeRandom (candidates), expires: false);
    if (candidates.Count > 0 && Count (HeldWeapon.Blowgun, pickups, players) < MaxBlowguns) Spawn (HeldWeapon.Blowgun, TakeRandom (candidates), expires: false, dartPayload: DartsPerGunPreload); // Issue #194; loaded (issue #421).
    // Exactly 1 airplane in the game (issue #102), refolded at a spawn point whenever
    // the level's only one is spent - a mine popped its target, or a thrown or slung
    // one ignited somebody (issue #191). A fresh spawn-point airplane is unarmed, so
    // it is a normal pickup; only one that has come down from flight is a mine.
    // In playtest mode the deterministic spawn-room pickup (EnsurePlaytestPickups) is
    // the airplane's ONLY spawn path, or the two paths together could mint a second
    // one (CodeRabbit on #180).
    if (candidates.Count > 0 && Count (HeldWeapon.PaperAirplane, pickups, players) < MaxPaperAirplanes) Spawn (HeldWeapon.PaperAirplane, TakeRandom (candidates), expires: false);
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
    if (Count (HeldWeapon.Blowgun, pickups, players) < MaxBlowguns) EnsurePlaytestPickup (HeldWeapon.Blowgun, PlaytestBlowgunPosition, pickups, DartsPerGunPreload); // Cap-guarded like the banana (CodeRabbit on #258); loaded (issue #421).
    EnsurePlaytestPickup (HeldWeapon.PoisonDart, PlaytestDartPosition, pickups); // A floating (unarmed) dart: ammo for a blowgun holder (issue #236).
  }

  private void EnsurePlaytestPickup (HeldWeapon type, Vector3 position, List <WeaponPickup> pickups, int dartPayload = 0)
  {
    if (pickups.Any (pickup => pickup.Position.DistanceTo (position) < OccupiedRadius)) return;
    Spawn (type, position, expires: false, dartPayload: dartPayload);
  }

  private Vector3 TakeRandom (List <Vector3> points)
  {
    var index = _rng.RandiRange (0, points.Count - 1);
    var point = points[index];
    points.RemoveAt (index);
    return point;
  }

  private void Spawn (HeldWeapon type, Vector3 position, bool expires, string previousOwner = "", bool armed = false, Vector3 tossFrom = default, int dartPayload = 0)
  {
    var pickup = _pickupScene.Instantiate <WeaponPickup>();
    pickup.Name = $"WeaponPickup{++_nextPickupId}";
    pickup.Weapon = type;
    pickup.Position = position;
    pickup.Expires = expires;
    pickup.PreviousOwner = previousOwner; // For theft-revenge messages (issue #84).
    pickup.DartPayload = dartPayload; // A fresh blowgun ships loaded (issue #421); drops ship empty.
    pickup.Armed = armed; // An airplane that came down from flight is a live landmine (issue #191).
    pickup.TossFrom = tossFrom; // Punched-loose fly-out start (issue #193); zero = normal drop.
    GetParent().AddChild (pickup); // The MultiplayerSpawner replicates the spawn to every peer.
    ServerLog.Event ($"weapon spawn: {type} pickup [{pickup.Name}] at {position}{(expires ? " (expiring drop)" : "")}{(armed ? " (armed)" : "")}");
  }

  // First request wins: a pickup that's already claimed or expired is simply gone.
  // Every claim/award/deny decision is logged server-side (issues #110 & #111).
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private async void RequestPickup (string pickupName, int collectorId)
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

    // No resolved player, no claim (CodeRabbit on #430): a null collector skipped
    // the reach & duplicate checks below & still despawned the pickup. A peer that
    // exists but has no Player node yet can simply re-claim after spawning.
    if (collector == null)
    {
      ServerLog.Event (collectorId, $"weapon deny: pickup [{pickupName}] claimed by a peer with no player");
      return;
    }

    // The server has both positions, so it enforces REACH (CodeRabbit on #430): a
    // peer naming a pickup across the map gets nothing - with the preload riding
    // pickups, a remote claim would be a free loaded gun.
    if (collector.GlobalPosition.DistanceTo (pickup.GlobalPosition) > PickupClaimRangeMeters)
    {
      // A position JUMP outraces its own replication (the #430 playtest red, denied
      // "from 48.6m away" while standing ON the banana): a respawn or the playtest's
      // teleports can arrive at a pickup before the server's view catches up. One
      // deferred re-read closes the race without loosening the rule: give
      // replication a beat & ask again; deny only a claim that is STILL remote.
      await ToSignal (GetTree().CreateTimer (ClaimRecheckSeconds), SceneTreeTimer.SignalName.Timeout);
      if (!IsInstanceValid (pickup) || pickup.IsQueuedForDeletion() || !IsInstanceValid (collector) || collector.Fallen) return; // First request won meanwhile, someone left, or the collector fell during the grace (CodeRabbit on #430).

      if (collector.GlobalPosition.DistanceTo (pickup.GlobalPosition) > PickupClaimRangeMeters)
      {
        ServerLog.Event (collectorId, $"weapon deny: pickup [{pickupName}] claimed from {collector.GlobalPosition.DistanceTo (pickup.GlobalPosition):0.0}m away");
        return;
      }
    }

    // Holding it already means a normal collect cannot happen (#190's rule; the
    // slingshot pouch-load is a different request): a duplicate claim would only
    // despawn the pickup - denying others - & double a blowgun's payload (#430).
    if (pickup.Weapon != HeldWeapon.PoisonDart && collector.Holds (pickup.Weapon))
    {
      ServerLog.Event (collectorId, $"weapon deny: pickup [{pickupName}] while already holding {pickup.Weapon}");
      return;
    }

    var type = pickup.Weapon;
    var previousOwner = pickup.PreviousOwner;
    var dartPayload = pickup.DartPayload; // The preload rides the gun (issue #421).
    pickup.QueueFree(); // Despawns on every peer via the MultiplayerSpawner.
    ServerLog.Event (collectorId, $"weapon award: {type} from pickup [{pickupName}]{(dartPayload > 0 ? $" ({dartPayload} darts aboard)" : "")}");

    if (collectorId == Multiplayer.GetUniqueId())
    {
      GrantToSelf ((int)type, previousOwner, dartPayload); // Synchronous: the server's own HeldWeapon shows it immediately.
      return;
    }

    TrackPendingGrant (collectorId, type, dartPayload); // Bridge until the collector's HeldWeapon replicates back (issue #154).
    RpcId (collectorId, MethodName.ConfirmPickup, (int)type, previousOwner, dartPayload);
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
    if (dropped.HasFlag (HeldWeapon.Blowgun)) Spawn (HeldWeapon.Blowgun, spot + Vector3.Forward * 1.6f, expires: true, dropperName); // Issue #194.
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

  // isDeathDrop (issue #212): a death releases the nocked item beside the slingshot
  // instead of exactly where the body stood, so the two are always two pickups.
  public void SendAmmoLandRequest (Vector3 position, bool isDeathDrop = false)
  {
    if (Multiplayer.IsServer()) { RequestAmmoLand (position, isDeathDrop); return; }
    RpcId (1, MethodName.RequestAmmoLand, position, isDeathDrop);
  }

  // A slingshot-equipped player walked onto a world item: despawn it for everyone &
  // hold it as that player's nocked ammo. First request wins, like RequestPickup, so
  // two players reaching the same item resolve to exactly one loader.
  // Server-side state check (the #145/#167/#184 convention): the sender's replicated
  // HeldWeapon must show a slingshot & their replicated SelectedWeapon must have it
  // out - a client can't claim to be equipped.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private async void RequestAmmoLoad (string pickupName)
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

    // A pouch-load is a walk-over like any claim, so it enforces the same REACH
    // (CodeRabbit on #430): without it a peer could vacuum a fresh loaded blowgun
    // from anywhere into escrow. Same one-beat grace as RequestPickup for the
    // teleport-outraces-replication case, with the escrow re-checked after the wait
    // so first-request-wins survives the await.
    if (loader.GlobalPosition.DistanceTo (pickup.GlobalPosition) > PickupClaimRangeMeters)
    {
      await ToSignal (GetTree().CreateTimer (ClaimRecheckSeconds), SceneTreeTimer.SignalName.Timeout);
      if (!IsInstanceValid (pickup) || pickup.IsQueuedForDeletion() || !IsInstanceValid (loader) || _ammoEscrow.Any (ammo => ammo.LoaderId == loaderId)) return;
      if ((loader.HeldOrRecentlyHeld & HeldWeapon.Slingshot) == 0 || loader.SelectedWeapon != SelectedWeapon.Slingshot) return; // The slingshot went away during the grace (CodeRabbit on #430).

      if (loader.GlobalPosition.DistanceTo (pickup.GlobalPosition) > PickupClaimRangeMeters)
      {
        ServerLog.Event (loaderId, $"ammo load deny: pickup [{pickupName}] loaded from {loader.GlobalPosition.DistanceTo (pickup.GlobalPosition):0.0}m away");
        return;
      }
    }

    _ammoEscrow.Add (new LoadedAmmo (loaderId, pickup.Weapon, pickup.PreviousOwner, pickup.DartPayload)); // A loaded gun's darts stay counted (CodeRabbit on #430).
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
  private void RequestAmmoLand (Vector3 position, bool isDeathDrop)
  {
    if (!Multiplayer.IsServer()) return;
    var loaderId = SenderOrSelf();
    var landed = TakeAmmoFor (loaderId);

    if (landed.Count == 0)
    {
      ServerLog.Event (loaderId, "ammo land deny: sender has nothing nocked");
      return;
    }

    foreach (var ammo in landed) LandAmmo (loaderId, ammo, position, isDeathDrop);
  }

  private List <LoadedAmmo> TakeAmmoFor (int loaderId)
  {
    var loaded = _ammoEscrow.Where (ammo => ammo.LoaderId == loaderId).ToList();
    _ammoEscrow.RemoveAll (ammo => ammo.LoaderId == loaderId);
    return loaded;
  }

  // Where a death-released nocked item lands relative to the death spot (issue #212):
  // its own side step, distinct from every per-weapon offset RequestDrop uses, so the
  // freed ammo & the slingshot are always two separately visible, separately
  // claimable pickups - never one pile a single walk-over could swallow whole.
  private static readonly Vector3 DeathAmmoOffset = Vector3.Left * 1.6f;

  private void LandAmmo (int loaderId, LoadedAmmo ammo, Vector3 position, bool isDeathDrop)
  {
    // Cosmetic ammo (banana chunks) is scenery no cap tracks: it just splatters.
    if (ammo.Type == HeldWeapon.BananaChunk) return;

    // The side step is applied BEFORE grounding (issue #212), never after: grounding
    // the death spot & then sliding the pickup 1.6m sideways would land it wherever
    // that ray happened to stop - floating over a ledge, or buried in a step up - so
    // the ray has to run from the spot the item actually ends up on.
    var target = isDeathDrop ? position + DeathAmmoOffset : position;

    // Nothing beneath it (over the void): skip the spawn like RequestDrop does & let
    // the caps put the item back at a spawn point instead of floating it out of reach.
    if (!TryFindGround (target, out var spot))
    {
      ServerLog.Event (loaderId, $"ammo land skip: no ground beneath {target}; [{ammo.Type}] returns via the caps");
      return;
    }

    ServerLog.Event (loaderId, $"ammo land: {ammo.Type} at {spot}{(isDeathDrop ? " (released by its dead loader, issue #212)" : "")}");
    // A slung airplane that missed comes down ARMED (issue #191): it re-arms as the
    // landmine right where it fell & never expires, since it's the only one there is.
    var isAirplane = ammo.Type == HeldWeapon.PaperAirplane;
    Spawn (ammo.Type, spot, expires: !isAirplane, ammo.PreviousOwner, armed: isAirplane, dartPayload: ammo.DartPayload);
  }

  // ------------------------------------------------ poison darts (issue #194)

  // Client -> server entry points; when this peer already is the server, skip the RPC.
  public void SendDartScatterRequest (Vector3 position, int count)
  {
    if (Multiplayer.IsServer()) { RequestDartScatter (position, count); return; }
    RpcId (1, MethodName.RequestDartScatter, position, count);
  }

  public void SendDartStrikeRequest()
  {
    if (Multiplayer.IsServer()) { RequestDartStrike(); return; }
    RpcId (1, MethodName.RequestDartStrike);
  }

  // A death shook the victim's embedded darts out (issue #194): they fall in a ring
  // beside the body as 5s-expiry pickups a slingshot can load. The count is the
  // sender's own report, sanity-clamped rather than checked against the replicated
  // PoisonDarts: the death-path clear can replicate ahead of this RPC (the #167
  // lesson), darts carry no cap to defend, & the worst a liar buys is a few
  // seconds of extra scenery.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestDartScatter (Vector3 position, int count)
  {
    if (!Multiplayer.IsServer()) return;
    var victimId = SenderOrSelf();
    var victim = Players().FirstOrDefault (player => player.NetworkId == victimId);
    if (victim == null) return;
    var scattered = Mathf.Clamp (count, 0, MaxScatteredDarts);
    ServerLog.Event (victimId, $"dart scatter: {scattered} dart(s) fall off [{victim.DisplayName}]");

    for (var i = 0; i < scattered; ++i)
    {
      var angle = Mathf.Tau * i / Mathf.Max (1, scattered);
      var target = position + new Vector3 (Mathf.Cos (angle), 0.0f, Mathf.Sin (angle)) * 0.9f;
      if (!TryFindGround (target, out var spot)) continue; // Over the void: the census respawns it.
      Spawn (HeldWeapon.PoisonDart, spot, expires: false, victim.DisplayName, armed: true); // A landed dart is a hazard (issue #248).
    }
  }

  // Client -> server entry points for the dart economy (issue #236).
  public void SendDartFiredRequest()
  {
    if (Multiplayer.IsServer()) { RequestDartFired(); return; }
    RpcId (1, MethodName.RequestDartFired);
  }

  public void SendDartLandRequest (Vector3 position)
  {
    if (Multiplayer.IsServer()) { RequestDartLand (position); return; }
    RpcId (1, MethodName.RequestDartLand, position);
  }

  public void SendDartAmmoRequest (string pickupName)
  {
    if (Multiplayer.IsServer()) { RequestDartAmmo (pickupName); return; }
    RpcId (1, MethodName.RequestDartAmmo, pickupName);
  }

  public void SendDartStepRequest (string pickupName)
  {
    if (Multiplayer.IsServer()) { RequestDartStep (pickupName); return; }
    RpcId (1, MethodName.RequestDartStep, pickupName);
  }

  // A dart left a blowgun: it counts as in flight for a few seconds so the census
  // never mints a replacement while it's still in the air.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestDartFired()
  {
    if (!Multiplayer.IsServer()) return;
    _dartFlightsUntilMs.Add (Time.GetTicksMsec() + (ulong)(DartFlightGraceSeconds * 1000.0f));
  }

  // A miss that hit geometry lands as an ARMED ground dart where it stopped (#248).
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestDartLand (Vector3 position)
  {
    if (!Multiplayer.IsServer()) return;
    if (!TryFindGround (position, out var spot)) { ServerLog.Event (SenderOrSelf(), "dart land skip: no ground; the census respawns it"); return; }
    ServerLog.Event (SenderOrSelf(), $"dart land: armed at {spot}");
    Spawn (HeldWeapon.PoisonDart, spot, expires: false, armed: true);
  }

  // Walking over a ground dart while HOLDING the blowgun loads it as ammo - floating
  // or landed alike. Validated against the replicated HeldWeapon (#145 convention).
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestDartAmmo (string pickupName)
  {
    if (!Multiplayer.IsServer()) return;
    var collectorId = SenderOrSelf();
    var collector = Players().FirstOrDefault (player => player.NetworkId == collectorId);
    var pickup = GetParent().GetNodeOrNull <WeaponPickup> (pickupName);

    if (collector == null || (collector.HeldOrRecentlyHeld & HeldWeapon.Blowgun) == 0 || pickup == null || pickup.IsQueuedForDeletion() || pickup.Weapon != HeldWeapon.PoisonDart)
    {
      ServerLog.Event (collectorId, $"dart ammo deny: [{pickupName}] is not a dart or the sender holds no blowgun");
      return;
    }

    pickup.QueueFree(); // Despawns on every peer via the MultiplayerSpawner.
    ServerLog.Event (collectorId, $"dart ammo: [{collector.DisplayName}] loaded a dart from [{pickupName}]");
    if (collectorId == Multiplayer.GetUniqueId()) { collector.ConfirmDartAmmoSelf(); return; }
    collector.RpcId (collectorId, Player.MethodName.ConfirmDartAmmo);
  }

  // Stepping on a LANDED (armed) dart with no blowgun in hand: it's gone from the
  // ground & into you, as if it hit you (issues #236 & #248).
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestDartStep (string pickupName)
  {
    if (!Multiplayer.IsServer()) return;
    var stepperId = SenderOrSelf();
    var stepper = Players().FirstOrDefault (player => player.NetworkId == stepperId);
    var pickup = GetParent().GetNodeOrNull <WeaponPickup> (pickupName);

    if (stepper == null || stepper.SpawnArmor || stepper.Fallen || pickup == null || pickup.IsQueuedForDeletion() || pickup.Weapon != HeldWeapon.PoisonDart || !pickup.Armed)
    {
      ServerLog.Event (stepperId, $"dart step deny: [{pickupName}] is not an armed dart, or the sender is armored or down");
      return;
    }

    pickup.QueueFree();
    ServerLog.Event (stepperId, $"dart step: [{stepper.DisplayName}] stepped on a landed dart");
    if (stepperId == Multiplayer.GetUniqueId()) { stepper.ConfirmDartStepSelf(); return; }
    stepper.RpcId (stepperId, Player.MethodName.ConfirmDartStep);
  }

  // A slung dart connected (issue #194): consume the shooter's escrowed dart so it
  // can't ALSO land as a pickup - the strike-consumes rule the airplane uses (#191).
  // The poisoning itself travels shooter -> victim (ReceiveDartHit), so attribution
  // stays victim-authoritative; this RPC only settles the server's books.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestDartStrike()
  {
    if (!Multiplayer.IsServer()) return;
    var attackerId = SenderOrSelf();
    var slung = _ammoEscrow.RemoveAll (ammo => ammo.LoaderId == attackerId && ammo.Type == HeldWeapon.PoisonDart) > 0;
    ServerLog.Event (attackerId, slung ? "dart strike: slung dart consumed from escrow" : "dart strike deny: sender had no dart nocked");
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

    // The boomerang is client-simulated, but its physics bound the claim (CodeRabbit
    // on #430): a real flight stays within OutboundMeters (25) of the thrower plus
    // curve drift & the thrower's own movement, so a pickup beyond the bound cannot
    // have met a boomerang - that claim is a cross-map vacuum, not a scoop.
    if (thrower.GlobalPosition.DistanceTo (pickup.GlobalPosition) > BoomerangScoopRangeMeters)
    {
      ServerLog.Event (throwerId, $"boomerang scoop deny: pickup [{pickupName}] scooped from {thrower.GlobalPosition.DistanceTo (pickup.GlobalPosition):0.0}m away");
      return;
    }

    _escrow.Add (new BoomerangCargo (throwerId, pickup.Weapon, pickup.PreviousOwner, pickup.DartPayload)); // A scooped gun's darts stay counted (CodeRabbit on #430).
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

  // A punch knocked the victim's equipped item loose (issue #193). The victim reports
  // its own loss (#145/#167 convention: validated against its replicated state, one
  // flag only). A steal transfers it straight into the puncher's hands through the
  // ConfirmPickup path (auto-equip #128, theft-revenge #84); a type the puncher
  // already holds (or is about to be granted), a vanished puncher, or a plain
  // knocked-loose carried weapon flies out to the ground near the victim instead.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestPunchTheft (int puncherId, Vector3 position, int type, bool steal)
  {
    if (!Multiplayer.IsServer()) return;
    var victimId = SenderOrSelf();
    var victim = Players().FirstOrDefault (player => player.NetworkId == victimId);
    var lost = FirstStealableFlag ((HeldWeapon)type & (victim?.HeldOrRecentlyHeld ?? HeldWeapon.None));

    if (lost == HeldWeapon.None)
    {
      ServerLog.Event (victimId, $"punch theft deny: mask [{(HeldWeapon)type}] not held by sender");
      return;
    }

    var victimName = victim!.DisplayName; // Non-null: the mask intersection above proved the sender exists.
    var puncher = Players().FirstOrDefault (player => player.NetworkId == puncherId);
    var occupied = puncher != null && (puncher.Holds (lost) || _pendingGrants.Any (grant => grant.CollectorId == puncherId && grant.Type == lost));

    if (!steal || puncher == null || occupied)
    {
      ServerLog.Event (victimId, $"punch theft: {lost} knocked from [{victimName}] to the ground{(occupied ? " (puncher already holds one)" : "")}");
      SpawnPunchedLoose (lost, position, puncher, victimName);
      return;
    }

    ServerLog.Event (victimId, $"punch theft: {lost} taken from [{victimName}] by peer {puncherId}");

    if (puncherId == Multiplayer.GetUniqueId())
    {
      GrantToSelf ((int)lost, victimName); // Synchronous: the server's own HeldWeapon shows it immediately.
      return;
    }

    TrackPendingGrant (puncherId, lost); // Bridge until the puncher's HeldWeapon replicates back (issue #154).
    RpcId (puncherId, MethodName.ConfirmPickup, (int)lost, victimName, 0);
  }

  // Punched-loose items fly out of the hands (issue #193): away from the puncher, a
  // couple of meters, ray-grounded like any drop (#151). The pickup carries its toss
  // origin so every peer plays the same arc, & fresh punched-loose drops use a longer
  // claim delay so the victim can't stand still & instantly re-grab what just left.
  private void SpawnPunchedLoose (HeldWeapon type, Vector3 from, Player? puncher, string previousOwner)
    => SpawnTossed (type, from, puncher == null ? RandomHorizontal() : Horizontal (from - puncher.GlobalPosition), previousOwner);

  // Any item leaving a player's hands on the fly (punched loose, #193; dropped on
  // purpose, #242) lands 1.5-2.5m along 'away', ray-grounded, with the fly-out arc.
  private void SpawnTossed (HeldWeapon type, Vector3 from, Vector3 away, string previousOwner)
  {
    var target = from + away * (float)GD.RandRange (1.5, 2.5);

    if (!TryFindGround (target, out var spot))
    {
      ServerLog.Event ($"toss skip: no ground beneath {target}; [{type}] returns via the caps");
      return;
    }

    Spawn (type, spot, expires: true, previousOwner, armed: false, tossFrom: from + Vector3.Up * 1.2f);
  }

  // Dropping on purpose (issue #242): Minecraft-style, the item in your hands flies
  // out the way you're looking. Client -> server entry point, then the same
  // validated single-flag path the punch theft uses.
  public void SendDropTossRequest (HeldWeapon dropped, Vector3 direction)
  {
    if (Multiplayer.IsServer()) { RequestDropToss ((int)dropped, direction); return; }
    RpcId (1, MethodName.RequestDropToss, (int)dropped, direction);
  }

  // The toss starts from the server's own view of where the dropper stands (CodeRabbit
  // on #243) - a client supplies only what it wants to drop & which way it's looking.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestDropToss (int type, Vector3 direction)
  {
    if (!Multiplayer.IsServer()) return;
    var dropperId = SenderOrSelf();
    var dropper = Players().FirstOrDefault (player => player.NetworkId == dropperId);
    var dropped = FirstStealableFlag ((HeldWeapon)type & (dropper?.HeldOrRecentlyHeld ?? HeldWeapon.None));

    if (dropped == HeldWeapon.None)
    {
      ServerLog.Event (dropperId, $"drop toss deny: mask [{(HeldWeapon)type}] not held by sender");
      return;
    }

    ServerLog.Event (dropperId, $"drop toss: {dropped} thrown down by [{dropper!.DisplayName}]");
    SpawnTossed (dropped, dropper.GlobalPosition, Horizontal (direction), dropper.DisplayName);
  }

  private static Vector3 Horizontal (Vector3 direction)
  {
    var flat = new Vector3 (direction.X, 0.0f, direction.Z);
    return flat.LengthSquared() < 0.01f ? RandomHorizontal() : flat.Normalized();
  }

  private static Vector3 RandomHorizontal()
  {
    var angle = (float)GD.RandRange (0.0, Mathf.Tau);
    return new Vector3 (Mathf.Cos (angle), 0.0f, Mathf.Sin (angle));
  }

  // Punch theft covers every hand-holdable (issue #193): the guns, the airplane, &
  // the equipped loaf (#192) - unlike a boomerang, a fist can take your lunch.
  public static HeldWeapon FirstStealableFlag (HeldWeapon mask)
  {
    foreach (var flag in new[] { HeldWeapon.Laser, HeldWeapon.Banana, HeldWeapon.Boomerang, HeldWeapon.Slingshot, HeldWeapon.PaperAirplane, HeldWeapon.Blowgun, HeldWeapon.Bread }) { if ((mask & flag) != 0) return flag; }
    return HeldWeapon.None;
  }

  // Escrow & pickups carry exactly one weapon each: reduce a validated mask to its
  // first flag so downstream Spawn/Deliver never see a multi-flag type (issue #184).
  // Bread is deliberately absent (issue #190): a boomerang steals weapons, not lunch.
  private static HeldWeapon FirstFlag (HeldWeapon mask)
  {
    foreach (var flag in new[] { HeldWeapon.Laser, HeldWeapon.Banana, HeldWeapon.Boomerang, HeldWeapon.Slingshot, HeldWeapon.Blowgun }) { if ((mask & flag) != 0) return flag; }
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
      Spawn (cargo.Type, thrower.GlobalPosition + Vector3.Up * PickupHoverHeight, expires: true, cargo.PreviousOwner, dartPayload: cargo.DartPayload);
      return;
    }

    if (throwerId == Multiplayer.GetUniqueId())
    {
      GrantToSelf ((int)cargo.Type, cargo.PreviousOwner, cargo.DartPayload); // Synchronous: the server's own HeldWeapon shows it immediately.
      return;
    }

    TrackPendingGrant (throwerId, cargo.Type, cargo.DartPayload); // Bridge until the thrower's HeldWeapon replicates back (issue #154).
    RpcId (throwerId, MethodName.ConfirmPickup, (int)cargo.Type, cargo.PreviousOwner, cargo.DartPayload);
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
    RpcId (catcherId, MethodName.ConfirmPickup, (int)HeldWeapon.PaperAirplane, thrower.DisplayName, 0);
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
    foreach (var cargo in TakeEscrowFor (throwerId)) Spawn (cargo.Type, spot + Vector3.Right * (0.8f * ++offset), expires: true, cargo.PreviousOwner, dartPayload: cargo.DartPayload);
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
