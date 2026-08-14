using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.players;

// Slingshot (issue #99): the slot-5 ballistic sidearm. Hold shoot to draw the band
// (like the laser's charge, but ballistic), release to sling an arcing stone; drawn
// longer = flatter, faster, & harder (draw scales speed & damage, 15-60). Hits are
// victim-authoritative with knockback, same as ReceiveHit & ReceiveBoomerangHit.
public partial class Player
{
  [Export] public float SlingshotMaxDrawSeconds = 1.2f;
  // Fire-rate cap (issue #163): a sub-minimum release just relaxes the band (no
  // stone), & the band needs a beat between shots, so tap-spam does nothing.
  [Export] public float SlingshotMinDrawSeconds = 0.2f;
  [Export] public float SlingshotCooldownSeconds = 0.5f;
  [Export] public float SlingshotMinSpeed = 22.0f;
  // Raised from 60 (issue #163): a full draw is a genuinely long shot.
  [Export] public float SlingshotMaxSpeed = 90.0f;
  // 15-60 damage via CalculateHealthDecrease (issue #99): a nuisance at a tap,
  // a proper wallop at full draw, never a one-hit zap-out.
  [Export] public float SlingshotMinEnergy = 0.15f;
  [Export] public float SlingshotMaxEnergy = 0.6f;
  [Export] public float SlingshotKnockbackScale = 0.6f;
  // Arc flattening (issue #163): stone gravity eases off as the draw rises, so
  // full-draw stones fly flat long shots while taps stay lobbed.
  [Export] public float SlingshotMinDrawGravity = 24.0f;
  [Export] public float SlingshotMaxDrawGravity = 10.0f;
  // How far the held frame pulls back toward the eye at full draw (issue #99).
  private const float SlingshotDrawPullMeters = 0.25f;
  private static readonly Vector3 SlingshotRestPosition = new(0.5f, -0.5f, -0.9f);
  private Node3D _slingshotHeld = null!;
  private AudioStreamPlayer _slingshotStretchSound = null!;
  private float _slingshotDrawSeconds;
  private float _slingshotCooldownLeft;
  private float SlingshotDrawFraction => Mathf.Clamp (_slingshotDrawSeconds / SlingshotMaxDrawSeconds, 0.0f, 1.0f);

  // Held model: the same code-built Y-frame as the pickup, resting in the hand
  // (issue #99). The stretch creak is the punch whiff slowed way down - reusing an
  // existing sound instead of downloading one.
  private void CreateSlingshotHeld()
  {
    _slingshotHeld = SlingshotStone.CreateSlingshotVisual();
    _slingshotHeld.Position = SlingshotRestPosition;
    _slingshotHeld.RotationDegrees = new Vector3 (0.0f, 10.0f, -8.0f);
    GetNode <Node3D> ("Camera3D").AddChild (_slingshotHeld);
    // MaxPolyphony 4 (issue #182): quick draw-cancel-draw retriggers inside the slowed creak's tail.
    _slingshotStretchSound = new AudioStreamPlayer { Stream = ResourceLoader.Load <AudioStream> ("res://assets/sounds/punch-whiff.wav"), PitchScale = 0.55f, MaxPolyphony = 4 };
    AddChild (_slingshotStretchSound);
  }

  // Draw-&-release (issue #99): holding shoot accumulates draw, releasing fires.
  // Poll-based (not just-released) so a wedged input state self-heals, & losing the
  // slingshot (or selection) mid-draw cancels cleanly.
  private void UpdateSlingshot (double delta)
  {
    _slingshotCooldownLeft = Mathf.Max (0.0f, _slingshotCooldownLeft - (float)delta);
    var active = IsSlingshotSelected && HasSlingshot && _isInputEnabled;
    if (!active) { CancelSlingshotDraw(); return; }
    if (Input.IsActionPressed ("shoot")) { AccumulateSlingshotDraw ((float)delta); return; }
    if (_slingshotDrawSeconds <= 0.0f) return;
    FireSlingshotStone();
  }

  private void AccumulateSlingshotDraw (float dt)
  {
    if (_slingshotCooldownLeft > 0.0f) return; // Fire-rate cap (issue #163): no new draw mid-cooldown.
    if (_slingshotDrawSeconds <= 0.0f) _slingshotStretchSound.Play(); // Once per draw.
    _slingshotDrawSeconds = Mathf.Min (SlingshotMaxDrawSeconds, _slingshotDrawSeconds + dt);
    ApplySlingshotDrawPose();
  }

  private void CancelSlingshotDraw()
  {
    if (_slingshotDrawSeconds <= 0.0f) return;
    _slingshotDrawSeconds = 0.0f;
    _slingshotStretchSound.Stop();
    ApplySlingshotDrawPose();
  }

  // The frame pulls back toward the eye as the band stretches (issue #99), the
  // nocked stone & pouch pull further back with the draw, & the band halves stretch
  // with them - snapping forward when the draw resets to zero (issue #163).
  private void ApplySlingshotDrawPose()
  {
    _slingshotHeld.Position = SlingshotRestPosition + Vector3.Back * (SlingshotDrawPullMeters * SlingshotDrawFraction);
    SlingshotStone.PoseBand (_slingshotHeld, SlingshotDrawFraction);
  }

  private void FireSlingshotStone()
  {
    var draw = SlingshotDrawFraction;
    var drawSeconds = _slingshotDrawSeconds;
    CancelSlingshotDraw();
    if (drawSeconds < SlingshotMinDrawSeconds) return; // Sub-minimum release: the band just relaxes, no shot (issue #163).
    CancelSpawnArmorIfFired();
    _slingshotCooldownLeft = SlingshotCooldownSeconds; // Fire-rate cap (issue #163).
    var speed = Mathf.Lerp (SlingshotMinSpeed, SlingshotMaxSpeed, draw);
    var energy = Mathf.Lerp (SlingshotMinEnergy, SlingshotMaxEnergy, draw);
    var gravity = Mathf.Lerp (SlingshotMinDrawGravity, SlingshotMaxDrawGravity, draw); // Flatter arc at full draw (issue #163).
    var direction = -_camera.GlobalTransform.Basis.Z;
    var sweepStart = _camera.GlobalPosition; // First sweep covers camera->muzzle (issues #112 & #163).
    var origin = sweepStart + direction * MuzzleOffsetMeters;
    SpawnStone (origin, sweepStart, direction, speed, gravity, energy, isLive: true);
    Rpc (MethodName.SpawnVisualStone, origin, sweepStart, direction, speed, gravity);
  }

  // Visual-only copy of the shooter's stone on every other peer. Firing proves the
  // shooter's spawn armor is gone, so stale armor whitewash clears here (issue #114).
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void SpawnVisualStone (Vector3 origin, Vector3 sweepStart, Vector3 direction, float speed, float gravity)
  {
    ClearArmorDisplayOnRemoteAttack();
    SpawnStone (origin, sweepStart, direction, speed, gravity, energy: 0.0f, isLive: false);
  }

  private void SpawnStone (Vector3 origin, Vector3 sweepStart, Vector3 direction, float speed, float gravity, float energy, bool isLive)
  {
    PlaySlingshotThwack (origin);
    var stone = new SlingshotStone();
    GetParent().AddChild (stone);
    stone.Launch (origin, sweepStart, direction, speed, gravity, energy, isLive, this);
    if (isLive) stone.HitPlayer += OnStoneHitPlayer;
  }

  // Distinct release thwack (issue #99): the punch thud replayed fast & positional
  // reads as the band snapping - reusing an existing sound, never downloading one.
  private void PlaySlingshotThwack (Vector3 origin)
  {
    var thwack = new AudioStreamPlayer3D { Stream = ResourceLoader.Load <AudioStream> ("res://assets/sounds/punch-thud.wav"), PitchScale = 1.6f };
    GetParent().AddChild (thwack);
    thwack.GlobalPosition = origin;
    thwack.Finished += thwack.QueueFree;
    thwack.Play();
  }

  // The shooter only reports the hit; the victim applies its own damage & knockback
  // (victim-authoritative, same as ReceiveHit & ReceiveBoomerangHit).
  private void OnStoneHitPlayer (Player victim, float energy)
  {
    if (victim.NetworkId == NetworkId) return;
    GD.Print ($"{DisplayName}: My stone thwacked {victim.DisplayName}!");
    _hitmarkerSound.Play();
    ReportToServer ($"slingshot: {DisplayName} thwacked {victim.DisplayName} (energy {energy:0.00})");
    victim.RpcId (victim.NetworkId, MethodName.ReceiveSlingshotHit, energy, DisplayName);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void ReceiveSlingshotHit (float energy, string slungByPlayerName)
  {
    if (!IsMultiplayerAuthority()) return;
    if (SpawnArmor) return;
    GD.Print ($"{DisplayName}: I was thwacked by {slungByPlayerName}'s slingshot!");
    LastDamageKind = DamageKind.Slingshot; // Message context (issue #84).
    ApplyDamage (energy, slungByPlayerName, SlingshotKnockbackScale);
  }
}
