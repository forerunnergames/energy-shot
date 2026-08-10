using com.forerunnergames.energyshot.weapons;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

// Smoke tests: the banana scenes & meshes must load & instantiate cleanly.
[TestSuite]
public partial class BananaScenesTest : Node
{
  [TestCase]
  public void BananaLauncherSceneInstantiates() => AssertObject (AutoFree (ResourceLoader.Load <PackedScene> ("res://core/weapons/BananaLauncher.tscn").Instantiate <BananaLauncher>())).IsNotNull();

  [TestCase]
  public void BananaProjectileSceneInstantiates() => AssertObject (AutoFree (ResourceLoader.Load <PackedScene> ("res://core/weapons/BananaProjectile.tscn").Instantiate <BananaProjectile>())).IsNotNull();

  [TestCase]
  public void BananaRifleMeshLoads() => AssertObject (ResourceLoader.Load <Mesh> ("res://assets/weapons/Banana_Rifle.obj")).IsNotNull();

  [TestCase]
  public void BananaMeshLoads() => AssertObject (ResourceLoader.Load <Mesh> ("res://assets/weapons/banana.obj")).IsNotNull();

  [TestCase]
  public void BreadMeshLoads() => AssertObject (ResourceLoader.Load <Mesh> ("res://assets/items/Bread.obj")).IsNotNull();
}
