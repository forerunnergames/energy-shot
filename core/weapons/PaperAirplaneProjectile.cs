using com.forerunnergames.energyshot.players;
using Godot;

namespace com.forerunnergames.energyshot.weapons;

// The paper airplane in homing flight (issue #191): launched by a triggered
// landmine, it locks onto exactly ONE player for the whole flight & swoops onto
// them - nobody else can be hit, & there is no blast radius. Only the target's own
// peer flies a "live" copy (it reports the strike & then applies its own burn),
// like every other victim-authoritative hit; other peers fly visual-only copies.
// Level geometry is ignored: a paper plane glides over railings & through doorways,
// & the flight ends on the target or on the safety timeout.
public partial class PaperAirplaneProjectile : Node3D
{
  [Export] public float Speed = 9.0f;
  [Export] public float TurnDegreesPerSecond = 260.0f;
  [Export] public float StrikeRadiusMeters = 1.2f;
  // A target who sprints away outlives the swoop; the plane then falls & re-arms
  // itself as the landmine wherever it came down.
  [Export] public float MaxLifetimeSeconds = 8.0f;
  // How far out the target's warning ring starts filling in (issue #191).
  [Export] public float WarningRangeMeters = 30.0f;
  [Export] public float MinBlinksPerSecond = 3.0f;
  [Export] public float MaxBlinksPerSecond = 14.0f;
  [Signal] public delegate void StruckEventHandler();
  [Signal] public delegate void LostEventHandler (Vector3 position);
  private Node3D _visual = null!;
  private Vector3 _direction = Vector3.Down;
  private Player? _target;
  private float _age;
  private bool _isLive;
  private Vector3 TargetPoint() => _target!.GlobalPosition + Vector3.Up;

  // 0 = far away, 1 = about to hit. Drives the targeted player's ring thickness,
  // brightness, blink rate, & beep rate (issue #191).
  public float ThreatFraction()
  {
    if (_target == null || !IsInstanceValid (_target)) return 0.0f;
    var distance = GlobalPosition.DistanceTo (TargetPoint()) - StrikeRadiusMeters;
    return Mathf.Clamp (1.0f - distance / WarningRangeMeters, 0.0f, 1.0f);
  }

  public void Launch (Vector3 origin, Player target, bool isLive)
  {
    GlobalPosition = origin;
    _target = target;
    _isLive = isLive;
    _direction = (TargetPoint() - origin).Normalized();
  }

  public override void _Ready()
  {
    _visual = PaperAirplane.CreateVisual();
    AddChild (_visual);
    AddWhooshLoop();
  }

  // The punch whiff replayed fast & thin reads as a paper plane cutting the air -
  // reusing an existing sound instead of downloading one (issue #191).
  private void AddWhooshLoop()
  {
    var whoosh = new AudioStreamPlayer3D { Stream = ResourceLoader.Load <AudioStream> ("res://assets/sounds/punch-whiff.wav"), PitchScale = 2.1f, VolumeDb = -6.0f };
    AddChild (whoosh);
    whoosh.Finished += () => whoosh.Play();
    whoosh.Play();
  }

  public override void _PhysicsProcess (double delta)
  {
    var dt = (float)delta;
    _age += dt;
    if (_target == null || !IsInstanceValid (_target)) { EndLost(); return; } // Target left mid-swoop.
    if (_age > MaxLifetimeSeconds) { EndLost(); return; }
    PaperAirplane.BlinkLed (_visual, _age, Mathf.Lerp (MinBlinksPerSecond, MaxBlinksPerSecond, ThreatFraction()));
    Steer (dt);
    GlobalPosition += _direction * Speed * dt;
    LookAlongFlight();
    TryStrike();
  }

  // Turn-rate-limited homing: the plane banks toward its locked target instead of
  // snapping, so a moving target genuinely stretches the swoop out.
  private void Steer (float dt)
  {
    var wanted = (TargetPoint() - GlobalPosition).Normalized();
    if (wanted.LengthSquared() < 0.001f) return;
    var maxTurn = Mathf.DegToRad (TurnDegreesPerSecond) * dt;
    var angle = _direction.AngleTo (wanted);
    if (angle <= maxTurn) { _direction = wanted; return; }
    var axis = _direction.Cross (wanted);
    if (axis.LengthSquared() < 0.000001f) { _direction = wanted; return; }
    _direction = _direction.Rotated (axis.Normalized(), maxTurn);
  }

  private void LookAlongFlight()
  {
    var nose = GlobalPosition + _direction;
    if (nose.IsEqualApprox (GlobalPosition)) return;
    LookAt (nose, Vector3.Up);
  }

  private void TryStrike()
  {
    if (GlobalPosition.DistanceTo (TargetPoint()) > StrikeRadiusMeters) return;
    if (_isLive) EmitSignal (SignalName.Struck);
    QueueFree();
  }

  // The swoop never connected: the plane comes down where it is & the server
  // re-arms it as the landmine there (issue #191).
  private void EndLost()
  {
    if (_isLive) EmitSignal (SignalName.Lost, GlobalPosition);
    QueueFree();
  }
}
