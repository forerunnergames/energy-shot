using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.players;

// Movement: walking, jumping, falling, sliding, crouching, world-boundary collisions,
// respawns, spawn positioning, & the death snapshot for scenario messages.
public partial class Player
{
  // Death snapshot (issue #84): what this player was doing & holding at the moment
  // it got zapped out, captured before the respawn resets everything, so the HUD
  // can pick the right scenario message afterward. LastDamageKind is recorded by
  // the receive-hit RPCs in Player.Combat.cs & Player.Banana.cs.
  public DamageKind LastDamageKind { get; private set; }
  public bool DiedSliding { get; private set; }
  public bool DiedArmed { get; private set; }
  public bool DiedHoldingBananaGun { get; private set; }
  // Zapped out mid-ritual (issue #192): its own scenario, & the funniest one.
  public bool DiedEating { get; private set; }
  public int LostStreakCount { get; private set; }
  // 0..1 for the HUD's slide cooldown bar, like PunchReadyFraction (issue #127).
  // Non-positive cooldown = always ready; clamped so the HUD bar never sees NaN or overshoot.
  public float SlideReadyFraction => SlideCooldownSeconds <= 0.0f ? 1.0f : Mathf.Clamp (1.0f - _slideCooldownLeft / SlideCooldownSeconds, 0.0f, 1.0f);
  // Playtest-observable (issue #149): the current slide's speed - base, or higher when chained.
  public float CurrentSlideSpeed => _currentSlideSpeed;
  private bool IsFalling() => !IsOnFloor();
  // Stun blocks jumping & sliding (issues #70 & #71); the eating ritual roots you
  // completely (issue #192) - no walking, jumping, sliding, or crouch changes.
  private bool IsJumping() => _isInputEnabled && !IsStunned && !Eating && !Fallen && _jumpTimer.IsStopped() && Input.IsActionJustPressed ("jump") && IsOnFloor();
  private void Fall (ref Vector3 velocity, double delta) => velocity += Gravity * (float)delta;
  private bool WantsSlide() => _isInputEnabled && !IsStunned && !Fallen && Input.IsActionPressed ("slide");
  // Edge-triggered start (issue #131): a wedged pressed key state (e.g. a swallowed
  // Shift release on focus loss) can't auto-restart slides after every cooldown.
  private bool StartsSlide() => _isInputEnabled && !IsStunned && !Eating && !Fallen && Input.IsActionJustPressed ("slide");
  // Escape hatch (issue #131): while sliding, a fresh slide press always cancels;
  // crouch cancels in UpdateCrouch & jump chains via SlideJump (issue #149).
  private bool CanceledSlide() => _isInputEnabled && Input.IsActionJustPressed ("slide");
  // Mirrors IsJumping plus the room-to-stand check (issue #149): the same press that
  // makes Jump() fire this frame also ends the slide with its momentum & no cooldown.
  private bool StartsSlideJump() => _isInputEnabled && !IsStunned && !Eating && !Fallen && _jumpTimer.IsStopped() && Input.IsActionJustPressed ("jump") && IsOnFloor() && !IsOverheadBlocked();
  private bool ToggledCrouch() => _isInputEnabled && !Fallen && Input.IsActionJustPressed ("crouch");
  private float MoveSpeed() => (Sliding ? _currentSlideSpeed : _crouching ? Speed * CrouchSpeedMultiplier : Speed) * StunSpeedMultiplier();

  // Press to slide, hold to sustain: double speed & a horizontal pose, capped at
  // SlideDurationSeconds, then a cooldown before the next slide (see issue #41).
  // Slide & crouch presses cancel a slide mid-way (issue #131); a jump press
  // slide-jumps out with momentum & no cooldown (issue #149).
  private void UpdateSlide (double delta)
  {
    _slideCooldownLeft = Mathf.Max (0.0f, _slideCooldownLeft - (float)delta);
    _slideChainWindowLeft = Mathf.Max (0.0f, _slideChainWindowLeft - (float)delta);
    if (_slideJumpCarrying && IsOnFloor() && Velocity.Y <= 0.0f) LandSlideJump();

    if (Sliding)
    {
      _slideSecondsLeft -= (float)delta;
      if (StartsSlideJump()) { SlideJump(); return; }
      if (WantsSlide() && !CanceledSlide() && _slideSecondsLeft > 0.0f) return;
      StopSlide();
      return;
    }

    if (!StartsSlide() || _slideCooldownLeft > 0.0f) return;
    StartSlide();
  }

  private void StartSlide()
  {
    _slideSecondsLeft = SlideDurationSeconds;
    _currentSlideSpeed = ChainedSlideSpeed();
    Sliding = true; // Setter re-poses the body; replicated so every peer sees it.
    ApplyCameraHeight();
  }

  // A slide chained within the landing window continues from the carried speed with
  // a small boost (issue #149), capped so back-to-back chains can't diverge. The
  // window counts physics time, immune to CI wall-clock dilation.
  private float ChainedSlideSpeed()
  {
    var baseSpeed = Speed * SlideSpeedMultiplier;
    if (_slideChainWindowLeft <= 0.0f) return baseSpeed;
    return Mathf.Clamp (_slideChainLandingSpeed * SlideChainBoostMultiplier, baseSpeed, baseSpeed * MaxChainedSlideSpeedScale);
  }

  // Jumping out of a slide (issue #149): the air keeps the slide's momentum (see
  // Move) & the cooldown is skipped entirely, so landing into another slide chains.
  private void SlideJump()
  {
    Sliding = false;
    _slideJumpCarrying = true;
    ApplyCameraHeight();
  }

  // Touchdown ends the momentum carry & opens the chain window (issue #149).
  private void LandSlideJump()
  {
    _slideJumpCarrying = false;
    _slideChainWindowLeft = SlideChainWindowSeconds;
    _slideChainLandingSpeed = new Vector3 (Velocity.X, 0.0f, Velocity.Z).Length();
  }

  private void StopSlide()
  {
    _slideCooldownLeft = SlideCooldownSeconds;
    Sliding = false;
    // Timer expiry & cancels end STANDING when there's room (issue #150); only a low
    // ceiling forces the crouch so the head can't come up into it.
    if (IsOverheadBlocked()) Crouching = true;
    ApplyCameraHeight();
  }

  // Press C to crouch, press again to stand (issue #85) - or hold-to-crouch when the
  // persisted setting says so (issue #147): shorter profile & slower speed (see issue
  // #51). Sliding cancels a crouch; a crouch press mid-slide cancels the slide into a
  // crouch (issue #131); standing needs overhead clearance so the head can't clip
  // into geometry above.
  private void UpdateCrouch()
  {
    if (Eating) return; // The stance you started the ritual in is locked in (issue #192).
    if (Sliding && _crouching) { Crouching = false; ApplyCameraHeight(); return; }
    if (_holdToCrouch) { UpdateHeldCrouch(); return; }
    if (!ToggledCrouch()) return;
    if (Sliding) { StopSlide(); Crouching = true; ApplyCameraHeight(); return; }
    if (_crouching && IsOverheadBlocked()) return;
    Crouching = !_crouching;
    ApplyCameraHeight();
  }

  // Hold mode (issue #147): crouched exactly while the key is held. The crouch-press
  // slide cancel (issue #131) still applies; standing on release re-tries every frame
  // until there's overhead room, so walking out from under a ledge stands you up.
  private void UpdateHeldCrouch()
  {
    if (Sliding && ToggledCrouch()) { StopSlide(); Crouching = true; ApplyCameraHeight(); return; }
    if (Sliding) return;
    var wantsCrouch = _isInputEnabled && Input.IsActionPressed ("crouch");
    if (wantsCrouch == _crouching) return;
    if (!wantsCrouch && IsOverheadBlocked()) return;
    Crouching = wantsCrouch;
    ApplyCameraHeight();
  }

  // Root cause of issues #171 & #150: this ray used to start at the body ORIGIN,
  // which the old crouch scale sank ~0.4m below the floor surface - under the
  // paper-thin ground slab the upward ray then hit the slab's underside, reporting
  // "blocked" everywhere, wedging the crouch toggle down & crouching every expired
  // slide. Starting the probe above any possible floor-clip keeps it honest; real
  // ceilings that matter sit well above 0.5m.
  private bool IsOverheadBlocked()
  {
    var from = GlobalPosition + Vector3.Up * 0.5f;
    var to = GlobalPosition + Vector3.Up * 2.1f; // Standing capsule head height + margin.
    var query = PhysicsRayQueryParameters3D.Create (from, to, exclude: new Godot.Collections.Array <Rid> { GetRid() });
    return GetWorld3D().DirectSpaceState.IntersectRay (query).Count > 0;
  }

  // Body & hitbox go horizontal while sliding, upright otherwise; runs on every peer
  // via the replicated Sliding property (styled like SpawnArmor).
  private void ApplySlidePose()
  {
    if (_mesh == null) return;
    // The dance owns the mesh (issue #103): Sliding syncs ALWAYS, so this re-runs
    // every tick on puppets & would fight the dance tween; the dance's stop restores.
    if (Dancing) return;
    if (Eating) return; // Same for the munch bob (issue #192).
    if (Fallen) return; // Same for the death tip-over tween (issue #152).
    var rotation = Sliding ? new Vector3 (-90.0f, 0.0f, 0.0f) : Vector3.Zero;
    var position = BodyPoseOffset();
    _mesh.RotationDegrees = rotation;
    _mesh.Position = position;
    _collisionShape.RotationDegrees = rotation;
    _collisionShape.Position = position;
  }

  // Where the mesh & collision nodes sit for the current stance: slide-height while
  // sliding, dropped to keep the FEET planted while crouched (issue #171), standing
  // center otherwise. Shared by both pose helpers so they can never disagree.
  private Vector3 BodyPoseOffset() => new(0.0f, Sliding ? 0.5f : _crouching ? CrouchHeightScale : 1.0f, 0.0f);

  // Runs on every peer via the replicated Crouching property. The shape scales about
  // its center, so the node also drops to keep the FEET planted (issue #171): the old
  // center-scale lifted the shape bottom & sank the whole body ~0.4m into the floor,
  // which broke the overhead probe (see IsOverheadBlocked).
  private void ApplyCrouchScale()
  {
    if (_mesh == null) return;
    if (Dancing) return; // Same ALWAYS-sync reason as ApplySlidePose (issue #103).
    if (Eating) return; // Same for the munch bob (issue #192).
    if (Fallen) return; // The death tip-over tween owns the mesh (issue #152).
    var scale = new Vector3 (1.0f, _crouching ? CrouchHeightScale : 1.0f, 1.0f);
    var position = BodyPoseOffset();
    _mesh.Scale = scale;
    _mesh.Position = position;
    _collisionShape.Scale = scale;
    _collisionShape.Position = position;
  }

  private void ApplyCameraHeight()
  {
    var height = Sliding ? SlideCameraHeight : _crouching ? _standingCameraHeight * CrouchHeightScale : _standingCameraHeight;
    _camera.Position = new Vector3 (_camera.Position.X, height, _camera.Position.Z);
  }

  private void Jump (ref Vector3 velocity)
  {
    velocity.Y = JumpVelocity;
    _jumpSound.Play();
    _jumpTimer.Start();
  }

  private void Move (ref Vector3 velocity)
  {
    // The root cause of walking corpses (issue #216): every action predicate honors
    // the input lock, but Move never did - so "input disabled" stopped everything
    // EXCEPT walking, through the death lie-down & the respawn lock alike. The
    // explicit Fallen check stays as defense for any Fallen-with-input-enabled path.
    if (!_isInputEnabled || Fallen) { velocity.X = 0.0f; velocity.Z = 0.0f; return; }
    // Rooted for the ritual (issue #192): movement input produces no motion at all.
    // Gravity still applies, so a floor vanishing underneath still drops you.
    if (Eating) { velocity.X = 0.0f; velocity.Z = 0.0f; return; }
    if (_stickyFlightSecondsLeft > 0.0f) return; // Banana-launched (issue #83): momentum owns the ride.
    if (_slideJumpCarrying) return; // Slide-jump air (issue #149): the slide's momentum owns the ride until touchdown.
    var speed = MoveSpeed();
    var inputDir = Input.GetVector ("move_left", "move_right", "move_forward", "move_back");
    var inputDirection = Wobble ((Transform.Basis * new Vector3 (inputDir.X, 0, inputDir.Y)).Normalized()); // Poisoned players weave (issue #261).

    if (inputDirection != Vector3.Zero)
    {
      velocity.X = inputDirection.X * speed;
      velocity.Z = inputDirection.Z * speed;
      return;
    }

    velocity.X = Mathf.MoveToward (Velocity.X, 0, speed);
    velocity.Z = Mathf.MoveToward (Velocity.Z, 0, speed);
  }

  private void HandleCollisions()
  {
    var collisionCount = GetSlideCollisionCount();
    for (var i = 0; i < collisionCount; ++i) HandleCollision (GetSlideCollision (i));
  }

  private void HandleCollision (KinematicCollision3D collision)
  {
    if (TryRopeBounce (collision) || TryHeadBounce (collision)) return; // The boxing ring (issue #174).
    if (collision.GetColliderShape() is not CollisionShape3D { Shape: WorldBoundaryShape3D }) return;
    RespawnFell();
  }

  // Zap-out deaths run the lie-down sequence (issue #152): weapon drops & the death
  // message resolve at death time as before, then the body lies at the death spot
  // for DeathSequenceSeconds before the usual respawn (spawn armor included).
  private async void RespawnShot (string shotByPlayerName)
  {
    CaptureDeathSnapshot();
    ++ZapOuts; // Round stats (issue #153).
    DropAllHeldWeapons(); // Death drops everything carried at the death spot (issue #72).
    ScatterEmbeddedDarts(); // Embedded darts fall beside the body as 5s pickups (issue #194).
    EmitSignal (SignalName.RespawnedShot, DisplayName, shotByPlayerName); // Message shows during the wait (issue #152).
    await LieFallen();
    // A disconnect can free this node mid-lie-down (CodeRabbit on #185): a freed
    // node has nothing left to respawn.
    if (!IsInstanceValid (this) || !IsInsideTree()) return;
    Respawn();
  }

  private void CaptureDeathSnapshot()
  {
    DiedSliding = Sliding;
    DiedArmed = !IsUnarmed; // Carrying bread isn't being armed (issue #190).
    DiedHoldingBananaGun = Holds (HeldWeapon.Banana);
    // The lethal hit already interrupted the ritual, so read what it captured (issue #192).
    DiedEating = _wasEatingWhenHit;
    LostStreakCount = ZapStreakCount;
  }

  // Belt & braces for issue #88: the on-fire glow & pulsing leaderboard entry must
  // never outlive a death. The authority already resets ZapStreakCount in Respawn(),
  // but ON_CHANGE sync only re-sends on the NEXT change, so a puppet that missed
  // that one reset delta (e.g. spawned mid-handshake) stays stuck on a stale 3+.
  // Every peer hears the reliable respawn broadcast, so the World clears the local
  // display state there too - the authority's value agrees (it's also 0).
  public void ClearStreakDisplayLocally()
  {
    _zapStreakCount = 0;
    ApplyStreakGlow();
  }

  // Killer-side context for jump-shot messages (issue #84): IsOnFloor() is only
  // meaningful on the authority, so puppets probe for ground beneath their feet.
  public bool IsLikelyAirborne()
  {
    if (IsMultiplayerAuthority()) return !IsOnFloor();
    var from = GlobalPosition + Vector3.Up * 0.2f;
    var query = PhysicsRayQueryParameters3D.Create (from, from + Vector3.Down * 0.8f, exclude: new Godot.Collections.Array <Rid> { GetRid() });
    return GetWorld3D().DirectSpaceState.IntersectRay (query).Count == 0;
  }

  private void RespawnFell()
  {
    if (Fallen) return; // A dead body drifting past the boundary mid-lie-down already has a respawn scheduled (issue #152).
    --Score; // Falling off the world costs a point.
    ++Falls; // Round stats (issue #153): self-inflicted, separately counted.
    ClearHeldWeapons(); // A drop below the world would be unreachable; the weapons respawn at spawn points instead (issue #72).
    Respawn();
    EmitSignal (SignalName.RespawnedFell, DisplayName);
  }

  // Respawn into the spawn room above the arena (drop in to re-enter), with a short
  // input lock, so respawns are no longer instant teleports into the fight.
  private async void Respawn()
  {
    ZapStreakCount = 0; // Any respawn ends the streak.
    ForgetDamager(); // A fresh life owes nobody an assist (issue #153).
    Health = MaxHealth;
    // Announce it (issue #201): the HUD's health bar & the red death-vignette both
    // follow this signal, so a silent reset left the vignette glowing for the rest
    // of the life - a permanent red halo after every respawn.
    EmitSignal (SignalName.HealthChanged, Health);
    Velocity = Vector3.Zero;
    Position = CalculateRandomSpawnPosition();
    ResetFallTracking(); // The spawn-room drop-in is never a fall (issue #263).
    SetBreadHeld (isHeld: true); // Fresh bread every life (issues #62 & #190).
    _energyWeapon.ResetCharge(); // Every life starts with a cold weapon (issue #67).
    ClearStun(); // Death shakes off any punch/banana stun.
    ClearBurning(); // No fire (or incoming airplane) carries into a new life (issue #191).
    ClearPoison(); // No embedded darts either - fresh lives are clean (issue #194).
    _stickyFlightSecondsLeft = 0.0f; // A new life isn't still banana-launched (issue #83).
    ActivateSpawnArmor();
    // Fresh lives start standing & slide-ready: no lingering pose, no cooldown carryover (#104).
    // EVERY action cooldown resets with the life (issue #299, Aaron): the slide one
    // always did; the rest join it - a respawn owes nothing from the last life.
    Sliding = false;
    _slideSecondsLeft = 0.0f;
    _slideCooldownLeft = 0.0f;
    _punchCooldownLeft = 0.0f;
    _fullAutoCooldownLeft = 0.0f;
    _blowgunCooldownLeft = 0.0f;
    _slingshotCooldownLeft = 0.0f;
    CancelSlingshotDraw(); // A draw held through a zap-out never fires into the new life.
    _bananaLauncher.ResetCooldown();
    _slideJumpCarrying = false; // No chain carry into a new life (issue #149).
    _slideChainWindowLeft = 0.0f;
    Crouching = false;
    Dancing = false; // A new life starts with the pose fully restored on every peer (issue #103).
    Eating = false; // Same for the eating ritual & its munch bob (issue #192).
    _eatSecondsLeft = 0.0f;
    _wasEatingWhenHit = false;
    Fallen = false; // Belt & braces: the lie-down always ends before this runs (issue #152).
    ApplyCameraHeight();
    SetInputEnabled (isEnabled: false);
    _respawnSound.Play();
    GD.Print ($"{DisplayName}: I respawned!");
    await ToSignal (GetTree().CreateTimer (RespawnInputLockSeconds), SceneTreeTimer.SignalName.Timeout);
    if (!IsInstanceValid (this) || !IsInsideTree()) return; // Same disconnect guard as the death sequence (CodeRabbit on #185).
    SetInputEnabled (isEnabled: true);
  }

  private Vector3 CalculateRandomSpawnPosition()
  {
    var offset = new Vector3 (_rng.RandfRange (-4.0f, 4.0f), 1.0f, _rng.RandfRange (-4.0f, 4.0f));
    return _spawnRoom.Position + offset;
  }
}
