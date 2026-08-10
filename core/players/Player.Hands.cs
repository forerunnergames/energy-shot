using Godot;

namespace com.forerunnergames.energyshot.players;

// Floating sphere hands (issue #71): two translucent player-colored fists on the
// camera that grip the visible weapon, with a boxer-style punch animation that
// replicates to every peer (same pattern as SpawnVisualLaser).
public partial class Player
{
  [Export] public float HandRadius = 0.11f;
  [Export] public float PunchExtendMeters = 1.2f;
  [Export] public float PunchAnimationSeconds = 0.25f;
  [Export] public Vector3 EnergyGripLeftOffset = new(0.42f, 0.16f, -1.5f);
  [Export] public Vector3 EnergyGripRightOffset = new(0.58f, 0.14f, -0.95f);
  [Export] public Vector3 BananaGripLeftOffset = new(0.38f, -0.42f, -1.25f);
  [Export] public Vector3 BananaGripRightOffset = new(0.52f, -0.45f, -0.65f);
  private readonly MeshInstance3D?[] _hands = new MeshInstance3D?[2];
  private readonly Tween?[] _handTweens = new Tween?[2];
  private int ChooseRandomPunchHand() => _rng.Randf() < 0.5f ? 0 : 1;

  // Runs on every peer so everyone sees everyone's hands.
  private void CreateHands()
  {
    var camera = GetNode <Node3D> ("Camera3D");
    var material = new StandardMaterial3D { AlbedoColor = new Color (NormalColor, 0.65f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.4f };
    for (var i = 0; i < _hands.Length; ++i) _hands[i] = CreateHand (camera, material);
    UpdateHandRestPositions();
  }

  private MeshInstance3D CreateHand (Node3D camera, StandardMaterial3D material)
  {
    var hand = new MeshInstance3D { Mesh = new SphereMesh { Radius = HandRadius, Height = HandRadius * 2.0f }, MaterialOverride = material };
    camera.AddChild (hand);
    return hand;
  }

  // Hands hold whichever weapon is visible; simple fixed grip offsets (issue #71).
  private void UpdateHandRestPositions()
  {
    if (_hands[0] == null || _hands[1] == null) return;
    _hands[0]!.Position = RestOffsetFor (hand: 0);
    _hands[1]!.Position = RestOffsetFor (hand: 1);
  }

  private Vector3 RestOffsetFor (int hand)
  {
    if (hand == 0) return _isBananaEquipped ? BananaGripLeftOffset : EnergyGripLeftOffset;
    return _isBananaEquipped ? BananaGripRightOffset : EnergyGripRightOffset;
  }

  // Boxer-style swing: the chosen hand visibly shoots forward & back (issue #71).
  private void AnimatePunch (int hand)
  {
    var handNode = _hands[hand];
    if (handNode == null) return;
    _handTweens[hand]?.Kill();
    var rest = RestOffsetFor (hand);
    handNode.Position = rest;
    var tween = handNode.CreateTween();
    tween.TweenProperty (handNode, "position", rest + Vector3.Forward * PunchExtendMeters, PunchAnimationSeconds * 0.4f).SetTrans (Tween.TransitionType.Quad).SetEase (Tween.EaseType.Out);
    tween.TweenProperty (handNode, "position", rest, PunchAnimationSeconds * 0.6f).SetTrans (Tween.TransitionType.Quad).SetEase (Tween.EaseType.In);
    _handTweens[hand] = tween;
  }

  // Peers replay this player's punch animation on their copy of this node.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void PlayRemotePunch (int hand) => AnimatePunch (hand);
}
