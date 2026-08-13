using com.forerunnergames.energyshot.weapons;
using Godot;

namespace com.forerunnergames.energyshot.players;

// Weapon lifecycle & selection (issues #72 & #82): players spawn with fists only &
// arm up from world pickups; death drops everything held at the death spot. Slot 1 =
// fists (always available), slot 2 = laser, slot 3 = banana; guns are selectable only
// while held. The server-side WeaponSpawner owns pickup spawning & the weapon caps.
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
      _heldWeapon = value;
      UpdateWeaponVisibility();
    }
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
  private bool HasLaser => Holds (HeldWeapon.Laser);
  private bool HasBanana => Holds (HeldWeapon.Banana);
  private bool HasBoomerang => Holds (HeldWeapon.Boomerang);
  private bool IsFistsSelected => _selectedWeapon == SelectedWeapon.Fists;
  private bool IsLaserSelected => _selectedWeapon == SelectedWeapon.Laser;
  private bool IsBananaSelected => _selectedWeapon == SelectedWeapon.Banana;
  private bool IsBoomerangSelected => _selectedWeapon == SelectedWeapon.Boomerang;
  private WeaponSpawner Spawner => _weaponSpawner ??= GetNode <WeaponSpawner> ("/root/World/WeaponSpawner");

  // Falling off the world: held weapons return to the spawn pool via the caps instead
  // of dropping as unreachable pickups below the map.
  private void ClearHeldWeapons()
  {
    ReleaseBoomerangInFlight(); // A boomerang out flying still drops where it is (issue #98).
    ForgetTheft (_heldWeapon);
    HeldWeapon = HeldWeapon.None;
    SelectedWeapon = SelectedWeapon.Fists;
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
    if (_bananaLauncher == null || _boomerangHeld == null) return;
    _energyWeapon.Visible = IsLaserSelected && HasLaser;
    _bananaLauncher.Visible = IsBananaSelected && HasBanana;
    _boomerangHeld.Visible = IsBoomerangSelected && HasBoomerang && !IsBoomerangOut; // Empty hand while it's out flying (issue #98).
    UpdateHandsVisibility(); // Hands render only while fists are selected (issue #82).
  }

  // Fists are always selectable; guns only while held (issue #82). No CanFire gate: a
  // cooling banana stays selected, it just can't fire yet (issue #83).
  private void UpdateWeaponSelection()
  {
    if (!_isInputEnabled) return;
    if (Input.IsActionJustPressed ("weapon_1")) SelectedWeapon = SelectedWeapon.Fists;
    if (Input.IsActionJustPressed ("weapon_2") && HasLaser) SelectedWeapon = SelectedWeapon.Laser;
    if (Input.IsActionJustPressed ("weapon_3") && HasBanana) SelectedWeapon = SelectedWeapon.Banana;
    if (Input.IsActionJustPressed ("weapon_4") && HasBoomerang) SelectedWeapon = SelectedWeapon.Boomerang; // Slot 4 (issue #98).
  }

  // A dropped or lost gun can't stay selected: fall back to fists (issue #82).
  private void DeselectUnheldWeapon()
  {
    if (IsLaserSelected && !HasLaser) SelectedWeapon = SelectedWeapon.Fists;
    if (IsBananaSelected && !HasBanana) SelectedWeapon = SelectedWeapon.Fists;
    if (IsBoomerangSelected && !HasBoomerang) SelectedWeapon = SelectedWeapon.Fists;
  }

  // Called back (via the WeaponSpawner's ConfirmPickup RPC) after the server despawns
  // the claimed pickup for everyone.
  public void GrantWeapon (HeldWeapon type, string previousOwner = "")
  {
    HeldWeapon |= type;
    // Every pickup auto-equips (issue #128), boomerang included (issue #98).
    SelectedWeapon = type switch { HeldWeapon.Banana => SelectedWeapon.Banana, HeldWeapon.Boomerang => SelectedWeapon.Boomerang, _ => SelectedWeapon.Laser };
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
    HeldWeapon &= ~type;
    ForgetTheft (type);
    DeselectUnheldWeapon();
    Spawner.SendDropRequest (GlobalPosition, type);
    GD.Print ($"{DisplayName}: I dropped my {type}!");
  }

  // Selected gun first, then any other carried one. A boomerang that's out flying
  // isn't in the hand, so it can't be knocked loose or stolen from it (issue #98).
  private HeldWeapon PickDroppableWeapon()
  {
    var preferred = _selectedWeapon switch { SelectedWeapon.Banana => HeldWeapon.Banana, SelectedWeapon.Boomerang => HeldWeapon.Boomerang, _ => HeldWeapon.Laser };

    foreach (var type in new[] { preferred, HeldWeapon.Laser, HeldWeapon.Banana, HeldWeapon.Boomerang })
    {
      if (!Holds (type)) continue;
      if (type == HeldWeapon.Boomerang && IsBoomerangInFlight) continue;
      return type;
    }

    return HeldWeapon.None;
  }

  // Death drops everything carried at the death spot (issue #72); a boomerang out
  // flying drops where the boomerang is instead (issue #98).
  private void DropAllHeldWeapons()
  {
    ReleaseBoomerangInFlight();
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
  }

  private static void ApplyOverlayMaterials (Node node)
  {
    if (node is MeshInstance3D mesh) ApplyOverlayMaterial (mesh);
    foreach (var child in node.GetChildren()) ApplyOverlayMaterials (child);
  }

  // Per-instance override materials (muzzle, banana launcher) are mutated in place;
  // shared imported materials (glb handle) are duplicated first so puppets keep theirs.
  private static void ApplyOverlayMaterial (MeshInstance3D mesh)
  {
    if (mesh.MaterialOverride is BaseMaterial3D overrideMaterial) { MakeOverlay (overrideMaterial); return; }
    for (var surface = 0; surface < mesh.GetSurfaceOverrideMaterialCount(); ++surface) ApplyOverlaySurface (mesh, surface);
  }

  private static void ApplyOverlaySurface (MeshInstance3D mesh, int surface)
  {
    if (mesh.GetActiveMaterial (surface) is not BaseMaterial3D material) return;
    var copy = (BaseMaterial3D)material.Duplicate();
    MakeOverlay (copy);
    mesh.SetSurfaceOverrideMaterial (surface, copy);
  }

  // Alpha transparency is required for render_priority to order the draw (priority
  // only applies in the transparent pass); 99 keeps the crosshairs (100) on top.
  private static void MakeOverlay (BaseMaterial3D material)
  {
    material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
    material.NoDepthTest = true;
    material.RenderPriority = 99;
  }
}
