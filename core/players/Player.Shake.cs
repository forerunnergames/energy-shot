using Godot;

namespace com.forerunnergames.energyshot.players;

// Camera shake (issue #70): a decaying random offset when a banana explodes nearby,
// triggered on every peer via the visual-banana path. Uses the camera's h/v offsets
// so the replicated camera transform is untouched.
public partial class Player
{
  [Export] public float ExplosionShakeRadius = 20.0f;
  [Export] public float ExplosionShakeSeconds = 0.4f;
  [Export] public float ExplosionShakeMagnitude = 0.35f;
  private float _shakeSecondsLeft;
  private float _shakeStrength;

  // Called by every exploding banana (live & visual copies alike); only the local
  // player's camera shakes, with magnitude falling off by distance.
  public static void NotifyExplosionAt (Vector3 origin) => _localPlayer?.StartShakeFrom (origin);

  private void StartShakeFrom (Vector3 origin)
  {
    var distance = GlobalPosition.DistanceTo (origin);
    if (distance > ExplosionShakeRadius) return;
    _shakeStrength = Mathf.Max (_shakeStrength, ExplosionShakeMagnitude * (1.0f - distance / ExplosionShakeRadius));
    _shakeSecondsLeft = ExplosionShakeSeconds;
  }

  private void UpdateCameraShake (double delta)
  {
    if (_shakeSecondsLeft <= 0.0f) return;
    _shakeSecondsLeft = Mathf.Max (0.0f, _shakeSecondsLeft - (float)delta);
    var falloff = _shakeSecondsLeft / ExplosionShakeSeconds;
    _camera.HOffset = _rng.RandfRange (-1.0f, 1.0f) * _shakeStrength * falloff;
    _camera.VOffset = _rng.RandfRange (-1.0f, 1.0f) * _shakeStrength * falloff;
    if (_shakeSecondsLeft > 0.0f) return;
    _shakeStrength = 0.0f;
  }
}
