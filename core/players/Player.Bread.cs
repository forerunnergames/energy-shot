using com.forerunnergames.energyshot.items;
using com.forerunnergames.energyshot.ui.hud;
using Godot;

namespace com.forerunnergames.energyshot.players;

// Bread (issues #62, #190, #209 & #192): the one-per-life healing loaf is slot 7 - a
// real weapon slot you spawn carrying, equip like any other item, & use with primary
// fire. It still drops on death & still loads into a slingshot; it just has a key now.
//
// Eating is a RITUAL, not the old instant B-key heal (issue #192): three seconds
// rooted in place, drained on a reverse center-screen meter. You must be STATIONARY
// to start - moving, sliding, or airborne is refused with an error cue & a line above
// the meter - & for the duration you can't walk, jump, slide, crouch, uncrouch, or
// switch slots. Looking around is all you get, & whatever stance you started in is
// locked in. YOU can't cancel it; an attacker can: any hit ends the ritual & WASTES
// the loaf, so eating in the open is a genuine gamble.
//
// Everyone can see it coming (issue #192): Eating replicates like Sliding, Dancing, &
// Fallen, so every peer renders the loaf raised to the face, the munch bob, tumbling
// crumbs, & a tag above the name tag - & hears the munching positionally. A rooted,
// defenceless player is only interesting if opponents can spot one & punish it.
public partial class Player
{
  // Replicated like Dancing so every peer sees (& can punish) the eater; synced
  // ALWAYS for the same self-healing reason (issue #131), so ApplyEating must stay
  // idempotent per state.
  [Export]
  public bool Eating
  {
    get => _eating;
    set
    {
      _eating = value;
      ApplyEating();
    }
  }

  [Export] public float BreadEatSeconds = 3.0f;
  [Export] public float EatBobScale = 0.12f;
  // Bites per ritual: the loop period is BreadEatSeconds / this, so the munch rate
  // follows the duration instead of drifting out of step with it.
  private const int EatMunchCount = 3;
  // Anything slower than this counts as standing still (issue #192); a knockback
  // shove or a stray step is enough to refuse the ritual.
  private const float EatStillSpeed = 0.5f;
  private const int CrumbsPerMunch = 4;
  private const float CrumbSizeMeters = 0.05f;
  private const float CrumbFallMeters = 1.4f;
  private const float CrumbSeconds = 0.8f;
  private const float EatTagNameTagSpacing = 1.2f;
  private static readonly Color CrumbTan = new(0.85f, 0.68f, 0.36f);
  private static readonly Vector3 BreadRestPosition = new(0.45f, -0.45f, -0.85f);
  // Raised into view & tilted toward the mouth at the peak of each bite.
  private static readonly Vector3 BreadBitePosition = new(0.12f, -0.22f, -0.55f);
  private static readonly Vector3 BreadRestRotation = new(0.0f, 25.0f, -12.0f);
  private bool _eating;
  private float _eatSecondsLeft;
  // Death-message context (issue #192): whether the hit that just landed caught us
  // mid-ritual, captured before the interrupt clears it.
  private bool _wasEatingWhenHit;
  private Node3D _breadHeld = null!;
  private AudioStreamPlayer3D _munchSound = null!;
  private Label3D? _eatTag;
  private Tween? _eatTween;
  // 0..1 of the ritual still to run (issue #192): the HUD's reverse meter drains it.
  public float BreadEatRemainingFraction => BreadEatSeconds <= 0.0f ? 0.0f : Mathf.Clamp (_eatSecondsLeft / BreadEatSeconds, 0.0f, 1.0f);

  // Held model: the same loaf as the world pickup & a slingshot's nocked ammo (issue
  // #190), resting in the hand - bread looks like bread everywhere - built in code
  // alongside the boomerang, slingshot, & airplane held models (issue #209).
  private void CreateBreadHeld()
  {
    _breadHeld = Bread.CreateVisual();
    _breadHeld.Position = BreadRestPosition;
    _breadHeld.RotationDegrees = BreadRestRotation;
    GetNode <Node3D> ("Camera3D").AddChild (_breadHeld);
    // Positional munching (issue #192): the crunch plays on the EATER's node on every
    // peer, so anyone nearby hears the snacking. A packaged Pixabay recording (Aaron,
    // 2026-08-22) replaced the code-generated munch; extra voices let bites overlap (issue #182).
    _munchSound = new AudioStreamPlayer3D { Stream = ResourceLoader.Load <AudioStream> ("res://assets/sounds/bread-munch.mp3"), MaxPolyphony = 3, UnitSize = 12.0f }; // Real munching (Aaron, 2026-08-22): Pixabay, no attribution required.
    AddChild (_munchSound);
  }

  // Primary fire with the loaf out starts the ritual (issue #192), replacing the old
  // eat-bread keypress entirely. Nothing but the timer or a hit ends it.
  private void UpdateBread (double delta)
  {
    if (Eating) { ContinueEating (delta); return; }
    if (!StartsEating()) return;
    TryStartEating();
  }

  private bool StartsEating() => _isInputEnabled && !Fallen && !Dancing && IsBreadSelected && HasBread && Input.IsActionJustPressed ("shoot");

  // Every refusal is spoken (issue #160's rule, kept): an error cue plus a short line
  // centered above the bread meter.
  private void TryStartEating()
  {
    if (Health >= MaxHealth) { DenyEat ("Already at full health"); return; }
    if (Crouching) { DenyEat ("Stand up to eat"); return; } // Standing only (Aaron, 2026-08-28, issue #429); crouch DURING the ritual is already swallowed (#192).
    if (IsTooBusyToEat()) { DenyEat ("Stand still to eat"); return; }
    StartEating();
  }

  // Stationary means stationary (issue #192): no walking, no slide, & never mid-air -
  // you may EQUIP the loaf mid-slide or mid-jump, but the ritual waits for the slide
  // to end & for your feet to be back on the ground & still.
  private bool IsTooBusyToEat() =>
    !IsOnFloor()
    || Sliding
    || _slideJumpCarrying
    || Input.GetVector ("move_left", "move_right", "move_forward", "move_back") != Vector2.Zero
    || new Vector2 (Velocity.X, Velocity.Z).Length() > EatStillSpeed;

  private void DenyEat (string reason)
  {
    GD.Print ($"{DisplayName}: I can't eat right now: {reason.ToLower()}");
    EmitSignal (SignalName.BreadDenied, reason);
  }

  private void StartEating()
  {
    _eatSecondsLeft = BreadEatSeconds;
    Eating = true; // Setter starts the animation on every peer.
    ReportToServer ($"bread: {DisplayName} started eating");
    GD.Print ($"{DisplayName}: Chomp. Nobody bother me for {BreadEatSeconds}s...");
  }

  // No input escapes the three seconds (issue #192): the movement, stance, & slot
  // gates elsewhere swallow everything, so only the timer finishes the loaf.
  private void ContinueEating (double delta)
  {
    _eatSecondsLeft -= (float)delta;
    if (_eatSecondsLeft > 0.0f) return;
    FinishEating();
  }

  private void FinishEating()
  {
    Eating = false;
    _eatSecondsLeft = 0.0f;
    SetBreadHeld (isHeld: false); // Consumes the loaf & falls slot 7 back to fists (issues #190 & #209).
    Health = MaxHealth;
    ReportToServer ($"bread: {DisplayName} finished the loaf & healed to full");
    GD.Print ($"{DisplayName}: I ate my bread & feel brand new!");
    EmitSignal (SignalName.BreadEaten, DisplayName);
    EmitSignal (SignalName.HealthChanged, Health);
  }

  // Taking a hit cancels the eat (owner's addition to issue #192). The loaf is
  // WASTED - no heal, no second loaf this life - which is what makes snacking in the
  // open a gamble. Called from ApplyDamage on the victim's OWN authority, from a hit
  // that peer already validated (spawn armor, fallen), so no client can ever claim
  // "stop eating" at somebody else.
  private void InterruptEating()
  {
    if (!Eating) return;
    Eating = false;
    _eatSecondsLeft = 0.0f;
    SetBreadHeld (isHeld: false);
    ReportToServer ($"bread: {DisplayName} was interrupted mid-bite & wasted the loaf");
    GD.Print ($"{DisplayName}: My bread! I dropped my bread!");
    EmitSignal (SignalName.BreadInterrupted);
  }

  // Runs on every peer via the replicated Eating property; ALWAYS-mode sync re-fires
  // the setter every tick, so start/stop exactly once per state flip (like Dancing).
  private void ApplyEating()
  {
    if (_mesh == null || _breadHeld == null) return; // Pre-_Ready sync; the next ALWAYS tick re-applies.
    if (_eating && _eatTween == null) { StartEatAnimation(); return; }
    if (!_eating && _eatTween != null) StopEatAnimation();
  }

  // Mechanically Minecraft-like, simplified & original (issue #192): the loaf swings
  // up to the face, the body munches on a bob, & crumbs tumble off every bite. One
  // looping method tween drives the lot from a single 0..pi phase, procedurally on
  // existing nodes - no animation assets, exactly like the dance (issue #103).
  private void StartEatAnimation()
  {
    _eatTween = CreateTween().SetLoops();
    _eatTween.TweenCallback (Callable.From (Munch));
    _eatTween.TweenMethod (Callable.From <float> (ApplyEatPose), 0.0f, Mathf.Pi, BreadEatSeconds / EatMunchCount);
    ShowEatTag (isVisible: true);
  }

  // Full restore on every peer, through the same canonical helpers the dance & death
  // sequence use (issue #103), so no pose ever survives the ritual.
  private void StopEatAnimation()
  {
    _eatTween?.Kill();
    _eatTween = null;
    _breadHeld.Position = BreadRestPosition;
    _breadHeld.RotationDegrees = BreadRestRotation;
    ShowEatTag (isVisible: false);
    ApplySlidePose();
    ApplyCrouchScale();
  }

  // phase runs 0..pi per bite: sin phi lifts the loaf into the mouth & squashes the
  // body once. Only the mesh squashes - the hitbox stays honest while you're a
  // sitting duck, same as the dance.
  private void ApplyEatPose (float phase)
  {
    var munch = Mathf.Sin (phase);
    var stance = _crouching ? CrouchHeightScale : 1.0f;
    _breadHeld.Position = BreadRestPosition.Lerp (BreadBitePosition, munch);
    _breadHeld.RotationDegrees = BreadRestRotation + new Vector3 (-40.0f * munch, 0.0f, 0.0f);
    _mesh.Scale = new Vector3 (1.0f + EatBobScale * 0.5f * munch, stance * (1.0f - EatBobScale * munch), 1.0f + EatBobScale * 0.5f * munch);
    _mesh.Position = BodyPoseOffset() - Vector3.Up * (EatBobScale * 0.5f * munch); // Feet stay planted through the squash.
  }

  private void Munch()
  {
    _munchSound.Play();
    for (var crumb = 0; crumb < CrumbsPerMunch; ++crumb) SpawnCrumb();
  }

  // Goofy, non-gory garnish (issue #192): tiny code-built crumbs tumble off the loaf
  // on every bite & fade out. Parented to the world, so they fall where they were
  // dropped instead of riding the eater around.
  private void SpawnCrumb()
  {
    var material = new StandardMaterial3D { AlbedoColor = CrumbTan, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
    var crumb = new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One * CrumbSizeMeters }, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    GetParent().AddChild (crumb);
    crumb.GlobalPosition = GlobalPosition + Vector3.Up * NameTagBaseHeight * 0.7f + new Vector3 (_rng.RandfRange (-0.25f, 0.25f), 0.0f, _rng.RandfRange (-0.25f, 0.25f));
    var tween = crumb.CreateTween().SetParallel();
    tween.TweenProperty (crumb, "position", crumb.Position + Vector3.Down * CrumbFallMeters, CrumbSeconds).SetTrans (Tween.TransitionType.Quad).SetEase (Tween.EaseType.In);
    tween.TweenProperty (material, "albedo_color:a", 0.0f, CrumbSeconds);
    tween.Finished += crumb.QueueFree;
  }

  // The long-range cue (issue #192): a goofy tag over the name tag, so opponents
  // across the arena can spot a rooted snacker & come collect. Every peer renders it.
  private void ShowEatTag (bool isVisible)
  {
    _eatTag ??= CreateEatTag();
    _eatTag.Visible = isVisible;
  }

  private Label3D CreateEatTag()
  {
    var tag = new Label3D
    {
      Text = "nom nom nom",
      Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
      Modulate = CrumbTan,
      OutlineSize = 16,
      FontSize = 64,
      Position = Vector3.Up * (NameTagBaseHeight + EatTagNameTagSpacing),
      Visible = false
    };

    AddChild (tag);
    return tag;
  }

  // Rides above the name tag & scales with it (issue #192), like the crown (issue
  // #107), so the cue is readable at arena distance. Driven by UpdatePuppetTags.
  private void UpdateEatTagPlacement (float scaleFactor, float verticalOffset)
  {
    if (_eatTag == null) return;
    _eatTag.Scale = Vector3.One * scaleFactor;
    _eatTag.Position = new Vector3 (0.0f, NameTagBaseHeight + verticalOffset + EatTagNameTagSpacing * scaleFactor, 0.0f);
  }
}
