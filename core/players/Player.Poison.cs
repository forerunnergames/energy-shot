using System.Collections.Generic;
using Godot;
using com.forerunnergames.energyshot.weapons;

namespace com.forerunnergames.energyshot.players;

// Poison darts (issue #194): blowgun & slung darts embed in the victim & accumulate
// visibly; every tick, EACH embedded dart costs 10% of max health. Bread can't cure
// the poison - it only refills health as usual, so you can out-eat one dart, not a
// cluster. Victim-authoritative like every damage path; the dart count replicates so
// all peers render the pincushion & the sickly-green bars. Attribution is victim-side.
public partial class Player
{
  // Replicated ALWAYS like Burning (issue #131): the idempotent setter re-renders
  // the embedded darts & the overhead bar tint on every peer, & self-heals.
  [Export]
  public int PoisonDarts
  {
    get => _poisonDarts;
    set
    {
      _poisonDarts = value;
      ApplyPoisonVisuals();
    }
  }

  [Signal] public delegate void PoisonTickedEventHandler(); // Own-HUD pulse (issue #261).
  [Export] public float PoisonTickSeconds = 5.0f;
  // Drunk walk (issue #261): the move direction wobbles side to side, more per dart.
  [Export] public float PoisonWobbleRadiansPerDart = 0.25f;
  [Export] public float PoisonWobbleHz = 0.9f;
  [Export] public float PoisonTickFractionPerDart = 0.1f;
  // The sting itself hurts now (Aaron, 2026-08-28, issue #421): a dart that sticks
  // costs 5% of max health on impact, through the standard victim-side sink - same
  // shape as the tick below, & separate from the poison dynamics on purpose.
  [Export] public float DartImpactFraction = 0.05f;
  // Poison wears off by the TICK, not the clock (Aaron, 2026-08-24): one dart costs
  // 10% of max health every 5s, three times, & then that dart is spent. Darts stack -
  // three embedded darts cost 30% a tick - & each runs its own three-tick life.
  [Export] public int PoisonTicksPerDart = 3;
  public static readonly Color PoisonGreen = new(0.35f, 0.72f, 0.2f);
  private int _poisonDarts;
  // Victim-side attribution, oldest dart first: each tick applies one damage packet
  // per dart through the standard sink, so handicap & scoring stay per-attacker.
  private readonly List <(int Id, string Name, int TicksLeft, int AngleDeg)> _dartOwners = new();

  // Where each embedded dart sticks out (issue #425): the yaw, in the victim's LOCAL
  // frame, of the side the shot came from - so every peer renders the pincushion on
  // the hit side, perpendicular to the body, & it rides the body's rotation. A pipe-
  // joined list replicated ALWAYS beside PoisonDarts; the setter re-renders, & a
  // mismatch with the count self-heals to the old deterministic ring.
  [Export]
  public string DartAngles
  {
    get => _dartAngles;
    set
    {
      _dartAngles = value;
      ApplyPoisonVisuals();
    }
  }

  private string _dartAngles = string.Empty;
  private void SyncDartAngles() => DartAngles = string.Join ("|", _dartOwners.ConvertAll (dart => dart.AngleDeg));

  // Pure for the unit tests: what one dart costs you over its whole life, & what a
  // cluster of them costs in a single tick - both as a fraction of max health.
  public static float DartLifetimeFraction (int ticksPerDart, float fractionPerTick) => ticksPerDart * fractionPerTick;
  public static float TickFractionFor (int darts, float fractionPerTick) => darts * fractionPerTick;
  private readonly List <Node3D> _dartVisuals = new();
  // Bumped on every respawn so a tick loop from a previous life stops - the burn
  // generation pattern (issue #191).
  private int _poisonGeneration;
  private bool _poisonTicking;

  public bool IsPoisoned => PoisonDarts > 0;

  // Poison inverts the movement controls (issue #277, thepro; Aaron: from the first
  // dart): forward walks you back, left walks you right, stacked on the drunk-walk
  // wobble. Look & aim stay honest - inverting the mouse would be nausea, not comedy.
  public static Vector2 PoisonSteer (Vector2 input, int darts) => darts > 0 ? -input : input;

  // A dart found us (issue #194). The impact itself does no damage - it plants the
  // next tick's problem. Runs only on the victim's own authority.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void ReceiveDartHit (string shotByPlayerName) => EmbedDart (Multiplayer.GetRemoteSenderId(), shotByPlayerName);

  // Stepping on a landed (armed) dart without the blowgun (issues #236 & #248): the
  // server despawned it & tells us to take it as if it hit us - ownerless, like a
  // landmine, so nobody scores the eventual zap-out.
  public void ConfirmDartStepSelf() => ConfirmDartStep(); // The host's own player (an RpcId to yourself is a no-op).

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void ConfirmDartStep()
  {
    var sender = Multiplayer.GetRemoteSenderId();
    if (sender != 1 && sender != 0) return;
    EmbedDart (0, "a dart on the ground");
  }

  private void EmbedDart (int attackerId, string attackerName)
  {
    if (!IsMultiplayerAuthority()) return;
    if (SpawnArmor) return; // Armor shrugs darts off like everything else (issue #48).
    if (Fallen) return; // A body mid-death-sequence is scenery (issue #152).
    // The sting hurts on arrival (issue #421): 5% of max health through the standard
    // sink (which plays the damage sound), before the dart even starts ticking. A
    // real attack, so it interrupts a bread ritual like any other hit (#409's rule).
    LastDamageKind = DamageKind.Poison; // A dart is a dart, for the death message (issue #84).
    ApplyDamageFrom (attackerId, MaxHealth * DartImpactFraction / 100.0f, attackerName, knockbackScale: 0.0f);
    if (Fallen) return; // The sting itself zapped us out: a body is scenery, no pincushion.
    _dartOwners.Add ((attackerId, attackerName, Mathf.Max (1, PoisonTicksPerDart), StickAngleDeg (attackerId)));
    PoisonDarts = _dartOwners.Count;
    SyncDartAngles();
    GD.Print ($"{DisplayName}: {attackerName}'s dart stuck in me! ({PoisonDarts} embedded)");
    StartPoisonTicksIfIdle();
  }

  // The side the shot came from, as a yaw in OUR local frame (issue #425) - so the
  // stick rides our body's rotation on every peer. An ownerless dart (stepped on)
  // falls back to the old deterministic ring spread.
  private int StickAngleDeg (int attackerId)
  {
    var attacker = attackerId > 0 ? GetParent().GetNodeOrNull <Player> ($"{attackerId}") : null;
    if (attacker == null) return Mathf.RoundToInt (Mathf.RadToDeg (Mathf.Tau * 0.318f * _dartOwners.Count)) % 360;
    var local = ToLocal (attacker.GlobalPosition);
    return Mathf.PosMod (Mathf.RoundToInt (Mathf.RadToDeg (Mathf.Atan2 (local.X, local.Z))), 360);
  }

  private async void StartPoisonTicksIfIdle()
  {
    if (_poisonTicking) return;
    _poisonTicking = true;
    var generation = _poisonGeneration;

    while (IsPoisoned)
    {
      await ToSignal (GetTree().CreateTimer (PoisonTickSeconds), SceneTreeTimer.SignalName.Timeout);
      if (!IsInstanceValid (this) || !IsInsideTree()) return;
      if (generation != _poisonGeneration || Fallen) break;
      ApplyPoisonTick();
    }

    _poisonTicking = false;
  }

  // One tick: every embedded dart costs its own 10% of max health through the
  // standard damage sink, oldest first, each attributed to whoever blew it.
  private void ApplyPoisonTick()
  {
    LastDamageKind = DamageKind.Poison; // Message context (issue #84).
    EmitSignal (SignalName.PoisonTicked); // The green vignette pulses in step (issue #261).

    // Every embedded dart costs its own 10%, oldest first, attributed to whoever
    // blew it - so darts are cumulative (three darts = 30% this tick).
    foreach (var (id, name, _, _) in _dartOwners.ToArray())
    {
      if (Fallen) return; // An earlier dart in this same tick already zapped us out.
      ApplyDamageFrom (id, MaxHealth * PoisonTickFractionPerDart / 100.0f, name, knockbackScale: 0.0f, interruptsEating: false); // Bread heals through poison (#194).
    }

    ShedSpentDarts();
  }

  // A dart poisons you three times & is then spent (Aaron, 2026-08-24). Each keeps
  // its OWN count, so a dart taken later is still good for its full three ticks; the
  // replicated total carries the change, so the pincushion, the green bar & the
  // drunk walk all ease off together.
  private void ShedSpentDarts()
  {
    var before = _dartOwners.Count;

    for (var i = _dartOwners.Count - 1; i >= 0; --i)
    {
      var dart = _dartOwners[i];
      var ticksLeft = dart.TicksLeft - 1;
      if (ticksLeft > 0) { _dartOwners[i] = (dart.Id, dart.Name, ticksLeft, dart.AngleDeg); continue; }
      _dartOwners.RemoveAt (i);
    }

    if (_dartOwners.Count == before) return;
    PoisonDarts = _dartOwners.Count;
    SyncDartAngles();
    GD.Print ($"{DisplayName}: {before - _dartOwners.Count} dart(s) ran out of poison ({PoisonDarts} left)");
  }

  // Death shakes the darts out (issues #194 & #236): they fall beside the body as
  // ARMED ground darts - hazards to step on, ammo to anyone holding the blowgun. Request BEFORE clearing (the #145 convention): the
  // server validates the count against this player's replicated PoisonDarts.
  private void ScatterEmbeddedDarts()
  {
    if (!IsPoisoned) return;
    Spawner.SendDartScatterRequest (GlobalPosition, PoisonDarts);
    ClearPoison();
  }

  // Fresh lives are clean (& the setter path makes this idempotent, issue #131).
  private void ClearPoison()
  {
    ++_poisonGeneration;
    _dartOwners.Clear();
    PoisonDarts = 0;
    SyncDartAngles();
  }

  // Drunk walk (issue #261): rotate the input direction by a slow sine, scaled by the
  // dart count, so a poisoned player weaves instead of walking straight.
  private Vector3 Wobble (Vector3 inputDirection) => IsPoisoned ? inputDirection.Rotated (Vector3.Up, WobbleAngle (Time.GetTicksMsec() / 1000.0f, PoisonDarts, PoisonWobbleRadiansPerDart, PoisonWobbleHz)) : inputDirection;

  // Pure & unit-tested: a sine sway whose amplitude grows with the dart count (capped
  // at four darts' worth, so a pincushion can still steer a little).
  public static float WobbleAngle (float seconds, int darts, float radiansPerDart, float hz) => Mathf.Sin (seconds * Mathf.Tau * hz) * radiansPerDart * Mathf.Min (darts, 4);

  // Idempotent per state (ALWAYS-mode sync re-fires this constantly, issue #131):
  // keep exactly PoisonDarts stick visuals on the body & tint the overhead bar.
  private void ApplyPoisonVisuals()
  {
    if (!IsInsideTree()) return;
    UpdateDartSticks();
    UpdateOverheadBarPoisonTint();
  }

  private string _renderedDartKey = string.Empty;

  private void UpdateDartSticks()
  {
    if (_mesh == null) return; // Pre-_Ready sync; the ALWAYS-mode re-fire self-heals.
    // Idempotent per state (the ALWAYS-mode sync re-fires this constantly, issue
    // #131): rebuild only when the count or the angles actually changed (issue #425).
    var key = $"{_poisonDarts}:{_dartAngles}";
    if (key == _renderedDartKey) return;
    _renderedDartKey = key;
    foreach (var visual in _dartVisuals) visual.QueueFree();
    _dartVisuals.Clear();

    var angles = _dartAngles.Length == 0 ? System.Array.Empty <string>() : _dartAngles.Split ('|');

    while (_dartVisuals.Count < _poisonDarts)
    {
      var stick = BlowgunDart.CreateDartVisual();
      // The replicated hit-side yaw (issue #425), falling back to the old
      // deterministic ring when the angle list hasn't caught up with the count.
      var index = _dartVisuals.Count;
      var angle = index < angles.Length && int.TryParse (angles[index], out var degrees) ? Mathf.DegToRad (degrees) : Mathf.Tau * 0.318f * index;
      var direction = new Vector3 (Mathf.Sin (angle), 0.0f, Mathf.Cos (angle));
      stick.Position = direction * 0.34f + Vector3.Up * (1.05f + 0.12f * (index % 3));
      // Rotation set directly (LookAt needs the node in the tree): the shaft sticks
      // straight out of the body - perpendicular, on the side the shot came from.
      stick.Rotation = new Vector3 (0.0f, Mathf.Atan2 (direction.X, direction.Z), 0.0f);
      // Children of the MESH, not the root (issue #425): the fall animation & the
      // crouch squash move the mesh, & darts parented to the root stayed upright on
      // a body lying sideways - the "weird angles" report.
      _mesh.AddChild (stick);
      _dartVisuals.Add (stick);
    }
  }

  private StyleBoxFlat? _healthFill;

  // Our OWN fill box (Aaron, 2026-08-23: the green never showed from a distance):
  // GetThemeStylebox returns a resource SHARED across every player's bar, & the
  // ALWAYS-mode sync setters made every unpoisoned player's tick stomp the shared
  // color back to red - last writer wins, the green never survived a frame. A
  // per-instance override ends the fight.
  private void UpdateOverheadBarPoisonTint()
  {
    if (_healthBar == null) return;

    if (_healthFill == null)
    {
      _healthFill = _healthBar.GetThemeStylebox ("fill") is StyleBoxFlat shared ? (StyleBoxFlat)shared.Duplicate() : new StyleBoxFlat();
      _healthBar.AddThemeStyleboxOverride ("fill", _healthFill);
    }

    _healthFill.BgColor = IsPoisoned ? PoisonGreen : new Color (0.756863f, 0.0f, 0.0f);
  }
}
