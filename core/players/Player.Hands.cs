using Godot;

namespace com.forerunnergames.energyshot.players;

// Fists (issues #71 & #82): two big player-colored sphere hands at the bottom screen
// edges, one left & one right, rendered only while fists are the selected weapon.
// They bob up & down while moving & throw a boxer-style punch that peers replay only
// when it actually connects.
public partial class Player
{
  [Export] public float HandRadius = 0.22f;
  [Export] public float PunchExtendMeters = 1.4f;
  [Export] public float PunchAnimationSeconds = 0.22f;
  [Export] public Vector3 LeftHandRestOffset = new(-0.55f, -0.45f, -0.85f);
  [Export] public Vector3 RightHandRestOffset = new(0.55f, -0.45f, -0.85f);
  [Export] public float HandBobFrequency = 2.2f;
  [Export] public float HandBobMeters = 0.05f;
  private readonly MeshInstance3D?[] _hands = new MeshInstance3D?[2];
  private readonly Tween?[] _handTweens = new Tween?[2];
  private float _handBobPhase;
  private int ChooseRandomPunchHand() => _rng.Randf() < 0.5f ? 0 : 1;
  private Vector3 HandRestOffset (int hand) => hand == 0 ? LeftHandRestOffset : RightHandRestOffset;

  // Runs on every peer so everyone sees everyone's fists.
  private void CreateHands()
  {
    var camera = GetNode <Node3D> ("Camera3D");
    var material = new StandardMaterial3D { AlbedoColor = new Color (NormalColor, 0.85f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.4f };
    for (var i = 0; i < _hands.Length; ++i) _hands[i] = CreateHand (i, camera, material);
    UpdateHandsVisibility();
  }

  // Both hands share one mesh & material, so they're always identical sizes (issue #82).
  private MeshInstance3D CreateHand (int hand, Node3D camera, StandardMaterial3D material)
  {
    var mesh = new MeshInstance3D { Mesh = new SphereMesh { Radius = HandRadius, Height = HandRadius * 2.0f }, MaterialOverride = material, Position = HandRestOffset (hand) };
    camera.AddChild (mesh);
    return mesh;
  }

  // Hands render only while fists are the selected weapon (issue #82).
  private void UpdateHandsVisibility()
  {
    foreach (var hand in _hands)
    {
      if (hand == null) continue;
      hand.Visible = IsFistsSelected;
    }
  }

  // Moving bobs the resting hands up & down (issue #82); a hand mid-punch is left alone.
  private void UpdateHandBob (double delta)
  {
    var speed = new Vector2 (Velocity.X, Velocity.Z).Length();
    _handBobPhase += speed * HandBobFrequency * (float)delta;
    var bob = Vector3.Up * (Mathf.Sin (_handBobPhase) * HandBobMeters * Mathf.Min (1.0f, speed / Speed));
    for (var i = 0; i < _hands.Length; ++i) ApplyHandRest (i, bob);
  }

  private void ApplyHandRest (int hand, Vector3 bob)
  {
    if (_hands[hand] == null || _handTweens[hand]?.IsRunning() == true) return;
    _hands[hand]!.Position = HandRestOffset (hand) + bob;
  }

  // Boxer-style swing: the chosen hand visibly shoots forward & back.
  private void AnimatePunch (int hand)
  {
    var handNode = _hands[hand];
    if (handNode == null) return;
    _handTweens[hand]?.Kill();
    var rest = HandRestOffset (hand);
    handNode.Position = rest;
    var tween = handNode.CreateTween();
    tween.TweenProperty (handNode, "position", rest + Vector3.Forward * PunchExtendMeters, PunchAnimationSeconds * 0.4f).SetTrans (Tween.TransitionType.Quad).SetEase (Tween.EaseType.Out);
    tween.TweenProperty (handNode, "position", rest, PunchAnimationSeconds * 0.6f).SetTrans (Tween.TransitionType.Quad).SetEase (Tween.EaseType.In);
    _handTweens[hand] = tween;
  }

  // Peers replay this player's punch on their copy of this node - sent only on a
  // connect, so a visible remote swing is always a real hit (issue #82).
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void PlayRemotePunch (int hand) => AnimatePunch (hand);
}
