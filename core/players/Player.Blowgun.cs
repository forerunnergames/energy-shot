using com.forerunnergames.energyshot.ui.hud;
using com.forerunnergames.energyshot.weapons;
using com.forerunnergames.energyshot.utilities;
using Godot;

namespace com.forerunnergames.energyshot.players;

// The blowgun (issues #194 & #236): the game's silenced sniper rifle, funny because a
// blowgun has a scope. Slot 8. Right click looks THROUGH the scope (the HUD draws the
// scope view); the wheel steps the zoom in & out, very far; the reticle drifts more
// the further in you are & only settles for about a second between heartbeats - you
// time the shot. Left click puffs a poison dart toward the reticle (no impact damage,
// the poison is the weapon). Ammo: the gun starts EMPTY & fires from a replicated
// dart count refilled by walking over darts while holding it (the dart economy); out
// of darts it swings as a club (Player.Blunt, issue #249). No recoil. The shooter hears the shot locally; bystanders only within a few feet.
public partial class Player
{
  [Export] public float BlowgunDartSpeed = 42.0f;
  [Export] public float BlowgunCooldownSeconds = 1.5f;
  [Export]
  public int BlowgunDarts
  {
    get => _blowgunDarts;
    set
    {
      if (_blowgunDarts != value) LogReplicatedChangeOnServer ($"blowgun darts: {DisplayName} {_blowgunDarts} -> {value}");
      _blowgunDarts = value;
    }
  }

  private int _blowgunDarts;
  private Node3D _blowgunHeld = null!;
  private AudioStreamPlayer3D _blowgunShotSound = null!;
  private AudioStreamPlayer _blowgunOwnShotSound = null!;
  private AudioStreamPlayer _heartbeatSound = null!;
  private float _blowgunCooldownLeft;
  private float _unscopedFovDegrees;
  private bool _isScoped;
  private bool _holdToScope;

  public void RefreshScopeMode() => _holdToScope = Settings.HoldToScope; // Applies immediately from the pause menu (issue #290).

  // Hold-vs-toggle scoping (issue #290), one pure decision: hold mode tracks the
  // button exactly; toggle mode flips on each fresh press; losing the gun unscopes.
  public static bool NextScoped (bool canScope, bool holdMode, bool pressed, bool justPressed, bool current) => canScope && (holdMode ? pressed : justPressed ? !current : current);
  private int _zoomStep;
  private float _scopeTime;
  private float _sinceBeat;

  public bool HasBlowgun => Holds (HeldWeapon.Blowgun);
  public bool IsBlowgunSelected => SelectedWeapon == SelectedWeapon.Blowgun;
  public bool IsScoped => _isScoped;
  public int ZoomStep => _zoomStep;
  public bool IsScopeSettled => _isScoped && Scope.IsSettled (_sinceBeat);
  // The reticle's offset from screen center as a fraction of the scope radius (HUD
  // draws it there; the dart flies toward it).
  public Vector2 ReticleDrift => _isScoped ? Scope.Wander (_scopeTime) * Scope.DriftFraction (_zoomStep) * Scope.BeatEnvelope (_sinceBeat) : Vector2.Zero;

  private void CreateBlowgunHeld()
  {
    _blowgunHeld = BlowgunDart.CreateBlowgunVisual();
    // Outside the body capsule (issue #303, Escendrix): x 0.32 sat inside the 0.5m
    // radius, so in third person the body mesh swallowed the whole gun. Same shelf as
    // the slingshot now - visible from every angle, still framed right in first person.
    _blowgunHeld.Position = new Vector3 (0.5f, -0.35f, -0.75f);
    var camera = GetNode <Camera3D> ("Camera3D"); // Fetched directly: held-model creators run before _Ready assigns _camera.
    camera.AddChild (_blowgunHeld);
    _unscopedFovDegrees = camera.Fov;
    // Bystanders hear the puff only within a few feet (the stealth rule, issue #194)...
    _blowgunShotSound = new AudioStreamPlayer3D { Stream = ResourceLoader.Load <AudioStream> ("res://assets/sounds/blowgun-blow.mp3"), UnitSize = 1.2f, MaxDistance = 3.0f, MaxPolyphony = 3 }; // Real air blow (Aaron, 2026-08-22): Pixabay; the stealth radius stays.
    AddChild (_blowgunShotSound);
    // ...while the shooter always hears their own shot, locally (issue #236).
    _blowgunOwnShotSound = new AudioStreamPlayer { Stream = ResourceLoader.Load <AudioStream> ("res://assets/sounds/blowgun-blow.mp3"), MaxPolyphony = 3 };
    AddChild (_blowgunOwnShotSound);
    _heartbeatSound = new AudioStreamPlayer { Stream = ProceduralSounds.Heartbeat(), VolumeDb = -4.0f };
    AddChild (_heartbeatSound);
  }

  // Gated like every action predicate: input, stance, stun, & ritual states all block.
  private bool CanFireBlowgun() => _isInputEnabled && !Dancing && !Fallen && !Eating && IsBlowgunSelected && HasBlowgun && _blowgunCooldownLeft <= 0.0f;

  private void UpdateBlowgun (double delta)
  {
    _blowgunCooldownLeft = Mathf.Max (0.0f, _blowgunCooldownLeft - (float)delta);
    UpdateScope ((float)delta);
    if (!CanFireBlowgun() || !Input.IsActionJustPressed ("shoot")) return;
    if (BlowgunDarts <= 0) { _blowgunCooldownLeft = ClubCooldownSeconds; SwingClub (_blowgunHeld); return; } // Empty: it's a club (issue #249).
    FireBlowgunDart();
  }

  // Scoping (issue #236): right click toggles the through-the-scope view; the wheel
  // steps zoom while scoped (weapon cycling is suspended then, see UpdateWeaponSelection).
  // Scoping forces first person - you can't look through a scope from behind yourself.
  private void UpdateScope (float dt)
  {
    var canScope = _isInputEnabled && !Fallen && IsBlowgunSelected && HasBlowgun;
    var next = NextScoped (canScope, _holdToScope, Input.IsActionPressed ("scope"), Input.IsActionJustPressed ("scope"), _isScoped);
    if (next != _isScoped) SetScoped (next);
    if (!_isScoped) return;
    _scopeTime += dt;
    _sinceBeat += dt;
    if (_sinceBeat >= Scope.BeatPeriodSeconds) { _sinceBeat = 0.0f; _heartbeatSound.Play(); }
    if (Input.IsActionJustPressed ("cycle_weapon_next")) SetZoomStep (Scope.StepIn (_zoomStep));
    if (Input.IsActionJustPressed ("cycle_weapon_previous")) SetZoomStep (Scope.StepOut (_zoomStep));
  }

  private void SetScoped (bool scoped)
  {
    _isScoped = scoped;
    if (scoped && IsThirdPerson) SetThirdPerson (false);
    _sinceBeat = 0.0f;
    if (scoped) _heartbeatSound.Play();
    SetZoomStep (scoped ? 0 : _zoomStep);
    if (!scoped) _camera.Fov = _unscopedFovDegrees;
    _blowgunHeld.Visible = !scoped && IsBlowgunSelected && HasBlowgun; // The gun is at your eye, not in view.
    GetNode <Node3D> ("Camera3D/Crosshairs").Visible = !scoped; // The scope's reticle replaces the crosshair.
  }

  private void SetZoomStep (int step)
  {
    _zoomStep = step;
    if (_isScoped) _camera.Fov = Scope.ZoomFovs[_zoomStep];
  }

  private void FireBlowgunDart()
  {
    _blowgunCooldownLeft = BlowgunCooldownSeconds;
    CancelSpawnArmorIfFired(); // Firing anything drops your spawn armor (issue #48).
    BlowgunDarts -= 1;
    Spawner.SendDartFiredRequest(); // The census counts it as in flight (issue #236).
    _blowgunShotSound.Play();
    _blowgunOwnShotSound.Play();
    var direction = AimDirection();
    var sweepStart = _camera.GlobalPosition;
    var origin = sweepStart + direction * MuzzleOffsetMeters;
    SpawnDart (origin, sweepStart, direction, isLive: true);
    Rpc (MethodName.SpawnVisualDart, origin, sweepStart, direction);
  }

  // The whole scoped view sways with the heartbeat (issue #279, thepro & Caleb):
  // the drift rotates the CAMERA & the laser dot stays screen-centered, so where the
  // dot points is where the dart goes - the aim ray is just the center ray now.
  private Vector3 AimDirection() => -_camera.GlobalTransform.Basis.Z;

  // The same wander that used to move the dot, as a camera rotation: a screen offset
  // of drift * scope-radius equals drift * RadiusFraction of the FOV, so the sway
  // auto-scales with zoom & the difficulty is exactly what the drifting dot was.
  public static Vector2 SwayRadians (Vector2 drift, float fovDegrees) => drift * (Mathf.DegToRad (fovDegrees) * ScopeView.RadiusFraction);

  private Vector2 _appliedSway;

  private void UpdateScopeSway()
  {
    var target = _isScoped ? SwayRadians (ReticleDrift, _camera.Fov) : Vector2.Zero;
    var rotation = _camera.Rotation;
    rotation.X -= target.Y - _appliedSway.Y; // Screen-down drift pitches the view down.
    rotation.Y -= target.X - _appliedSway.X; // Screen-right drift yaws the view right.
    _camera.Rotation = rotation;
    _appliedSway = target;
  }

  // Every peer flies a cosmetic copy (the SpawnVisualLaser pattern): the whoosh has
  // to pass NEAR each listener locally for the stealth audio to work.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void SpawnVisualDart (Vector3 origin, Vector3 sweepStart, Vector3 direction) => SpawnDart (origin, sweepStart, direction, isLive: false);

  private void SpawnDart (Vector3 origin, Vector3 sweepStart, Vector3 direction, bool isLive)
  {
    var dart = new BlowgunDart();
    GetParent().AddChild (dart);
    dart.Launch (origin, sweepStart, direction, BlowgunDartSpeed, isLive, this);
    if (!isLive) return;
    dart.HitPlayer += OnDartHitPlayer;
    dart.Landed += position => Spawner.SendDartLandRequest (position); // A miss becomes a ground dart (issue #248).
  }

  // Victim-authoritative like every damage path: the shooter only reports the hit.
  private void OnDartHitPlayer (Player victim)
  {
    PlayHitmarker (false);
    if (victim.NetworkId == Multiplayer.GetUniqueId()) return; // Can't dart yourself.
    victim.RpcId (victim.NetworkId, MethodName.ReceiveDartHit, DisplayName);
  }

  // Losing the blowgun (drop, theft, death) returns its darts to the level's census:
  // the caps respawn them as floating pickups. Called from the HeldWeapon setter path.
  private void SpillBlowgunDarts()
  {
    if (!IsMultiplayerAuthority()) return;
    BlowgunDarts = 0;
    if (_isScoped) SetScoped (false);
  }

  // The server confirmed a walk-over pickup of a ground dart while holding the blowgun.
  // The host's own player gets the direct call (an RpcId to yourself is a no-op).
  public void ConfirmDartAmmoSelf() => ConfirmDartAmmo();

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void ConfirmDartAmmo()
  {
    if (Multiplayer.GetRemoteSenderId() != 1 && Multiplayer.GetRemoteSenderId() != 0) return;
    if (!IsMultiplayerAuthority()) return;
    ++BlowgunDarts;
    _weaponPickupSound.Play();
  }
}
