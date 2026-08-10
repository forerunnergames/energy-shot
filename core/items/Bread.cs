namespace com.forerunnergames.energyshot.items;

// One-per-life healing snack (issue #62): every (re)spawn restocks it, & eating it
// (the eat_bread action) restores the player to full health.
public class Bread
{
  public bool IsAvailable { get; private set; } = true;
  public void Restock() => IsAvailable = true;

  public bool TryEat()
  {
    if (!IsAvailable) return false;
    IsAvailable = false;
    return true;
  }
}
