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
  [Signal] public delegate void HitPlayerEventHandler (Player victim, float energy);
  // Where the flight ended (issue #190): a slung world item becomes a normal pickup
  // again wherever it stops, so nothing loaded into a slingshot can vanish.
  [Signal] public delegate void LandedEventHandler (Vector3 position);
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
  private Vector3 _sweepStart;
  private bool _sweptFromStart;
  private float _energy;
  private float _age;
  private bool _isLive;
  private Rid _shooterRid;

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
    HeldWeapon.Laser => MeshVisual ("res://assets/weapons/weapon-energy.obj", new Color (0.7f, 0.75f, 0.85f), 0.25f),
    HeldWeapon.Banana => MeshVisual ("res://assets/weapons/Banana_Rifle.obj", BananaYellow, 0.35f),
    HeldWeapon.Boomerang => Scaled (BoomerangProjectile.CreateVisual(), 0.6f),
    HeldWeapon.Slingshot => Scaled (CreateSlingshotVisual(), 0.6f),
    HeldWeapon.Bread => Scaled (Bread.CreateVisual(), 0.9f),
    HeldWeapon.PaperAirplane => Scaled (PaperAirplaneProjectile.CreateVisual(), 0.8f),
    HeldWeapon.BananaChunk => MeshVisual (null, BananaYellow, 1.0f),
    _ => CreateStoneVisual()
  };

  private static Node3D Scaled (Node3D visual, float scale)
  {
    visual.Scale = Vector3.One * scale;
    return visual;
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
  public void Launch (Vector3 origin, Vector3 sweepStart, Vector3 direction, float speed, float gravity, float energy, bool isLive, CharacterBody3D shooter)
  {
    GlobalPosition = origin;
    _sweepStart = sweepStart;
    _velocity = direction.Normalized() * speed;
    GravityAcceleration = gravity; // Draw-scaled (issue #163): full draws fly flatter arcs.
    _energy = energy;
    _isLive = isLive;
    _shooterRid = shooter.GetRid();
  }

  public override void _PhysicsProcess (double delta)
  {
    var dt = (float)delta;
    _age += dt;
    if (_age > MaxLifetimeSeconds) { End (victim: null); return; }
    var from = _sweptFromStart ? GlobalPosition : _sweepStart;
    _sweptFromStart = true;
    _velocity.Y -= GravityAcceleration * dt;
    var to = GlobalPosition + _velocity * dt;
    var query = PhysicsRayQueryParameters3D.Create (from, to, exclude: new Godot.Collections.Array <Rid> { _shooterRid });
    query.HitFromInside = true;
    var hit = GetWorld3D().DirectSpaceState.IntersectRay (query);

    if (hit.Count == 0)
    {
      GlobalPosition = to;
      return;
    }

    // First contact ends the flight (issue #99): a live stone that met a player
    // reports the hit; anything else just stops the stone. Resting a hair OFF the
    // surface, not exactly on it (issue #190): the server grounds a slung item by
    // casting down from where it stopped, & a ray that starts exactly on a floor
    // passes straight through it - which dropped items through the spawn-room slab
    // onto the arena far below.
    GlobalPosition = (Vector3)hit["position"] + (Vector3)hit["normal"] * SurfaceClearance;
    End (hit["collider"].AsGodotObject() as Player);
  }

  // One terminal report per flight (issue #190): the hit (if any) & then the resting
  // spot, so the shooter can both damage the victim & ask the server to turn the
  // slung item back into a world pickup where it came to rest.
  private void End (Player? victim)
  {
    PlayImpactFlavor();

    if (_isLive)
    {
      if (victim != null) EmitSignal (SignalName.HitPlayer, victim, _energy);
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
