using Godot;

public partial class ConfirmationDialog2 : MarginContainer
{
  [Signal] public delegate void ConfirmedEventHandler();
  [Signal] public delegate void CanceledEventHandler();
  [Signal] public delegate void ClosedEventHandler();
  private Button _okButton = null!;
  private Button _cancelButton = null!;
  private Button _closeButton = null!;

  public override void _Ready()
  {
    _okButton = GetNode <Button> ("VBoxContainer/MarginContainer/HBoxContainer/OkButton");
    _cancelButton = GetNode <Button> ("VBoxContainer/MarginContainer/HBoxContainer/CancelButton");
    _closeButton = GetNode <Button> ("VBoxContainer/Title/HBoxContainer/VBoxContainer/CloseButton");

    _okButton.Pressed += () =>
    {
      Hide();
      EmitSignal (SignalName.Confirmed);
    };

    _cancelButton.Pressed += () =>
    {
      Hide();
      EmitSignal (SignalName.Canceled);
    };

    _closeButton.Pressed += () =>
    {
      Hide();
      EmitSignal (SignalName.Closed);
    };

    Hide();
  }
}
