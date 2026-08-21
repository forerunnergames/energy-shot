using System.Linq;
using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.players;

// Banana launcher (issues #61 & #83): firing the arcing banana with heavy shooter
// recoil & knockback, the victim-authoritative blast RPC (AoE damage with falloff +
// knockback, self included), & the sticky direct hit that pins a banana to the victim,
// launches them across the level, & detonates for one-hit-kill damage.
public partial class Player
{
  [Signal] public delegate void SplatteredEventHandler();
  // 0..1 readiness for the HUD's Banana cooldown bar (1 = ready), see issue #70.
  public float BananaReadyFraction => _bananaLauncher.CooldownFraction;
  private Tween? _launcherRecoilTween;
  private Vector3 _launcherRestPosition;
  private float _stickyFlightSecondsLeft;

  private void UpdateBananaLauncher()
  {
    if (!IsBananaSelected || !HasBanana || !_isInputEnabled || Dancing) return; // Dancing blocks firing (issue #103).
    if (!Input.IsActionJustPressed ("shoot")) return;
    if (!_bananaLauncher.CanFire) return;
    FireBanana();
  }

  // No auto-switch after firing (issue #83): the banana stays selected through its
  // cooldown, it just can't fire again yet.
  private void FireBanana()
  {
    CancelSpawnArmorIfFired();
    _bananaLauncher.StartCooldown();
    var direction = -_camera.GlobalTransform.Basis.Z;
    var origin = _camera.GlobalPosition + direction * 0.9f;
    SpawnBanana (origin, direction, isLive: true);
    Rpc (MethodName.SpawnVisualBanana, origin, direction);
    ApplyBananaFiringFeel (direction);
    // The same shoot press must not also fire a full-auto laser this frame.
    _nextAutoShotIn = FullAutoShotIntervalSeconds;
  }

  // Launching a banana shoves the shooter backward & kicks the camera & launcher
  // ridiculously hard (issue #83).
  private void ApplyBananaFiringFeel (Vector3 aimDirection)
  {
    Velocity -= aimDirection * BananaShooterKnockbackSpeed;
    _camera.RotateX (BananaRecoilRadians);
    _cameraKickRemaining += BananaRecoilRadians;
    AnimateLauncherRecoil();
  }

  private void AnimateLauncherRecoil()
  {
    _launcherRecoilTween?.Kill();
    _bananaLauncher.Position = _launcherRestPosition;
    var tween = _bananaLauncher.CreateTween();
    tween.TweenProperty (_bananaLauncher, "position", _launcherRestPosition + new Vector3 (0.0f, 0.3f, 0.9f), 0.08f).SetTrans (Tween.TransitionType.Quad).SetEase (Tween.EaseType.Out);
    tween.TweenProperty (_bananaLauncher, "position", _launcherRestPosition, 0.5f).SetTrans (Tween.TransitionType.Quad).SetEase (Tween.EaseType.In);
    _launcherRecoilTween = tween;
  }

  // Visual-only copy of the shooter's banana on every other peer, with the launcher
  // thump heard positionally from the shooter's location. Firing proves the shooter's
  // spawn armor is gone, so stale armor whitewash clears here too (issue #114).
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void SpawnVisualBanana (Vector3 origin, Vector3 direction)
  {
    ClearArmorDisplayOnRemoteAttack();
    SpawnBanana (origin, direction, isLive: false);
  }

  private void SpawnBanana (Vector3 origin, Vector3 direction, bool isLive)
  {
    _bananaLauncher.PlayFireSound();
    var banana = _bananaProjectileScene.Instantiate <BananaProjectile>();
    GetParent().AddChild (banana);
    banana.Launch (origin, direction, isLive, this);
    if (!isLive) return;
    banana.Exploded += OnBananaExploded;
    banana.StuckToPlayer += OnBananaStuck;
    banana.CaughtBySlingshot += OnBananaCaught; // A drawn slingshot can catch it (issue #251).
  }

  private void OnBananaExploded (Vector3 blastOrigin)
  {
    foreach (var victim in GetParent().GetChildren().OfType <Player>()) ReportBlast (victim, blastOrigin);
  }

  // The shooter only reports the blast; each victim applies its own damage &
  // knockback (victim-authoritative, same as ReceiveHit). The shooter's own blast
  // hurts, shakes, & knocks back the shooter too (issue #83), via the same path.
  private void ReportBlast (Player victim, Vector3 blastOrigin)
  {
    var energy = BlastEnergyAt (victim.GlobalPosition.DistanceTo (blastOrigin));
    if (energy <= 0.0f) return;

    if (victim.NetworkId == NetworkId)
    {
      ReceiveBlast (blastOrigin, energy, DisplayName);
      return;
    }

    GD.Print ($"{DisplayName}: My banana blasted {victim.DisplayName}!");
    _hitmarkerSound.Play();
    ReportToServer ($"blast: {DisplayName} banana-blasted {victim.DisplayName} (energy {energy:0.00})");
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
    if (Fallen) return; // A body mid-death-sequence is scenery (issue #152).
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

  // A direct hit stuck to a victim (issue #83): pin the banana to their body on every
  // peer, launch them across the level, & schedule the one-hit-kill detonation.
  private void OnBananaStuck (Player victim, Vector3 hitPosition)
  {
    GD.Print ($"{DisplayName}: My banana stuck to {victim.DisplayName}!");
    _hitmarkerSound.Play();
    var localOffset = victim.ToLocal (hitPosition);
    Rpc (MethodName.AttachStickyBanana, victim.NetworkId, localOffset);
    AttachStickyBanana (victim.NetworkId, localOffset);
    var launchDirection = (victim.GlobalPosition - GlobalPosition).Normalized();
    victim.RpcId (victim.NetworkId, MethodName.ReceiveStickyBanana, launchDirection, DisplayName);
  }

  // Runs on every peer: shows the banana pinned to the victim's body, then detonates
  // it after the sticky fuse. Only the shooter's own instance reports AoE damage.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private async void AttachStickyBanana (int victimId, Vector3 localOffset)
  {
    var victim = GetParent().GetNodeOrNull <Player> ($"{victimId}");
    if (victim == null) return;
    var banana = CreateStickyBananaMesh();
    victim.AddChild (banana);
    banana.Position = localOffset;
    await ToSignal (GetTree().CreateTimer (StickyBananaSeconds), SceneTreeTimer.SignalName.Timeout);
    if (!IsInstanceValid (this) || !IsInsideTree() || !IsInstanceValid (banana) || !banana.IsInsideTree()) return;
    var origin = banana.GlobalPosition;
    banana.QueueFree();
    BananaProjectile.SpawnExplosionEffects (GetParent(), origin); // Level-shaking flash + debris on every peer.
    if (!IsMultiplayerAuthority()) return; // Only the shooter reports damage.
    ReportStickyBlast (origin, victimId);
  }

  private static MeshInstance3D CreateStickyBananaMesh() => new()
  {
    Mesh = ResourceLoader.Load <Mesh> ("res://assets/weapons/banana.obj"),
    MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color (0.92f, 0.78f, 0.12f), Roughness = 0.5f }
  };

  // AoE for everyone in radius except the stuck victim, who takes the fixed
  // one-hit-kill damage via ReceiveStickyBanana instead (issue #83).
  private void ReportStickyBlast (Vector3 blastOrigin, int victimId)
  {
    foreach (var victim in GetParent().GetChildren().OfType <Player>())
    {
      if (victim.NetworkId == victimId) continue;
      ReportBlast (victim, blastOrigin);
    }
  }

  // The stuck victim rockets ridiculously far, then takes unclamped damage that
  // one-hit-kills an Expert when the banana detonates (issue #83).
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private async void ReceiveStickyBanana (Vector3 launchDirection, string firedByPlayerName)
  {
    if (!IsMultiplayerAuthority()) return;
    if (SpawnArmor) return;
    if (Fallen) return; // A body mid-death-sequence can't be launched (issue #152); a pre-death sticky still detonates on it harmlessly.
    var attackerId = Multiplayer.GetRemoteSenderId(); // Captured now - the RPC context is gone after the fuse.
    GD.Print ($"{DisplayName}: {firedByPlayerName}'s banana stuck to me!");
    ApplyBananaStun();
    EmitSignal (SignalName.Splattered);
    _stickyFlightSecondsLeft = StickyBananaSeconds; // Momentum owns the ride until the boom.
    Velocity = (launchDirection + Vector3.Up * 0.5f).Normalized() * StickyLaunchSpeed;
    await ToSignal (GetTree().CreateTimer (StickyBananaSeconds), SceneTreeTimer.SignalName.Timeout);
    if (!IsInstanceValid (this) || !IsInsideTree()) return;
    ApplyDamageFrom (attackerId, StickyBananaEnergy, firedByPlayerName, knockbackScale: 0.0f); // No survivable clamp for the stuck victim.
  }

  private void UpdateStickyFlight (double delta) => _stickyFlightSecondsLeft = Mathf.Max (0.0f, _stickyFlightSecondsLeft - (float)delta);
}
