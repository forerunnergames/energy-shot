using System;
using com.forerunnergames.energyshot.players;
using com.forerunnergames.energyshot.weapons;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// Hands hold the weapons (issue #351): the per-weapon grip table parks both
// sphere hands on the held visual, in first & third person alike.
[TestSuite]
public class HandsTest
{
  private static readonly Vector3 LeftRest = new(-0.55f, -0.45f, -0.85f);
  private static readonly Vector3 RightRest = new(0.55f, -0.45f, -0.85f);

  [TestCase]
  public void FistsKeepBothHandsAtRest()
  {
    AssertObject (Player.GripOffset (SelectedWeapon.Fists, 0, LeftRest, RightRest)).IsEqual (LeftRest);
    AssertObject (Player.GripOffset (SelectedWeapon.Fists, 1, LeftRest, RightRest)).IsEqual (RightRest);
  }

  [TestCase]
  public void TwoHandedWeaponsMoveBothHandsOffRest()
  {
    foreach (var weapon in new[] { SelectedWeapon.Laser, SelectedWeapon.Banana, SelectedWeapon.Slingshot, SelectedWeapon.Bread, SelectedWeapon.Blowgun })
    {
      AssertObject (Player.GripOffset (weapon, 0, LeftRest, RightRest)).IsNotEqual (LeftRest);
      AssertObject (Player.GripOffset (weapon, 1, LeftRest, RightRest)).IsNotEqual (RightRest);
    }
  }

  [TestCase]
  public void OneHandedWeaponsGripWithTheRightHandOnly()
  {
    foreach (var weapon in new[] { SelectedWeapon.Boomerang, SelectedWeapon.PaperAirplane })
    {
      AssertObject (Player.GripOffset (weapon, 0, LeftRest, RightRest)).IsEqual (LeftRest);
      AssertObject (Player.GripOffset (weapon, 1, LeftRest, RightRest)).IsNotEqual (RightRest);
    }
  }

  [TestCase]
  public void EveryWeaponsGrippingHandIsInFrontOfTheCamera()
  {
    // Camera-local -Z is forward: a grip behind the camera would be invisible.
    foreach (SelectedWeapon weapon in Enum.GetValues <SelectedWeapon>())
    {
      AssertFloat (Player.GripOffset (weapon, 0, LeftRest, RightRest).Z).IsLess (0.0f);
      AssertFloat (Player.GripOffset (weapon, 1, LeftRest, RightRest).Z).IsLess (0.0f);
    }
  }

  [TestCase]
  public void GripsAreDistinctPerWeapon()
  {
    // The right (gripping) hand must sit somewhere unique per weapon - identical
    // grips would mean a copy-paste row in the table.
    var seen = new System.Collections.Generic.HashSet <Vector3>();
    foreach (var weapon in new[] { SelectedWeapon.Laser, SelectedWeapon.Banana, SelectedWeapon.Boomerang, SelectedWeapon.Slingshot, SelectedWeapon.PaperAirplane, SelectedWeapon.Bread, SelectedWeapon.Blowgun })
      AssertBool (seen.Add (Player.GripOffset (weapon, 1, LeftRest, RightRest))).IsTrue();
  }
}
