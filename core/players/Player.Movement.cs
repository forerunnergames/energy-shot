using Godot;

namespace com.forerunnergames.energyshot.players;

// Movement: walking, jumping, falling, sliding, crouching, world-boundary collisions,
// respawns, & spawn positioning.
public partial class Player
{
  private bool IsFalling() => !IsOnFloor();
  private bool IsJumping() => _isInputEnabled && _jumpTimer.IsStopped() && Input.IsActionJustPressed ("jump") && IsOnFloor();
  private void Fall (ref Vector3 velocity, double delta) => velocity += Gravity * (float)delta;
  private bool WantsSlide() => _isInputEnabled && Input.IsActionPressed ("slide");
  // Slide wins: crouch is ignored while sliding (see issue #51).
  private bool WantsCrouch() => _isInputEnabled && !Sliding && Input.IsActionPressed ("crouch");
  private float MoveSpeed() => Sliding ? Speed * SlideSpeedMultiplier : _crouching ? Speed * CrouchSpeedMultiplier : Speed;

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
    ApplyCameraHeight();
  }

  // Hold C to crouch: shorter profile & half speed (see issue #51). Standing back up
  // requires overhead clearance so the head can't clip into geometry above.
  private void UpdateCrouch()
  {
    var crouching = WantsCrouch();
    if (crouching == _crouching) return;
    if (!crouching && IsOverheadBlocked()) return;
    Crouching = crouching;
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
    Respawn();
    EmitSignal (SignalName.RespawnedShot, DisplayName, shotByPlayerName);
  }

  private void RespawnFell()
  {
    --Score; // Falling off the world costs a point.
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
    ActivateSpawnArmor();
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
