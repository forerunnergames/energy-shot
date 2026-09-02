using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.players;

// Boomerang (issue #98): the slot-4 throwable that curves out, comes back, & is
// auto-caught on proximity. Hits en route deal moderate victim-authoritative damage
// (non-lethal from full health, like the banana blast) & steal the victim's held
// weapon; world pickups on the flight path get scooped. Stolen & scooped cargo rides
// home on the boomerang: the server holds it in escrow & grants it to the thrower on
// the catch, with previousOwner set so the theft-revenge messages (issue #84) fire.
public partial class Player
{
  [Export] public float BoomerangEnergy = 0.4f;
  [Export] public float BoomerangKnockbackScale = 0.5f;
  private BoomerangProjectile? _liveBoomerang;
  private BoomerangProjectile? _visualBoomerang;
  private Node3D _boomerangHeld = null!;
  // Playtest-observable (issue #98): true from the throw until the catch or loss.
  public bool IsBoomerangInFlight => _liveBoomerang != null && IsInstanceValid (_liveBoomerang);
  // Every peer renders this player's empty hand while their boomerang is out flying.
  private bool IsBoomerangOut => IsBoomerangInFlight || (_visualBoomerang != null && IsInstanceValid (_visualBoomerang));

  // Held model: the same crossed-arms visual as the projectile, resting in the hand.
  private void CreateBoomerangHeld()
  {
    _boomerangHeld = BoomerangProjectile.CreateVisual();
    _boomerangHeld.Position = new Vector3 (0.5f, -0.4f, -0.9f);
    _boomerangHeld.RotationDegrees = new Vector3 (0.0f, 15.0f, 75.0f);
    GetNode <Node3D> ("Camera3D").AddChild (_boomerangHeld);
  }

  private void UpdateBoomerang()
  {
    if (!IsBoomerangSelected || !HasBoomerang || !_isInputEnabled || Dancing) return; // Dancing blocks throwing (issue #103).
    if (IsBoomerangOut || !Input.IsActionJustPressed ("shoot")) return;
    ThrowBoomerang();
  }

  private void ThrowBoomerang()
  {
    CancelSpawnArmorIfFired();
    var direction = ShotDirection(); // Converged in third person (issue #338).
    var origin = _camera.GlobalPosition + direction * 0.9f;
    _liveBoomerang = SpawnBoomerang (origin, direction, isLive: true);
    Rpc (MethodName.SpawnVisualBoomerang, origin, direction);
    UpdateWeaponVisibility(); // The hand empties while the boomerang is out.
    // The same shoot press must not also fire a full-auto laser this frame.
    _nextAutoShotIn = FullAutoShotIntervalSeconds;
  }

  // Visual-only copy of the thrower's boomerang on every other peer. Throwing proves
  // the thrower's spawn armor is gone, so stale armor whitewash clears here (issue #114).
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void SpawnVisualBoomerang (Vector3 origin, Vector3 direction)
  {
    ClearArmorDisplayOnRemoteAttack();
    _visualBoomerang = SpawnBoomerang (origin, direction, isLive: false);
    _visualBoomerang.TreeExited += OnVisualBoomerangGone;
    UpdateWeaponVisibility();
  }

  private void OnVisualBoomerangGone()
  {
    _visualBoomerang = null;
    if (IsInsideTree()) UpdateWeaponVisibility(); // The caught boomerang reappears in the hand.
  }

  private BoomerangProjectile SpawnBoomerang (Vector3 origin, Vector3 direction, bool isLive)
  {
    var boomerang = new BoomerangProjectile();
    GetParent().AddChild (boomerang);
    boomerang.Launch (origin, direction, isLive, this);
    if (!isLive) return boomerang;
    boomerang.HitPlayer += OnBoomerangHitPlayer;
    boomerang.ScoopedPickup += OnBoomerangScoopedPickup;
    boomerang.Caught += OnBoomerangCaught;
    boomerang.Lost += OnBoomerangLost;
    return boomerang;
  }

  // The thrower only reports the clip; the victim applies its own damage & theft
  // (victim-authoritative, same as ReceiveHit & ReceiveBlast).
  private void OnBoomerangHitPlayer (Player victim)
  {
    GD.Print ($"{DisplayName}: My boomerang clipped {victim.DisplayName}!");
    _hitmarkerSound.Play();
    ReportToServer ($"boomerang: {DisplayName} clipped {victim.DisplayName}");
    victim.RpcId (victim.NetworkId, MethodName.ReceiveBoomerangHit, DisplayName);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void ReceiveBoomerangHit (string thrownByPlayerName)
  {
    if (!IsMultiplayerAuthority()) return;
    if (SpawnArmor) return;
    if (Fallen) return; // A body mid-death-sequence is scenery: nothing left to steal or hurt (issue #152).
    var throwerId = Multiplayer.GetRemoteSenderId();
    GD.Print ($"{DisplayName}: I was clipped by {thrownByPlayerName}'s boomerang!");
    LastDamageKind = DamageKind.Boomerang; // Message context (issue #84).
    SurrenderWeaponToBoomerang (throwerId);
    // A boomerang never zaps out a full-health player, like the banana blast (issue #98).
    ApplyDamage (BoomerangEnergy, thrownByPlayerName, BoomerangKnockbackScale, isSurvivableAtFullHealth: true);
  }

  // The theft half of the hit (issue #98): the victim owns its HeldWeapon, so it
  // releases the weapon itself & files it into the server's boomerang escrow, bound
  // for the thrower. An unarmed victim (or one whose only weapon is a boomerang
  // that's out flying) loses nothing.
  // Pure & unit-tested (issue #246): the boomerang takes what the victim has in HAND -
  // a selected loaf counts as equipped - & falls back to the loaf when nothing else is left.
  public static HeldWeapon BoomerangLoot (HeldWeapon droppable, bool breadSelected, bool hasBread)
  {
    if (breadSelected && hasBread) return HeldWeapon.Bread;
    return droppable != HeldWeapon.None ? droppable : hasBread ? HeldWeapon.Bread : HeldWeapon.None;
  }

  private void SurrenderWeaponToBoomerang (int throwerId)
  {
    var type = BoomerangLoot (PickDroppableWeapon(), IsBreadSelected, HasBread);
    if (type == HeldWeapon.None) return;
    // Request BEFORE clearing, like DropHeldWeapon (issue #154): clearing first let
    // the replicated HeldWeapon delta beat the escrow RPC to the server, & a
    // reconcile pass in that window counted the weapon as gone & spawned a
    // duplicate - the suspected banana-launcher double.
    Spawner.SendStolenEscrowRequest (throwerId, type);
    if (type == HeldWeapon.Bread) SetBreadHeld (isHeld: false); else HeldWeapon &= ~type; // The loaf keeps its own bookkeeping (issue #62).
    ForgetTheft (type);
    DeselectUnheldWeapon();
    GD.Print ($"{DisplayName}: That boomerang made off with my {type}!");
  }

  private void OnBoomerangScoopedPickup (string pickupName) => Spawner.SendScoopRequest (pickupName);

  private void OnBoomerangCaught()
  {
    _liveBoomerang = null;
    Rpc (MethodName.FreeVisualBoomerang); // Visual copies may lag the catch slightly.
    _weaponPickupSound.Play(); // Satisfying catch chime, owner-local (issue #123).
    GD.Print ($"{DisplayName}: I caught my boomerang!");
    UpdateWeaponVisibility();
    Spawner.SendBoomerangCatchRequest(); // The server grants any escrowed cargo (issue #98).
  }

  // The boomerang couldn't complete the trip (safety timeout): it drops as a pickup
  // where it is, along with any cargo it was carrying (issue #98).
  private void OnBoomerangLost (Vector3 position)
  {
    _liveBoomerang = null;
    Rpc (MethodName.FreeVisualBoomerang);
    // Request BEFORE clearing, like DropHeldWeapon (issue #154): the release RPC
    // must reach the server while its replicated view still shows the boomerang
    // held, or a reconcile pass in the gap spawns a duplicate.
    Spawner.SendBoomerangReleaseRequest (position);
    HeldWeapon &= ~HeldWeapon.Boomerang;
    DeselectUnheldWeapon();
  }

  // Zapping out (or falling off the world) mid-flight: the boomerang & its cargo
  // drop as pickups wherever the boomerang currently is (issue #98).
  private void ReleaseBoomerangInFlight()
  {
    if (!IsBoomerangInFlight) { _liveBoomerang = null; return; }
    var position = _liveBoomerang!.GlobalPosition;
    _liveBoomerang.QueueFree();
    OnBoomerangLost (position);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void FreeVisualBoomerang()
  {
    if (_visualBoomerang == null || !IsInstanceValid (_visualBoomerang)) return;
    _visualBoomerang.QueueFree();
  }
}
