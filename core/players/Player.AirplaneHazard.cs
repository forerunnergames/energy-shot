using System.Threading.Tasks;
using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.players;

// Being the paper airplane's target (issue #191). The slot-6 glider from issue #102
// still flies, locks on, & can be punch-caught (Player.PaperAirplane.cs) - but an
// UNCAUGHT hit no longer just stings: it sets that one player alight for ~2s of
// damage over time & then pops them. Strictly personal: no blast radius at all, so
// nobody standing next to the target is touched.
//
// Three ways to become the target, all ending in the same sequence:
//   - a thrown airplane reaching the player it locked onto (issue #102),
//   - a slingshot-launched one hitting anybody (issue #190),
//   - stepping on an armed, grounded one - the landmine (issue #191).
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
  // The mine's fuse (issue #191): the ring & beeping go to their fastest the instant
  // you step on it, & about a second later the airplane lights you up.
  [Export] public float MineFuseSeconds = 1.0f;
  private static readonly Color FlameOrange = new(1.0f, 0.55f, 0.12f);
  private bool _burning;
  private OmniLight3D? _burnLight;
  private ulong _mineFuseEndMs;
  // Whichever airplane is locked onto us, live or a visual copy - the ring reads its
  // distance. Registered by SpawnAirplane on every peer (issues #102 & #191).
  private PaperAirplaneProjectile? _incomingAirplane;
  // Bumped on every ignite & on every respawn, so a burn tick loop from a previous
  // life (or a previous hazard) can never keep damaging a fresh one.
  private int _burnGeneration;

  // 0 = nothing incoming, 1 = impact. The HUD's warning ring & beeping read this, &
  // only the targeted player's own HUD ever sees a non-zero value (issue #191).
  public float AirplaneThreatFraction => _mineFuseEndMs > 0 ? 1.0f : IncomingAirplaneThreat();

  // A mine is already at your feet, so its fuse pins the ring at maximum; an incoming
  // glide fills the ring in as it closes.
  private float IncomingAirplaneThreat()
  {
    if (_incomingAirplane == null || !IsInstanceValid (_incomingAirplane) || !_incomingAirplane.IsInsideTree()) return 0.0f;
    return _incomingAirplane.ThreatFractionFor (this);
  }

  // Called on every peer as a flight spawns, on the TARGET's node (issue #191): the
  // ring & beeping belong to the locked player alone, so nobody else's HUD reacts.
  public void NoteIncomingAirplane (PaperAirplaneProjectile airplane) => _incomingAirplane = airplane;

  // Server-confirmed: we stepped on an armed, grounded airplane & it picked us.
  // Fastest beeping & blinking immediately, then the ignite about a second later.
  public async void BeginMineFuse (Vector3 minePosition)
  {
    if (!IsMultiplayerAuthority() || Burning || Fallen) return;
    _mineFuseEndMs = Time.GetTicksMsec() + (ulong)(MineFuseSeconds * 1000.0f);
    PaperAirplane.Arm (GetParent(), minePosition); // The mine's tick, heard by everyone nearby.
    GD.Print ($"{DisplayName}: I stepped on the paper airplane!");
    await ToSignal (GetTree().CreateTimer (MineFuseSeconds), SceneTreeTimer.SignalName.Timeout);
    if (!IsInstanceValid (this) || !IsInsideTree()) return;
    _mineFuseEndMs = 0;
    IgniteFromAirplane (attackerId: 0, "the paper airplane", DamageKind.Landmine);
  }

  // Server-confirmed strike (issues #102, #190 & #191): a thrown airplane reached the
  // player it locked onto without being caught, or a slung one hit somebody. Either
  // way the server checked the attacker really had the airplane before saying so.
  public void IgniteFromAirplane (int attackerId, string attackerName) => IgniteFromAirplane (attackerId, attackerName, DamageKind.PaperAirplane);

  private async void IgniteFromAirplane (int attackerId, string attackerName, DamageKind kind)
  {
    if (!IsMultiplayerAuthority() || SpawnArmor || Fallen || Burning) return;
    var generation = ++_burnGeneration;
    _incomingAirplane = null; // It arrived; the warning ring's work is done.
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
    _mineFuseEndMs = 0;
    _incomingAirplane = null;
    Burning = false;
  }
}
