using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

[TestSuite]
public partial class PlaceholderTest : Node
{
  [BeforeTest]
  public void BeforeTest() { }

  [AfterTest]
  public void AfterTest() { }

  [TestCase]
  public void ExampleTest() => AssertBool (true);
}
