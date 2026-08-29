using com.forerunnergames.energyshot.items;
using com.forerunnergames.energyshot.players;
using Godot;

namespace com.forerunnergames.energyshot.weapons;

// A slung stone (issue #99): flies a gravity arc whose speed & sting scale with how
// long the shooter drew the band - drawn longer = flatter, faster, & harder (the
// shooter passes a draw-scaled gravity, issue #163). Only the shooter's own stone is
// "live" (reports the hit); other peers fly visual-only copies, like BananaProjectile.
// Impacts are detected by sweeping a ray along the path traveled each physics frame,
// so fast stones can't tunnel; the first frame sweeps from the camera, so a wall
// closer than the muzzle offset can't be skipped either (issues #112 & #163). Built
// entirely from primitive meshes & existing sounds - no downloaded assets.
public partial class SlingshotStone : Node3D
{
  [Export] public float GravityAcceleration = 24.0f;
  // Doubled from 6 (issue #163): stones fly their full arc & only despawn on impact
  // or well past relevance.
  [Export] public float MaxLifetimeSeconds = 12.0f;
  [Signal] public delegate void HitPlayerEventHandler (Player victim, float energy, bool isHeadshot);
  // Where the flight ended (issue #190): a slung world item becomes a normal pickup
  // again wherever it stops, so nothing loaded into a slingshot can vanish.
  [Signal] public delegate void LandedEventHandler (Vector3 position);
  // A slung LASER goes berserk in flight (issue #208): full-auto shots spray in random
  // directions as it tumbles. The live stone's spree reports hits here; visual copies
  // spray cosmetic bolts so every peer sees (& fears) the same chaos.
  [Signal] public delegate void SpreeHitEventHandler (CharacterBody3D body, float energy, bool throughBarrier);
  [Export] public float SpreeIntervalSeconds = 0.15f;
  // Every slung GUN sprays its own ammo in flight (issue #244): the launcher lobs
  // bananas, a slung slingshot flings stones, the laser sprays bolts. A slung blowgun
  // sprays nothing - losing it returned its darts to the level (the #236 economy).
  [Export] public float BananaSpreeIntervalSeconds = 0.6f;
  [Export] public float StoneSpreeIntervalSeconds = 0.3f;
  [Export] public float StoneSpreeSpeed = 24.0f;
  [Export] public float StoneSpreeEnergy = 0.3f;
  [Signal] public delegate void SpreeBananaEventHandler (BananaProjectile banana);
  [Signal] public delegate void SpreeStoneEventHandler (SlingshotStone stone);
  private static readonly PackedScene BananaScene = ResourceLoader.Load <PackedScene> ("res://core/weapons/BananaProjectile.tscn");
  [Export] public float SpreeEnergy = 0.24f; // The full-auto per-shot energy (issue #218).
  private static readonly PackedScene BoltScene = ResourceLoader.Load <PackedScene> ("res://core/weapons/LaserBolt.tscn");
  private static readonly AudioStream SpreeShotSound = ResourceLoader.Load <AudioStream> ("res://assets/sounds/shoot2.mp3");
  private float _spreeLeft;
  // What's flying: None = a plain stone, anything else = a loaded world item (issue #190).
  public HeldWeapon Ammo { get; init; } = HeldWeapon.None;
  private static readonly Color StoneGray = new(0.55f, 0.55f, 0.58f);
  private static readonly Color BananaYellow = new(0.92f, 0.78f, 0.12f);
  private static readonly Color FrameBrown = new(0.45f, 0.28f, 0.12f);
  private static readonly Color BandTan = new(0.85f, 0.72f, 0.35f);
  // Prong-tip band anchors & the pouch's rest spot, in the visual's local space; the
  // pouch (with the nocked stone) pulls straight back with the draw (issue #163).
  private static readonly Vector3 BandTipLeft = new(-0.19f, 0.42f, 0.0f);
  private static readonly Vector3 BandTipRight = new(0.19f, 0.42f, 0.0f);
  private static readonly Vector3 PouchRest = new(0.0f, 0.42f, 0.03f);
  private const float PouchPullMeters = 0.4f;
  private const float SurfaceClearance = 0.3f;
  private Vector3 _velocity;
  public Vector3 TravelDirection => _velocity.Normalized(); // The true flight at impact, for the dart stick angle (issue #425).
  private Vector3 _sweepStart;
  private bool _sweptFromStart;
  private float _energy;
  private float _age;
  private bool _isLive;
  public CharacterBody3D? Shooter { get; set; } // Set at CONSTRUCTION (issue #272): TrackStone fires on AddChild, before Launch runs.
  private Godot.Collections.Array <Rid> _exclusions = new();

  // Shared look for the world pickup & the held model (issue #99): a simple Y-frame
  // slingshot built from primitive boxes - a wooden handle, two angled prongs, & a
  // two-half band running from the prong tips to a pouch holding a nocked stone, so
  // the held model can stretch the band back with the draw (issue #163). Fresh
  // materials per call so the first-person overlay tweak (issue #124) can't bleed
  // into pickups.
  public static Node3D CreateSlingshotVisual()
  {
    var wood = new StandardMaterial3D { AlbedoColor = FrameBrown, Roughness = 0.9f };
    var band = new StandardMaterial3D { AlbedoColor = BandTan, Roughness = 0.6f };
    var stone = new StandardMaterial3D { AlbedoColor = StoneGray, Roughness = 0.8f };
    var visual = new Node3D();
    visual.AddChild (new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3 (0.07f, 0.4f, 0.07f) }, MaterialOverride = wood });
    visual.AddChild (new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3 (0.06f, 0.3f, 0.06f) }, MaterialOverride = wood, Position = new Vector3 (-0.1f, 0.3f, 0.0f), RotationDegrees = new Vector3 (0.0f, 0.0f, 35.0f) });
    visual.AddChild (new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3 (0.06f, 0.3f, 0.06f) }, MaterialOverride = wood, Position = new Vector3 (0.1f, 0.3f, 0.0f), RotationDegrees = new Vector3 (0.0f, 0.0f, -35.0f) });
    visual.AddChild (new MeshInstance3D { Name = "BandLeft", Mesh = new BoxMesh { Size = new Vector3 (0.035f, 0.035f, 1.0f) }, MaterialOverride = band });
    visual.AddChild (new MeshInstance3D { Name = "BandRight", Mesh = new BoxMesh { Size = new Vector3 (0.035f, 0.035f, 1.0f) }, MaterialOverride = band });
    visual.AddChild (new MeshInstance3D { Name = "NockedStone", Mesh = new SphereMesh { Radius = 0.05f, Height = 0.1f }, MaterialOverride = stone });
    PoseBand (visual, 0.0f);
    return visual;
  }

  // Draw pose for the shared visual (issue #163): the pouch & nocked stone pull back
  // toward the eye with the draw, & both band halves stretch from the prong tips to
  // meet them; drawFraction 0 is the relaxed rest pose (used by pickups & on release,
  // so the band visibly snaps forward).
  public static void PoseBand (Node3D visual, float drawFraction)
  {
    var pouch = PouchRest + Vector3.Back * (PouchPullMeters * drawFraction);
    visual.GetNode <Node3D> ("NockedStone").Position = pouch;
    var loaded = visual.GetNodeOrNull <Node3D> (NockedAmmoNodeName);
    if (loaded != null) loaded.Position = pouch; // Loaded ammo rides the pouch too (issue #190).
    StretchBandSegment (visual.GetNode <MeshInstance3D> ("BandLeft"), BandTipLeft, pouch);
    StretchBandSegment (visual.GetNode <MeshInstance3D> ("BandRight"), BandTipRight, pouch);
  }

  public const string NockedAmmoNodeName = "NockedAmmo";

  // What a slung item looks like in the pouch & in flight (issue #190): the item's
  // own shared visual, shrunk to projectile size. None = the plain stone.
  public static Node3D CreateAmmoVisual (HeldWeapon ammo) => ammo switch
  {
    // The textured GLB the pickup & held views use (issue #284): the raw OBJ + a
    // flat override nocked an all-white gun standing on end.
    HeldWeapon.Laser => PouchFit (Scaled (ResourceLoader.Load <PackedScene> ("res://assets/weapons/weapon-energy-handle.glb").Instantiate <Node3D>(), 0.25f)),
    HeldWeapon.Banana => MeshVisual ("res://assets/weapons/Banana_Rifle.obj", BananaYellow, 0.35f),
    HeldWeapon.Boomerang => Scaled (BoomerangProjectile.CreateVisual(), 0.6f),
    HeldWeapon.Slingshot => Scaled (CreateSlingshotVisual(), 0.6f),
    HeldWeapon.Bread => Scaled (Bread.CreateVisual(), 0.9f),
    HeldWeapon.PaperAirplane => Scaled (PaperAirplaneProjectile.CreateVisual(), 0.8f),
    HeldWeapon.BananaChunk => MeshVisual (null, BananaYellow, 1.0f),
    HeldWeapon.BananaGrenade => MeshVisual ("res://assets/weapons/banana.obj", BananaYellow, 0.35f), // A caught live banana (issue #251).
    HeldWeapon.Blowgun => Scaled (BlowgunDart.CreateBlowgunVisual(), 0.55f), // A found blowgun is slingable like any ground item (issue #194).
    HeldWeapon.PoisonDart => Scaled (BlowgunDart.CreateDartVisual(), 1.0f), // Issue #194.
    _ => CreateStoneVisual()
  };

  private static Node3D Scaled (Node3D visual, float scale)
  {
    visual.Scale = Vector3.One * scale;
    return visual;
  }

  // Fit ANY imported layout into the pouch (issue #388, Escendrix & Jonathan's
  // screenshot: the nocked laser's GLB pieces loomed over a quarter of the screen
  // & floated detached above the frame - the model's internal node offsets sit far
  // from its origin, so root-scaling alone leaves fragments scattered). The
  // combined mesh AABB recenters on the origin & the longest side clamps to pouch
  // size. Deferred: imported scenes report their AABBs only once inside the tree.
  private const float NockedFitMeters = 0.55f;

  private static Node3D PouchFit (Node3D visual)
  {
    var container = new Node3D();
    container.AddChild (visual);
    // On TreeEntered, not one-shot-deferred (Aaron, 2026-08-24: "still visually
    // glitched"): a deferred call lands once, & if the visual isn't in the tree
    // yet at that instant the fit silently never ran - the giant unscaled gun
    // was back for any caller that attaches a frame late. The signal fires
    // whenever it actually enters, every time it re-enters, & deferred again so
    // the imported meshes report real AABBs.
    container.TreeEntered += () => Callable.From (() => FitToPouch (container, visual)).CallDeferred();
    if (container.IsInsideTree()) Callable.From (() => FitToPouch (container, visual)).CallDeferred();
    return container;
  }

  private static void FitToPouch (Node3D container, Node3D visual)
  {
    if (!GodotObject.IsInstanceValid (container) || !container.IsInsideTree()) return;
    if (container.HasMeta ("pouch_fitted")) return; // Re-entering the tree must not shrink it twice.
    container.SetMeta ("pouch_fitted", true);
    var combined = new Aabb();
    var first = true;

    foreach (var mesh in MeshDescendants (visual))
    {
      var local = container.GlobalTransform.AffineInverse() * mesh.GlobalTransform;
      var box = local * mesh.GetAabb();
      combined = first ? box : combined.Merge (box);
      first = false;
    }

    if (first) return; // No meshes to fit.
    var longest = Mathf.Max (combined.Size.X, Mathf.Max (combined.Size.Y, combined.Size.Z));
    var fit = longest > NockedFitMeters && longest > 0.0f ? NockedFitMeters / longest : 1.0f;
    visual.Scale *= fit;
    visual.Position -= combined.GetCenter() * fit;
  }

  private static System.Collections.Generic.IEnumerable <MeshInstance3D> MeshDescendants (Node root)
  {
    if (root is MeshInstance3D self) yield return self;
    foreach (var child in root.GetChildren())
      foreach (var mesh in MeshDescendants (child)) yield return mesh;
  }

  private static Node3D MeshVisual (string? meshPath, Color color, float scale)
  {
    var visual = new Node3D();
    var mesh = meshPath == null ? new BoxMesh { Size = new Vector3 (0.16f, 0.1f, 0.26f) } : ResourceLoader.Load <Mesh> (meshPath);
    visual.AddChild (new MeshInstance3D { Mesh = mesh, MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.6f }, Scale = Vector3.One * scale });
    return visual;
  }

  private static Node3D CreateStoneVisual()
  {
    var visual = new Node3D();
    visual.AddChild (new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.09f, Height = 0.18f }, MaterialOverride = new StandardMaterial3D { AlbedoColor = StoneGray, Roughness = 0.8f } });
    return visual;
  }

  // Orients & scales a unit-length band box to connect two local-space points.
  private static void StretchBandSegment (MeshInstance3D segment, Vector3 from, Vector3 to)
  {
    var span = to - from;
    segment.Position = (from + to) * 0.5f;
    segment.Basis = Basis.LookingAt (span.Normalized(), Vector3.Up) * Basis.FromScale (new Vector3 (1.0f, 1.0f, span.Length()));
  }

  // A plain stone or whatever world item was loaded into the slingshot (issue #190).
  public override void _Ready() => AddChild (CreateAmmoVisual (Ammo));

  // sweepStart is the shooter's camera position: the stone spawns at the muzzle, but
  // the first sweep covers camera->muzzle too, so a wall closer than the muzzle
  // offset can't be skipped (issues #112 & #163).
  // Unpredictable tumble (issue #288): every launch rolls its own spin axis, spin
  // speed & spree cadence factor, so no two slung guns fly or fire alike. Darts &
  // the paper airplane are exempt - they always fly dead straight, no spin.
  private Vector3 _spinAxis = Vector3.Up;
  private float _spinSpeed;
  private float _cadenceFactor = 1.0f;
  private bool SpinsInFlight => Ammo != HeldWeapon.PoisonDart && Ammo != HeldWeapon.PaperAirplane;

  public void Launch (Vector3 origin, Vector3 sweepStart, Vector3 direction, float speed, float gravity, float energy, bool isLive, CharacterBody3D shooter)
  {
    _spinAxis = new Vector3 (GD.Randf() - 0.5f, GD.Randf() - 0.5f, GD.Randf() - 0.5f).Normalized(); // Issue #288: a fresh tumble every launch.
    _spinSpeed = 4.0f + GD.Randf() * 8.0f;
    _cadenceFactor = 0.7f + GD.Randf() * 0.8f;
    GlobalPosition = origin;
    _sweepStart = sweepStart;
    _velocity = direction.Normalized() * speed;
    GravityAcceleration = gravity; // Draw-scaled (issue #163): full draws fly flatter arcs.
    _energy = energy;
    _isLive = isLive;
    Shooter = shooter; // The playtest matches stones to the firer (CodeRabbit on #273); the spree paths reuse it.
    _exclusions = new Godot.Collections.Array <Rid> { shooter.GetRid() };
    if (shooter is Player own && own.HeadRid.IsValid) _exclusions.Add (own.HeadRid); // Your own dome is not a target (issue #179); no head while parked (#238).
  }

  public override void _PhysicsProcess (double delta)
  {
    var dt = (float)delta;
    _age += dt;
    if (_age > MaxLifetimeSeconds) { End (victim: null); return; }
    if (SpinsInFlight) Rotate (_spinAxis, _spinSpeed * dt); // Issue #288: the tumble; darts & airplanes stay straight.
    if (Sprays (Ammo)) UpdateSpree (dt); // Slung guns spray (issues #208 & #244).
    var from = _sweptFromStart ? GlobalPosition : _sweepStart;
    _sweptFromStart = true;
    _velocity.Y -= GravityAcceleration * dt;
    var to = GlobalPosition + _velocity * dt;
    var query = PhysicsRayQueryParameters3D.Create (from, to, exclude: _exclusions);
    query.HitFromInside = true;
    query.CollideWithAreas = true; // Heads are Area3D hitboxes (issue #179).
    var hit = GetWorld3D().DirectSpaceState.IntersectRay (query);

    if (hit.Count == 0)
    {
      GlobalPosition = to;
      return;
    }

    // First contact ends the flight (issue #99): a live stone that met a player
    // reports the hit; anything else just stops the stone. Resting a hair OFF the
    // surface, not exactly on it (issue #190): a ray that starts exactly on a floor
    // passes straight through it, which dropped slung items through the spawn-room
    // slab onto the arena far below. Issue #196 has since lifted the server's ground
    // ray a metre too, so this is belt & braces - & it also keeps the resting item
    // from z-fighting with whatever it landed against.
    GlobalPosition = (Vector3)hit["position"] + (Vector3)hit["normal"] * SurfaceClearance;
    var collider = hit["collider"].AsGodotObject();
    if (collider is HeadHitbox head) { End (head.Player, isHeadshot: true); return; } // Issue #179.
    End (collider as Player, isHeadshot: false);
  }

  private void UpdateSpree (float dt)
  {
    _spreeLeft -= dt;
    if (_spreeLeft > 0.0f) return;
    var direction = new Vector3 (GD.Randf() - 0.5f, GD.Randf() - 0.5f, GD.Randf() - 0.5f).Normalized();
    if (Ammo == HeldWeapon.Banana) { _spreeLeft = BananaSpreeIntervalSeconds * _cadenceFactor; SpreeBananaShot (direction); return; }
    if (Ammo == HeldWeapon.Slingshot) { _spreeLeft = StoneSpreeIntervalSeconds * _cadenceFactor; SpreeStoneShot (direction); return; }
    _spreeLeft = SpreeIntervalSeconds * _cadenceFactor;
    var bolt = BoltScene.Instantiate <LaserBolt>();
    GetParent().AddChild (bolt);
    bolt.Launch (GlobalPosition, GlobalPosition, direction, SpreeEnergy, _isLive, shooter: null);
    if (_isLive) bolt.HitPlayer += (body, energy, throughBarrier, _) => EmitSignal (SignalName.SpreeHit, body, energy, throughBarrier); // A tumbling gun never lands dome shots.
    var pew = new AudioStreamPlayer3D { Stream = SpreeShotSound, PitchScale = 1.3f, VolumeDb = -6.0f };
    GetParent().AddChild (pew);
    pew.GlobalPosition = GlobalPosition;
    pew.Finished += pew.QueueFree;
    pew.Play();
  }

  // Which slung items spray: the guns with ammo of their own. Pure & unit-tested.
  public static bool Sprays (HeldWeapon ammo) => ammo is HeldWeapon.Laser or HeldWeapon.Banana or HeldWeapon.Slingshot;

  // A slung launcher lobs bananas (issue #244): live ones are wired by the thrower
  // (Exploded / StuckToPlayer, attributed to them); every peer sees the cosmetic ones.
  private void SpreeBananaShot (Vector3 direction)
  {
    if (Shooter == null) return;
    var banana = BananaScene.Instantiate <BananaProjectile>();
    GetParent().AddChild (banana);
    banana.Launch (GlobalPosition, direction, _isLive, Shooter);
    if (_isLive) EmitSignal (SignalName.SpreeBanana, banana);
  }

  // A slung slingshot flings plain stones (issue #244); a child stone carries no ammo,
  // so it never sprays in turn.
  private void SpreeStoneShot (Vector3 direction)
  {
    if (Shooter == null) return;
    var stone = new SlingshotStone { Ammo = HeldWeapon.None };
    GetParent().AddChild (stone);
    stone.Launch (GlobalPosition, GlobalPosition, direction, StoneSpreeSpeed, GravityAcceleration, StoneSpreeEnergy, _isLive, Shooter);
    if (_isLive) EmitSignal (SignalName.SpreeStone, stone);
  }

  // Bulk (issue #208): big things hit hard. Scales the draw-scaled energy & the
  // knockback when a slung item connects; a stone stays a stone, a launcher is a
  // wrecking ball. Pure & unit-tested.
  public static float BulkFactor (HeldWeapon ammo) => ammo switch
  {
    HeldWeapon.Banana => 3.0f,
    HeldWeapon.Laser or HeldWeapon.Blowgun => 2.2f,
    HeldWeapon.Boomerang or HeldWeapon.Slingshot => 2.0f,
    _ => 1.0f
  };

  // One terminal report per flight (issue #190): the hit (if any) & then the resting
  // spot, so the shooter can both damage the victim & ask the server to turn the
  // slung item back into a world pickup where it came to rest.
  private void End (Player? victim, bool isHeadshot = false)
  {
    PlayImpactFlavor();

    if (_isLive)
    {
      if (victim != null) EmitSignal (SignalName.HitPlayer, victim, _energy, isHeadshot);
      EmitSignal (SignalName.Landed, GlobalPosition);
    }

    QueueFree();
  }

  // Cheap item-specific impact flavor, local on every peer (issue #190): bread
  // bonks, banana pieces splatter, & the airplane is handled by its own hazard.
  private void PlayImpactFlavor()
  {
    if (Ammo == HeldWeapon.Bread) PlayBonk();
    if (Ammo is HeldWeapon.Banana or HeldWeapon.BananaChunk) BananaDebris.Scatter (GetParent(), GlobalPosition);
  }

  // The punch thud slowed way down reads as a loaf bonking off something - reusing
  // an existing sound instead of downloading one (issue #190).
  private void PlayBonk()
  {
    var bonk = new AudioStreamPlayer3D { Stream = ResourceLoader.Load <AudioStream> ("res://assets/sounds/punch-thud.wav"), PitchScale = 0.7f };
    GetParent().AddChild (bonk);
    bonk.GlobalPosition = GlobalPosition;
    bonk.Finished += bonk.QueueFree;
    bonk.Play();
  }
}
