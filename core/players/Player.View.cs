using com.forerunnergames.energyshot.utilities;
using Godot;

namespace com.forerunnergames.energyshot.players;

// Third-person chase view (issue #119): V toggles between the first-person view & a
// camera behind/above the head. The "Camera3D" head node stays the aim source & its
// replicated transform is untouched - the chase camera is a separate local-only rig
// hung off it - so shots, punches, rocket boosts, & the crosshair ray behave
// identically in both views, & remote players see nothing change.
public partial class Player
{
  [Export] public float ThirdPersonBackMeters = 2.8f;
  [Export] public float ThirdPersonUpMeters = 1.2f;
  // Death view (issue #152): a wider pull-back over the death spot than the chase view.
  [Export] public float DeathViewBackMeters = 6.0f;
  [Export] public float DeathViewUpMeters = 3.0f;
  // The crosshair sprite sits 10m down the aim ray (Player.tscn); the chase camera
  // pitches down just enough to keep it centered on screen.
  private const float CrosshairDistanceMeters = 10.0f;
  private bool _thirdPerson;
  private bool _deathView;
  private SpringArm3D? _thirdPersonArm;
  private Camera3D? _thirdPersonCamera;
  // The playtest toggles mid-run & asserts bolts still spawn (issue #119).
  public bool IsThirdPerson => _thirdPerson;
  // The playtest asserts the death camera goes live during the lie-down (issue #152).
  public bool IsDeathViewActive => _deathView && _thirdPersonCamera is { Current: true };

  private void ApplySavedViewPreference()
  {
    if (Settings.ThirdPersonView) SetThirdPerson (enabled: true);
  }

  private void UpdateViewToggle()
  {
    if (!_isInputEnabled || !Input.IsActionJustPressed ("toggle_view")) return;
    SetThirdPerson (!_thirdPerson);
    Settings.ThirdPersonView = _thirdPerson; // The chosen view survives restarts (issue #119).
  }

  private void SetThirdPerson (bool enabled)
  {
    _thirdPerson = enabled;
    if (enabled && _thirdPersonCamera == null) CreateThirdPersonRig();
    // The #124 draw-over-walls weapon overlay is a first-person trick only: in third
    // person the own weapons & hands render normally, like every other peer sees them.
    SetFirstPersonOverlayEnabled (!enabled);
    (enabled ? _thirdPersonCamera! : _camera).Current = true;
  }

  // The SpringArm3D pulls the chase camera in whenever world geometry (layer 1) sits
  // behind the head, so walls never end up between the camera & the player or clip
  // it inside them. Created in code, local-only: the replicated Camera3D transform &
  // the synchronizer's property indices stay untouched, & the rig inherits crouch/
  // slide camera heights, camera kick, & aim pitch for free.
  private void CreateThirdPersonRig()
  {
    _thirdPersonArm = new SpringArm3D { Position = new Vector3 (0.0f, ThirdPersonUpMeters, 0.0f), SpringLength = ThirdPersonBackMeters, CollisionMask = 1, Margin = 0.3f };
    _thirdPersonArm.AddExcludedObject (GetRid());
    _camera.AddChild (_thirdPersonArm);
    _thirdPersonCamera = new Camera3D { Rotation = new Vector3 (ChaseViewPitch(), 0.0f, 0.0f) };
    _thirdPersonArm.AddChild (_thirdPersonCamera);
  }

  private float ChaseViewPitch() => -Mathf.Atan (ThirdPersonUpMeters / (ThirdPersonBackMeters + CrosshairDistanceMeters));

  // Death view (issue #152): the same spring-arm rig from #119, stretched back & up
  // over the death spot so the victim can watch their killer emote on the body.
  private void EnterDeathView()
  {
    if (_thirdPersonCamera == null) CreateThirdPersonRig();
    _deathView = true;
    _thirdPersonArm!.Position = new Vector3 (0.0f, DeathViewUpMeters, 0.0f);
    _thirdPersonArm.SpringLength = DeathViewBackMeters;
    SetFirstPersonOverlayEnabled (enabled: false); // Own weapons must not ghost over the scene from up here.
    _thirdPersonCamera!.Current = true;
  }

  // Keeps the fallen body framed however the head was aimed at death; runs only
  // during the lie-down (issue #152).
  private void UpdateDeathView()
  {
    if (!_deathView) return;
    var target = GlobalPosition + Vector3.Up * 0.5f;
    if (target.IsEqualApprox (_thirdPersonCamera!.GlobalPosition)) return;
    _thirdPersonCamera.LookAt (target, Vector3.Up);
  }

  // Respawn restores whichever view the player had chosen (issue #119).
  private void ExitDeathView()
  {
    _deathView = false;
    _thirdPersonArm!.Position = new Vector3 (0.0f, ThirdPersonUpMeters, 0.0f);
    _thirdPersonArm.SpringLength = ThirdPersonBackMeters;
    _thirdPersonCamera!.Rotation = new Vector3 (ChaseViewPitch(), 0.0f, 0.0f);
    SetThirdPerson (_thirdPerson);
  }
}
