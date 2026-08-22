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
  // Over-the-shoulder framing (issue #187): the rig sits to the RIGHT of the head, so
  // your own body hangs toward the lower-left of the frame instead of squatting dead
  // center on top of whatever you're trying to shoot.
  [Export] public float ThirdPersonRightMeters = 0.8f;
  // 1.2m -> 0.5m (issue #187): near eye level rather than looking over the head. The
  // old height read as a slightly top-down view, which made aiming hard.
  [Export] public float ThirdPersonUpMeters = 0.5f;
  // Death view (issue #152): a wider pull-back over the death spot than the chase view.
  [Export] public float DeathViewBackMeters = 6.0f;
  [Export] public float DeathViewUpMeters = 3.0f;
  // The crosshair sprite sits 10m down the aim ray (Player.tscn); the chase camera
  // aims at that exact point so the crosshair stays dead center & truthful.
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
    UpdateHeadVisibility(); // Issue #179.
    (enabled ? _thirdPersonCamera! : _camera).Current = true;
  }

  // The SpringArm3D pulls the chase camera in whenever world geometry (layer 1) sits
  // behind the head, so walls never end up between the camera & the player or clip
  // it inside them. Created in code, local-only: the replicated Camera3D transform &
  // the synchronizer's property indices stay untouched, & the rig inherits crouch/
  // slide camera heights, camera kick, & aim pitch for free.
  private void CreateThirdPersonRig()
  {
    _thirdPersonArm = new SpringArm3D { Position = ChaseViewOffset(), SpringLength = ThirdPersonBackMeters, CollisionMask = 1, Margin = 0.6f }; // Wider margin (issue #234): the near plane never grazes a surface.
    _thirdPersonArm.AddExcludedObject (GetRid());
    _camera.AddChild (_thirdPersonArm);
    _thirdPersonCamera = new Camera3D { Rotation = ChaseViewAim (ThirdPersonBackMeters) };
    _thirdPersonArm.AddChild (_thirdPersonCamera);
  }

  // The spring arm SHORTENS against world geometry (issue #187): an aim computed once
  // from the full length stops pointing at the crosshair the moment the camera is
  // pulled in - which is exactly when you're backed against a wall & need the shot
  // line most. Re-aimed every physics frame from the arm's ACTUAL current length, so
  // the crosshair stays truthful at any extension. The death view owns the rotation
  // itself (it LookAts the body), so it is left alone.
  private void UpdateChaseViewAim()
  {
    if (_deathView || !_thirdPerson || _thirdPersonCamera == null) return;
    _thirdPersonCamera.Rotation = ChaseViewAim (_thirdPersonArm!.GetHitLength());
  }

  // Shoulder & height offset in the head camera's own space (issue #187), so the rig
  // still inherits crouch/slide camera heights, camera kick, & aim pitch for free.
  private Vector3 ChaseViewOffset() => new(ThirdPersonRightMeters, ThirdPersonUpMeters, 0.0f);

  // The chase camera aims at the crosshair point on the HEAD camera's ray (issue
  // #187), compensating both the shoulder offset (yaw) & the height (pitch) - so
  // whatever the crosshair covers is what a bolt fired from the head camera hits, &
  // the view no longer reads top-down. The spring arm hangs the camera at
  // (right, up, backMeters) from the head, so the crosshair sits that far off its
  // axis; backMeters is the arm's CURRENT length, which wall clipping can shorten.
  private Vector3 ChaseViewAim (float backMeters)
  {
    var forward = backMeters + CrosshairDistanceMeters;
    return new Vector3 (-Mathf.Atan (ThirdPersonUpMeters / forward), Mathf.Atan (ThirdPersonRightMeters / forward), 0.0f);
  }

  // Playtest-observable (issue #187): how far off the chase camera's center the
  // crosshair point sits, in degrees. Near zero = the crosshair is truthful to the
  // shot line in third person too. Positive infinity while there's no chase rig.
  public float ChaseViewCrosshairErrorDegrees => _thirdPersonCamera == null ? float.PositiveInfinity : Mathf.RadToDeg ((-_thirdPersonCamera.GlobalTransform.Basis.Z).AngleTo (CrosshairPoint() - _thirdPersonCamera.GlobalPosition));

  // Playtest-observable (issue #187): where our own head sits in the chase camera's
  // frame, in camera-local meters - negative X is left of center & negative Y is
  // below it, which is exactly the over-the-shoulder framing this view is tuned for.
  public Vector3 ChaseViewBodyOffset => _thirdPersonCamera == null ? Vector3.Zero : _thirdPersonCamera.ToLocal (_camera.GlobalPosition);
  // Playtest-observable (issue #187): the arm's CURRENT length, which wall clipping
  // shortens - so a test can prove it really clipped before judging the re-aim.
  public float ChaseViewArmLengthMeters => _thirdPersonArm?.GetHitLength() ?? 0.0f;
  private Vector3 CrosshairPoint() => _camera.GlobalPosition - _camera.GlobalTransform.Basis.Z * CrosshairDistanceMeters;

  // Where a shot must GO (issue #338, Aaron: the laser missed the crosshair in third
  // person). The chase rig only converges at one fixed range - everywhere else the
  // head ray & the crosshair ray diverge. In third person, raycast the CHASE
  // camera's center to the real aimed point & fire from the muzzle TOWARD it; in
  // first person the head camera's forward is already truthful.
  public Vector3 ShotDirection()
  {
    if (!_thirdPerson || _thirdPersonCamera == null) return -_camera.GlobalTransform.Basis.Z;
    var from = _thirdPersonCamera.GlobalPosition;
    var far = from + -_thirdPersonCamera.GlobalTransform.Basis.Z * 300.0f;
    var query = PhysicsRayQueryParameters3D.Create (from, far);
    query.Exclude = new Godot.Collections.Array <Rid> { GetRid() };
    var hit = GetWorld3D().DirectSpaceState.IntersectRay (query);
    var target = hit.Count > 0 ? (Vector3)hit["position"] : far;
    return (target - _camera.GlobalPosition).Normalized();
  }

  // Death view (issue #152): the same spring-arm rig from #119, stretched back & up
  // over the death spot so the victim can watch their killer emote on the body.
  private void EnterDeathView()
  {
    if (_thirdPersonCamera == null) CreateThirdPersonRig();
    _deathView = true;
    _thirdPersonArm!.Position = new Vector3 (0.0f, DeathViewUpMeters, 0.0f); // Centered over the body, not over the shoulder (issue #187).
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
    var toTarget = target - _thirdPersonCamera!.GlobalPosition;
    // A collapsed spring arm can leave the camera directly above the body; a
    // near-vertical LookAt has no valid up & spams warnings (CodeRabbit on #185).
    if (toTarget.IsZeroApprox() || toTarget.Cross (Vector3.Up).IsZeroApprox()) return;
    _thirdPersonCamera.LookAt (target, Vector3.Up);
  }

  // Respawn restores whichever view the player had chosen (issue #119).
  private void ExitDeathView()
  {
    _deathView = false;
    _thirdPersonArm!.Position = ChaseViewOffset(); // Back over the shoulder (issue #187).
    _thirdPersonArm.SpringLength = ThirdPersonBackMeters;
    _thirdPersonCamera!.Rotation = ChaseViewAim (ThirdPersonBackMeters); // UpdateChaseViewAim re-aims from the live length next frame.
    SetThirdPerson (_thirdPerson);
  }
}
