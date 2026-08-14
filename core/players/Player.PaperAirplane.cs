using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.players;

// Paper airplane (issue #102): the slot-6 homing glider. Thrown with left click, it
// locks onto the player under the crosshair & banks slowly toward them. The
// signature mechanic: the target (or anyone in its path) can punch the incoming
// airplane to catch it - it goes straight into their hand & they can throw it back.
// An uncaught hit deals moderate victim-authoritative damage with modest knockback,
// then the airplane lands as a pickup nearby. Exactly one airplane exists in the
// game (see WeaponSpawner).
public partial class Player
{
  [Export] public float PaperAirplaneEnergy = 0.3f;
  [Export] public float PaperAirplaneKnockbackScale = 0.5f;
  // Punch-catch reach (issue #102): the swing grabs any airplane this close.
  // Matches PunchRange (issue #71): if your fist reaches a player at 4m it reaches
  // an airplane at 4m. The catch is the fun part & the glider closes a meter every
  // ~0.1s, so anything tighter made well-timed catches lose the race to the hit -
  // at the old 3m a catch had to land inside a single frame's worth of travel.
  [Export] public float AirplaneCatchRadiusMeters = 4.0f;
  // Server-side-of-the-thrower slack for the catch check: the catcher's punch was
  // validated on their own peer; this only rejects wildly stale/forged requests.
  [Export] public float AirplaneCatchSlackMeters = 6.0f;
  [Signal] public delegate void AirplaneCaughtEventHandler (string catcherName);
  private PaperAirplaneProjectile? _liveAirplane;
  private PaperAirplaneProjectile? _visualAirplane;
  private Node3D _airplaneHeld = null!;
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

  // The target locks at throw time (issue #102): whoever is under the crosshair.
  // With nobody aimed, the airplane just glides straight & lands as a pickup.
  private void ThrowPaperAirplane()
  {
    CancelSpawnArmorIfFired();
    var target = FindAimedPlayer (200.0f);
    var direction = -_camera.GlobalTransform.Basis.Z;
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
    ClearArmorDisplayOnRemoteAttack();
    var target = targetId == 0 ? null : GetParent().GetNodeOrNull <Player> ($"{targetId}");
    _visualAirplane = SpawnAirplane (origin, direction, isLive: false, target);
    _visualAirplane.TreeExited += OnVisualAirplaneGone;
    UpdateWeaponVisibility();
  }

  private void OnVisualAirplaneGone()
  {
    _visualAirplane = null;
    if (IsInsideTree()) UpdateWeaponVisibility();
  }

  private PaperAirplaneProjectile SpawnAirplane (Vector3 origin, Vector3 direction, bool isLive, Player? target)
  {
    var airplane = new PaperAirplaneProjectile();
    GetParent().AddChild (airplane);
    airplane.Launch (origin, direction, isLive, this, target);
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
    GD.Print ($"{DisplayName}: My paper airplane found {victim.DisplayName}!");
    _hitmarkerSound.Play();
    ReportToServer ($"paper airplane: {DisplayName} hit {victim.DisplayName}");
    victim.RpcId (victim.NetworkId, MethodName.ReceivePaperAirplaneHit, DisplayName);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void ReceivePaperAirplaneHit (string thrownByPlayerName)
  {
    if (!IsMultiplayerAuthority()) return;
    if (SpawnArmor) return;
    GD.Print ($"{DisplayName}: I was paper-cut by {thrownByPlayerName}'s airplane!");
    LastDamageKind = DamageKind.PaperAirplane; // Message context (issue #84).
    // Moderate & survivable by the numbers (30 base, issue #102), modest knockback.
    ApplyDamage (PaperAirplaneEnergy, thrownByPlayerName, PaperAirplaneKnockbackScale);
  }

  // The flight ended (a hit, geometry, or the flutter reaching ground): the airplane
  // becomes a pickup where it stopped. Request BEFORE clearing (CodeRabbit on #145 &
  // issue #167): the server validates the drop mask against this player's replicated
  // HeldWeapon & grounds the spot onto the level beneath (issue #151).
  private void OnAirplaneLanded (Vector3 position)
  {
    _liveAirplane = null;
    Rpc (MethodName.FreeVisualAirplane); // Visual copies may lag the landing slightly.
    Spawner.SendDropRequest (position, HeldWeapon.PaperAirplane);
    HeldWeapon &= ~HeldWeapon.PaperAirplane;
    ForgetTheft (HeldWeapon.PaperAirplane);
    DeselectUnheldWeapon();
    GD.Print ($"{DisplayName}: My paper airplane landed!");
  }

  // The punch branch calls this first (issue #102): a swing with any airplane in
  // reach - incoming, passing by, or even your own - grabs it out of the air
  // instead of punching. The thrower's peer owns the live flight, so the catch is
  // requested from them & the server hands the airplane over.
  private bool TryCatchPaperAirplane()
  {
    var airplane = FindCatchableAirplane();
    if (airplane?.Thrower == null) return false;
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
      if (airplane.GlobalPosition.DistanceTo (GlobalPosition + Vector3.Up) <= AirplaneCatchRadiusMeters) return airplane;
    }

    return null;
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
    Spawner.SendAirplaneCatchRequest (catcherId);
    EmitSignal (SignalName.AirplaneCaught, catcher.DisplayName); // HUD catch announcement (issue #102).
    _liveAirplane.QueueFree();
    _liveAirplane = null;
    Rpc (MethodName.FreeVisualAirplane);
    HeldWeapon &= ~HeldWeapon.PaperAirplane;
    ForgetTheft (HeldWeapon.PaperAirplane);
    DeselectUnheldWeapon();
  }

  // Zapping out (or falling off the world) mid-flight: the airplane lands as a
  // pickup wherever it currently is, same as the boomerang release (issue #98).
  private void ReleaseAirplaneInFlight()
  {
    if (!IsAirplaneInFlight) { _liveAirplane = null; return; }
    var position = _liveAirplane!.GlobalPosition;
    _liveAirplane.QueueFree();
    OnAirplaneLanded (position);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void FreeVisualAirplane()
  {
    if (_visualAirplane == null || !IsInstanceValid (_visualAirplane)) return;
    _visualAirplane.QueueFree();
  }
}
