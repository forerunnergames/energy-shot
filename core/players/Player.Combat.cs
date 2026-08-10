using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.players;

// Combat: energy weapon & laser bolts, full-auto ability, punching, spawn armor,
// camera kick, crosshair tint, & the hit/damage/score RPCs.
public partial class Player
{
  private bool IsFullAutoActive() => _fullAutoSecondsLeft > 0.0f;
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
    _fullAutoCooldownLeft = Mathf.Max (0.0f, _fullAutoCooldownLeft - dt);

    if (_isInputEnabled && Input.IsActionJustPressed ("ability") && _fullAutoCooldownLeft <= 0.0f)
    {
      _fullAutoSecondsLeft = FullAutoDurationSeconds;
      _fullAutoCooldownLeft = FullAutoCooldownSeconds;
      _nextAutoShotIn = 0.0f;
    }

    if (!IsFullAutoActive()) return;
    _fullAutoSecondsLeft -= dt;
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
    _punchSound.Play();
    var victim = FindAimedPlayer (PunchRange);
    if (victim == null) return;
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
    var origin = _camera.GlobalPosition + direction * 0.9f;
    SpawnBolt (origin, direction, energy, isLive: true);
    Rpc (MethodName.SpawnVisualLaser, origin, direction, energy);
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
    ApplyDamage (energy, shotByPlayerName);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void ReceivePunch (string punchedByPlayerName)
  {
    if (!IsMultiplayerAuthority()) return;
    if (SpawnArmor) return;
    GD.Print ($"{DisplayName}: I was punched by {punchedByPlayerName}!");
    EmitSignal (SignalName.Punched);
    ApplyDamage (PunchEnergy, punchedByPlayerName);
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

  private void ApplyDamage (float energy, string attackerName, bool isSurvivableAtFullHealth = false)
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
    _damageSound.Play();

    if (Health <= 0)
    {
      _zapOutSound.Play();
      attacker?.RpcId (attackerId, MethodName.NotifyScored, DisplayName);
      RespawnShot (attackerName);
    }

    EmitSignal (SignalName.HealthChanged, Health);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void NotifyScored (string shotPlayerName)
  {
    if (!IsMultiplayerAuthority()) return;
    ++Score;
    GD.Print ($"{DisplayName}: I scored!");
    EmitSignal (SignalName.Scored, DisplayName, shotPlayerName);
  }
}
