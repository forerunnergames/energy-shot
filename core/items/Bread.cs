using Godot;

namespace com.forerunnergames.energyshot.items;

// One-per-life healing snack (issue #62): every (re)spawn restocks it, & eating it
// (the eat_bread action) restores the player to full health. Since issue #190 the
// loaf also rides the HeldWeapon mask, so dying drops it as a world pickup & a
// slingshot can load it as ammo; Player owns that projection (Player.Weapons.cs).
public class Bread
{
  private static readonly Color CrustBrown = new(0.62f, 0.42f, 0.18f);
  public bool IsAvailable { get; private set; } = true;
  public void Restock() => IsAvailable = true;

  public bool TryEat()
  {
    if (!IsAvailable) return false;
    IsAvailable = false;
    return true;
  }

  // Shared look for the world pickup & for a loaf nocked in a slingshot (issue
  // #190): the existing bread model, tinted like the HUD icon. Fresh materials per
  // call so the first-person overlay tweak (issue #124) can't bleed into pickups.
  public static Node3D CreateVisual()
  {
    var visual = new Node3D();
    visual.AddChild (new MeshInstance3D
    {
      Mesh = ResourceLoader.Load <Mesh> ("res://assets/items/Bread.obj"),
      MaterialOverride = new StandardMaterial3D { AlbedoColor = CrustBrown, Roughness = 0.85f },
      Scale = Vector3.One * 0.6f
    });

    return visual;
  }
}
