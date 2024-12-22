using Godot;

public partial class Player : CharacterBody3D
{
  public const float Speed = 10.0f;
  public const float JumpVelocity = 20.0f;
  public readonly Vector3 Gravity = new(0.0f, -50.0f, 0.0f);
  private Camera3D _camera = null!;
  private AudioStreamPlayer3D _shootingSound = null!;

  public override void _Ready()
  {
    _camera = GetNode <Camera3D> ("Camera3D");
    _shootingSound = GetNode <AudioStreamPlayer3D> ("ShootingSound");
    Input.MouseMode = Input.MouseModeEnum.Captured;
  }

  public override void _PhysicsProcess (double delta)
  {
    var velocity = Velocity;

    // Gravity
    if (!IsOnFloor()) velocity += Gravity * (float)delta;

    // Jumping
    if (Input.IsActionJustPressed ("jump") && IsOnFloor()) velocity.Y = JumpVelocity;

    // Get the input direction and handle the movement/deceleration.
    // As good practice, you should replace UI actions with custom gameplay actions.
    var inputDir = Input.GetVector ("move_left", "move_right", "move_forward", "move_back");
    var direction = (Transform.Basis * new Vector3 (inputDir.X, 0, inputDir.Y)).Normalized();

    if (direction != Vector3.Zero)
    {
      velocity.X = direction.X * Speed;
      velocity.Z = direction.Z * Speed;
    }
    else
    {
      velocity.X = Mathf.MoveToward (Velocity.X, 0, Speed);
      velocity.Z = Mathf.MoveToward (Velocity.Z, 0, Speed);
    }

    Velocity = velocity;
    MoveAndSlide();
  }

  public override void _UnhandledInput (InputEvent @event)
  {
    if (Input.IsActionJustPressed ("shoot")) _shootingSound.Play();
    if (@event is not InputEventMouseMotion motionEvent) return;
    RotateY (-motionEvent.Relative.X * 0.005f);
    _camera.RotateX (-motionEvent.Relative.Y * 0.005f);
    _camera.Rotation = new Vector3 (Mathf.Clamp (_camera.Rotation.X, -Mathf.Pi / 2.0f, Mathf.Pi / 2.0f), _camera.Rotation.Y, _camera.Rotation.Z);
  }
}
