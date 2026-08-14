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
  // Soft feedback for a bread press that can't eat (issue #160): isOut = the loaf is
  // gone this life; otherwise the player is already at full health.
  [Signal] public delegate void BreadDeniedEventHandler (bool isOut);
  [Signal] public delegate void PunchedEventHandler();
  [Signal] public delegate void ScoredEventHandler (string playerName, string shotPlayerName);
  [Signal] public delegate void RespawnedShotEventHandler (string playerName, string shotByPlayerName);
  [Signal] public delegate void RespawnedFellEventHandler (string playerName);
  // Replicated like Health so every peer can render the leaderboard. Never clamped:
  // fall penalties can take it negative (issue #108).
  [Export]
  public int Score
  {
    get => _score;
    set
    {
      if (_score != value) LogReplicatedChangeOnServer ($"score: {DisplayName} {_score} -> {value}");
      _score = value;
    }
  }

  // Selected body color (issue #43): an index into PlayerColors, chosen in the
  // host/join dialog & replicated like DisplayName so every peer tints this player's
  // body, fists, & leaderboard entry. Duplicates are allowed - name tags & the crown
  // disambiguate; the flash/glow effects always return to this chosen color.
  [Export]
  public int ColorIndex
  {
    get => _colorIndex;
    set
    {
      _colorIndex = value;
      ApplyChosenColor();
    }
  }

  // Round-trip time to the server in ms, measured server-side once a second &
  // replicated like Score so every peer can render it on the leaderboard (issue
  // #100); -1 = not measured yet.
  [Export] public int PingMs { get; set; } = -1;

  // Only the server measures pings; it tells the owning client, which writes the
  // replicated property (issue #100).
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void ReceivePingMeasurement (int pingMs)
  {
    if (Multiplayer.GetRemoteSenderId() != 1) return;
    if (!IsMultiplayerAuthority()) return;
    PingMs = pingMs;
  }

  // Server-side score & streak logging (issue #111): the replicated property setters
  // run on the server whenever a peer's value syncs there, covering every code path.
  private void LogReplicatedChangeOnServer (string message)
  {
    if (!IsInsideTree() || Multiplayer.MultiplayerPeer is not ENetMultiplayerPeer || !Multiplayer.IsServer()) return;
    ServerLog.Event (NetworkId, message);
  }

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
  // Puppets also track a local display deadline: if the armor-off delta is missed
  // (ON_CHANGE can drop), the white glow still clears on time (issue #114).
  [Export]
  public bool SpawnArmor
  {
    get => _spawnArmor;
    set
    {
      _spawnArmor = value;
      if (value) _armorDisplayEndMs = Time.GetTicksMsec() + (ulong)((SpawnArmorSeconds + ArmorDisplaySlackSeconds) * 1000.0f);
      if (_mesh != null) RestoreBaseColor();
    }
  }

  // Replicated like SpawnArmor so every peer renders (& can shoot at) the horizontal
  // slide pose (see issue #41). Synced ALWAYS, not ON_CHANGE (issue #131): a dropped
  // pose delta is never re-sent, which left remote copies wedged mid-slide forever;
  // always-sync self-heals on the next tick, like position & rotation already do.
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
  // crouched hitbox (see issue #51). Synced ALWAYS for the same self-healing
  // reason as Sliding (issue #131).
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
      if (_zapStreakCount != value) LogReplicatedChangeOnServer ($"streak: {DisplayName} {_zapStreakCount} -> {value}");
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
  // Halved from 0.6 (issue #82): fast enough for combos, not autoclicker-spam fast.
  [Export] public float PunchCooldownSeconds = 0.3f;
  // Longer than physically normal - close-range fights were too hard to land (issue #71).
  [Export] public float PunchRange = 4.0f;
  [Export] public float PunchEnergy = 0.2f;
  [Export] public float PunchDropChance = 0.2f;
  [Export] public float BananaBlastRadius = 6.0f;
  [Export] public float BananaDirectRadius = 1.5f;
  // Doubled from 0.9 (issue #83); the survivable-at-full-health clamp still prevents
  // one-shots from full, except for sticky direct hits.
  [Export] public float BananaBlastEnergy = 1.8f;
  [Export] public float BananaKnockbackSpeed = 18.0f;
  [Export] public float BananaShooterKnockbackSpeed = 12.0f;
  // INSANE on purpose (issue #83): launching a banana should feel like it.
  [Export] public float BananaRecoilRadians = 0.35f;
  [Export] public float StickyBananaSeconds = 1.0f;
  [Export] public float StickyLaunchSpeed = 60.0f;
  // 200 damage: one-hit-kills an Expert, bypassing the survivable clamp (issue #83).
  [Export] public float StickyBananaEnergy = 2.0f;
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
  // Longer reach for the single airborne boost, so shooting the ground below a jump
  // still connects (issue #86).
  [Export] public float AirRocketBoostRange = 8.0f;
  [Export] public float KnockbackStrength = 16.0f;
  // Hits shove mostly horizontally (issue #163): knockback never pushes upward speed
  // past this, so stacked or full-draw hits can't launch victims sky-high.
  [Export] public float KnockbackUpPopCap = 6.0f;
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
  // Whether the one-per-life loaf is still uneaten, for the HUD bread icon (issue #160).
  public bool HasBread => _bread.IsAvailable;
  public float LastZapEnergy { get; private set; }
  // Whether the last zap this player took came through a pierced barrier (issue #94).
  public bool LastZapThroughBarrier { get; private set; }
  private const float ArmorDisplaySlackSeconds = 1.0f;
  private const float CrosshairPulseSeconds = 0.4f;
  private const float CrosshairPulseScale = 1.8f;
  private static readonly Color FullChargeCrosshairColor = new(1.0f, 0.2f, 0.1f);
  private readonly RandomNumberGenerator _rng = new();
  private bool _spawnArmor;
  private ulong _spawnArmorEndMs;
  private ulong _armorDisplayEndMs;
  private ulong _crosshairPulseEndMs;
  private Vector3 _crosshairBaseScale;
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
  private bool _airBoostUsed;
  private float _fullAutoSecondsLeft;
  private float _fullAutoCooldownLeft;
  private float _nextAutoShotIn;
  private float _punchCooldownLeft;
  private float _cameraKickRemaining;
  private AudioStreamPlayer _punchSound = null!;
  private AudioStreamPlayer _punchWhiffSound = null!;
  private AudioStreamPlayer _punchThudSound = null!;
  private AudioStreamPlayer _weaponPickupSound = null!;
  private AudioStreamPlayer _throughWallZapSound = null!;
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
  private int _colorIndex;
  private int _health;
  private int _score;
  private int _maxHealth = 200;
  private bool _isInputEnabled;
  private static Player? _localPlayer;
  // The pickup claim fallback needs the local player without a scene search (issue #110).
  public static Player? Local => _localPlayer;
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
    _launcherRestPosition = _bananaLauncher.Position;
    CreateBoomerangHeld(); // Code-built held model, no scene asset (issue #98).
    CreateSlingshotHeld(); // Same, for the slot-5 slingshot (issue #99).
    CreateAirplaneHeld(); // Same, for the slot-6 paper airplane (issue #102).
    UpdateWeaponVisibility();
    CreateHands();
    _crossHairs = GetNode <Sprite3D> ("Camera3D/Crosshairs");
    _jumpTimer = GetNode <Timer> ("JumpTimer");
    _hitRedTimer = GetNode <Timer> ("HitRedTimer");
    _nameTag = GetNode <Label3D> ("NameTag");
    _crown = GetNodeOrNull <Node3D> ("Crown");
    _healthTag = GetNode <Sprite3D> ("HealthTag");
    _streakLight = GetNode <OmniLight3D> ("StreakLight");
    _healthBar = GetNode <ProgressBar> ("SubViewport/HealthBar");
    _punchSound = GetNode <AudioStreamPlayer> ("PunchSound");
    _punchWhiffSound = GetNode <AudioStreamPlayer> ("PunchWhiffSound");
    _punchThudSound = GetNode <AudioStreamPlayer> ("PunchThudSound");
    _weaponPickupSound = GetNode <AudioStreamPlayer> ("WeaponPickupSound");
    _throughWallZapSound = GetNode <AudioStreamPlayer> ("ThroughWallZapSound");
    _hitmarkerSound = GetNode <AudioStreamPlayer> ("HitmarkerSound");
    _damageSound = GetNode <AudioStreamPlayer> ("DamageSound");
    _zapOutSound = GetNode <AudioStreamPlayer> ("ZapOutSound");
    _respawnSound = GetNode <AudioStreamPlayer> ("RespawnSound");
    _jumpSound = GetNode <AudioStreamPlayer> ("JumpSound");
    // Rapid-retrigger sfx mix instead of cutting each other off (issue #182): the
    // default polyphony (1) restarts the stream on every play, glitching quick
    // punches, full-auto hitmarker bursts, & back-to-back damage hits; extra voices
    // let overlapping plays ring out their tails.
    foreach (var sound in new[] { _punchSound, _punchWhiffSound, _punchThudSound, _weaponPickupSound, _throughWallZapSound, _hitmarkerSound, _damageSound, _jumpSound }) sound.MaxPolyphony = 6;
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
      // Spawn-state sync runs before _Ready, when the glow node refs were still null,
      // so re-apply it here or a late joiner never sees an existing streak (issue #88).
      ApplyStreakGlow();
      return;
    }

    _rng.Randomize();
    _localPlayer = this;
    ApplyFirstPersonWeaponOverlay(); // Own weapons draw over walls (issue #124).
    _healthBar.Hide();
    _nameTag.Hide();
    _energyWeapon.ShotFired += OnWeaponShotFired;
    _energyWeapon.FullChargeReached += OnFullChargeReached;
    _crosshairBaseScale = _crossHairs.Scale;
    _camera = GetNode <Camera3D> ("Camera3D");
    _camera.Current = true;
    _standingCameraHeight = _camera.Position.Y;
    _isInputEnabled = true;
    Input.MouseMode = Input.MouseModeEnum.Captured;
    Position = CalculateRandomSpawnPosition();
    ActivateSpawnArmor();
    ApplySavedViewPreference(); // Third-person view survives restarts (issue #119).
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
    ClearStaleArmorDisplay();
  }

  public override void _PhysicsProcess (double delta)
  {
    if (!IsMultiplayerActive()) return;
    if (!IsMultiplayerAuthority()) return;
    UpdateSpawnArmor();
    UpdateXrayReveal();
    UpdateViewToggle(); // Third-person toggle on V (issue #119).
    UpdateWeaponSelection();
    CancelStaleLaserCharge(); // Leaving the laser slot cancels the charge (issue #156).
    UpdateBananaLauncher();
    UpdateBoomerang();
    UpdateSlingshot (delta); // Draw-&-release stones (issue #99).
    UpdateAirplane(); // Homing glider throws (issue #102).
    UpdateAirplaneCatchWindow(); // An open swing keeps grabbing briefly (issue #102).
    UpdateBread();
    UpdateFullAuto (delta);
    UpdatePunch (delta);
    UpdateDance (delta); // After the fire/punch updates, so a canceling press can't also attack (issue #103).
    UpdateHandBob (delta);
    UpdateAirBoost();
    UpdateStickyFlight (delta);
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
