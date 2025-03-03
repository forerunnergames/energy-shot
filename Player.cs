using Godot;

public partial class Player : CharacterBody3D
{
  [Signal] public delegate void HealthChangedEventHandler (int value);
  [Signal] public delegate void ScoredEventHandler();
  public const float Speed = 7.0f;
  public const float JumpVelocity = 20.0f;
  public readonly Vector3 Gravity = new(0.0f, -50.0f, 0.0f);
  public int NetworkId => Name.ToString().ToInt();
  public float WeaponRotationSpeed => _energyWeapon.CurrentRotationSpeed;
  public Timer HitRedTimer { get; private set; } = null!;
  private static readonly Color NormalColor = new("0027ff");
  private static readonly Color HitColor = Colors.DarkRed;
  private Camera3D _camera = null!;
  private RayCast3D _aim = null!;
  private MeshInstance3D _mesh = null!;
  private Sprite3D _crossHairs = null!;
  private Timer _jumpTimer = null!;
  private EnergyWeapon _energyWeapon = null!;
  private int _health = 3;
  private bool _isInputEnabled;
  public override void _EnterTree() => SetMultiplayerAuthority (NetworkId);
  public void SetInputEnabled (bool isEnabled) => _isInputEnabled = isEnabled;
  private bool IsJumping() => _isInputEnabled && _jumpTimer.IsStopped() && Input.IsActionJustPressed ("jump") && IsOnFloor();
  private bool IsShooting() => _isInputEnabled && !_energyWeapon.IsShooting && Input.IsActionPressed ("shoot");
  private void SetColor (Color color) => (_mesh.GetSurfaceOverrideMaterial (0) as StandardMaterial3D)!.AlbedoColor = color;

  public override void _Ready()
  {
    _mesh = GetNode <MeshInstance3D> ("MeshInstance3D");
    _aim = GetNode <RayCast3D> ("Camera3D/Aim");
    _energyWeapon = GetNode <EnergyWeapon> ("Camera3D/EnergyWeapon");
    _crossHairs = GetNode <Sprite3D> ("Camera3D/Crosshairs");
    _jumpTimer = GetNode <Timer> ("JumpTimer");
    HitRedTimer = GetNode <Timer> ("HitRedTimer");

    if (!IsMultiplayerAuthority())
    {
      HitRedTimer.Timeout += () => SetColor (NormalColor);
      _crossHairs.Hide();
      return;
    }

    _energyWeapon.Shot += OnWeaponShot;
    _camera = GetNode <Camera3D> ("Camera3D");
    _camera.Current = true;
    _isInputEnabled = true;
    Input.MouseMode = Input.MouseModeEnum.Captured;
  }

  public override void _PhysicsProcess (double delta)
  {
    if (!IsMultiplayerAuthority()) return;
    var velocity = Velocity;
    if (!IsOnFloor()) velocity += Gravity * (float)delta;
    if (IsJumping()) Jump (ref velocity);

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
    if (!_isInputEnabled) return;
    if (!IsMultiplayerAuthority()) return;
    if (IsShooting()) Shoot();
    if (@event is not InputEventMouseMotion motionEvent) return;
    RotateY (-motionEvent.Relative.X * 0.005f);
    _camera.RotateX (-motionEvent.Relative.Y * 0.005f);
    _camera.Rotation = new Vector3 (Mathf.Clamp (_camera.Rotation.X, -Mathf.Pi / 2.0f, Mathf.Pi / 2.0f), _camera.Rotation.Y, _camera.Rotation.Z);
  }

  private void Jump (ref Vector3 velocity)
  {
    velocity.Y = JumpVelocity;
    _jumpTimer.Start();
  }

  private void Shoot()
  {
    _energyWeapon.Shoot();
    Rpc (MethodName.PlayShootEffects);
  }

  private void OnWeaponShot()
  {
    if (!_aim.IsColliding() || _aim.GetCollider() is not Player hitPlayer || hitPlayer.NetworkId == NetworkId) return;
    GD.Print ($"{Name}: I am shooting: {hitPlayer.GetMultiplayerAuthority()}");
    hitPlayer.SetColor (HitColor); // This is only for the puppet.
    hitPlayer.HitRedTimer.Start(); // This is only for the puppet.
    --hitPlayer._health;

    if (hitPlayer._health == 0)
    {
      GD.Print ("Emitting scored signal");
      EmitSignal (SignalName.Scored);
    }

    GD.Print ($"Puppet health: {hitPlayer._health}");
    hitPlayer.RpcId (hitPlayer.NetworkId, MethodName.Shot);
  }

  [Rpc (CallLocal = true)]
  private void PlayShootEffects() { GD.Print ("PlayShootEffects(): ", _aim.IsColliding(), ", collider: ", _aim.GetCollider()?.GetType()); }

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
