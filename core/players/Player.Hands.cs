using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.players;

// Fists (issues #71 & #82): two big player-colored sphere hands, one left & one
// right. They bob up & down while moving & throw a boxer-style punch that peers
// replay only when it actually connects. With issue #351 the hands stay out for
// EVERY selected weapon, parked on per-weapon grip points, so first & third
// person both show the weapon actually held.
public partial class Player
{
  [Export] public float HandRadius = 0.22f;
  [Export] public float PunchExtendMeters = 1.4f;
  [Export] public float PunchAnimationSeconds = 0.22f;
  [Export] public Vector3 LeftHandRestOffset = new(-0.55f, -0.45f, -0.85f);
  [Export] public Vector3 RightHandRestOffset = new(0.55f, -0.45f, -0.85f);
  [Export] public float HandBobFrequency = 2.2f;
  [Export] public float HandBobMeters = 0.05f;
  private const float HandAlpha = 0.85f;
  private readonly MeshInstance3D?[] _hands = new MeshInstance3D?[2];
  private readonly Tween?[] _handTweens = new Tween?[2];
  private StandardMaterial3D? _handsMaterial;
  private float _handBobPhase;
  private int ChooseRandomPunchHand() => _rng.Randf() < 0.5f ? 0 : 1;
  private Vector3 HandRestOffset (int hand) => hand == 0 ? LeftHandRestOffset : RightHandRestOffset;

  // Per-weapon grip points, camera-local like the held visuals they hold (issue
  // #351): trigger + foregrip on the guns, both palms along the blowgun's tube,
  // frame + pouch on the slingshot, cradling the loaf. Hand 0 is the left. Pure
  // & static like NextCycleSlot, so the mapping is unit-testable without a scene.
  public static Vector3 GripOffset (SelectedWeapon weapon, int hand, Vector3 leftRest, Vector3 rightRest) => weapon switch
  {
    SelectedWeapon.Laser => hand == 0 ? new Vector3 (0.55f, 0.18f, -1.3f) : new Vector3 (0.42f, 0.12f, -0.95f),
    SelectedWeapon.Banana => hand == 0 ? new Vector3 (0.52f, -0.38f, -1.1f) : new Vector3 (0.38f, -0.42f, -0.75f),
    SelectedWeapon.Boomerang => hand == 0 ? leftRest : new Vector3 (0.48f, -0.45f, -0.85f),
    SelectedWeapon.Slingshot => hand == 0 ? new Vector3 (0.5f, -0.5f, -0.55f) : new Vector3 (0.5f, -0.58f, -0.85f),
    SelectedWeapon.PaperAirplane => hand == 0 ? leftRest : new Vector3 (0.45f, -0.47f, -0.8f),
    SelectedWeapon.Bread => hand == 0 ? new Vector3 (0.35f, -0.5f, -0.8f) : new Vector3 (0.55f, -0.5f, -0.8f),
    SelectedWeapon.Blowgun => hand == 0 ? new Vector3 (0.5f, -0.38f, -0.95f) : new Vector3 (0.5f, -0.4f, -0.65f),
    _ => hand == 0 ? leftRest : rightRest,
  };

  private Vector3 HandGripOffset (int hand) => GripOffset (_selectedWeapon, hand, LeftHandRestOffset, RightHandRestOffset);

  // The playtest driver's window into the grip (issue #351).
  public bool HandsVisible => _hands[0]?.Visible ?? false;
  // The fists follow the chosen body color too (issue #43).
  private void UpdateHandColor() { if (_handsMaterial != null) _handsMaterial.AlbedoColor = new Color (BaseColor, HandAlpha); }

  // Runs on every peer so everyone sees everyone's fists.
  private void CreateHands()
  {
    var camera = GetNode <Node3D> ("Camera3D");
    _handsMaterial = new StandardMaterial3D { AlbedoColor = new Color (BaseColor, HandAlpha), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.4f };
    if (IsMultiplayerAuthority()) MakeOverlay (_handsMaterial); // Own first-person hands draw over walls (issue #124).
    for (var i = 0; i < _hands.Length; ++i) _hands[i] = CreateHand (i, camera, _handsMaterial);
    UpdateHandsVisibility();
  }

  // Both hands share one mesh & material, so they're always identical sizes (issue #82).
  private MeshInstance3D CreateHand (int hand, Node3D camera, StandardMaterial3D material)
  {
    var mesh = new MeshInstance3D { Mesh = new SphereMesh { Radius = HandRadius, Height = HandRadius * 2.0f }, MaterialOverride = material, Position = HandRestOffset (hand) };
    camera.AddChild (mesh);
    return mesh;
  }

  // The hands are out for every weapon, gripping it (issue #351); dancing waves
  // them regardless (issue #103). The one exception is our OWN scoped view: the
  // zoomed sight must stay clear, while other peers still see us holding the tube.
  private void UpdateHandsVisibility()
  {
    foreach (var hand in _hands)
    {
      if (hand == null) continue;
      hand.Visible = !(IsScoped && IsMultiplayerAuthority());
    }
  }

  // Moving bobs the resting hands up & down (issue #82); a hand mid-punch is left alone.
  private void UpdateHandBob (double delta)
  {
    UpdateHandsVisibility(); // Scoping in & out must hide/show the own hands the same frame (issue #351).
    if (Dancing) return; // The dance owns the hands (issue #103).
    var speed = new Vector2 (Velocity.X, Velocity.Z).Length();
    _handBobPhase += speed * HandBobFrequency * (float)delta;
    var bob = Vector3.Up * (Mathf.Sin (_handBobPhase) * HandBobMeters * Mathf.Min (1.0f, speed / Speed));
    for (var i = 0; i < _hands.Length; ++i) ApplyHandRest (i, bob);
  }

  private void ApplyHandRest (int hand, Vector3 bob)
  {
    if (_hands[hand] == null || _handTweens[hand]?.IsRunning() == true) return;
    _hands[hand]!.Position = HandGripOffset (hand) + bob;
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
  // connect, so a visible remote swing is always a real hit (issue #82). Punching
  // also drops spawn armor, so stale armor whitewash clears here too (issue #114).
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void PlayRemotePunch (int hand)
  {
    ClearArmorDisplayOnRemoteAttack();
    AnimatePunch (hand);
  }
}
