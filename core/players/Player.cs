using com.forerunnergames.energyshot.items;
using com.forerunnergames.energyshot.utilities;
using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.players;

// Core state & lifecycle: exports, signals, replicated properties, node wiring, & thin
// per-frame dispatchers. Behavior lives in Player.Movement.cs, Player.Combat.cs, &
// Player.Cosmetics.cs.
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
  [Signal] public delegate void BreadEatenEventHandler (string playerName);
  [Signal] public delegate void PunchedEventHandler();
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
      if (_mesh != null) RestoreBaseColor();
    }
  }

  // Replicated like SpawnArmor so every peer renders (& can shoot at) the horizontal
  // slide pose (see issue #41).
  [Export]
  public bool Sliding
  {
    get => _sliding;
    set
    {
      _sliding = value;
      ApplySlidePose();
    }
  }

  // Replicated like Sliding so every peer renders (& can shoot at) the shorter
  // crouched hitbox (see issue #51).
  [Export]
  public bool Crouching
  {
    get => _crouching;
    set
    {
      _crouching = value;
      ApplyCrouchScale();
    }
  }

  // Current zap streak, replicated so every peer can render the "on fire" glow &
  // the pulsing leaderboard entry at 3+ (see issue #77).
  [Export]
  public int ZapStreakCount
  {
    get => _zapStreakCount;
    set
    {
      _zapStreakCount = value;
      ApplyStreakGlow();
    }
  }

  public bool IsOnStreak => ZapStreakCount >= 3;

  [Export] public float SpawnArmorSeconds = 5.0f;
  [Export] public float MouseSensitivity = 0.0025f;
  [Export] public float FullAutoDurationSeconds = 3.0f;
  [Export] public float FullAutoCooldownSeconds = 15.0f;
  [Export] public float FullAutoShotIntervalSeconds = 0.15f;
  [Export] public float FullAutoEnergy = 0.12f;
  // Brief - the spawn room & spawn armor already prevent instant re-engagement (#48).
  [Export] public float RespawnInputLockSeconds = 0.3f;
  [Export] public float PunchCooldownSeconds = 0.6f;
  // Longer than physically normal - close-range fights were too hard to land (issue #71).
  [Export] public float PunchRange = 4.0f;
  [Export] public float PunchEnergy = 0.2f;
  [Export] public float PunchDropChance = 0.2f;
  [Export] public float BananaBlastRadius = 6.0f;
  [Export] public float BananaDirectRadius = 1.5f;
  [Export] public float BananaBlastEnergy = 0.9f;
  [Export] public float BananaKnockbackSpeed = 18.0f;
  [Export] public float CameraKickRadians = 0.06f;
  [Export] public float CameraKickRecoverySpeed = 0.4f;
  [Export] public float Speed = 7.0f;
  [Export] public float SlideSpeedMultiplier = 2.0f;
  [Export] public float SlideDurationSeconds = 5.0f;
  [Export] public float SlideCooldownSeconds = 5.0f;
  [Export] public float SlideCameraHeight = 0.6f;
  [Export] public float CrouchHeightScale = 0.6f;
  [Export] public float CrouchSpeedMultiplier = 0.3f;
  [Export] public float RocketBoostMultiplier = 1.5f;
  [Export] public float RocketBoostRange = 3.0f;
  [Export] public float KnockbackStrength = 16.0f;
  [Export] public int KillHealAmount = 50;
  [Export] public float PunchKnockbackScale = 0.33f;
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
  public float LastZapEnergy { get; private set; }
  private readonly RandomNumberGenerator _rng = new();
  private bool _spawnArmor;
  private ulong _spawnArmorEndMs;
  private bool _sliding;
  private bool _crouching;
  private int _zapStreakCount;
  private OmniLight3D _streakLight = null!;
  private float _slideSecondsLeft;
  private float _slideCooldownLeft;
  private float _standingCameraHeight;
  private CollisionShape3D _collisionShape = null!;
  private NetworkManager _networkManager = null!;
  private Node3D _spawnRoom = null!;
  private MeshInstance3D _mesh = null!;
  private PackedScene _laserBoltScene = null!;
  private PackedScene _bananaProjectileScene = null!;
  private EnergyWeapon _energyWeapon = null!;
  private BananaLauncher _bananaLauncher = null!;
  private readonly Bread _bread = new();
  private bool _isBananaEquipped;
  private float _fullAutoSecondsLeft;
  private float _fullAutoCooldownLeft;
  private float _nextAutoShotIn;
  private float _punchCooldownLeft;
  private float _cameraKickRemaining;
  private AudioStreamPlayer _punchSound = null!;
  private AudioStreamPlayer _hitmarkerSound = null!;
  private AudioStreamPlayer _damageSound = null!;
  private AudioStreamPlayer _zapOutSound = null!;
  private AudioStreamPlayer _respawnSound = null!;
  private AudioStreamPlayer _jumpSound = null!;
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
  public override void _EnterTree() => SetMultiplayerAuthority (NetworkId);
  public void SetInputEnabled (bool isEnabled) => _isInputEnabled = isEnabled;
  // Guards against "The multiplayer instance isn't currently active" error spam from
  // IsMultiplayerAuthority() after the session ends but before player nodes are freed (see issue #22).
  private bool IsMultiplayerActive() => Multiplayer.MultiplayerPeer != null && Multiplayer.MultiplayerPeer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;

  public override void _Ready()
  {
    _spawnRoom = GetNode <Node3D> ("/root/World/SpawnRoom");
    _laserBoltScene = ResourceLoader.Load <PackedScene> ("res://core/weapons/LaserBolt.tscn");
    _bananaProjectileScene = ResourceLoader.Load <PackedScene> ("res://core/weapons/BananaProjectile.tscn");
    _mesh = GetNode <MeshInstance3D> ("MeshInstance3D");
    _collisionShape = GetNode <CollisionShape3D> ("CollisionShape3D");
    _energyWeapon = GetNode <EnergyWeapon> ("Camera3D/EnergyWeapon");
    _bananaLauncher = GetNode <BananaLauncher> ("Camera3D/BananaLauncher");
    UpdateWeaponVisibility();
    CreateHands();
    _crossHairs = GetNode <Sprite3D> ("Camera3D/Crosshairs");
    _jumpTimer = GetNode <Timer> ("JumpTimer");
    _hitRedTimer = GetNode <Timer> ("HitRedTimer");
    _nameTag = GetNode <Label3D> ("NameTag");
    _healthTag = GetNode <Sprite3D> ("HealthTag");
    _streakLight = GetNode <OmniLight3D> ("StreakLight");
    _healthBar = GetNode <ProgressBar> ("SubViewport/HealthBar");
    _punchSound = GetNode <AudioStreamPlayer> ("PunchSound");
    _hitmarkerSound = GetNode <AudioStreamPlayer> ("HitmarkerSound");
    _damageSound = GetNode <AudioStreamPlayer> ("DamageSound");
    _zapOutSound = GetNode <AudioStreamPlayer> ("ZapOutSound");
    _respawnSound = GetNode <AudioStreamPlayer> ("RespawnSound");
    _jumpSound = GetNode <AudioStreamPlayer> ("JumpSound");
    _healthBar.MaxValue = MaxHealth;
    _health = MaxHealth;
    _healthBar.Value = _health;

    if (!IsMultiplayerAuthority())
    {
      UpdateNameTag();
      _hitRedTimer.Timeout += RestoreBaseColor;
      _crossHairs.Hide();
      RestoreBaseColor();
      ApplySlidePose();
      return;
    }

    _rng.Randomize();
    _localPlayer = this;
    _healthBar.Hide();
    _nameTag.Hide();
    _energyWeapon.ShotFired += OnWeaponShotFired;
    _camera = GetNode <Camera3D> ("Camera3D");
    _camera.Current = true;
    _standingCameraHeight = _camera.Position.Y;
    _isInputEnabled = true;
    Input.MouseMode = Input.MouseModeEnum.Captured;
    Position = CalculateRandomSpawnPosition();
    ActivateSpawnArmor();
  }

  public override void _ExitTree()
  {
    base._ExitTree();
    if (!IsMultiplayerAuthority() || _localPlayer != this) return;
    _localPlayer = null;
  }

  public override void _Process (double delta)
  {
    if (!IsMultiplayerActive()) return;
    UpdatePuppetTags();
    UpdateCrosshairTint();
  }

  public override void _PhysicsProcess (double delta)
  {
    if (!IsMultiplayerActive()) return;
    if (!IsMultiplayerAuthority()) return;
    UpdateSpawnArmor();
    UpdateWeaponSelection();
    UpdateBananaLauncher();
    UpdateBread();
    UpdateFullAuto (delta);
    UpdatePunch (delta);
    UpdateCameraKick (delta);
    UpdateCameraShake (delta);
    UpdateStun (delta);
    UpdateSlide (delta);
    UpdateCrouch();
    var velocity = Velocity;
    if (IsFalling()) Fall (ref velocity, delta);
    if (IsJumping()) Jump (ref velocity);
    Move (ref velocity);
    Velocity = velocity;
    if (!MoveAndSlide()) return;
    HandleCollisions();
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
}
