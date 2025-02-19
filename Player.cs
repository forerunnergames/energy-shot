using Godot;

public partial class Player : CharacterBody3D
{
  [Signal] public delegate void HealthChangedEventHandler (int value);
  public const float Speed = 7.0f;
  public const float JumpVelocity = 20.0f;
  public readonly Vector3 Gravity = new(0.0f, -50.0f, 0.0f);
  public int NetworkId => Name.ToString().ToInt();
  public Timer HitRedTimer { get; private set; } = null!;
  private static readonly Color NormalColor = new("0027ff");
  private static readonly Color HitColor = Colors.DarkRed;
  private Camera3D _camera = null!;
  private RayCast3D _aim = null!;
  private AudioStreamPlayer3D _shootingSound = null!;
  private MeshInstance3D _mesh = null!;
  private Sprite3D _crosshairs = null!;
  private Timer _shotTimer = null!;
  private int _health = 3;
  public override void _EnterTree() => SetMultiplayerAuthority (NetworkId);
  private void SetColor (Color color) => (_mesh.GetSurfaceOverrideMaterial (0) as StandardMaterial3D)!.AlbedoColor = color;

  public override void _Ready()
  {
    _mesh = GetNode <MeshInstance3D> ("MeshInstance3D");
    _aim = GetNode <RayCast3D> ("Camera3D/Aim");
    _shootingSound = GetNode <AudioStreamPlayer3D> ("ShootingSound");
    _crosshairs = GetNode <Sprite3D> ("Camera3D/Crosshairs");
    _shotTimer = GetNode <Timer> ("ShotTimer");
    HitRedTimer = GetNode <Timer> ("HitRedTimer");

    if (!IsMultiplayerAuthority())
    {
      HitRedTimer.Timeout += () => SetColor (NormalColor);
      _crosshairs.Hide();
      return;
    }

    _camera = GetNode <Camera3D> ("Camera3D");
    _camera.Current = true;
    Input.MouseMode = Input.MouseModeEnum.Captured;
  }

  public override void _PhysicsProcess (double delta)
  {
    if (!IsMultiplayerAuthority()) return;
    var velocity = Velocity;
    if (!IsOnFloor()) velocity += Gravity * (float)delta;
    if (Input.IsActionJustPressed ("jump") && IsOnFloor()) velocity.Y = JumpVelocity;
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
    if (!IsMultiplayerAuthority()) return;

    if (Input.IsActionJustPressed ("shoot") && _shotTimer.IsStopped())
    {
      _shotTimer.Start();
      Rpc (MethodName.PlayShootEffects);
      if (!_aim.IsColliding() || _aim.GetCollider() is not Player hitPlayer || hitPlayer.NetworkId == NetworkId) return;
      GD.Print ($"{Name}: I am shooting: {hitPlayer.GetMultiplayerAuthority()}");
      hitPlayer.SetColor (HitColor); // This is only for the puppet.
      hitPlayer.HitRedTimer.Start(); // This is only for the puppet.
      hitPlayer.RpcId (hitPlayer.NetworkId, MethodName.Shot);
    }

    if (@event is not InputEventMouseMotion motionEvent) return;
    RotateY (-motionEvent.Relative.X * 0.005f);
    _camera.RotateX (-motionEvent.Relative.Y * 0.005f);
    _camera.Rotation = new Vector3 (Mathf.Clamp (_camera.Rotation.X, -Mathf.Pi / 2.0f, Mathf.Pi / 2.0f), _camera.Rotation.Y, _camera.Rotation.Z);
  }

  [Rpc (CallLocal = true)]
  private void PlayShootEffects()
  {
    _shootingSound.Play();
    GD.Print ("PlayShootEffects(): ", _aim.IsColliding(), ", collider: ", _aim.GetCollider()?.GetType());
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void Shot()
  {
    GD.Print ($"{GetMultiplayerAuthority()}: I was shot by {Multiplayer.GetRemoteSenderId()}!");
    --_health;

    if (_health <= 0)
    {
      GD.Print ($"{Name}: I respawned!");
      _health = 3;
      GD.Print ($"{Name} Position before: {Position}");
      Position = Vector3.Zero;
      GD.Print ($"{Name} Position after: {Position}");
    }

    EmitSignal (SignalName.HealthChanged, _health);
  }
}
