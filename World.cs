using Godot;

public partial class World : Node3D
{
  public override void _UnhandledInput (InputEvent @event)
  {
    if (!Input.IsActionJustPressed ("quit")) return;
    GetTree().Quit();
  }
}
