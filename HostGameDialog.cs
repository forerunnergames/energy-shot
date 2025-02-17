using energyshot;
using Godot;

public partial class HostGameDialog : Control
{
  [Signal] public delegate void HostGameSuccessEventHandler();
  [Signal] public delegate void ClosedEventHandler();
  private Button _closeButton = null!;
  private Button _hostGameButton = null!;
  private LineEdit _serverAddress = null!;
  private Label _topText = null!;
  private Label _bottomText = null!;
  private ENetMultiplayerPeer? _peer;
  private int _serverPort = -1;

  public override void _Ready()
  {
    _closeButton = GetNode <Button> ("PanelContainer/MarginContainer/VBoxContainer/HBoxContainer/MarginContainer/CloseButton");
    _hostGameButton = GetNode <Button> ("PanelContainer/MarginContainer/VBoxContainer/HostGameButton");
    _serverAddress = GetNode <LineEdit> ("PanelContainer/MarginContainer/VBoxContainer/ServerAddress");
    _topText = GetNode <Label> ("PanelContainer/MarginContainer/VBoxContainer/TopText");
    _bottomText = GetNode <Label> ("PanelContainer/MarginContainer/VBoxContainer/BottomText");
    _closeButton.Pressed += Hide;
    _hostGameButton.Pressed += OnHostGameButtonPressed;
  }

  public async void Show (ENetMultiplayerPeer peer, int serverPort)
  {
    _peer = peer;
    _serverPort = serverPort;
    _topText.Text = "Finding your server address...";
    _serverAddress.Text = string.Empty;
    _bottomText.Text = string.Empty;
    _hostGameButton.Disabled = true;
    Show();
    await ToSignal (GetTree(), SceneTree.SignalName.ProcessFrame);
    await ToSignal (GetTree(), SceneTree.SignalName.ProcessFrame);
    var (success, address, error) = Tools.FindServerAddress (serverPort);
    _topText.Text = success ? "Your server address:" : $"Failed to find your server address. Please type it manually\n{error}";
    _serverAddress.Text = address;
    _bottomText.Text = success ? "Please share this with your friends so they can join your game!" : string.Empty;
    _hostGameButton.Disabled = false;
    _serverAddress.Editable = !success;
  }

  private void OnHostGameButtonPressed()
  {
    _peer?.Close();
    _hostGameButton.Disabled = false;
    _bottomText.Text = $"Creating server at [{_serverAddress.Text}:{_serverPort}]...";

    if (_peer == null)
    {
      OnError ("Failed to host game, error [ENetMultiplayerPeer not set]");
      return;
    }

    if (_serverPort == -1)
    {
      OnError ("Failed to host game, error [server port not set]");
      return;
    }

    var error = _peer.CreateServer (_serverPort);
    Multiplayer.MultiplayerPeer = _peer;

    if (error != Error.Ok)
    {
      OnError ($"Failed to host game, error [{error}]");
      return;
    }

    GD.Print ($"Successfully hosted game on [{_serverAddress.Text}:{_serverPort}]!");
    Hide();
    EmitSignal (SignalName.HostGameSuccess);
  }

  private void OnError (string error)
  {
    _peer?.Close();
    _bottomText.Text = error;
    _hostGameButton.Disabled = false;
    GD.Print (error);
  }
}
