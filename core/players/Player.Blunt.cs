using Godot;

namespace com.forerunnergames.energyshot.players;

// Blunt mode (issue #249): an ammo weapon that's out of ammo still swings - like a
// club. Punch reach, punch stun & blur, more damage than a fist, but NEVER a knock-
// loose or a theft: a stick is a stick. The blowgun is the first ammo weapon; any
// later one calls SwingClub the same way. Peers see the held model swing.
public partial class Player
{
  [Export] public float ClubEnergy = 0.4f; // Twice a punch (0.2).
  [Export] public float ClubCooldownSeconds = 0.45f;
  private Tween? _clubTween;

  private void SwingClub (Node3D heldModel)
  {
    CancelSpawnArmorIfFired();
    AnimateClub (heldModel);
    var target = FindAimedCollider (PunchRange);

    if (target is not Player victim)
    {
      PlayMissedPunchFeedback (target); // Whiff on air, thud (& a sting) on a wall, like a fist.
      return;
    }

    Rpc (MethodName.PlayRemoteClub);
    _punchSound.Play();
    GD.Print ($"{DisplayName}: I clubbed {victim.DisplayName}!");
    ReportToServer ($"club: {DisplayName} clubbed {victim.DisplayName}");
    victim.RpcId (victim.NetworkId, MethodName.ReceiveClub, DisplayName);
  }

  // The held model chops forward & down, then returns.
  private void AnimateClub (Node3D heldModel)
  {
    _clubTween?.Kill();
    var rest = heldModel.Position;
    var restRotation = heldModel.Rotation;
    _clubTween = heldModel.CreateTween().SetParallel();
    _clubTween.TweenProperty (heldModel, "position", rest + Vector3.Forward * 0.25f + Vector3.Down * 0.1f, PunchAnimationSeconds * 0.4f).SetTrans (Tween.TransitionType.Quad).SetEase (Tween.EaseType.Out);
    _clubTween.TweenProperty (heldModel, "rotation", restRotation + new Vector3 (-0.8f, 0.0f, 0.0f), PunchAnimationSeconds * 0.4f).SetTrans (Tween.TransitionType.Quad).SetEase (Tween.EaseType.Out);
    _clubTween.Chain().TweenProperty (heldModel, "position", rest, PunchAnimationSeconds * 0.6f).SetTrans (Tween.TransitionType.Quad).SetEase (Tween.EaseType.In);
    _clubTween.TweenProperty (heldModel, "rotation", restRotation, PunchAnimationSeconds * 0.6f).SetTrans (Tween.TransitionType.Quad).SetEase (Tween.EaseType.In);
  }

  // Peers replay the swing on their copy of this node - sent only on a connect, like
  // the punch (issue #82). Whatever slot is out on their copy is what swings.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void PlayRemoteClub()
  {
    if (IsMultiplayerAuthority()) return;
    if (_blowgunHeld != null) AnimateClub (_blowgunHeld);
  }

  // Victim-authoritative like the punch: stun, blur, & twice the damage - & nothing
  // leaves the hands (issue #249).
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void ReceiveClub (string clubbedByPlayerName)
  {
    if (!IsMultiplayerAuthority()) return;
    if (SpawnArmor) return;
    if (Fallen) return; // A body mid-death-sequence is scenery (issue #152).
    GD.Print ($"{DisplayName}: I was clubbed by {clubbedByPlayerName}!");
    LastDamageKind = DamageKind.Punch; // Reads as a punch in the messages - it's melee.
    ApplyPunchStun();
    EmitSignal (SignalName.Punched);
    ApplyDamage (ClubEnergy, clubbedByPlayerName, PunchKnockbackScale);
  }
}
