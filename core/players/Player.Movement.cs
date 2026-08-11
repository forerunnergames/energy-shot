using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.players;

// Movement: walking, jumping, falling, sliding, crouching, world-boundary collisions,
// respawns, spawn positioning, & the death snapshot for scenario messages.
public partial class Player
{
  // Death snapshot (issue #84): what this player was doing & holding at the moment
  // it got zapped out, captured before the respawn resets everything, so the HUD
  // can pick the right scenario message afterward. LastDamageKind is recorded by
  // the receive-hit RPCs in Player.Combat.cs & Player.Banana.cs.
  public DamageKind LastDamageKind { get; private set; }
  public bool DiedSliding { get; private set; }
  public bool DiedArmed { get; private set; }
  public bool DiedHoldingBananaGun { get; private set; }
  public int LostStreakCount { get; private set; }
  private bool IsFalling() => !IsOnFloor();
  // Stun blocks jumping & sliding (issues #70 & #71).
  private bool IsJumping() => _isInputEnabled && !IsStunned && _jumpTimer.IsStopped() && Input.IsActionJustPressed ("jump") && IsOnFloor();
  private void Fall (ref Vector3 velocity, double delta) => velocity += Gravity * (float)delta;
  private bool WantsSlide() => _isInputEnabled && !IsStunned && Input.IsActionPressed ("slide");
  // Slide wins: crouch is ignored while sliding (see issue #51).
  private bool ToggledCrouch() => _isInputEnabled && Input.IsActionJustPressed ("crouch");
  private float MoveSpeed() => (Sliding ? Speed * SlideSpeedMultiplier : _crouching ? Speed * CrouchSpeedMultiplier : Speed) * StunSpeedMultiplier();

  // Hold to slide: double speed & a horizontal pose, capped at SlideDurationSeconds,
  // then a cooldown before the next slide (see issue #41).
  private void UpdateSlide (double delta)
  {
    _slideCooldownLeft = Mathf.Max (0.0f, _slideCooldownLeft - (float)delta);

    if (Sliding)
    {
      _slideSecondsLeft -= (float)delta;
      if (WantsSlide() && _slideSecondsLeft > 0.0f) return;
      StopSlide();
      return;
    }

    if (!WantsSlide() || _slideCooldownLeft > 0.0f) return;
    StartSlide();
  }

  private void StartSlide()
  {
    _slideSecondsLeft = SlideDurationSeconds;
    Sliding = true; // Setter re-poses the body; replicated so every peer sees it.
    ApplyCameraHeight();
  }

  private void StopSlide()
  {
    _slideCooldownLeft = SlideCooldownSeconds;
    Sliding = false;
    if (IsOverheadBlocked()) Crouching = true; // Slid under something low: come up into a crouch, not the ceiling.
    ApplyCameraHeight();
  }

  // Hold C to crouch: shorter profile & half speed (see issue #51). Standing back up
  // requires overhead clearance so the head can't clip into geometry above.
  // Press C to crouch, press again to stand (issue #85). Sliding cancels a crouch;
  // standing needs overhead clearance.
  private void UpdateCrouch()
  {
    if (Sliding && _crouching) { Crouching = false; ApplyCameraHeight(); return; }
    if (!ToggledCrouch() || Sliding) return;
    if (_crouching && IsOverheadBlocked()) return;
    Crouching = !_crouching;
    ApplyCameraHeight();
  }

  private bool IsOverheadBlocked()
  {
    var from = GlobalPosition;
    var to = from + Vector3.Up * 2.1f; // Standing capsule head height + margin.
    var query = PhysicsRayQueryParameters3D.Create (from, to, exclude: new Godot.Collections.Array <Rid> { GetRid() });
    return GetWorld3D().DirectSpaceState.IntersectRay (query).Count > 0;
  }

  // Body & hitbox go horizontal while sliding, upright otherwise; runs on every peer
  // via the replicated Sliding property (styled like SpawnArmor).
  private void ApplySlidePose()
  {
    if (_mesh == null) return;
    var rotation = Sliding ? new Vector3 (-90.0f, 0.0f, 0.0f) : Vector3.Zero;
    var position = new Vector3 (0.0f, Sliding ? 0.5f : 1.0f, 0.0f);
    _mesh.RotationDegrees = rotation;
    _mesh.Position = position;
    _collisionShape.RotationDegrees = rotation;
    _collisionShape.Position = position;
  }

  // Runs on every peer via the replicated Crouching property.
  private void ApplyCrouchScale()
  {
    if (_mesh == null) return;
    var scale = new Vector3 (1.0f, _crouching ? CrouchHeightScale : 1.0f, 1.0f);
    _mesh.Scale = scale;
    _collisionShape.Scale = scale;
  }

  private void ApplyCameraHeight()
  {
    var height = Sliding ? SlideCameraHeight : _crouching ? _standingCameraHeight * CrouchHeightScale : _standingCameraHeight;
    _camera.Position = new Vector3 (_camera.Position.X, height, _camera.Position.Z);
  }

  private void Jump (ref Vector3 velocity)
  {
    velocity.Y = JumpVelocity;
    _jumpSound.Play();
    _jumpTimer.Start();
  }

  private void Move (ref Vector3 velocity)
  {
    var speed = MoveSpeed();
    var inputDir = Input.GetVector ("move_left", "move_right", "move_forward", "move_back");
    var inputDirection = (Transform.Basis * new Vector3 (inputDir.X, 0, inputDir.Y)).Normalized();

    if (inputDirection != Vector3.Zero)
    {
      velocity.X = inputDirection.X * speed;
      velocity.Z = inputDirection.Z * speed;
      return;
    }

    velocity.X = Mathf.MoveToward (Velocity.X, 0, speed);
    velocity.Z = Mathf.MoveToward (Velocity.Z, 0, speed);
  }

  private void HandleCollisions()
  {
    var collisionCount = GetSlideCollisionCount();
    for (var i = 0; i < collisionCount; ++i) HandleCollision (GetSlideCollision (i));
  }

  private void HandleCollision (KinematicCollision3D collision)
  {
    if (collision.GetColliderShape() is not CollisionShape3D { Shape: WorldBoundaryShape3D }) return;
    RespawnFell();
  }

  private void RespawnShot (string shotByPlayerName)
  {
    CaptureDeathSnapshot();
    DropAllHeldWeapons(); // Death drops everything carried at the death spot (issue #72).
    Respawn();
    EmitSignal (SignalName.RespawnedShot, DisplayName, shotByPlayerName);
  }

  private void CaptureDeathSnapshot()
  {
    DiedSliding = Sliding;
    DiedArmed = HeldWeapon != HeldWeapon.None;
    DiedHoldingBananaGun = Holds (HeldWeapon.Banana);
    LostStreakCount = ZapStreakCount;
  }

  // Belt & braces for issue #88: the on-fire glow & pulsing leaderboard entry must
  // never outlive a death. The authority already resets ZapStreakCount in Respawn(),
  // but ON_CHANGE sync only re-sends on the NEXT change, so a puppet that missed
  // that one reset delta (e.g. spawned mid-handshake) stays stuck on a stale 3+.
  // Every peer hears the reliable respawn broadcast, so the World clears the local
  // display state there too - the authority's value agrees (it's also 0).
  public void ClearStreakDisplayLocally()
  {
    _zapStreakCount = 0;
    ApplyStreakGlow();
  }

  // Killer-side context for jump-shot messages (issue #84): IsOnFloor() is only
  // meaningful on the authority, so puppets probe for ground beneath their feet.
  public bool IsLikelyAirborne()
  {
    if (IsMultiplayerAuthority()) return !IsOnFloor();
    var from = GlobalPosition + Vector3.Up * 0.2f;
    var query = PhysicsRayQueryParameters3D.Create (from, from + Vector3.Down * 0.8f, exclude: new Godot.Collections.Array <Rid> { GetRid() });
    return GetWorld3D().DirectSpaceState.IntersectRay (query).Count == 0;
  }

  private void RespawnFell()
  {
    --Score; // Falling off the world costs a point.
    ClearHeldWeapons(); // A drop below the world would be unreachable; the weapons respawn at spawn points instead (issue #72).
    Respawn();
    EmitSignal (SignalName.RespawnedFell, DisplayName);
  }

  // Respawn into the spawn room above the arena (drop in to re-enter), with a short
  // input lock, so respawns are no longer instant teleports into the fight.
  private async void Respawn()
  {
    ZapStreakCount = 0; // Any respawn ends the streak.
    Health = MaxHealth;
    Velocity = Vector3.Zero;
    Position = CalculateRandomSpawnPosition();
    _bread.Restock(); // Fresh bread every life (issue #62).
    _energyWeapon.ResetCharge(); // Every life starts with a cold weapon (issue #67).
    ClearStun(); // Death shakes off any punch/banana stun.
    ActivateSpawnArmor();
    // Fresh lives start standing & slide-ready: no lingering pose, no cooldown carryover (#104).
    Sliding = false;
    _slideSecondsLeft = 0.0f;
    _slideCooldownLeft = 0.0f;
    Crouching = false;
    ApplyCameraHeight();
    SetInputEnabled (isEnabled: false);
    _respawnSound.Play();
    GD.Print ($"{DisplayName}: I respawned!");
    await ToSignal (GetTree().CreateTimer (RespawnInputLockSeconds), SceneTreeTimer.SignalName.Timeout);
    SetInputEnabled (isEnabled: true);
  }

  private Vector3 CalculateRandomSpawnPosition()
  {
    var offset = new Vector3 (_rng.RandfRange (-4.0f, 4.0f), 1.0f, _rng.RandfRange (-4.0f, 4.0f));
    return _spawnRoom.Position + offset;
  }
}
