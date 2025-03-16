using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.players;

public partial class Player : CharacterBody3D
{
  [Export] public string DisplayName
  {
    get => _displayName;
    set
    {
      _displayName = value;
      UpdateNameLabel();
    }
  }

  [Signal] public delegate void HealthChangedEventHandler (int value);
  [Signal] public delegate void ScoredEventHandler (string shooterPlayerName, string shotPlayerName);
  [Signal] public delegate void RespawnedEventHandler (string shotPlayerName, string shooterPlayerName);
  [Export] public int MaxHealth = 100;
  [Export] public float Speed = 7.0f;
  [Export] public float JumpVelocity = 20.0f;
  [Export] public Vector3 Gravity = new(0.0f, -50.0f, 0.0f);
  public int NetworkId => Name.ToString().ToInt();
  private Timer _hitRedTimer = null!;
  private static readonly Color NormalColor = new("0027ff");
  private static readonly Color HitColor = Colors.DarkRed;
  private ProgressBar _healthBar = null!;
  private Camera3D _camera = null!;
  private RayCast3D _aim = null!;
  private MeshInstance3D _mesh = null!;
  private Sprite3D _crossHairs = null!;
  private Timer _jumpTimer = null!;
  private EnergyWeapon _energyWeapon = null!;
  private Label3D? _displayNameLabel;
  private string _displayName = string.Empty;
  private int _health;
  private Vector3 _throwBackForce = Vector3.Zero;
  private float _throwBackStrength = 5.0f;
  private float _throwBackDecay = 0.8f;
  private float _throwbackEnergyThreshold = 0.5f;
  private bool _isInputEnabled;
  public override void _EnterTree() => SetMultiplayerAuthority (NetworkId);
  public void SetInputEnabled (bool isEnabled) => _isInputEnabled = isEnabled;
  [Rpc] private void PlayShootEffects() => _energyWeapon.PlayShootingSound();
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
  private static int CalculateHealthDecrease (float energyShot) => Mathf.Min (100, Mathf.RoundToInt (energyShot * 100.0f));

  public override void _Ready()
  {
    _mesh = GetNode <MeshInstance3D> ("MeshInstance3D");
    _aim = GetNode <RayCast3D> ("Camera3D/Aim");
    _energyWeapon = GetNode <EnergyWeapon> ("Camera3D/EnergyWeapon");
    _crossHairs = GetNode <Sprite3D> ("Camera3D/Crosshairs");
    _jumpTimer = GetNode <Timer> ("JumpTimer");
    _hitRedTimer = GetNode <Timer> ("HitRedTimer");
    _displayNameLabel = GetNode <Label3D> ("PlayerName");
    _healthBar = GetNode <ProgressBar> ("SubViewport/HealthBar");
    _health = MaxHealth;

    if (!IsMultiplayerAuthority())
    {
      UpdateNameLabel();
      _hitRedTimer.Timeout += () => SetColor (NormalColor);
      _crossHairs.Hide();
      return;
    }

    _healthBar.Hide();
    _displayNameLabel.Hide();
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
    if (!_aim.IsColliding() || _aim.GetCollider() is not Player hitPlayerPuppet || hitPlayerPuppet.NetworkId == NetworkId) return;
    hitPlayerPuppet.SetColor (HitColor);
    hitPlayerPuppet._hitRedTimer.Start();
    hitPlayerPuppet._health -= CalculateHealthDecrease (energy);
    hitPlayerPuppet._healthBar.Value = hitPlayerPuppet._health;
    GD.Print ($"{DisplayName}: I shot {hitPlayerPuppet.DisplayName}! (Health: {hitPlayerPuppet._health})");

    if (hitPlayerPuppet._health <= 0)
    {
      hitPlayerPuppet._health = MaxHealth;
      hitPlayerPuppet._healthBar.Value = hitPlayerPuppet._health;
      GD.Print ($"{DisplayName}: I scored!");
      EmitSignal (SignalName.Scored, DisplayName, hitPlayerPuppet.DisplayName);
    }

    hitPlayerPuppet.RpcId (hitPlayerPuppet.NetworkId, MethodName.ReceiveShot, energy, DisplayName);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void ReceiveShot (float energy, string shooterDisplayName)
  {
    GD.Print ($"{DisplayName}: I was shot by {shooterDisplayName}!");
    _health -= CalculateHealthDecrease (energy);
    _healthBar.Value = _health;

    if (_health <= 0)
    {
      _health = MaxHealth;
      _healthBar.Value = _health;
      Position = Vector3.Zero;
      EmitSignal (SignalName.Respawned, DisplayName, shooterDisplayName);
      GD.Print ($"{DisplayName}: I respawned!");
    }

    EmitSignal (SignalName.HealthChanged, _health);
  }

  private void UpdateNameLabel()
  {
    if (_displayNameLabel == null) return;
    _displayNameLabel.Text = _displayName;
  }
}
