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

  // Replicated via MultiplayerSynchronizer; only the owning player writes it, so every
  // peer's health bar for this player stays in sync (see issue #20).
  [Export]
  public int Health
  {
    get => _health;
    set
    {
      var previous = _health;
      _health = value;
      if (_healthBar != null) _healthBar.Value = value;
      if (IsMultiplayerAuthority() || value >= previous) return;
      FlashHitColor();
    }
  }

  [Signal] public delegate void HealthChangedEventHandler (int value);
  [Signal] public delegate void ScoredEventHandler (string playerName, string shotPlayerName);
  [Signal] public delegate void RespawnedShotEventHandler (string playerName, string shotByPlayerName);
  [Signal] public delegate void RespawnedFellEventHandler (string playerName);
  // Replicated like Health so every peer can render the leaderboard.
  [Export] public int Score { get; set; }

  // Difficulty handicap: beginners get a bigger health pool. Replicated so every
  // peer scales this player's health bar correctly.
  [Export]
  public int MaxHealth
  {
    get => _maxHealth;
    set
    {
      _maxHealth = value;
      if (_healthBar != null) _healthBar.MaxValue = value;
    }
  }

  // Maps a difficulty selection (0=Beginner, 1=Intermediate, 2=Expert) to a health
  // pool; anything unrecognized (including spoofed values) gets Expert health.
  public static int MaxHealthFor (int difficulty) => difficulty switch { 0 => 400, 1 => 300, _ => 200 };

  // Recovers the difficulty tier (0=Beginner, 1=Intermediate, 2=Expert) from the
  // replicated health pool, for the damage handicap.
  private static int TierOf (int maxHealth) => maxHealth switch { 400 => 0, 300 => 1, _ => 2 };

  // 5s of invulnerability after every (re)spawn, canceled by firing. Replicated so
  // every peer renders the armored (white) player & the victim rejects damage.
  [Export]
  public bool SpawnArmor
  {
    get => _spawnArmor;
    set
    {
      _spawnArmor = value;
      if (_mesh != null) SetColor (value ? SpawnArmorColor : NormalColor);
    }
  }

  [Export] public float SpawnArmorSeconds = 5.0f;
  [Export] public float MouseSensitivity = 0.0025f;
  [Export] public float FullAutoDurationSeconds = 3.0f;
  [Export] public float FullAutoCooldownSeconds = 15.0f;
  [Export] public float FullAutoShotIntervalSeconds = 0.15f;
  [Export] public float FullAutoEnergy = 0.12f;
  [Export] public float RespawnInputLockSeconds = 2.0f;
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
  private static readonly Color SpawnArmorColor = Colors.White;
  private bool _spawnArmor;
  private ulong _spawnArmorEndMs;
  private NetworkManager _networkManager = null!;
  private Node3D _spawnRoom = null!;
  private MeshInstance3D _mesh = null!;
  private PackedScene _laserBoltScene = null!;
  private EnergyWeapon _energyWeapon = null!;
  private float _fullAutoSecondsLeft;
  private float _fullAutoCooldownLeft;
  private float _nextAutoShotIn;
  private Sprite3D _crossHairs = null!;
  private Timer _jumpTimer = null!;
  private Timer _hitRedTimer = null!;
  private Camera3D _camera = null!;
  private Label3D? _nameTag = null!;
  private Sprite3D _healthTag = null!;
  private ProgressBar _healthBar = null!;
  private string _displayName = string.Empty;
  private int _health;
  private int _maxHealth = 200;
  private bool _isInputEnabled;
  private static Player? _localPlayer;
  public override void _Process (double delta)
  {
    if (!IsMultiplayerActive()) return;
    UpdatePuppetTags();
  }

  // Guards against "The multiplayer instance isn't currently active" error spam from
  // IsMultiplayerAuthority() after the session ends but before player nodes are freed (see issue #22).
  private bool IsMultiplayerActive() => Multiplayer.MultiplayerPeer != null && Multiplayer.MultiplayerPeer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;
  public override void _EnterTree() => SetMultiplayerAuthority (NetworkId);
  public void SetInputEnabled (bool isEnabled) => _isInputEnabled = isEnabled;
  private bool IsFalling() => !IsOnFloor();
  private bool IsJumping() => _isInputEnabled && _jumpTimer.IsStopped() && Input.IsActionJustPressed ("jump") && IsOnFloor();
  private bool IsFullAutoActive() => _fullAutoSecondsLeft > 0.0f;
  private bool IsChargingWeapon() => _isInputEnabled && !IsFullAutoActive() && Input.IsActionPressed ("shoot");
  private bool IsDischargingWeapon() => _isInputEnabled && _energyWeapon.IsSpinningUp && Input.IsActionJustReleased ("shoot");
  private void Fall (ref Vector3 velocity, double delta) => velocity += Gravity * (float)delta;
  private void ChargeWeapon() => _energyWeapon.Charge();
  private void DischargeWeapon() => _energyWeapon.Discharge();
  private void SetColor (Color color) => (_mesh.GetSurfaceOverrideMaterial (0) as StandardMaterial3D)!.AlbedoColor = color;
  private static int CalculateHealthDecrease (float energyShot) => Mathf.Min (100, Mathf.RoundToInt (energyShot * 100.0f));

  public override void _Ready()
  {
    _spawnRoom = GetNode <Node3D> ("/root/World/SpawnRoom");
    _laserBoltScene = ResourceLoader.Load <PackedScene> ("res://core/weapons/LaserBolt.tscn");
    _mesh = GetNode <MeshInstance3D> ("MeshInstance3D");
    _energyWeapon = GetNode <EnergyWeapon> ("Camera3D/EnergyWeapon");
    _crossHairs = GetNode <Sprite3D> ("Camera3D/Crosshairs");
    _jumpTimer = GetNode <Timer> ("JumpTimer");
    _hitRedTimer = GetNode <Timer> ("HitRedTimer");
    _nameTag = GetNode <Label3D> ("NameTag");
    _healthTag = GetNode <Sprite3D> ("HealthTag");
    _healthBar = GetNode <ProgressBar> ("SubViewport/HealthBar");
    _healthBar.MaxValue = MaxHealth;
    _health = MaxHealth;
    _healthBar.Value = _health;

    if (!IsMultiplayerAuthority())
    {
      UpdateNameTag();
      _hitRedTimer.Timeout += () => SetColor (SpawnArmor ? SpawnArmorColor : NormalColor);
      _crossHairs.Hide();
      SetColor (SpawnArmor ? SpawnArmorColor : NormalColor);
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
    ActivateSpawnArmor();
  }

  public override void _PhysicsProcess (double delta)
  {
    if (!IsMultiplayerActive()) return;
    if (!IsMultiplayerAuthority()) return;
    UpdateSpawnArmor();
    UpdateFullAuto (delta);
    var velocity = Velocity;
    if (IsFalling()) Fall (ref velocity, delta);
    if (IsJumping()) Jump (ref velocity);
    Move (ref velocity);
    Velocity = velocity;
    if (!MoveAndSlide()) return;
    HandleCollisions();
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
    _fullAutoCooldownLeft = Mathf.Max (0.0f, _fullAutoCooldownLeft - dt);

    if (_isInputEnabled && Input.IsActionJustPressed ("ability") && _fullAutoCooldownLeft <= 0.0f)
    {
      _fullAutoSecondsLeft = FullAutoDurationSeconds;
      _fullAutoCooldownLeft = FullAutoCooldownSeconds;
      _nextAutoShotIn = 0.0f;
    }

    if (!IsFullAutoActive()) return;
    _fullAutoSecondsLeft -= dt;
    _nextAutoShotIn -= dt;
    if (!_isInputEnabled || !Input.IsActionPressed ("shoot") || _nextAutoShotIn > 0.0f) return;
    _nextAutoShotIn = FullAutoShotIntervalSeconds;
    _energyWeapon.FireLowPower (FullAutoEnergy);
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
    RotateY (-motionEvent.Relative.X * MouseSensitivity);
    _camera.RotateX (-motionEvent.Relative.Y * MouseSensitivity);
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
    CancelSpawnArmorIfFired();
    var direction = -_camera.GlobalTransform.Basis.Z;
    var origin = _camera.GlobalPosition + direction * 0.9f;
    SpawnBolt (origin, direction, energy, isLive: true);
    Rpc (MethodName.SpawnVisualLaser, origin, direction, energy);
  }

  // Visual-only copy of the shooter's bolt on every other peer.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void SpawnVisualLaser (Vector3 origin, Vector3 direction, float energy) => SpawnBolt (origin, direction, energy, isLive: false);

  private void SpawnBolt (Vector3 origin, Vector3 direction, float energy, bool isLive)
  {
    var bolt = _laserBoltScene.Instantiate <LaserBolt>();
    GetParent().AddChild (bolt);
    bolt.Launch (origin, direction, energy, isLive, this);
    if (isLive) bolt.HitPlayer += OnLaserHitPlayer;
  }

  private void OnLaserHitPlayer (CharacterBody3D body, float energy)
  {
    if (body is not Player victim || victim.NetworkId == NetworkId) return;
    HitPuppet (victim, energy);
  }

  // The victim is the single owner of its health: the shooter only reports the hit,
  // the victim applies damage & replicates Health to everyone, & tells the shooter
  // when it scored a kill (see issue #20).
  private void HitPuppet (Player playerPuppet, float energy)
  {
    GD.Print ($"{DisplayName}: I hit {playerPuppet.DisplayName}!");
    playerPuppet.RpcId (playerPuppet.NetworkId, MethodName.ReceiveHit, energy, DisplayName);
  }

  private void FlashHitColor()
  {
    SetColor (HitColor);
    _hitRedTimer.Start();
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void ReceiveHit (float energy, string shotByPlayerName)
  {
    if (!IsMultiplayerAuthority()) return;
    if (SpawnArmor) return;
    var shooterId = Multiplayer.GetRemoteSenderId();
    // Difficulty damage handicap: lower-skill attackers hit higher-skill targets harder
    // (+50% per tier gap: Beginner->Intermediate 1.5x, Beginner->Expert 2x,
    // Intermediate->Expert 1.5x). Attacking downward is unchanged - the bigger health
    // pool already is the handicap.
    var shooter = GetParent().GetNodeOrNull <Player> ($"{shooterId}");
    var handicap = 1.0f + 0.5f * Mathf.Max (0, TierOf (MaxHealth) - TierOf (shooter?.MaxHealth ?? MaxHealth));
    Health -= Mathf.RoundToInt (CalculateHealthDecrease (energy) * handicap);
    GD.Print ($"{DisplayName}: I was hit by {shotByPlayerName}! Health {Health}");

    if (Health <= 0)
    {
      GetParent().GetNodeOrNull <Player> ($"{shooterId}")?.RpcId (shooterId, MethodName.NotifyScored, DisplayName);
      RespawnShot (shotByPlayerName);
    }

    EmitSignal (SignalName.HealthChanged, Health);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void NotifyScored (string shotPlayerName)
  {
    if (!IsMultiplayerAuthority()) return;
    ++Score;
    GD.Print ($"{DisplayName}: I scored!");
    EmitSignal (SignalName.Scored, DisplayName, shotPlayerName);
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
    GD.Print ($"{DisplayName}: I respawned!");
    await ToSignal (GetTree().CreateTimer (RespawnInputLockSeconds), SceneTreeTimer.SignalName.Timeout);
    SetInputEnabled (isEnabled: true);
  }

  private Vector3 CalculateRandomSpawnPosition()
  {
    var offset = new Vector3 (_rng.RandfRange (-4.0f, 4.0f), 1.0f, _rng.RandfRange (-4.0f, 4.0f));
    return _spawnRoom.Position + offset;
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

  private void UpdateNameTag()
  {
    if (_nameTag == null) return;
    _nameTag.Text = _displayName;
  }
}
