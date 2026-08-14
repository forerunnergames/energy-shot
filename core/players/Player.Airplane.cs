using System.Threading.Tasks;
using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.players;

// Being the paper airplane's target (issue #191). The airplane is a personal hazard
// with no blast radius at all: it locks onto exactly ONE player & only that player
// is ever harmed. Two ways to become the target - stepping on the grounded airplane
// (the landmine, which pops it into the air & sends it swooping onto you), or being
// hit by a slingshot-launched one (issue #190). Either way the sequence is the same:
// you catch fire for ~2s of damage over time, & then you pop. Non-gory throughout:
// a zappy flame, white paper scraps, & a respawn.
//
// Everything here runs on the target's own peer (victim-authoritative, like every
// other hit), with the server owning the "which player" decision & the exactly-one
// airplane bookkeeping in WeaponSpawner. Burning replicates so every peer sees who's
// alight; the warning ring & its beeping are strictly local to the target.
public partial class Player
{
  // Replicated like Sliding & Fallen so every peer renders the burning player;
  // synced ALWAYS for the same self-healing reason (issue #131), so ApplyBurning
  // must stay idempotent per state.
  [Export]
  public bool Burning
  {
    get => _burning;
    set
    {
      _burning = value;
      ApplyBurning();
    }
  }

  [Export] public float AirplaneBurnSeconds = 2.0f;
  [Export] public float AirplaneBurnTickSeconds = 0.25f;
  [Export] public float AirplaneBurnTickEnergy = 0.06f;
  // The pop itself finishes the target off, single-target & unclamped - the same
  // "no survivable clamp" rule the sticky banana uses (issue #83).
  [Export] public float AirplanePopEnergy = 2.0f;
  private static readonly Color FlameOrange = new(1.0f, 0.55f, 0.12f);
  private bool _burning;
  private OmniLight3D? _burnLight;
  private PaperAirplaneProjectile? _incomingAirplane;
  private PaperAirplaneProjectile? _visualAirplane;
  // Bumped on every ignite & on every respawn, so a burn tick loop from a previous
  // life (or a previous hazard) can never keep damaging a fresh one.
  private int _burnGeneration;

  // 0 = nothing incoming, 1 = impact. The HUD's warning ring & beeping read this,
  // & only the targeted player's HUD ever sees a non-zero value (issue #191).
  public float AirplaneThreatFraction => _incomingAirplane != null && IsInstanceValid (_incomingAirplane) ? _incomingAirplane.ThreatFraction() : 0.0f;

  // The landmine went off under us (server-confirmed): the airplane pops up off the
  // ground & swoops onto us. It locks onto this player for the whole flight - nobody
  // else can be hit by it, however close they're standing.
  public void BeginAirplaneSwoop (Vector3 minePosition)
  {
    if (!IsMultiplayerAuthority()) return;
    var origin = minePosition + Vector3.Up * MineLaunchHeightMeters;
    _incomingAirplane = SpawnAirplane (origin, isLive: true);
    _incomingAirplane.Struck += OnAirplaneStruckMe;
    _incomingAirplane.Lost += OnAirplaneFell;
    Rpc (MethodName.SpawnVisualAirplane, origin);
    GD.Print ($"{DisplayName}: I stepped on the paper airplane!");
  }

  // How high the triggered mine flips the airplane before it dives back onto the
  // player who stepped on it - about a second in the air, so the warning ring &
  // beeping start at their fastest immediately, exactly as the mine spec asks.
  private const float MineLaunchHeightMeters = 8.0f;

  // Visual-only copy of the swoop on every other peer; this RPC runs on the TARGET's
  // node everywhere, so every copy homes onto the same locked player.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void SpawnVisualAirplane (Vector3 origin)
  {
    if (_visualAirplane != null && IsInstanceValid (_visualAirplane)) _visualAirplane.QueueFree();
    _visualAirplane = SpawnAirplane (origin, isLive: false);
  }

  private PaperAirplaneProjectile SpawnAirplane (Vector3 origin, bool isLive)
  {
    var airplane = new PaperAirplaneProjectile();
    GetParent().AddChild (airplane);
    airplane.Launch (origin, this, isLive);
    return airplane;
  }

  private void OnAirplaneStruckMe()
  {
    EndSwoop();
    IgniteFromAirplane (attackerId: 0, "the paper airplane", DamageKind.Landmine);
  }

  // Outran the swoop: the airplane comes down where it gave up & the server re-arms
  // it there as the landmine, ready for whoever finds it next (issue #191).
  private void OnAirplaneFell (Vector3 position)
  {
    EndSwoop();
    GD.Print ($"{DisplayName}: I outran the paper airplane!");
    Spawner.SendAirplaneFellRequest (position);
  }

  // The live copy is already gone (or being abandoned); every peer's visual copy has
  // to go with it, or it would keep homing onto this player's next life.
  private void EndSwoop()
  {
    if (_incomingAirplane != null && IsInstanceValid (_incomingAirplane)) _incomingAirplane.QueueFree();
    _incomingAirplane = null;
    if (IsInsideTree() && IsMultiplayerActive()) Rpc (MethodName.FreeVisualAirplane);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void FreeVisualAirplane()
  {
    if (_visualAirplane != null && IsInstanceValid (_visualAirplane)) _visualAirplane.QueueFree();
    _visualAirplane = null;
  }

  // Server-confirmed entry point for a slingshot-launched strike (issue #190): the
  // shooter reported the contact, the server checked they really had the airplane
  // nocked, & now we light ourselves up exactly as a targeted swoop would.
  public void IgniteFromAirplane (int shooterId, string shooterName) => IgniteFromAirplane (shooterId, shooterName, DamageKind.Airplane);

  private async void IgniteFromAirplane (int attackerId, string attackerName, DamageKind kind)
  {
    if (!IsMultiplayerAuthority() || SpawnArmor || Fallen || Burning) return;
    var generation = ++_burnGeneration;
    LastDamageKind = kind; // Message context (issue #84).
    Burning = true;
    Dancing = false; // Catching fire mid-dance ends the groove on every peer (issue #103).
    GD.Print ($"{DisplayName}: I'm on fire! Popping in {AirplaneBurnSeconds}s...");
    await BurnDownTo (generation, attackerId, attackerName);
    if (!IsInstanceValid (this) || !IsInsideTree() || generation != _burnGeneration) return;
    Burning = false;
    if (!Fallen) Pop (attackerId, attackerName);
    Spawner.SendAirplaneSpentRequest(); // The caps fold a fresh airplane into the level.
  }

  // Damage over time while alight; a burn that gets cut short (zapped out by someone
  // else, a respawn, a disconnect) just stops - no delayed damage on a new life.
  private async Task BurnDownTo (int generation, int attackerId, string attackerName)
  {
    for (var burned = 0.0f; burned < AirplaneBurnSeconds; burned += AirplaneBurnTickSeconds)
    {
      await ToSignal (GetTree().CreateTimer (AirplaneBurnTickSeconds), SceneTreeTimer.SignalName.Timeout);
      if (!IsInstanceValid (this) || !IsInsideTree() || generation != _burnGeneration || Fallen) return;
      ApplyDamageFrom (attackerId, AirplaneBurnTickEnergy, attackerName, knockbackScale: 0.0f);
    }
  }

  // Strictly single-target (issue #191): the pop damages only this player, however
  // many others are standing in it. The flash & paper scraps are cosmetic.
  private void Pop (int attackerId, string attackerName)
  {
    var origin = GlobalPosition + Vector3.Up;
    PaperAirplane.Pop (GetParent(), origin);
    Rpc (MethodName.ShowAirplanePop, origin);
    ApplyDamageFrom (attackerId, AirplanePopEnergy, attackerName, knockbackScale: 0.0f);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void ShowAirplanePop (Vector3 origin) => PaperAirplane.Pop (GetParent(), origin);

  // Runs on every peer via the replicated Burning property; ALWAYS-mode sync re-fires
  // the setter every tick, so start/stop exactly once per state flip (like Fallen).
  private void ApplyBurning()
  {
    if (_mesh == null) return; // Pre-_Ready sync; the next ALWAYS tick re-applies.
    if (_burning && _burnLight == null) { StartBurnEffect(); return; }
    if (!_burning && _burnLight != null) StopBurnEffect();
  }

  // A zappy flame, not a fire: a flickering orange light riding the body, visible to
  // everyone so the whole arena can see who's about to pop.
  private void StartBurnEffect()
  {
    _burnLight = new OmniLight3D { LightColor = FlameOrange, LightEnergy = 4.0f, OmniRange = 7.0f, Position = Vector3.Up * 1.2f };
    AddChild (_burnLight);
    var flicker = _burnLight.CreateTween().SetLoops();
    flicker.TweenProperty (_burnLight, "light_energy", 1.6f, 0.09f);
    flicker.TweenProperty (_burnLight, "light_energy", 4.0f, 0.09f);
  }

  private void StopBurnEffect()
  {
    _burnLight?.QueueFree();
    _burnLight = null;
  }

  // A respawn (or falling off the world) puts the fire out & abandons any burn ticks
  // still queued from the previous life.
  private void ClearBurning()
  {
    ++_burnGeneration;
    Burning = false;
    if (_incomingAirplane != null) EndSwoop();
  }
}
