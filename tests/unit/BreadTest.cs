using com.forerunnergames.energyshot.items;
using GdUnit4;
using static GdUnit4.Assertions;

namespace com.forerunnergames.energyshot;

[TestSuite]
public class BreadTest
{
  [TestCase]
  public void StartsAvailable() => AssertBool (new Bread().IsAvailable).IsTrue();

  [TestCase]
  public void EatingConsumesTheBread()
  {
    var bread = new Bread();
    AssertBool (bread.TryEat()).IsTrue();
    AssertBool (bread.IsAvailable).IsFalse();
  }

  [TestCase]
  public void CannotEatTwicePerLife()
  {
    var bread = new Bread();
    bread.TryEat();
    AssertBool (bread.TryEat()).IsFalse();
  }

  [TestCase]
  public void RespawnRestocks()
  {
    var bread = new Bread();
    bread.TryEat();
    bread.Restock();
    AssertBool (bread.TryEat()).IsTrue();
  }
}
