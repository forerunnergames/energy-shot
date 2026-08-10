using Godot;

namespace com.forerunnergames.energyshot.players;

// Movement: walking, jumping, falling, world-boundary collisions, respawns, & spawn
// positioning.
public partial class Player
{
  private bool IsFalling() => !IsOnFloor();
  private bool IsJumping() => _isInputEnabled && _jumpTimer.IsStopped() && Input.IsActionJustPressed ("jump") && IsOnFloor();
  private void Fall (ref Vector3 velocity, double delta) => velocity += Gravity * (float)delta;

  private void Jump (ref Vector3 velocity)
  {
    velocity.Y = JumpVelocity;
    _jumpSound.Play();
    _jumpTimer.Start();
  }

  private void Move (ref Vector3 velocity)
  {
    var inputDir = Input.GetVector ("move_left", "move_right", "move_forward", "move_back");
    var inputDirection = (Transform.Basis * new Vector3 (inputDir.X, 0, inputDir.Y)).Normalized();

    if (inputDirection != Vector3.Zero)
    {
      velocity.X = inputDirection.X * Speed;
      velocity.Z = inputDirection.Z * Speed;
      return;
    }

    velocity.X = Mathf.MoveToward (Velocity.X, 0, Speed);
    velocity.Z = Mathf.MoveToward (Velocity.Z, 0, Speed);
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
    Respawn();
    EmitSignal (SignalName.RespawnedFell, DisplayName);
  }

  // Respawn into the spawn room above the arena (drop in to re-enter), with a short
  // input lock, so respawns are no longer instant teleports into the fight.
  private async void Respawn()
  {
    Health = MaxHealth;
    Velocity = Vector3.Zero;
    Position = CalculateRandomSpawnPosition();
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
