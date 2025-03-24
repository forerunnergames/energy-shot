using com.forerunnergames.energyshot.utilities;
using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.players;

public partial class Player : CharacterBody3D
{
  // @formatter:off
  [Export]
  public string DisplayName
  {
    get => _displayName;
    set
    {
      _displayName = value;
      UpdateNameTag();
    }
  }
  // @formatter:on

  [Signal] public delegate void HealthChangedEventHandler (int value);
  [Signal] public delegate void ScoredEventHandler (string playerName, string shotPlayerName);
  [Signal] public delegate void RespawnedShotEventHandler (string playerName, string shotByPlayerName);
  [Signal] public delegate void RespawnedFellEventHandler (string playerName);
  [Export] public int MaxHealth = 100;
  [Export] public float Speed = 7.0f;
  [Export] public float JumpVelocity = 20.0f;
  [Export] public Vector3 Gravity = new(0.0f, -50.0f, 0.0f);
  [Export] public float MinNameTagScale = 1.0f;
  [Export] public float MaxNameTagScale = 20.0f;
  [Export] public float TagScaleStartDistance = 5.0f;
  [Export] public float TagScaleStopDistance = 200.0f;
  [Export] public float HealthTagNameTagMinSpacing = 0.2f;
  [Export] public float HealthTagNameTagMaxSpacing = 3.0f;
  [Export] public float NameTagBaseHeight = 2.3f;
  public int NetworkId => Name.ToString().ToInt();
  private readonly RandomNumberGenerator _rng = new();
  private static readonly Color NormalColor = new("0027ff");
  private static readonly Color HitColor = Colors.DarkRed;
  private NetworkManager _networkManager = null!;
  private Area3D _spawnZoneArea = null!;
  private CylinderShape3D _spawnZoneCylinder = null!;
  private MeshInstance3D _mesh = null!;
  private RayCast3D _aim = null!;
  private EnergyWeapon _energyWeapon = null!;
  private Sprite3D _crossHairs = null!;
  private Timer _jumpTimer = null!;
  private Timer _hitRedTimer = null!;
  private Camera3D _camera = null!;
  private Label3D? _nameTag = null!;
  private Sprite3D _healthTag = null!;
  private ProgressBar _healthBar = null!;
  private string _displayName = string.Empty;
  private int _health;
  private bool _isInputEnabled;
  private static Player? _localPlayer;
  public override void _Process (double delta) => UpdatePuppetTags();
  public override void _EnterTree() => SetMultiplayerAuthority (NetworkId);
  public void SetInputEnabled (bool isEnabled) => _isInputEnabled = isEnabled;
  private bool IsFalling() => !IsOnFloor();
  private bool IsJumping() => _isInputEnabled && _jumpTimer.IsStopped() && Input.IsActionJustPressed ("jump") && IsOnFloor();
  private bool IsChargingWeapon() => _isInputEnabled && Input.IsActionPressed ("shoot");
  private bool IsDischargingWeapon() => _isInputEnabled && _energyWeapon.IsSpinningUp && Input.IsActionJustReleased ("shoot");
  private void Fall (ref Vector3 velocity, double delta) => velocity += Gravity * (float)delta;
  private void ChargeWeapon() => _energyWeapon.Charge();
  private void DischargeWeapon() => _energyWeapon.Discharge();
  private void SetColor (Color color) => (_mesh.GetSurfaceOverrideMaterial (0) as StandardMaterial3D)!.AlbedoColor = color;
  private (bool hit, Player? puppet) TryHitPlayerPuppet() => _aim.IsColliding() && _aim.GetCollider() is Player hitPlayer && hitPlayer.NetworkId != NetworkId ? (true, hitPlayer) : (false, null);
  private static int CalculateHealthDecrease (float energyShot) => Mathf.Min (100, Mathf.RoundToInt (energyShot * 100.0f));

  public override void _Ready()
  {
    _spawnZoneArea = GetNode <Area3D> ("/root/World/SpawnZone");
    _spawnZoneCylinder = (GetNode <CollisionShape3D> ("/root/World/SpawnZone/CollisionShape3D").Shape as CylinderShape3D)!;
    _mesh = GetNode <MeshInstance3D> ("MeshInstance3D");
    _aim = GetNode <RayCast3D> ("Camera3D/Aim");
    _energyWeapon = GetNode <EnergyWeapon> ("Camera3D/EnergyWeapon");
    _crossHairs = GetNode <Sprite3D> ("Camera3D/Crosshairs");
    _jumpTimer = GetNode <Timer> ("JumpTimer");
    _hitRedTimer = GetNode <Timer> ("HitRedTimer");
    _nameTag = GetNode <Label3D> ("NameTag");
    _healthTag = GetNode <Sprite3D> ("HealthTag");
    _healthBar = GetNode <ProgressBar> ("SubViewport/HealthBar");
    _health = MaxHealth;

    if (!IsMultiplayerAuthority())
    {
      UpdateNameTag();
      _hitRedTimer.Timeout += () => SetColor (NormalColor);
      _crossHairs.Hide();
      return;
    }

    _rng.Randomize();
    _localPlayer = this;
    _healthBar.Hide();
    _nameTag.Hide();
    _energyWeapon.ShotFired += OnWeaponShotFired;
    _camera = GetNode <Camera3D> ("Camera3D");
    _camera.Current = true;
    _isInputEnabled = true;
    Input.MouseMode = Input.MouseModeEnum.Captured;
    Position = CalculateRandomSpawnPosition();
  }

  public override void _PhysicsProcess (double delta)
  {
    if (!IsMultiplayerAuthority()) return;
    var velocity = Velocity;
    if (IsFalling()) Fall (ref velocity, delta);
    if (IsJumping()) Jump (ref velocity);
    Move (ref velocity);
    Velocity = velocity;
    if (!MoveAndSlide()) return;
    HandleCollisions();
  }

  public override void _ExitTree()
  {
    base._ExitTree();
    if (!IsMultiplayerAuthority() || _localPlayer != this) return;
    _localPlayer = null;
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

  private void OnWeaponShotFired (float energy)
  {
    var result = TryHitPlayerPuppet();
    if (!result.hit || result.puppet == null) return;
    HitPuppet (result.puppet, energy);
  }

  private void HitPuppet (Player playerPuppet, float energy)
  {
    playerPuppet.SetColor (HitColor);
    playerPuppet._hitRedTimer.Start();
    playerPuppet._health -= CalculateHealthDecrease (energy);
    playerPuppet._healthBar.Value = playerPuppet._health;
    GD.Print ($"{DisplayName}: I hit {playerPuppet.DisplayName}! (Health: {playerPuppet._health})");
    playerPuppet.RpcId (playerPuppet.NetworkId, MethodName.ReceiveHit, energy, DisplayName);
    if (playerPuppet._health > 0) return;
    playerPuppet._health = MaxHealth;
    playerPuppet._healthBar.Value = playerPuppet._health;
    GD.Print ($"{DisplayName}: I scored!");
    EmitSignal (SignalName.Scored, DisplayName, playerPuppet.DisplayName);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void ReceiveHit (float energy, string shotByPlayerName)
  {
    _health -= CalculateHealthDecrease (energy);
    _healthBar.Value = _health;
    GD.Print ($"{DisplayName}: I was hit by {shotByPlayerName}! Health {_health}");
    if (_health <= 0) RespawnShot (shotByPlayerName);
    EmitSignal (SignalName.HealthChanged, _health);
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

  private void Respawn()
  {
    _health = MaxHealth;
    _healthBar.Value = _health;
    Position = CalculateRandomSpawnPosition();
    GD.Print ($"{DisplayName}: I respawned!");
  }

  private void UpdatePuppetTags()
  {
    if (IsMultiplayerAuthority() || _localPlayer == null || _nameTag == null) return;
    var distanceFromLocalPlayer = GlobalPosition.DistanceTo (_localPlayer.GlobalPosition);
    var scaleFactor = CalculateTagScaleFactor (distanceFromLocalPlayer);
    var healthTagMinWidthFactor = 0.8f;
    var healthTagWidthFactor = Mathf.Max (healthTagMinWidthFactor, 0.5f * scaleFactor);
    var originalHealthTagScale = new Vector3 (0.18f, 0.101f, 0.42f);
    var healthTagScaleFactor = new Vector3 (healthTagWidthFactor, 1.0f * scaleFactor, 0.5f * scaleFactor);
    var verticalOffset = scaleFactor * 0.2f;
    var t = (distanceFromLocalPlayer - TagScaleStartDistance) / (TagScaleStopDistance - TagScaleStartDistance);
    var tagSpacing = Mathf.Lerp (HealthTagNameTagMinSpacing, HealthTagNameTagMaxSpacing, Mathf.Clamp (t, 0.0f, 1.0f));
    _nameTag.Scale = Vector3.One * scaleFactor;
    _nameTag.Position = new Vector3 (_nameTag.Position.X, NameTagBaseHeight + verticalOffset, _nameTag.Position.Z);
    _healthTag.Scale = originalHealthTagScale * healthTagScaleFactor;
    _healthTag.Position = new Vector3 (_healthTag.Position.X, NameTagBaseHeight + verticalOffset - tagSpacing, _healthTag.Position.Z);
  }

  private float CalculateTagScaleFactor (float distance)
  {
    if (distance <= TagScaleStartDistance) return MinNameTagScale;
    if (distance >= TagScaleStopDistance) return MaxNameTagScale;
    var t = (distance - TagScaleStartDistance) / (TagScaleStopDistance - TagScaleStartDistance);
    return Mathf.Lerp (MinNameTagScale, MaxNameTagScale, t);
  }

  private Vector3 CalculateRandomSpawnPosition()
  {
    var theta = _rng.RandfRange (0.0f, Mathf.Pi * 2.0f);
    var r = _spawnZoneCylinder.Radius * Mathf.Sqrt (_rng.Randf());
    return new Vector3 (r * Mathf.Cos (theta) + _spawnZoneArea.Position.X, _spawnZoneArea.Position.Y, r * Mathf.Sin (theta) + _spawnZoneArea.Position.Z);
  }

  private void UpdateNameTag()
  {
    if (_nameTag == null) return;
    _nameTag.Text = _displayName;
  }
}
