using Godot;

namespace com.forerunnergames.energyshot;

public partial class HostGameDialog : Control
{
  [Signal] public delegate void HostGameSuccessEventHandler (string playerName);
  [Signal] public delegate void ClosedEventHandler();
  private Button _closeButton = null!;
  private Button _hostGameButton = null!;
  private LineEdit _playerName = null!;
  private LineEdit _serverAddress = null!;
  private Label _middleText = null!;
  private Label _bottomText = null!;
  private ENetMultiplayerPeer? _peer;
  private int _serverPort = -1;
  private void OnPlayerNameTextChanged (string newText) => UpdateHostGameButtonState();
  private void OnServerAddressTextChanged (string newText) => UpdateHostGameButtonState();
  private void UpdateHostGameButtonState() => _hostGameButton.Disabled = !IsValid (_playerName.Text, _serverAddress.Text);
  private static bool IsValid (string playerName, string serverAddress) => Tools.IsValidPlayerName (playerName) && Tools.IsValidServerAddress (serverAddress);

  public override void _Ready()
  {
    _closeButton = GetNode <Button> ("PanelContainer/MarginContainer/VBoxContainer/HBoxContainer/MarginContainer/CloseButton");
    _hostGameButton = GetNode <Button> ("PanelContainer/MarginContainer/VBoxContainer/HostGameButton");
    _playerName = GetNode <LineEdit> ("PanelContainer/MarginContainer/VBoxContainer/PlayerName");
    _serverAddress = GetNode <LineEdit> ("PanelContainer/MarginContainer/VBoxContainer/ServerAddress");
    _middleText = GetNode <Label> ("PanelContainer/MarginContainer/VBoxContainer/MiddleText");
    _bottomText = GetNode <Label> ("PanelContainer/MarginContainer/VBoxContainer/BottomText");
    _hostGameButton.Disabled = true;
    _playerName.TextChanged += OnPlayerNameTextChanged;
    _serverAddress.TextChanged += OnServerAddressTextChanged;
    _closeButton.Pressed += Hide;
    _hostGameButton.Pressed += OnHostGameButtonPressed;
  }

  public async void Show (ENetMultiplayerPeer peer, int serverPort)
  {
    _peer = peer;
    _serverPort = serverPort;
    _middleText.Text = "Finding your server address...";
    _serverAddress.Text = string.Empty;
    _bottomText.Text = string.Empty;
    UpdateHostGameButtonState();
    Show();
    await ToSignal (GetTree(), SceneTree.SignalName.ProcessFrame);
    await ToSignal (GetTree(), SceneTree.SignalName.ProcessFrame);
    var (success, address, error) = Tools.FindServerAddress (serverPort);
    _middleText.Text = success ? "Your server address:" : $"Failed to find your server address. Please type it manually\n{error}";
    _serverAddress.Text = address;
    _bottomText.Text = success ? "Please share this with your friends so they can join your game!" : string.Empty;
    _serverAddress.Editable = !success;
    UpdateHostGameButtonState();
  }

  private void OnHostGameButtonPressed()
  {
    _hostGameButton.Disabled = true;
    _peer?.Close();
    var message = $"Creating server at [{_serverAddress.Text}:{_serverPort}]...";
    GD.Print (message);
    _bottomText.Text = message;

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

    GD.Print ($"Successfully hosted server at [{_serverAddress.Text}:{_serverPort}]!");
    Hide();
    EmitSignal (SignalName.HostGameSuccess, _playerName.Text);
  }

  private void OnError (string error)
  {
    _peer?.Close();
    _bottomText.Text = error;
    GD.Print (error);
    UpdateHostGameButtonState();
  }
}
