using com.forerunnergames.energyshot.utilities;
using Godot;

namespace com.forerunnergames.energyshot.ui.dialogs;

public partial class HostGameDialog : Control
{
  [Signal] public delegate void HostGameSuccessEventHandler (string playerName, int difficulty, int maxPlayers);
  [Signal] public delegate void ClosedEventHandler();
  private Button _closeButton = null!;
  private Button _hostGameButton = null!;
  private LineEdit _playerName = null!;
  private OptionButton _difficulty = null!;
  private SpinBox _maxPlayers = null!;
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
    _difficulty = GetNode <OptionButton> ("PanelContainer/MarginContainer/VBoxContainer/Difficulty");
    // The dropdown's popup items don't inherit the button's font size override.
    _difficulty.GetPopup().AddThemeFontSizeOverride ("font_size", 90);
    _maxPlayers = GetNode <SpinBox> ("PanelContainer/MarginContainer/VBoxContainer/MaxPlayers");
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
    if (string.IsNullOrEmpty (_playerName.Text)) _playerName.Text = Settings.PlayerName;
    _difficulty.Selected = Settings.Difficulty;
    _maxPlayers.Value = Settings.MaxPlayers;
    UpdateHostGameButtonState();
    Show();
    // UPnP discovery can take seconds; run it off the main thread so the UI stays responsive (see issue #25).
    var (success, address, error) = await System.Threading.Tasks.Task.Run (() => Tools.FindServerAddress (serverPort));
    if (!IsInsideTree()) return;
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

    if (error != Error.Ok)
    {
      OnError ($"Failed to host game, error [{error}]");
      return;
    }

    // Only assign a working peer, so a failed attempt doesn't leave a dead peer active (see issue #24).
    Multiplayer.MultiplayerPeer = _peer;

    GD.Print ($"Successfully hosted server at [{_serverAddress.Text}:{_serverPort}]!");
    Settings.PlayerName = _playerName.Text;
    Settings.Difficulty = _difficulty.Selected;
    Settings.MaxPlayers = (int)_maxPlayers.Value;
    Hide();
    EmitSignal (SignalName.HostGameSuccess, _playerName.Text, _difficulty.Selected, (int)_maxPlayers.Value);
  }

  private void OnError (string error)
  {
    _peer?.Close();
    _bottomText.Text = error;
    GD.Print (error);
    UpdateHostGameButtonState();
  }
}
