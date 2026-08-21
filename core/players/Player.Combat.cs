using com.forerunnergames.energyshot.utilities;
using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.players;

// Combat: energy weapon & laser bolts, full-auto ability, punching, spawn armor,
// camera kick, crosshair tint, & the hit/damage/score RPCs.
public partial class Player
{
  // Bolts spawn this far ahead of the camera; the first sweep still starts at the
  // camera so nearer geometry isn't skipped (issue #112).
  private const float MuzzleOffsetMeters = 0.9f;
  // Punching a wall stings a bit (issue #122): small self-inflicted dent, clamped
  // above zero so it can never zap you out.
  private const int GeometryPunchSelfDamage = 5;

  // Combat reports for the server log (issue #111): the attacker->victim damage RPCs
  // never execute on the server, so the attacker also files a tiny report the server
  // just prints - one small reliable RPC per landed hit, cheap & always on.
  private void ReportToServer (string message)
  {
    if (Multiplayer.IsServer())
    {
      ServerLog.Event (NetworkId, message);
      return;
    }

    RpcId (1, MethodName.LogOnServer, message);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void LogOnServer (string message)
  {
    if (!Multiplayer.IsServer()) return;
    ServerLog.Event (Multiplayer.GetRemoteSenderId(), message);
  }

  private bool IsFullAutoActive() => _fullAutoSecondsLeft > 0.0f;
  // 0..1 readiness fractions for the HUD cooldown bars (1 = ready).
  public float ShotReadyFraction => _energyWeapon.CooldownFraction;
  public float PunchReadyFraction => 1.0f - _punchCooldownLeft / PunchCooldownSeconds;
  public float FullAutoReadyFraction => 1.0f - _fullAutoCooldownLeft / FullAutoCooldownSeconds;
  // The laser only charges & fires while it's the selected weapon (issues #72 & #82);
  // dancing blocks charging - the press cancels the dance instead (issue #103).
  private bool IsChargingWeapon() => _isInputEnabled && !Dancing && IsLaserSelected && HasLaser && !IsFullAutoActive() && Input.IsActionPressed ("shoot");
  private bool IsDischargingWeapon() => _isInputEnabled && IsLaserSelected && HasLaser && _energyWeapon.IsSpinningUp && Input.IsActionJustReleased ("shoot");

  // Leaving the laser slot cancels the charge (issue #156): ResetCharge spins down,
  // resets the accumulated energy, stops the charging sound, & clears the full-charge
  // lock-in (#113); the x-ray reveal (#105) keys off IsFullyCharged & clears on the
  // next UpdateXrayReveal poll. Poll-based like the slingshot's CancelSlingshotDraw,
  // so weapon switches, drops, theft, death, & respawn all self-heal through the one
  // path instead of each needing its own hook.
  private void CancelStaleLaserCharge()
  {
    if (!_energyWeapon.IsSpinningUp) return;
    if (IsLaserSelected && HasLaser) return;
    _energyWeapon.ResetCharge();
  }
  private void ChargeWeapon() => _energyWeapon.Charge();
  private void DischargeWeapon() => _energyWeapon.Discharge();
  // Capped at 200: only banana energies exceed 1.0, & the sticky one-hit kill needs
  // the full 200 to zap out an Expert (issue #83).
  public static int CalculateHealthDecrease (float energyShot) => Mathf.Min (200, Mathf.RoundToInt (energyShot * 100.0f));

  // Casts a ray from the camera along the aim direction, ignoring ourselves, &
  // returns whatever it hits (or null); punches need to know geometry from air (issue #122).
  private GodotObject? FindAimedCollider (float range)
  {
    var from = _camera.GlobalPosition;
    var to = from + -_camera.GlobalTransform.Basis.Z * range;
    var query = PhysicsRayQueryParameters3D.Create (from, to, exclude: new Godot.Collections.Array <Rid> { GetRid() });
    var hit = GetWorld3D().DirectSpaceState.IntersectRay (query);
    if (hit.Count == 0) return null;
    return hit["collider"].AsGodotObject();
  }

  private Player? FindAimedPlayer (float range) => FindAimedCollider (range) as Player;

  // Crosshair stays white until it's actually over another player; the full-charge
  // pulse (issue #113) briefly owns the tint.
  private void UpdateCrosshairTint()
  {
    if (!IsMultiplayerAuthority()) return;
    if (Time.GetTicksMsec() < _crosshairPulseEndMs) return;
    _crossHairs.Modulate = FindAimedPlayer (200.0f) != null ? Colors.Red : Colors.White;
  }

  // Full-charge lock-in cue (issue #113): the crosshair flashes hot & pops in size
  // alongside the weapon's click, so max charge is unmistakable.
  private void OnFullChargeReached()
  {
    _crosshairPulseEndMs = Time.GetTicksMsec() + (ulong)(CrosshairPulseSeconds * 1000.0f);
    _crossHairs.Modulate = FullChargeCrosshairColor;
    _crossHairs.Scale = _crosshairBaseScale * CrosshairPulseScale;
    CreateTween().TweenProperty (_crossHairs, "scale", _crosshairBaseScale, CrosshairPulseSeconds);
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

    if (_isInputEnabled && !Dancing && HasLaser && Input.IsActionJustPressed ("ability") && _fullAutoCooldownLeft <= 0.0f) // Dancing blocks the ability (issue #103).
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
    if (!_isInputEnabled || Dancing || !IsLaserSelected || !Input.IsActionPressed ("shoot") || _nextAutoShotIn > 0.0f) return; // Dancing blocks firing (issue #103).
    _nextAutoShotIn = FullAutoShotIntervalSeconds;
    _energyWeapon.FireLowPower (FullAutoEnergy);
  }

  // Punching requires fists as the selected weapon (issue #82); dancing blocks
  // punching - the press cancels the dance instead (issue #103).
  private void UpdatePunch (double delta)
  {
    _punchCooldownLeft = Mathf.Max (0.0f, _punchCooldownLeft - (float)delta);
    if (!_isInputEnabled || Dancing || !IsFistsSelected || _punchCooldownLeft > 0.0f || !Input.IsActionJustPressed ("punch")) return;
    _punchCooldownLeft = PunchCooldownSeconds;
    CancelSpawnArmorIfFired(); // Punching drops your spawn armor, same as firing.
    var hand = ChooseRandomPunchHand();
    AnimatePunch (hand); // Local swing feedback for the puncher...
    if (TryCatchPaperAirplane()) return; // The grab consumes the swing (issue #102).
    var target = FindAimedCollider (PunchRange);

    if (target is not Player victim)
    {
      PlayMissedPunchFeedback (target);
      return;
    }

    Rpc (MethodName.PlayRemotePunch, hand); // ...but peers only ever see real connects (issue #82).
    _punchSound.Play(); // Puncher-only, connect-only (issue #82): the victim hears the damage sound instead.
    GD.Print ($"{DisplayName}: I punched {victim.DisplayName}!");
    ReportToServer ($"punch: {DisplayName} punched {victim.DisplayName}");
    victim.RpcId (victim.NetworkId, MethodName.ReceivePunch, DisplayName);
  }

  // Missed punches are puncher-only feedback (issues #121 & #122): peers still only
  // ever see real connects (issue #82). Hitting air whiffs; hitting level geometry
  // thuds & stings the puncher a little - self-inflicted, no killer, never lethal.
  private void PlayMissedPunchFeedback (GodotObject? target)
  {
    if (target == null)
    {
      _punchWhiffSound.Play(); // Swing & a miss (issue #121).
      return;
    }

    _punchThudSound.Play(); // Knuckles vs. wall (issue #122).
    Health = Mathf.Max (1, Health - GeometryPunchSelfDamage);
    EmitSignal (SignalName.HealthChanged, Health);
  }

  private void UpdateCameraKick (double delta)
  {
    if (_cameraKickRemaining <= 0.0f) return;
    var recover = Mathf.Min (_cameraKickRemaining, CameraKickRecoverySpeed * (float)delta);
    _camera.RotateX (-recover);
    _cameraKickRemaining -= recover;
  }

  private void OnWeaponShotFired (float energy, bool isFullAuto)
  {
    CancelSpawnArmorIfFired();
    // Aim direction is captured before the camera kick so the kick is purely visual.
    var direction = -_camera.GlobalTransform.Basis.Z;
    var kick = energy * CameraKickRadians;
    _camera.RotateX (kick);
    _cameraKickRemaining += kick;
    TryRocketBoost (direction, energy);
    var sweepStart = _camera.GlobalPosition;
    var origin = sweepStart + direction * MuzzleOffsetMeters;
    SpawnBolt (origin, sweepStart, direction, energy, isLive: true, isFullAuto);
    Rpc (MethodName.SpawnVisualLaser, origin, sweepStart, direction, energy);
  }

  // Firing at the ground close beneath you rocket-boosts you upward, scaling with
  // charge (see issue #56). Works mid-air too, with a longer reach for shooting the
  // ground below a jump, but only once per airtime (issue #86).
  private void TryRocketBoost (Vector3 direction, float energy)
  {
    if (direction.Y >= 0.0f) return;
    var isGrounded = IsOnFloor();
    if (!isGrounded && _airBoostUsed) return; // Max one airborne boost until next touching the floor.
    var from = _camera.GlobalPosition;
    var range = isGrounded ? RocketBoostRange : AirRocketBoostRange;
    var query = PhysicsRayQueryParameters3D.Create (from, from + direction * range, exclude: new Godot.Collections.Array <Rid> { GetRid() });
    var hit = GetWorld3D().DirectSpaceState.IntersectRay (query);
    if (hit.Count == 0) return;
    if (hit["collider"].AsGodotObject() is CharacterBody3D) return; // Ground only, not players.
    if (hit["normal"].AsVector3().Y < 0.5f) return; // Floors only - walls don't launch you.
    _airBoostUsed = !isGrounded;
    Velocity = new Vector3 (Velocity.X, JumpVelocity * RocketBoostMultiplier * energy, Velocity.Z);
  }

  // Touching the floor re-arms the single airborne rocket boost (issue #86).
  private void UpdateAirBoost()
  {
    if (IsOnFloor()) _airBoostUsed = false;
  }

  // Visual-only copy of the shooter's bolt on every other peer. Firing also proves
  // the shooter's spawn armor is gone, so stale armor whitewash clears here (issue #114).
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void SpawnVisualLaser (Vector3 origin, Vector3 sweepStart, Vector3 direction, float energy)
  {
    ClearArmorDisplayOnRemoteAttack();
    SpawnBolt (origin, sweepStart, direction, energy, isLive: false, isFullAuto: false);
  }

  private void SpawnBolt (Vector3 origin, Vector3 sweepStart, Vector3 direction, float energy, bool isLive, bool isFullAuto)
  {
    var bolt = _laserBoltScene.Instantiate <LaserBolt>();
    GetParent().AddChild (bolt);
    bolt.Launch (origin, sweepStart, direction, energy, isLive, this);
    if (isLive) bolt.HitPlayer += (body, hitEnergy, throughBarrier, isHeadshot) => OnLaserHitPlayer (body, hitEnergy, throughBarrier, isFullAuto, isHeadshot);
  }

  private void OnLaserHitPlayer (CharacterBody3D body, float energy, bool throughBarrier, bool isFullAuto, bool isHeadshot)
  {
    if (body is not Player victim || victim.NetworkId == NetworkId) return;
    HitPuppet (victim, energy, throughBarrier, isFullAuto, isHeadshot);
  }

  // The victim is the single owner of its health: the shooter only reports the hit,
  // the victim applies damage & replicates Health to everyone, & tells the shooter
  // when it scored a kill (see issue #20).
  private void HitPuppet (Player playerPuppet, float energy, bool throughBarrier, bool isFullAuto, bool isHeadshot)
  {
    GD.Print ($"{DisplayName}: I hit {playerPuppet.DisplayName}{(isHeadshot ? " on the dome" : "")}!");
    PlayHitmarker (isHeadshot);
    ReportToServer ($"hit: {DisplayName} zapped {playerPuppet.DisplayName} (energy {energy:0.00}{(isHeadshot ? ", headshot" : "")})");
    playerPuppet.RpcId (playerPuppet.NetworkId, MethodName.ReceiveHit, energy, DisplayName, throughBarrier, isFullAuto, isHeadshot);
  }

  // A headshot's hitmarker rings higher (issue #179): the shooter knows it landed.
  private void PlayHitmarker (bool isHeadshot)
  {
    _hitmarkerSound.PitchScale = isHeadshot ? 1.6f : 1.0f;
    _hitmarkerSound.Play();
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void ReceiveHit (float energy, string shotByPlayerName, bool throughBarrier, bool isFullAuto, bool isHeadshot)
  {
    if (!IsMultiplayerAuthority()) return;
    if (SpawnArmor) return;
    if (Fallen) return; // A body mid-death-sequence is scenery (issue #152).
    GD.Print ($"{DisplayName}: I was hit by {shotByPlayerName}{(throughBarrier ? " through a barrier" : "")}!");
    // The fire mode travels with the hit (CodeRabbit on #96): a weak quick-tap
    // discharge must not be misread as full-auto by an energy threshold.
    LastDamageKind = isFullAuto ? DamageKind.FullAuto : DamageKind.Laser; // Message context (issue #84).
    // Distinct victim feedback for a through-barrier zap (issue #94).
    if (throughBarrier) _throughWallZapSound.Play();
    ApplyDamage (energy, shotByPlayerName, knockbackScale: 1.0f, throughBarrier: throughBarrier, isHeadshot: isHeadshot);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void ReceivePunch (string punchedByPlayerName)
  {
    if (!IsMultiplayerAuthority()) return;
    if (SpawnArmor) return;
    if (Fallen) return; // A body mid-death-sequence is scenery (issue #152).
    GD.Print ($"{DisplayName}: I was punched by {punchedByPlayerName}!");
    LastDamageKind = DamageKind.Punch; // Message context (issue #84).
    // No punch sfx here (issue #82): the victim hears the damage sound via ApplyDamage.
    ApplyPunchStun(); // Stacking slow; the blur stacks HUD-side via Punched (issues #68 & #71).
    TryLoseWeaponToPunch (Multiplayer.GetRemoteSenderId());
    EmitSignal (SignalName.Punched);
    ApplyDamage (PunchEnergy, punchedByPlayerName, PunchKnockbackScale);
  }

  // 20% chance a connect costs the victim their item (issue #71); with #193 that's
  // a theft: the equipped item transfers straight to the puncher, server-validated.
  private void TryLoseWeaponToPunch (int puncherId)
  {
    if (_rng.Randf() >= PunchDropChance) return;
    LoseWeaponToPunch (puncherId);
  }

  private void ApplyDamage (float energy, string attackerName, float knockbackScale, bool isSurvivableAtFullHealth = false, bool throughBarrier = false, bool isHeadshot = false)
    => ApplyDamageFrom (Multiplayer.GetRemoteSenderId(), energy, attackerName, knockbackScale, isSurvivableAtFullHealth, throughBarrier, isHeadshot);

  // Split from ApplyDamage so delayed damage (the sticky banana fuse) can carry the
  // attacker id captured while the RPC context still existed (issue #83).
  private void ApplyDamageFrom (int attackerId, float energy, string attackerName, float knockbackScale, bool isSurvivableAtFullHealth = false, bool throughBarrier = false, bool isHeadshot = false)
  {
    // Already zapped out (issue #152): a body lying through its death sequence takes
    // no further damage - late stickies, blasts, & punches land on scenery.
    if (Fallen) return;
    // Difficulty damage handicap: lower-skill attackers hit higher-skill targets harder
    // (+50% per tier gap: Beginner->Intermediate 1.5x, Beginner->Expert 2x,
    // Intermediate->Expert 1.5x). Attacking downward is unchanged - the bigger health
    // pool already is the handicap.
    Dancing = false; // Getting zapped mid-dance ends the groove on every peer (issue #103).
    _wasEatingWhenHit = Eating; // Death-message context, captured before the interrupt clears it (issue #192).
    InterruptEating(); // YOU can't cancel the ritual; an attacker can - & the loaf is wasted (issue #192).
    var attacker = GetParent().GetNodeOrNull <Player> ($"{attackerId}");
    var handicap = 1.0f + 0.5f * Mathf.Max (0, TierOf (MaxHealth) - TierOf (attacker?.MaxHealth ?? MaxHealth));
    var decrease = Mathf.RoundToInt (CalculateHealthDecrease (energy) * handicap);
    // A headshot is a flat, unhandicapped HeadshotDamage (issue #179): one zaps an
    // Expert or Intermediate outright, a Beginner takes exactly two.
    if (isHeadshot) decrease = HeadshotDamage;
    // A full-charge shot is lethal on ANY target (issue #93): the 100-damage cap &
    // tier handicap don't apply - only the survivable banana blast is exempt.
    if (energy >= EnergyWeapon.FullChargeEnergyThreshold && !isSurvivableAtFullHealth) decrease = Health;
    // A banana blast never one-shots a full-health player (issue #61): leave ≥1 HP.
    if (isSurvivableAtFullHealth && Health >= MaxHealth) decrease = Mathf.Min (decrease, Health - 1);
    Health -= decrease;
    LastZapEnergy = energy;
    LastZapThroughBarrier = throughBarrier;
    ApplyKnockback (attacker, energy, knockbackScale);
    _damageSound.Play();

    if (Health <= 0)
    {
      _zapOutSound.Play();
      attacker?.RpcId (attackerId, MethodName.NotifyScored, DisplayName);
      CreditAssist (attackerId); // Whoever softened us up within the window (issue #153).
      RespawnShot (attackerName);
    }

    if (Health > 0) RememberDamager (attackerId); // Survived: this hit may become somebody's assist (issue #153).
    // Survived it (CodeRabbit on #214): the capture is only ever the death snapshot's
    // context, so a nonlethal interruption must not still read as "died eating" when
    // a later, unrelated hit finishes the job.
    else _wasEatingWhenHit = false;

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
    var push = horizontal.Normalized() * strength;
    // Mostly-horizontal shove (issue #163): the up-pop compounded with energy & scale
    // & stacked additively across rapid hits, launching full-draw slingshot victims
    // sky-high. Knockback may never push upward speed past the cap; an already-faster
    // ascent (a jump, a rocket boost) keeps its own speed.
    var poppedUp = Mathf.Min (Velocity.Y + strength * 0.3f, KnockbackUpPopCap);
    Velocity = new Vector3 (Velocity.X + push.X, Mathf.Max (Velocity.Y, poppedUp), Velocity.Z + push.Z);
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
