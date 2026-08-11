using System.Linq;
using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.players;

// Banana launcher (issue #61): weapon selection, firing the arcing banana, & the
// victim-authoritative blast RPC (AoE damage with falloff + knockback), mirroring
// the laser's shooter-reports/victim-applies model.
public partial class Player
{
  // Replicated so every peer renders the right weapon model on this player.
  [Export]
  public bool IsBananaEquipped
  {
    get => _isBananaEquipped;
    set
    {
      _isBananaEquipped = value;
      UpdateWeaponVisibility();
    }
  }

  [Signal] public delegate void SplatteredEventHandler();
  // 0..1 readiness for the HUD's Banana cooldown bar (1 = ready), see issue #70.
  public float BananaReadyFraction => _bananaLauncher.CooldownFraction;

  // Unarmed players show neither model - fists only (issue #72).
  private void UpdateWeaponVisibility()
  {
    if (_bananaLauncher == null) return;
    _bananaLauncher.Visible = HasBanana && _isBananaEquipped;
    _energyWeapon.Visible = HasLaser && !_isBananaEquipped;
    UpdateHandRestPositions(); // Hands follow the visible weapon's grip (issue #71).
  }

  // Only weapons actually held can be selected (issue #72).
  private void UpdateWeaponSelection()
  {
    if (!_isInputEnabled) return;
    if (Input.IsActionJustPressed ("weapon_1") && HasLaser) IsBananaEquipped = false;
    if (Input.IsActionJustPressed ("weapon_2") && HasBanana && _bananaLauncher.CanFire) IsBananaEquipped = true;
  }

  private void UpdateBananaLauncher()
  {
    if (!IsBananaEquipped || !HasBanana || !_isInputEnabled) return;
    if (!Input.IsActionJustPressed ("shoot")) return;
    if (!_bananaLauncher.CanFire) return;
    FireBanana();
  }

  private void FireBanana()
  {
    CancelSpawnArmorIfFired();
    _bananaLauncher.StartCooldown();
    var direction = -_camera.GlobalTransform.Basis.Z;
    var origin = _camera.GlobalPosition + direction * 0.9f;
    SpawnBanana (origin, direction, isLive: true);
    Rpc (MethodName.SpawnVisualBanana, origin, direction);
    IsBananaEquipped = false; // Single use: auto-switch back to the laser during cooldown.
    // The same shoot press must not also fire a full-auto laser this frame.
    _nextAutoShotIn = FullAutoShotIntervalSeconds;
  }

  // Visual-only copy of the shooter's banana on every other peer.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void SpawnVisualBanana (Vector3 origin, Vector3 direction) => SpawnBanana (origin, direction, isLive: false);

  private void SpawnBanana (Vector3 origin, Vector3 direction, bool isLive)
  {
    var banana = _bananaProjectileScene.Instantiate <BananaProjectile>();
    GetParent().AddChild (banana);
    banana.Launch (origin, direction, isLive, this);
    if (isLive) banana.Exploded += OnBananaExploded;
  }

  private void OnBananaExploded (Vector3 blastOrigin)
  {
    foreach (var victim in GetParent().GetChildren().OfType <Player>()) ReportBlast (victim, blastOrigin);
  }

  // The shooter only reports the blast; each victim applies its own damage &
  // knockback (victim-authoritative, same as ReceiveHit).
  private void ReportBlast (Player victim, Vector3 blastOrigin)
  {
    if (victim.NetworkId == NetworkId) return;
    var energy = BlastEnergyAt (victim.GlobalPosition.DistanceTo (blastOrigin));
    if (energy <= 0.0f) return;
    GD.Print ($"{DisplayName}: My banana blasted {victim.DisplayName}!");
    _hitmarkerSound.Play();
    victim.RpcId (victim.NetworkId, MethodName.ReceiveBlast, blastOrigin, energy, DisplayName);
  }

  // Full energy inside the direct radius, falling off linearly to zero at the
  // blast radius edge.
  private float BlastEnergyAt (float distance)
  {
    if (distance >= BananaBlastRadius) return 0.0f;
    if (distance <= BananaDirectRadius) return BananaBlastEnergy;
    return BananaBlastEnergy * (1.0f - (distance - BananaDirectRadius) / (BananaBlastRadius - BananaDirectRadius));
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void ReceiveBlast (Vector3 blastOrigin, float energy, string firedByPlayerName)
  {
    if (!IsMultiplayerAuthority()) return;
    if (SpawnArmor) return;
    GD.Print ($"{DisplayName}: I was blasted by {firedByPlayerName}'s banana!");
    LastDamageKind = DamageKind.Banana; // Message context (issue #84).
    ApplyBananaStun(); // Flat 5s stun synced with the splatter overlay (issue #70).
    EmitSignal (SignalName.Splattered);
    ApplyBlastKnockback (blastOrigin);
    // Blast knockback is applied radially above; no directional knockback on top.
    ApplyDamage (energy, firedByPlayerName, knockbackScale: 0.0f, isSurvivableAtFullHealth: true);
  }

  private void ApplyBlastKnockback (Vector3 blastOrigin)
  {
    var away = (GlobalPosition - blastOrigin).Normalized();
    Velocity += (away + Vector3.Up * 0.5f).Normalized() * BananaKnockbackSpeed;
  }
}
