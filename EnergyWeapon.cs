using Godot;

public partial class EnergyWeapon : Node3D
{
  [Export] public float MinRotationSpeed = 1.0f;
  [Export] public float MaxRotationSpeed = 15.0f;
  [Signal] public delegate void ShotEventHandler();
  public float CurrentRotationSpeed { get; private set; }
  public bool IsShooting { get; private set; }
  private AudioStreamPlayer3D _shootingSound = null!;
  private Node3D _muzzle = null!;
  private Node3D _pivot = null!;
  private Tween? _tween;
  public override void _PhysicsProcess (double delta) => _pivot.Rotate (Vector3.Right, CurrentRotationSpeed * (float)delta);
  public void Shoot() => SpinUp();

  public override void _Ready()
  {
    _muzzle = GetNode <Node3D> ("Pivot/Muzzle");
    _pivot = GetNode <Node3D> ("Pivot");
    _shootingSound = GetNode <AudioStreamPlayer3D> ("ShootingSound");
    CurrentRotationSpeed = MinRotationSpeed;
  }

  private void SpinUp()
  {
    IsShooting = true;
    _tween?.Kill();
    _tween = CreateTween();
    _tween.TweenProperty (this, "CurrentRotationSpeed", MaxRotationSpeed, 2.0f).SetTrans (Tween.TransitionType.Quad).SetEase (Tween.EaseType.Out);
    _tween.Finished += FireShot;
  }

  private void FireShot()
  {
    _shootingSound.Play();
    EmitSignal (SignalName.Shot);
    SpinDown();
  }

  private void SpinDown()
  {
    _tween?.Kill();
    _tween = CreateTween();
    _tween.TweenProperty (this, "CurrentRotationSpeed", MinRotationSpeed, 2.0f).SetTrans (Tween.TransitionType.Quad).SetEase (Tween.EaseType.Out);
    _tween.Finished += () => IsShooting = false;
  }
}
