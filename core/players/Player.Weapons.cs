using System.Collections.Generic;
using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.players;

// Weapon lifecycle & selection (issues #72 & #82): players spawn with fists only &
// arm up from world pickups; death drops everything held at the death spot. Slot 1 =
// fists (always available), slot 2 = laser, slot 3 = banana, slot 4 = boomerang (#98),
// slot 5 = slingshot (#99), slot 6 = paper airplane (#102), slot 7 = bread (#209);
// everything but fists is selectable only while held. The server-side WeaponSpawner
// owns pickup spawning & the weapon caps.
public partial class Player
{
  // Replicated like SpawnArmor so every peer knows what this player carries & renders
  // the right weapon model (or none while unarmed).
  [Export]
  public HeldWeapon HeldWeapon
  {
    get => _heldWeapon;
    set
    {
      RememberRecentlyHeld (value);
      _heldWeapon = value;
      UpdateWeaponVisibility();
    }
  }

  // Drop-validation grace (issue #167): a death drop clears the replicated HeldWeapon
  // right after sending the drop RPC, & the synchronizer delta can beat the RPC to the
  // server (different channels, no cross-ordering guarantee) - so the server validates
  // masks against current-or-recently-held instead of denying the legit drop.
  public HeldWeapon HeldOrRecentlyHeld => _heldWeapon | RecentlyHeld();
  // Per-flag grace deadlines (CodeRabbit on #168): a single shared pair let a second
  // removal inside the window overwrite the first weapon's remaining grace.
  private readonly Dictionary <HeldWeapon, ulong> _recentlyHeldUntilMs = new();
  private const float RecentlyHeldGraceSeconds = 2.0f;

  private HeldWeapon RecentlyHeld()
  {
    var now = Time.GetTicksMsec();
    var recent = HeldWeapon.None;
    foreach (var (flag, until) in _recentlyHeldUntilMs) recent |= now < until ? flag : HeldWeapon.None;
    return recent;
  }

  // Each removed weapon keeps its own full grace period (CodeRabbit on #168), even
  // when another removal follows inside the first one's window.
  private void RememberRecentlyHeld (HeldWeapon incoming)
  {
    var removed = _heldWeapon & ~incoming;
    if (removed == HeldWeapon.None) return;
    var until = Time.GetTicksMsec() + (ulong)(RecentlyHeldGraceSeconds * 1000.0f);
    // PaperAirplane rides the grace too (issue #102): its landing & catch handoff
    // depend on it. So does bread (issue #190): the death drop clears the loaf right
    // after sending the drop RPC, so without the grace the server denies it.
    foreach (var flag in new[] { HeldWeapon.Laser, HeldWeapon.Banana, HeldWeapon.Boomerang, HeldWeapon.Slingshot, HeldWeapon.PaperAirplane, HeldWeapon.Bread, HeldWeapon.Blowgun }) { if ((removed & flag) != 0) _recentlyHeldUntilMs[flag] = until; }
  }

  // Which slot is out (issue #82). Replicated so every peer renders the right model
  // (fist hands, laser, or banana launcher) on this player.
  [Export]
  public SelectedWeapon SelectedWeapon
  {
    get => _selectedWeapon;
    set
    {
      _selectedWeapon = value;
      UpdateWeaponVisibility();
    }
  }

  private HeldWeapon _heldWeapon = HeldWeapon.None;
  private SelectedWeapon _selectedWeapon = SelectedWeapon.Fists;
  // Theft revenge (issue #84): whose dropped weapon we most recently grabbed, so
  // the HUD can gloat when we zap its previous owner with it still in hand.
  private HeldWeapon _stolenWeapon = HeldWeapon.None;
  private string _stolenFrom = string.Empty;
  private WeaponSpawner? _weaponSpawner;
  public bool Holds (HeldWeapon type) => (_heldWeapon & type) != 0;
  // A loaf is not a weapon (issue #190): "armed" & "unarmed" still mean guns only,
  // so carrying bread can't flip the death-message scenarios (issue #84).
  public bool IsUnarmed => (_heldWeapon & ~HeldWeapon.Bread) == HeldWeapon.None;
  private bool HasLaser => Holds (HeldWeapon.Laser);
  private bool HasBanana => Holds (HeldWeapon.Banana);
  private bool HasBoomerang => Holds (HeldWeapon.Boomerang);
  private bool HasSlingshot => Holds (HeldWeapon.Slingshot);
  private bool HasPaperAirplane => Holds (HeldWeapon.PaperAirplane);
  private bool IsFistsSelected => _selectedWeapon == SelectedWeapon.Fists;
  private bool IsBreadSelected => _selectedWeapon == SelectedWeapon.Bread; // Slot 7 (issue #209).
  private bool IsLaserSelected => _selectedWeapon == SelectedWeapon.Laser;
  private bool IsBananaSelected => _selectedWeapon == SelectedWeapon.Banana;
  private bool IsBoomerangSelected => _selectedWeapon == SelectedWeapon.Boomerang;
  private bool IsSlingshotSelected => _selectedWeapon == SelectedWeapon.Slingshot;
  private bool IsPaperAirplaneSelected => _selectedWeapon == SelectedWeapon.PaperAirplane;
  private WeaponSpawner Spawner => _weaponSpawner ??= GetNode <WeaponSpawner> ("/root/World/WeaponSpawner");

  // Falling off the world: held weapons return to the spawn pool via the caps instead
  // of dropping as unreachable pickups below the map.
  private void ClearHeldWeapons()
  {
    ReleaseBoomerangInFlight(); // A boomerang out flying still drops where it is (issue #98).
    ReleaseAirplaneInFlight(); // Same for a paper airplane mid-glide (issue #102).
    DropLoadedAmmo(); // Anything nocked in the slingshot lands too (issue #190).
    ForgetTheft (_heldWeapon);
    SetBreadHeld (isHeld: false); // The loaf leaves with everything else (issue #190).
    HeldWeapon = HeldWeapon.None;
    SelectedWeapon = SelectedWeapon.Fists;
  }

  // The one-per-life loaf rides the HeldWeapon mask (issue #190) so it flows through
  // the same pickup, cap-free drop, & universal-ammo machinery as the guns, while
  // the Bread item keeps owning the eat rule (issue #62). This single helper is the
  // only place the two are written, so they can't drift apart.
  private void SetBreadHeld (bool isHeld)
  {
    if (isHeld)
    {
      _bread.Restock();
      HeldWeapon |= HeldWeapon.Bread;
      return;
    }

    _bread.TryEat();
    HeldWeapon &= ~HeldWeapon.Bread;
    DeselectUnheldWeapon(); // An eaten, wasted, or dropped loaf can't stay in slot 7 (issue #209).
  }

  // Losing the stolen weapon ends the revenge window (CodeRabbit on #96): a fresh
  // pickup of the same type must not trigger a stale gloat.
  private void ForgetTheft (HeldWeapon lostTypes)
  {
    if ((_stolenWeapon & lostTypes) == 0) return;
    _stolenWeapon = HeldWeapon.None;
    _stolenFrom = string.Empty;
  }

  // Runs on every peer via the replicated HeldWeapon & SelectedWeapon properties.
  private void UpdateWeaponVisibility()
  {
    if (_bananaLauncher == null || _boomerangHeld == null || _slingshotHeld == null || _airplaneHeld == null || _breadHeld == null || _blowgunHeld == null) return;
    _breadHeld.Visible = IsBreadSelected && HasBread; // Slot 7 (issue #209): everyone sees the loaf in hand.
    _energyWeapon.Visible = IsLaserSelected && HasLaser;
    _bananaLauncher.Visible = IsBananaSelected && HasBanana;
    _boomerangHeld.Visible = IsBoomerangSelected && HasBoomerang && !IsBoomerangOut; // Empty hand while it's out flying (issue #98).
    _slingshotHeld.Visible = IsSlingshotSelected && HasSlingshot; // Slot 5 (issue #99).
    _airplaneHeld.Visible = IsPaperAirplaneSelected && HasPaperAirplane && !IsAirplaneOut; // Slot 6, empty hand mid-glide (issue #102).
    _blowgunHeld.Visible = IsBlowgunSelected && HasBlowgun; // Slot 8 (issue #194).
    UpdateHandsVisibility(); // Hands render only while fists are selected (issue #82).
  }

  // Fists are always selectable; guns only while held (issue #82). No CanFire gate: a
  // cooling banana stays selected, it just can't fire yet (issue #83).
  private void UpdateWeaponSelection()
  {
    if (!_isInputEnabled) return;
    if (Eating) return; // The eating ritual locks the slot you started it with (issue #192).
    if (Input.IsActionJustPressed ("weapon_1")) SelectedWeapon = SelectedWeapon.Fists;
    if (Input.IsActionJustPressed ("weapon_2") && HasLaser) SelectedWeapon = SelectedWeapon.Laser;
    if (Input.IsActionJustPressed ("weapon_3") && HasBanana) SelectedWeapon = SelectedWeapon.Banana;
    if (Input.IsActionJustPressed ("weapon_4") && HasBoomerang) SelectedWeapon = SelectedWeapon.Boomerang; // Slot 4 (issue #98).
    if (Input.IsActionJustPressed ("weapon_5") && HasSlingshot) SelectedWeapon = SelectedWeapon.Slingshot; // Slot 5 (issue #99).
    if (Input.IsActionJustPressed ("weapon_6") && HasPaperAirplane) SelectedWeapon = SelectedWeapon.PaperAirplane; // Slot 6 (issue #102).
    if (Input.IsActionJustPressed ("weapon_7") && HasBread) SelectedWeapon = SelectedWeapon.Bread; // Slot 7 (issue #209).
    if (Input.IsActionJustPressed ("weapon_8") && HasBlowgun) SelectedWeapon = SelectedWeapon.Blowgun; // Slot 8 (issue #194).
    // Cycling (issue #186): mouse wheel for mouse players, Q & E for trackpads -
    // reaching for 7 mid-fight to eat is not a real option.
    if (Input.IsActionJustPressed ("cycle_weapon_next")) CycleSelectedWeapon (1);
    if (Input.IsActionJustPressed ("cycle_weapon_previous")) CycleSelectedWeapon (-1);
  }

  // Trackpad two-finger scrolling arrives as a FLOOD of fine-grained wheel ticks -
  // uncooled, one swipe machine-guns through every slot (issue #186). One step per
  // interval keeps a deliberate notch-scroll feeling 1:1.
  [Export] public float WeaponCycleCooldownSeconds = 0.2f;
  private ulong _nextCycleAllowedMs;

  // Steps through what you're actually CARRYING, in slot order, wrapping around -
  // empty slots are skipped, so cycling never lands on a weapon you don't have.
  private void CycleSelectedWeapon (int step)
  {
    if (Time.GetTicksMsec() < _nextCycleAllowedMs) return;
    var carried = SelectableSlots();
    if (carried.Count < 2) return; // Fists alone: nothing to cycle to.
    _nextCycleAllowedMs = Time.GetTicksMsec() + (ulong)(WeaponCycleCooldownSeconds * 1000.0f);
    SelectedWeapon = NextCycleSlot (carried, _selectedWeapon, step);
  }

  // Pure wrap math, public static so the unit tests can hit the boundaries directly
  // (CodeRabbit on #219): forward & reverse wrap, & a current slot not in the list.
  public static SelectedWeapon NextCycleSlot (List <SelectedWeapon> carried, SelectedWeapon current, int step)
  {
    var index = carried.IndexOf (current);
    var next = index < 0 ? 0 : ((index + step) % carried.Count + carried.Count) % carried.Count;
    return carried[next];
  }

  // Fists are always available; everything else only while it's in your hands.
  private List <SelectedWeapon> SelectableSlots()
  {
    var slots = new List <SelectedWeapon> { SelectedWeapon.Fists };
    if (HasLaser) slots.Add (SelectedWeapon.Laser);
    if (HasBanana) slots.Add (SelectedWeapon.Banana);
    if (HasBoomerang) slots.Add (SelectedWeapon.Boomerang);
    if (HasSlingshot) slots.Add (SelectedWeapon.Slingshot);
    if (HasPaperAirplane) slots.Add (SelectedWeapon.PaperAirplane);
    if (HasBlowgun) slots.Add (SelectedWeapon.Blowgun); // Slot 8 rides with the guns (issue #194)...
    if (HasBread) slots.Add (SelectedWeapon.Bread); // ...& the loaf stays the last stop on the wheel.
    return slots;
  }

  // A dropped or lost gun can't stay selected: fall back to fists (issue #82).
  private void DeselectUnheldWeapon()
  {
    if (IsBreadSelected && !HasBread) SelectedWeapon = SelectedWeapon.Fists; // An eaten or lost loaf (issue #209).
    if (IsLaserSelected && !HasLaser) SelectedWeapon = SelectedWeapon.Fists;
    if (IsBananaSelected && !HasBanana) SelectedWeapon = SelectedWeapon.Fists;
    if (IsBoomerangSelected && !HasBoomerang) SelectedWeapon = SelectedWeapon.Fists;
    if (IsSlingshotSelected && !HasSlingshot) SelectedWeapon = SelectedWeapon.Fists;
    if (IsPaperAirplaneSelected && !HasPaperAirplane) SelectedWeapon = SelectedWeapon.Fists;
    if (IsBlowgunSelected && !HasBlowgun) SelectedWeapon = SelectedWeapon.Fists; // Slot 8 (issue #194).
  }

  // Called back (via the WeaponSpawner's ConfirmPickup RPC) after the server despawns
  // the claimed pickup for everyone.
  public void GrantWeapon (HeldWeapon type, string previousOwner = "")
  {
    // Bread lives in slot 7 now (issue #209), but it's the one pickup that never
    // auto-equips (issue #128): swapping a gun for a snack mid-fight is the last
    // thing anyone wants. Press 7 when you actually mean to eat.
    if (type == HeldWeapon.Bread)
    {
      SetBreadHeld (isHeld: true);
      _weaponPickupSound.Play();
      GD.Print ($"{DisplayName}: I picked up a loaf of bread!");
      return;
    }

    HeldWeapon |= type;
    // Every pickup auto-equips (issue #128), boomerang (#98), slingshot (#99), & paper airplane (#102) included.
    SelectedWeapon = type switch { HeldWeapon.Banana => SelectedWeapon.Banana, HeldWeapon.Boomerang => SelectedWeapon.Boomerang, HeldWeapon.Slingshot => SelectedWeapon.Slingshot, HeldWeapon.PaperAirplane => SelectedWeapon.PaperAirplane, HeldWeapon.Blowgun => SelectedWeapon.Blowgun, _ => SelectedWeapon.Laser };
    RememberTheft (type, previousOwner);
    _weaponPickupSound.Play(); // Satisfying pickup chime, owner-local only (issue #123).
    GD.Print ($"{DisplayName}: I picked up a {type}!");
  }

  private void RememberTheft (HeldWeapon type, string previousOwner)
  {
    if (previousOwner.Length == 0 || previousOwner == DisplayName) return;
    _stolenWeapon = type;
    _stolenFrom = previousOwner;
  }

  // One-shot check (issue #84): did this kill zap the previous owner of a weapon
  // we're still carrying? Clears after reporting so repeat kills don't keep gloating.
  public bool TookRevengeOn (string victimName)
  {
    if (victimName != _stolenFrom || !Holds (_stolenWeapon)) return false;
    _stolenWeapon = HeldWeapon.None;
    _stolenFrom = string.Empty;
    return true;
  }

  // Drops the selected gun (or another carried one while boxing) as a world pickup;
  // the punch branch calls this with a drop chance when a player gets punched.
  public void DropHeldWeapon()
  {
    var type = PickDroppableWeapon();
    if (type == HeldWeapon.None) return;
    // Request BEFORE clearing (CodeRabbit on #145): the server validates the drop
    // mask against this player's replicated HeldWeapon, so the request must leave
    // while the local flags (& therefore the server's replicated view) still show
    // the weapon; clearing first would race the request with the clear delta.
    Spawner.SendDropRequest (GlobalPosition, type);
    HeldWeapon &= ~type;
    ForgetTheft (type);
    DeselectUnheldWeapon();
    GD.Print ($"{DisplayName}: I dropped my {type}!");
  }

  // Selected gun first, then any other carried one. A boomerang or paper airplane
  // that's out flying isn't in the hand, so it can't be knocked loose or stolen
  // from it (issues #98 & #102). Bread is never on this list (issue #190): punches &
  // boomerangs take weapons, not lunch - only dying drops the loaf.
  private HeldWeapon PickDroppableWeapon()
  {
    var preferred = _selectedWeapon switch { SelectedWeapon.Banana => HeldWeapon.Banana, SelectedWeapon.Boomerang => HeldWeapon.Boomerang, SelectedWeapon.Slingshot => HeldWeapon.Slingshot, SelectedWeapon.PaperAirplane => HeldWeapon.PaperAirplane, SelectedWeapon.Blowgun => HeldWeapon.Blowgun, _ => HeldWeapon.Laser };

    foreach (var type in new[] { preferred, HeldWeapon.Laser, HeldWeapon.Banana, HeldWeapon.Boomerang, HeldWeapon.Slingshot, HeldWeapon.PaperAirplane, HeldWeapon.Blowgun })
    {
      if (!Holds (type)) continue;
      if (type == HeldWeapon.Boomerang && IsBoomerangInFlight) continue;
      if (type == HeldWeapon.PaperAirplane && IsAirplaneInFlight) continue;
      return type;
    }

    return HeldWeapon.None;
  }

  // Death drops EVERYTHING carried at the death spot (issues #72 & #190): guns, the
  // uneaten loaf, & anything nocked in the slingshot. A boomerang out flying drops
  // where the boomerang is instead (issue #98). Request before clear, same as
  // DropHeldWeapon: the server validates the mask against the replicated HeldWeapon
  // (CodeRabbit on #145).
  private void DropAllHeldWeapons()
  {
    ReleaseBoomerangInFlight();
    ReleaseAirplaneInFlight(); // A mid-glide airplane lands where the airplane is (issues #102 & #191).
    DropLoadedAmmo();
    if (_heldWeapon == HeldWeapon.None) return;
    Spawner.SendDropRequest (GlobalPosition, _heldWeapon);
    ClearHeldWeapons();
  }

  // First-person overlay (issue #124): the local player's own weapons & hands draw
  // over world geometry so turning against a wall can't clip them inside it.
  // Authority-only on purpose: these same nodes are the weapon model every other
  // peer sees on this player, & those must keep normal depth testing.
  private void ApplyFirstPersonWeaponOverlay()
  {
    ApplyOverlayMaterials (_energyWeapon);
    ApplyOverlayMaterials (_bananaLauncher);
    ApplyOverlayMaterials (_boomerangHeld);
    ApplyOverlayMaterials (_slingshotHeld); // Slot 5 (issue #99).
    ApplyOverlayMaterials (_airplaneHeld); // Slot 6 (issue #102).
    ApplyOverlayMaterials (_breadHeld); // Slot 7 (issue #209).
    ApplyOverlayMaterials (_blowgunHeld); // Slot 8 (issue #194).
  }

  private void ApplyOverlayMaterials (Node node)
  {
    if (node is MeshInstance3D mesh) ApplyOverlayMaterial (mesh);
    foreach (var child in node.GetChildren()) ApplyOverlayMaterials (child);
  }

  // Per-instance override materials (muzzle, banana launcher) are mutated in place;
  // shared imported materials (glb handle) are duplicated first so puppets keep theirs.
  private void ApplyOverlayMaterial (MeshInstance3D mesh)
  {
    if (mesh.MaterialOverride is BaseMaterial3D overrideMaterial) { MakeOverlay (overrideMaterial); return; }
    for (var surface = 0; surface < mesh.GetSurfaceOverrideMaterialCount(); ++surface) ApplyOverlaySurface (mesh, surface);
  }

  private void ApplyOverlaySurface (MeshInstance3D mesh, int surface)
  {
    if (mesh.GetActiveMaterial (surface) is not BaseMaterial3D material) return;
    var copy = (BaseMaterial3D)material.Duplicate();
    MakeOverlay (copy);
    mesh.SetSurfaceOverrideMaterial (surface, copy);
  }

  // Each overlaid material's original settings, so the third-person view (issue #119)
  // can restore the normal on-the-body look & the toggle can re-overlay it.
  private readonly List <(BaseMaterial3D Material, BaseMaterial3D.TransparencyEnum Transparency, bool NoDepthTest, int RenderPriority)> _overlayMaterials = [];

  private void MakeOverlay (BaseMaterial3D material)
  {
    _overlayMaterials.Add ((material, material.Transparency, material.NoDepthTest, material.RenderPriority));
    ApplyOverlay (material);
  }

  // Alpha transparency is required for render_priority to order the draw (priority
  // only applies in the transparent pass); 99 keeps the crosshairs (100) on top.
  private static void ApplyOverlay (BaseMaterial3D material)
  {
    material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
    material.NoDepthTest = true;
    material.RenderPriority = 99;
  }

  // The overlay is a first-person trick only (issue #119): the third-person view
  // restores every material's originals so the weapons render normally on the body.
  private void SetFirstPersonOverlayEnabled (bool enabled)
  {
    foreach (var original in _overlayMaterials)
    {
      if (enabled) { ApplyOverlay (original.Material); continue; }
      original.Material.Transparency = original.Transparency;
      original.Material.NoDepthTest = original.NoDepthTest;
      original.Material.RenderPriority = original.RenderPriority;
    }
  }
}
