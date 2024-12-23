using energyshot;
using Godot;

public partial class HostGameStartDialog : Control
{
  [Signal] public delegate void StartGamePressedEventHandler();
  [Signal] public delegate void ClosedEventHandler();
  private Button _closeButton = null!;
  private Button _startGameButton = null!;
  private LineEdit _serverAddress = null!;
  private Label _topText = null!;
  private Label _bottomText = null!;

  public override void _Ready()
  {
    _closeButton = GetNode <Button> ("PanelContainer/MarginContainer/VBoxContainer/HBoxContainer/MarginContainer/CloseButton");
    _startGameButton = GetNode <Button> ("PanelContainer/MarginContainer/VBoxContainer/StartGameButton");
    _serverAddress = GetNode <LineEdit> ("PanelContainer/MarginContainer/VBoxContainer/ServerAddress");
    _topText = GetNode <Label> ("PanelContainer/MarginContainer/VBoxContainer/TopText");
    _bottomText = GetNode <Label> ("PanelContainer/MarginContainer/VBoxContainer/BottomText");
    _closeButton.Pressed += Hide;
    _startGameButton.Pressed += OnStartGamePressed;
  }

  public async void Show (int serverPort)
  {
    _topText.Text = "Finding your server address...";
    _serverAddress.Text = string.Empty;
    _bottomText.Text = string.Empty;
    _startGameButton.Disabled = true;
    Show();
    await ToSignal (GetTree(), SceneTree.SignalName.ProcessFrame);
    await ToSignal (GetTree(), SceneTree.SignalName.ProcessFrame);
    var (success, address) = Tools.FindServerAddress (serverPort);
    _topText.Text = success ? "Your server address:" : "Failed to find your server address.";
    _serverAddress.Text = address;
    _bottomText.Text = success ? "Please share this with your friends so they can join your game!" : string.Empty;
    _startGameButton.Disabled = !success;
  }

  private void OnStartGamePressed()
  {
    Hide();
    EmitSignal (SignalName.StartGamePressed);
  }
}
