using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.players;

// Paper airplane (issue #102): the slot-6 homing glider. Thrown with left click, it
// locks onto the player under the crosshair & banks slowly toward them. The
// signature mechanic: the target (or anyone in its path) can punch the incoming
// airplane to catch it - it goes straight into their hand & they can throw it back.
// Exactly one airplane exists in the game (see WeaponSpawner).
//
// An UNCAUGHT hit is no longer a paper cut (issue #191): it hands the airplane's
// hazard sequence to that one player - alight for ~2s, then a personal pop, with no
// blast radius (Player.AirplaneHazard.cs). A glide that never finds anyone comes down
// ARMED instead of as a plain pickup, so a grounded airplane is a landmine that
// targets whoever steps on it - unless they have a slingshot out, in which case they
// load it as ammo instead (issue #190).
public partial class Player
{
  // Punch-catch reach (issue #102): the swing grabs any airplane this close.
  // Matches PunchRange (issue #71): if your fist reaches a player at 4m it reaches
  // an airplane at 4m. The catch is the fun part & the glider closes a meter every
  // ~0.1s, so anything tighter made well-timed catches lose the race to the hit -
  // at the old 3m a catch had to land inside a single frame's worth of travel.
  [Export] public float AirplaneCatchRadiusMeters = 4.0f;
  // The catch needs your eyes (caleb, 2026-08-28, issue #427): the punch only grabs
  // an airplane inside this facing cone of the crosshair - ~35 degrees off-center.
  [Export] public float AirplaneCatchFacingMinDot = 0.82f;
  // How long a swing keeps grabbing after it misses (issue #102): about one punch
  // animation, so catching rewards timing instead of frame luck.
  [Export] public float AirplaneCatchWindowSeconds = 0.35f;
  // Replicated so the flight's authority can settle a mid-swing impact as a catch
  // without waiting for the catcher's request to cross the wire (issue #102).
  [Export] public bool CatchingAirplane { get; set; }
  private ulong _catchWindowEndMs;
  // Server-side-of-the-thrower slack for the catch check: the catcher's punch was
  // validated on their own peer; this only rejects wildly stale/forged requests.
  [Export] public float AirplaneCatchSlackMeters = 6.0f;
  [Signal] public delegate void AirplaneCaughtEventHandler (string catcherName);
  // Fired the moment a player enters the lock ring (issue #211): the HUD chirps so
  // the lock is audible as well as visible, without watching the ring.
  [Signal] public delegate void AirplaneLockAcquiredEventHandler();
  private PaperAirplaneProjectile? _liveAirplane;
  private PaperAirplaneProjectile? _visualAirplane;
  private Node3D _airplaneHeld = null!;
  // The flight already went somewhere else - a catch handoff or a strike - so the
  // landing report that follows it in the same frame must not ALSO place an airplane
  // (issues #102 & #191). FlyStep emits HitPlayer & then Landed back to back, so
  // without this a mid-swing catch would mint a second airplane as an armed mine.
  private bool _airplaneFlightConsumed;
  // Playtest-observable (issue #102): true from the throw until the catch or landing.
  public bool IsAirplaneInFlight => _liveAirplane != null && IsInstanceValid (_liveAirplane);
  // Every peer renders this player's empty hand while their airplane is out flying.
  private bool IsAirplaneOut => IsAirplaneInFlight || (_visualAirplane != null && IsInstanceValid (_visualAirplane));

  // Held model: the same folded dart visual as the projectile, resting in the hand.
  private void CreateAirplaneHeld()
  {
    _airplaneHeld = PaperAirplaneProjectile.CreateVisual();
    _airplaneHeld.Position = new Vector3 (0.45f, -0.4f, -0.85f);
    _airplaneHeld.RotationDegrees = new Vector3 (5.0f, 25.0f, 0.0f);
    GetNode <Node3D> ("Camera3D").AddChild (_airplaneHeld);
  }

  private void UpdateAirplane()
  {
    if (!IsPaperAirplaneSelected || !HasPaperAirplane || !_isInputEnabled || Dancing) return; // Dancing blocks throwing (issue #103).
    if (IsAirplaneOut || !Input.IsActionJustPressed ("shoot")) return;
    ThrowPaperAirplane();
  }

  // Lock-on feedback (issue #205): with the airplane in hand & a player under the
  // crosshair, the throw WILL home on them - the HUD draws a big ring so that's
  // visible before committing, instead of the lock being silent & invisible.
  public bool HasAirplaneLock => _lockedTarget != null;
  // The ring is a big TARGET AREA, not a crosshair (issue #211): anyone inside the
  // circle when you release is who the glider chases. Measured as a cone off the aim
  // axis rather than in screen pixels - 27 degrees is what TargetRing's 0.34-of-screen
  // circle subtends at the default FOV, & unlike a viewport measurement it means the
  // same thing at any window size (& in a headless playtest, where there is none).
  private const float LockConeDegrees = 27.0f;
  private Player? _lockedTarget;

  private void UpdateAirplaneLock()
  {
    if (!IsMultiplayerAuthority()) return;
    var wasLocked = _lockedTarget != null;
    // Same gates the throw itself applies (CodeRabbit on #206): showing a lock during
    // the respawn input lock or a dance promises a throw that won't happen.
    var canThrow = _isInputEnabled && !Dancing && IsPaperAirplaneSelected && HasPaperAirplane && !IsAirplaneOut;
    _lockedTarget = canThrow ? FindPlayerInsideLockRing() : null;
    if (_lockedTarget != null && !wasLocked) EmitSignal (SignalName.AirplaneLockAcquired); // Chirp on acquisition.
  }

  // Whoever sits nearest the middle of the ring - screen-space, so a distant player
  // inside the circle locks just as well as a close one.
  private Player? FindPlayerInsideLockRing()
  {
    var aim = ShotDirection(); // Converged in third person (issue #338).
    var cone = Mathf.DegToRad (LockConeDegrees);
    Player? best = null;
    var bestAngle = float.MaxValue;

    foreach (var node in GetParent().GetChildren())
    {
      if (node is not Player player || player == this) continue;
      var toHead = player.GlobalPosition + Vector3.Up - _camera.GlobalPosition;
      if (toHead.LengthSquared() < 0.001f) continue;
      var angle = aim.AngleTo (toHead.Normalized());
      if (angle > cone || angle >= bestAngle) continue; // Nearest the middle of the ring wins.
      best = player;
      bestAngle = angle;
    }

    return best;
  }

  // The target locks at throw time (issue #102): whoever is under the crosshair.
  // With nobody aimed, the airplane just glides straight & lands as a pickup.
  private void ThrowPaperAirplane()
  {
    CancelSpawnArmorIfFired();
    var target = _lockedTarget; // Whoever was inside the ring at release (issue #211).
    var direction = ShotDirection(); // Converged in third person (issue #338).
    var origin = _camera.GlobalPosition + direction * MuzzleOffsetMeters;
    // The server registers the flight (CodeRabbit on #180): the single-use record a
    // later catch handoff must consume, so replays can't mint extra airplanes.
    Spawner.SendAirplaneThrowRequest();
    _liveAirplane = SpawnAirplane (origin, direction, isLive: true, target);
    Rpc (MethodName.SpawnVisualAirplane, origin, direction, target?.NetworkId ?? 0);
    UpdateWeaponVisibility(); // The hand empties while the airplane is out.
    // The same shoot press must not also fire a full-auto laser this frame.
    _nextAutoShotIn = FullAutoShotIntervalSeconds;
    GD.Print ($"{DisplayName}: I threw my paper airplane{(target != null ? $" at {target.DisplayName}" : "")}!");
  }

  // Visual-only copy of the thrower's airplane on every other peer, homing on the
  // same locked target. Throwing proves the thrower's spawn armor is gone, so stale
  // armor whitewash clears here (issue #114).
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void SpawnVisualAirplane (Vector3 origin, Vector3 direction, int targetId)
  {
    if (!IsFromOwner()) return;
    ClearArmorDisplayOnRemoteAttack();
    var target = targetId == 0 ? null : GetParent().GetNodeOrNull <Player> ($"{targetId}");
    FreeVisualAirplane(); // A previous copy would otherwise leak & keep chasing.
    var spawned = SpawnAirplane (origin, direction, isLive: false, target);
    _visualAirplane = spawned;
    // The exit callback captures ITS OWN copy (CodeRabbit): a stale exit event
    // clearing the newer reference put the hand back mid-flight.
    spawned.TreeExited += () => OnVisualAirplaneGone (spawned);
    UpdateWeaponVisibility();
  }

  // Only this player's own peer narrates their airplane (CodeRabbit): otherwise any
  // peer could spawn or free the visual copy on somebody else's node. A direct local
  // call has no remote sender, which is the authority calling itself.
  private bool IsFromOwner() => Multiplayer.GetRemoteSenderId() is var sender && (sender == 0 || sender == NetworkId);

  private void OnVisualAirplaneGone (PaperAirplaneProjectile gone)
  {
    if (_visualAirplane != gone) return; // A newer flight already owns the hand.
    _visualAirplane = null;
    if (IsInsideTree()) UpdateWeaponVisibility();
  }

  private PaperAirplaneProjectile SpawnAirplane (Vector3 origin, Vector3 direction, bool isLive, Player? target)
  {
    var airplane = new PaperAirplaneProjectile();
    GetParent().AddChild (airplane);
    airplane.Launch (origin, direction, isLive, this, target);
    // Runs on every peer, on the TARGET's own node: only their HUD gets the closing
    // warning ring & its accelerating beep (issue #191).
    target?.NoteIncomingAirplane (airplane);
    if (!isLive) return airplane;
    airplane.HitPlayer += OnAirplaneHitPlayer;
    airplane.Landed += OnAirplaneLanded;
    return airplane;
  }

  // The thrower only reports the hit; the victim applies its own damage & knockback
  // (victim-authoritative, same as ReceiveHit & ReceiveBoomerangHit).
  private void OnAirplaneHitPlayer (Player victim)
  {
    if (victim.NetworkId == NetworkId) return;

    // Swinging when it arrives IS the catch (issue #102). The catcher's own grab
    // asks us over the wire, & the airplane keeps flying here while that request
    // travels - so on any real latency the impact beat the catch & a correctly
    // timed punch got paper-cut anyway. CatchingAirplane replicates, so we can
    // settle it here on the flight's authority with nothing in flight but state.
    if (victim.CatchingAirplane)
    {
      GD.Print ($"{DisplayName}: {victim.DisplayName} caught my paper airplane mid-swing!");
      _airplaneFlightConsumed = true;
      GrantCaughtAirplaneTo (victim);
      return;
    }

    // The airplane doesn't paper-cut anymore (issue #191): it picks this one player
    // & hands them the ignite-then-pop sequence. The server validates that we really
    // had a flight registered before it tells them to light up, & the airplane is
    // consumed by the strike - so no landing, no pickup, & the caps fold a new one.
    GD.Print ($"{DisplayName}: My paper airplane found {victim.DisplayName}!");
    _hitmarkerSound.Play();
    ReportToServer ($"paper airplane: {DisplayName} lit up {victim.DisplayName}");
    _airplaneFlightConsumed = true;
    Spawner.SendAirplaneStrikeRequest (victim.NetworkId);
    ReleaseAirplaneFromHands();
  }

  // The flight ended without finding anyone (geometry, or the flutter reaching the
  // ground): the airplane comes down ARMED where it stopped & waits there as a
  // landmine (issue #191). Request BEFORE clearing (CodeRabbit on #145 & issue #167):
  // the server validates against this player's replicated HeldWeapon & grounds the
  // spot onto the level beneath (issue #151).
  private void OnAirplaneLanded (Vector3 position)
  {
    _liveAirplane = null;
    Rpc (MethodName.FreeVisualAirplane); // Visual copies may lag the landing slightly.
    if (_airplaneFlightConsumed) { _airplaneFlightConsumed = false; return; } // A catch or a strike already consumed it.
    Spawner.SendAirplaneLandRequest (position);
    ReleaseAirplaneFromHands();
    GD.Print ($"{DisplayName}: My paper airplane came down armed!");
  }

  // Our hands stop showing the airplane once its flight is over, however it ended.
  private void ReleaseAirplaneFromHands()
  {
    HeldWeapon &= ~HeldWeapon.PaperAirplane;
    ForgetTheft (HeldWeapon.PaperAirplane);
    DeselectUnheldWeapon();
  }

  // The punch branch calls this first (issue #102): a swing with any airplane in
  // reach - incoming, passing by, or even your own - grabs it out of the air
  // instead of punching. The thrower's peer owns the live flight, so the catch is
  // requested from them & the server hands the airplane over.
  // Pure for the unit tests (issue #427): is the target inside the facing cone?
  public static bool IsFacing (Vector3 lookDirection, Vector3 toTarget, float minDot) => toTarget.LengthSquared() > 0.000001f && lookDirection.Normalized().Dot (toTarget.Normalized()) >= minDot;

  private bool FacingAirplane (PaperAirplaneProjectile airplane) => IsFacing (AimDirection(), airplane.GlobalPosition - _camera.GlobalPosition, AirplaneCatchFacingMinDot);

  private bool TryCatchPaperAirplane()
  {
    if (TryGrabAirplane()) return true;
    // The open-swing window (issue #102) needs the eyes too (issue #427): a blind
    // swing must not become a catch when the flight's authority settles a mid-swing
    // impact against the replicated CatchingAirplane flag.
    if (!FacingAnyAirplane()) return false;
    // Nothing in reach yet, so the swing stays "open" briefly (issue #102): an
    // instantaneous proximity test made catching frame-perfect, since a loaded
    // frame can advance the glider more than a meter - a well-timed swing kept
    // landing the frame before the airplane arrived & became a plain punch.
    _catchWindowEndMs = Time.GetTicksMsec() + (ulong)(AirplaneCatchWindowSeconds * 1000.0f);
    CatchingAirplane = true;
    return false;
  }

  // An open swing keeps grabbing until its window lapses (issue #102): whichever
  // airplane flies into reach during it is caught, so catching depends on timing
  // the swing, not on which frame the glider happens to land in.
  private void UpdateAirplaneCatchWindow()
  {
    if (_catchWindowEndMs == 0 || !IsMultiplayerAuthority()) return;
    if (Time.GetTicksMsec() < _catchWindowEndMs && !TryGrabAirplane()) return;
    _catchWindowEndMs = 0;
    CatchingAirplane = false;
  }

  private bool TryGrabAirplane()
  {
    var airplane = FindCatchableAirplane();
    if (airplane?.Thrower == null) return false;
    _catchWindowEndMs = 0;
    CatchingAirplane = false;
    GD.Print ($"{DisplayName}: I snagged {airplane.Thrower.DisplayName}'s paper airplane out of the air!");
    _weaponPickupSound.Play(); // Satisfying grab chime, catcher-local (issue #123).
    if (airplane.ThrowerNetworkId == NetworkId) ReceiveAirplaneCatchRequest();
    else airplane.Thrower.RpcId (airplane.ThrowerNetworkId, MethodName.ReceiveAirplaneCatchRequest);
    return true;
  }

  private PaperAirplaneProjectile? FindCatchableAirplane()
  {
    foreach (var node in GetParent().GetChildren())
    {
      if (node is not PaperAirplaneProjectile airplane) continue;
      if (!FacingAirplane (airplane)) continue; // No blind grabs (issue #427).
      if (airplane.GlobalPosition.DistanceTo (GlobalPosition + Vector3.Up) <= AirplaneCatchRadiusMeters) return airplane;
    }

    return null;
  }

  private bool FacingAnyAirplane()
  {
    foreach (var node in GetParent().GetChildren())
      if (node is PaperAirplaneProjectile airplane && FacingAirplane (airplane)) return true;
    return false;
  }

  // Runs on the thrower's authority (issue #102): it owns the live flight & the
  // replicated HeldWeapon, so it ends the flight & asks the server to hand the
  // airplane to the catcher. Request BEFORE clearing (CodeRabbit on #145 & issue
  // #167), same as every drop path.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void ReceiveAirplaneCatchRequest()
  {
    if (!IsMultiplayerAuthority()) return;
    if (!IsAirplaneInFlight) return; // Already hit, landed, or caught by someone quicker.
    var senderId = Multiplayer.GetRemoteSenderId();
    var catcherId = senderId == 0 ? NetworkId : senderId; // 0 = own direct call (self-catch).
    var catcher = GetParent().GetNodeOrNull <Player> ($"{catcherId}");
    if (catcher == null) return;
    if (_liveAirplane!.GlobalPosition.DistanceTo (catcher.GlobalPosition + Vector3.Up) > AirplaneCatchSlackMeters) return;
    GD.Print ($"{DisplayName}: {catcher.DisplayName} caught my paper airplane!");
    GrantCaughtAirplaneTo (catcher);
  }

  // Ends the flight & hands the airplane over, on the flight's authority: both the
  // catcher's request & a mid-swing impact land here (issue #102).
  private void GrantCaughtAirplaneTo (Player catcher)
  {
    if (!IsAirplaneInFlight) return;
    Spawner.SendAirplaneCatchRequest (catcher.NetworkId);
    _liveAirplane!.QueueFree();
    _liveAirplane = null;
    Rpc (MethodName.FreeVisualAirplane);
    HeldWeapon &= ~HeldWeapon.PaperAirplane;
    ForgetTheft (HeldWeapon.PaperAirplane);
    DeselectUnheldWeapon();
  }

  // Zapping out (or falling off the world) mid-flight: the airplane comes down where
  // it was, & since it came down FROM FLIGHT it comes down armed (issue #191) - a
  // thrower who gets zapped out mid-glide leaves a landmine behind them, not a plain
  // pickup. It travels the same OnAirplaneLanded path every other flight end uses.
  private void ReleaseAirplaneInFlight()
  {
    if (!IsAirplaneInFlight) { _liveAirplane = null; return; }
    var position = _liveAirplane!.GlobalPosition;
    _liveAirplane.QueueFree();
    OnAirplaneLanded (position);
  }

  // The server confirmed the handoff actually committed (CodeRabbit): only then does
  // the thrower announce it, so a denied catch can never be broadcast as a real one.
  public void NotifyAirplaneCaught (string catcherName)
  {
    if (!IsMultiplayerAuthority()) return;
    EmitSignal (SignalName.AirplaneCaught, catcherName); // HUD catch announcement (issue #102).
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void FreeVisualAirplane()
  {
    if (!IsFromOwner()) return; // Only this player's own peer frees their copy (CodeRabbit).
    if (_visualAirplane == null || !IsInstanceValid (_visualAirplane)) return;
    _visualAirplane.QueueFree();
  }
}
