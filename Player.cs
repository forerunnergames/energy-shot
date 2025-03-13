using Godot;

public partial class Player : CharacterBody3D
{
  [Signal]
  public delegate void HealthChangedEventHandler (int value);

  [Signal]
  public delegate void ScoredEventHandler();

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
  private int _health = 100;
  private Vector3 _throwBackForce = Vector3.Zero;
  private float _throwBackStrength = 5.0f;
  private float _throwBackDecay = 0.8f;
  private float _throwbackEnergyThreshold = 0.5f; // Don't throw back unless energy is greater than this threshold.
  private bool _isInputEnabled;
  public override void _EnterTree() => SetMultiplayerAuthority (NetworkId);
  public void SetInputEnabled (bool isEnabled) => _isInputEnabled = isEnabled;
  private bool IsFalling() => !IsOnFloor();
  private bool IsJumping() => _isInputEnabled && _jumpTimer.IsStopped() && Input.IsActionJustPressed ("jump") && IsOnFloor();
  private bool IsChargingWeapon() => _isInputEnabled && Input.IsActionPressed ("shoot");
  private bool IsDischargingWeapon() => _isInputEnabled && _energyWeapon.IsSpinningUp && Input.IsActionJustReleased ("shoot");
  private bool IsThrowingBack() => _throwBackForce != Vector3.Zero;
  private void Fall (ref Vector3 velocity, double delta) => velocity += Gravity * (float)delta;
  private void ChargeWeapon() => _energyWeapon.Charge();
  private void DischargeWeapon() => _energyWeapon.Discharge();
  private void StartThrowBack (float energy) => _throwBackForce = _camera.GlobalTransform.Basis.Z.Normalized() * _throwBackStrength * (energy >= _throwbackEnergyThreshold ? energy : 0.0f);
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

    _energyWeapon.ShotFired += OnWeaponShotFired;
    _camera = GetNode <Camera3D> ("Camera3D");
    _camera.Current = true;
    _isInputEnabled = true;
    Input.MouseMode = Input.MouseModeEnum.Captured;
  }

  public override void _PhysicsProcess (double delta)
  {
    if (!IsMultiplayerAuthority()) return;
    var velocity = Velocity;
    if (IsThrowingBack()) ThrowBack (ref velocity);
    if (IsFalling()) Fall (ref velocity, delta);
    if (IsJumping()) Jump (ref velocity);
    Move (ref velocity);
    Velocity = velocity;
    MoveAndSlide();
  }

  public override void _UnhandledInput (InputEvent @event)
  {
    if (!_isInputEnabled) return;
    if (!IsMultiplayerAuthority()) return;
    if (IsChargingWeapon()) ChargeWeapon();
    if (IsDischargingWeapon()) DischargeWeapon();
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

  private void ThrowBack (ref Vector3 velocity)
  {
    velocity += _throwBackForce;
    _throwBackForce *= _throwBackDecay;
    if (_throwBackForce.Length() >= 0.1f) return;
    _throwBackForce = Vector3.Zero;
  }

  private void Move (ref Vector3 velocity)
  {
    if (IsThrowingBack()) return;
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

  private void OnWeaponShotFired (float energy)
  {
    StartThrowBack (energy);
    Rpc (MethodName.PlayShootEffects);
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
    hitPlayer.RpcId (hitPlayer.NetworkId, MethodName.ReceiveShot, energy);
  }

  [Rpc (CallLocal = true)]
  private void PlayShootEffects()
  {
    _energyWeapon.PlayShootingSound();
    GD.Print ("PlayShootEffects(): ", _aim.IsColliding(), ", collider: ", _aim.GetCollider()?.GetType());
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void ReceiveShot (float energy)
  {
    GD.Print ($"{GetMultiplayerAuthority()}: I was shot by {Multiplayer.GetRemoteSenderId()}!");
    _health -= Mathf.Min (100, Mathf.RoundToInt (energy * 100.0f));

    if (_health <= 0)
    {
      GD.Print ($"{Name}: I respawned!");
      _health = 100;
      GD.Print ($"{Name} Position before: {Position}");
      Position = Vector3.Zero;
      GD.Print ($"{Name} Position after: {Position}");
    }

    EmitSignal (SignalName.HealthChanged, _health);
  }
}
