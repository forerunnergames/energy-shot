using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.players;

// Combat: energy weapon & laser bolts, full-auto ability, punching, spawn armor,
// camera kick, crosshair tint, & the hit/damage/score RPCs.
public partial class Player
{
  private bool IsFullAutoActive() => _fullAutoSecondsLeft > 0.0f;
  // 0..1 readiness fractions for the HUD cooldown bars (1 = ready).
  public float ShotReadyFraction => _energyWeapon.CooldownFraction;
  public float PunchReadyFraction => 1.0f - _punchCooldownLeft / PunchCooldownSeconds;
  public float FullAutoReadyFraction => 1.0f - _fullAutoCooldownLeft / FullAutoCooldownSeconds;
  private bool IsChargingWeapon() => _isInputEnabled && !IsBananaEquipped && !IsFullAutoActive() && Input.IsActionPressed ("shoot");
  private bool IsDischargingWeapon() => _isInputEnabled && !IsBananaEquipped && _energyWeapon.IsSpinningUp && Input.IsActionJustReleased ("shoot");
  private void ChargeWeapon() => _energyWeapon.Charge();
  private void DischargeWeapon() => _energyWeapon.Discharge();
  private static int CalculateHealthDecrease (float energyShot) => Mathf.Min (100, Mathf.RoundToInt (energyShot * 100.0f));

  // Casts a ray from the camera along the aim direction, ignoring ourselves, &
  // returns the player it hits (or null).
  private Player? FindAimedPlayer (float range)
  {
    var from = _camera.GlobalPosition;
    var to = from + -_camera.GlobalTransform.Basis.Z * range;
    var query = PhysicsRayQueryParameters3D.Create (from, to, exclude: new Godot.Collections.Array <Rid> { GetRid() });
    var hit = GetWorld3D().DirectSpaceState.IntersectRay (query);
    if (hit.Count == 0) return null;
    return hit["collider"].AsGodotObject() as Player;
  }

  // Crosshair stays white until it's actually over another player.
  private void UpdateCrosshairTint()
  {
    if (!IsMultiplayerAuthority()) return;
    _crossHairs.Modulate = FindAimedPlayer (200.0f) != null ? Colors.Red : Colors.White;
  }

  private void ActivateSpawnArmor()
  {
    SpawnArmor = true;
    _spawnArmorEndMs = Time.GetTicksMsec() + (ulong)(SpawnArmorSeconds * 1000.0f);
  }

  private void CancelSpawnArmorIfFired()
  {
    if (!SpawnArmor) return;
    SpawnArmor = false;
    GD.Print ($"{DisplayName}: Spawn armor canceled by firing");
  }

  private void UpdateSpawnArmor()
  {
    if (!SpawnArmor || Time.GetTicksMsec() < _spawnArmorEndMs) return;
    SpawnArmor = false;
    GD.Print ($"{DisplayName}: Spawn armor expired");
  }

  private void UpdateFullAuto (double delta)
  {
    var dt = (float)delta;
    var wasRecharging = _fullAutoCooldownLeft > 0.0f;
    _fullAutoCooldownLeft = Mathf.Max (0.0f, _fullAutoCooldownLeft - dt);
    if (wasRecharging && _fullAutoCooldownLeft <= 0.0f) _energyWeapon.PlayFullAutoReadySound();

    if (_isInputEnabled && Input.IsActionJustPressed ("ability") && _fullAutoCooldownLeft <= 0.0f)
    {
      _fullAutoSecondsLeft = FullAutoDurationSeconds;
      _fullAutoCooldownLeft = FullAutoCooldownSeconds;
      _nextAutoShotIn = 0.0f;
      _energyWeapon.SetFullAutoMode (true); // Red gun + switch sound (#58).
    }

    if (!IsFullAutoActive()) return;
    _fullAutoSecondsLeft -= dt;
    if (_fullAutoSecondsLeft <= 0.0f) _energyWeapon.SetFullAutoMode (false);
    _nextAutoShotIn -= dt;
    if (!_isInputEnabled || IsBananaEquipped || !Input.IsActionPressed ("shoot") || _nextAutoShotIn > 0.0f) return;
    _nextAutoShotIn = FullAutoShotIntervalSeconds;
    _energyWeapon.FireLowPower (FullAutoEnergy);
  }

  private void UpdatePunch (double delta)
  {
    _punchCooldownLeft = Mathf.Max (0.0f, _punchCooldownLeft - (float)delta);
    if (!_isInputEnabled || _punchCooldownLeft > 0.0f || !Input.IsActionJustPressed ("punch")) return;
    _punchCooldownLeft = PunchCooldownSeconds;
    CancelSpawnArmorIfFired(); // Punching drops your spawn armor, same as firing.
    var hand = ChooseRandomPunchHand();
    AnimatePunch (hand);
    Rpc (MethodName.PlayRemotePunch, hand);
    var victim = FindAimedPlayer (PunchRange);
    if (victim == null) return;
    _punchSound.Play(); // Connect-only (issue #71): no sound on a whiffed swing.
    GD.Print ($"{DisplayName}: I punched {victim.DisplayName}!");
    victim.RpcId (victim.NetworkId, MethodName.ReceivePunch, DisplayName);
  }

  private void UpdateCameraKick (double delta)
  {
    if (_cameraKickRemaining <= 0.0f) return;
    var recover = Mathf.Min (_cameraKickRemaining, CameraKickRecoverySpeed * (float)delta);
    _camera.RotateX (-recover);
    _cameraKickRemaining -= recover;
  }

  private void OnWeaponShotFired (float energy)
  {
    CancelSpawnArmorIfFired();
    // Aim direction is captured before the camera kick so the kick is purely visual.
    var direction = -_camera.GlobalTransform.Basis.Z;
    var kick = energy * CameraKickRadians;
    _camera.RotateX (kick);
    _cameraKickRemaining += kick;
    TryRocketBoost (direction, energy);
    var origin = _camera.GlobalPosition + direction * 0.9f;
    SpawnBolt (origin, direction, energy, isLive: true);
    Rpc (MethodName.SpawnVisualLaser, origin, direction, energy);
  }

  // Firing at the ground close beneath you rocket-boosts you upward, scaling with
  // charge (see issue #56).
  private void TryRocketBoost (Vector3 direction, float energy)
  {
    if (direction.Y >= 0.0f) return;
    var from = _camera.GlobalPosition;
    var query = PhysicsRayQueryParameters3D.Create (from, from + direction * RocketBoostRange, exclude: new Godot.Collections.Array <Rid> { GetRid() });
    var hit = GetWorld3D().DirectSpaceState.IntersectRay (query);
    if (hit.Count == 0) return;
    if (hit["collider"].AsGodotObject() is CharacterBody3D) return; // Ground only, not players.
    if (hit["normal"].AsVector3().Y < 0.5f) return; // Floors only - walls don't launch you.
    Velocity = new Vector3 (Velocity.X, JumpVelocity * RocketBoostMultiplier * energy, Velocity.Z);
  }

  // Visual-only copy of the shooter's bolt on every other peer.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void SpawnVisualLaser (Vector3 origin, Vector3 direction, float energy) => SpawnBolt (origin, direction, energy, isLive: false);

  private void SpawnBolt (Vector3 origin, Vector3 direction, float energy, bool isLive)
  {
    var bolt = _laserBoltScene.Instantiate <LaserBolt>();
    GetParent().AddChild (bolt);
    bolt.Launch (origin, direction, energy, isLive, this);
    if (isLive) bolt.HitPlayer += OnLaserHitPlayer;
  }

  private void OnLaserHitPlayer (CharacterBody3D body, float energy)
  {
    if (body is not Player victim || victim.NetworkId == NetworkId) return;
    HitPuppet (victim, energy);
  }

  // The victim is the single owner of its health: the shooter only reports the hit,
  // the victim applies damage & replicates Health to everyone, & tells the shooter
  // when it scored a kill (see issue #20).
  private void HitPuppet (Player playerPuppet, float energy)
  {
    GD.Print ($"{DisplayName}: I hit {playerPuppet.DisplayName}!");
    _hitmarkerSound.Play();
    playerPuppet.RpcId (playerPuppet.NetworkId, MethodName.ReceiveHit, energy, DisplayName);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void ReceiveHit (float energy, string shotByPlayerName)
  {
    if (!IsMultiplayerAuthority()) return;
    if (SpawnArmor) return;
    GD.Print ($"{DisplayName}: I was hit by {shotByPlayerName}!");
    ApplyDamage (energy, shotByPlayerName, knockbackScale: 1.0f);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void ReceivePunch (string punchedByPlayerName)
  {
    if (!IsMultiplayerAuthority()) return;
    if (SpawnArmor) return;
    GD.Print ($"{DisplayName}: I was punched by {punchedByPlayerName}!");
    _punchSound.Play(); // The victim hears the connect too (issue #71).
    ApplyPunchStun(); // Stacking slow; the blur stacks HUD-side via Punched (issues #68 & #71).
    TryDropWeaponFromPunch();
    EmitSignal (SignalName.Punched);
    ApplyDamage (PunchEnergy, punchedByPlayerName, PunchKnockbackScale);
  }

  // 20% chance a connect knocks the victim's weapon loose (issue #71). The pickup/drop
  // system lands in another branch; until DropHeldWeapon() exists, this just logs.
  private void TryDropWeaponFromPunch()
  {
    if (_rng.Randf() >= PunchDropChance) return;

    if (!HasMethod ("DropHeldWeapon"))
    {
      GD.Print ($"{DisplayName}: That punch nearly knocked my weapon loose!");
      return;
    }

    Call ("DropHeldWeapon");
  }

  // One-per-life full heal (issue #62): restocked on every (re)spawn.
  private void UpdateBread()
  {
    if (!_isInputEnabled || !Input.IsActionJustPressed ("eat_bread")) return;
    if (Health >= MaxHealth) return; // Don't waste the bread at full health.
    if (!_bread.TryEat()) return;
    Health = MaxHealth;
    GD.Print ($"{DisplayName}: I ate my bread & feel brand new!");
    EmitSignal (SignalName.BreadEaten, DisplayName);
    EmitSignal (SignalName.HealthChanged, Health);
  }

  private void ApplyDamage (float energy, string attackerName, float knockbackScale, bool isSurvivableAtFullHealth = false)
  {
    var attackerId = Multiplayer.GetRemoteSenderId();
    // Difficulty damage handicap: lower-skill attackers hit higher-skill targets harder
    // (+50% per tier gap: Beginner->Intermediate 1.5x, Beginner->Expert 2x,
    // Intermediate->Expert 1.5x). Attacking downward is unchanged - the bigger health
    // pool already is the handicap.
    var attacker = GetParent().GetNodeOrNull <Player> ($"{attackerId}");
    var handicap = 1.0f + 0.5f * Mathf.Max (0, TierOf (MaxHealth) - TierOf (attacker?.MaxHealth ?? MaxHealth));
    var decrease = Mathf.RoundToInt (CalculateHealthDecrease (energy) * handicap);
    // A banana blast never one-shots a full-health player (issue #61): leave ≥1 HP.
    if (isSurvivableAtFullHealth && Health >= MaxHealth) decrease = Mathf.Min (decrease, Health - 1);
    Health -= decrease;
    LastZapEnergy = energy;
    ApplyKnockback (attacker, energy, knockbackScale);
    _damageSound.Play();

    if (Health <= 0)
    {
      _zapOutSound.Play();
      attacker?.RpcId (attackerId, MethodName.NotifyScored, DisplayName);
      RespawnShot (attackerName);
    }

    EmitSignal (SignalName.HealthChanged, Health);
  }

  // Shove the victim away from the attacker (plus a slight pop upward), scaled by
  // shot energy; punches shove about a third as much (see issue #45).
  private void ApplyKnockback (Player? attacker, float energy, float scale)
  {
    if (attacker == null) return;
    var away = GlobalPosition - attacker.GlobalPosition;
    var horizontal = new Vector3 (away.X, 0.0f, away.Z);
    if (horizontal.LengthSquared() < 0.001f) return;
    var strength = KnockbackStrength * energy * scale;
    Velocity += horizontal.Normalized() * strength + Vector3.Up * strength * 0.3f;
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void NotifyScored (string shotPlayerName)
  {
    if (!IsMultiplayerAuthority()) return;
    ++Score;
    ++ZapStreakCount;
    // Zapping someone out patches you up a bit (see issue #76).
    Health = Mathf.Min (MaxHealth, Health + KillHealAmount);
    EmitSignal (SignalName.HealthChanged, Health);
    GD.Print ($"{DisplayName}: I scored! (streak {ZapStreakCount})");
    EmitSignal (SignalName.Scored, DisplayName, shotPlayerName);
  }
}
