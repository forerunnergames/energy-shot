using com.forerunnergames.energyshot.players;
using com.forerunnergames.energyshot.utilities;
using Godot;

namespace com.forerunnergames.energyshot.ui.dialogs;

public partial class HostGameDialog : Control
{
  [Signal] public delegate void HostGameSuccessEventHandler (string playerName, int difficulty, int maxPlayers, string password, int colorIndex);
  [Signal] public delegate void ClosedEventHandler();
  private Button _closeButton = null!;
  private Button _hostGameButton = null!;
  private LineEdit _playerName = null!;
  private OptionButton _difficulty = null!;
  private OptionButton _playerColor = null!;
  private SpinBox _maxPlayers = null!;
  private OptionButton _gameMode = null!; // Issue #44.
  private SpinBox _roundMinutes = null!; // Issue #153.
  private SpinBox _zapLimit = null!;
  private LineEdit _password = null!;
  private LineEdit _serverAddress = null!;
  private Label _middleText = null!;
  private Label _bottomText = null!;
  private ENetMultiplayerPeer? _peer;
  private int _serverPort = -1;
  private void OnPlayerNameTextChanged (string newText) => UpdateHostGameButtonState();
  private void OnPasswordTextChanged (string newText) => UpdateHostGameButtonState();
  private void OnServerAddressTextChanged (string newText) => UpdateHostGameButtonState();
  private void UpdateHostGameButtonState() => _hostGameButton.Disabled = !IsValid (_playerName.Text, _serverAddress.Text, _password.Text);
  // Hosted games always require a password (issue #90).
  private static bool IsValid (string playerName, string serverAddress, string password) => Tools.IsValidPlayerName (playerName) && Tools.IsValidServerAddress (serverAddress) && !string.IsNullOrEmpty (password);

  public override void _Ready()
  {
    _closeButton = GetNode <Button> ("PanelContainer/ScrollContainer/MarginContainer/VBoxContainer/HBoxContainer/MarginContainer/CloseButton");
    _hostGameButton = GetNode <Button> ("PanelContainer/ScrollContainer/MarginContainer/VBoxContainer/HostGameButton");
    _playerName = GetNode <LineEdit> ("PanelContainer/ScrollContainer/MarginContainer/VBoxContainer/PlayerName");
    _difficulty = GetNode <OptionButton> ("PanelContainer/ScrollContainer/MarginContainer/VBoxContainer/Difficulty");
    // The dropdown's popup items don't inherit the button's font size override.
    _difficulty.GetPopup().AddThemeFontSizeOverride ("font_size", 90);
    _playerColor = GetNode <OptionButton> ("PanelContainer/ScrollContainer/MarginContainer/VBoxContainer/PlayerColor");
    _playerColor.GetPopup().AddThemeFontSizeOverride ("font_size", 90);
    PlayerColors.Populate (_playerColor); // Selectable body color (issue #43).
    _maxPlayers = GetNode <SpinBox> ("PanelContainer/ScrollContainer/MarginContainer/VBoxContainer/MaxPlayers");
    _gameMode = GetNode <OptionButton> ("PanelContainer/ScrollContainer/MarginContainer/VBoxContainer/GameMode");
    _gameMode.ItemSelected += mode => _zapLimit.Value = Settings.PointLimit ((core.world.GameMode)(int)mode); // Show that mode's own limit (issue #44).
    _roundMinutes = GetNode <SpinBox> ("PanelContainer/ScrollContainer/MarginContainer/VBoxContainer/RoundMinutes");
    _zapLimit = GetNode <SpinBox> ("PanelContainer/ScrollContainer/MarginContainer/VBoxContainer/ZapLimit");
    // A SpinBox's text lives in its INTERNAL LineEdit, which ignores the SpinBox
    // node's font override (Aaron, 2026-08-22: the type-a-number fields were
    // insanely tiny on Mac) - push the size where the text actually renders.
    foreach (var spinBox in new[] { _maxPlayers, _roundMinutes, _zapLimit })
    {
      spinBox.GetLineEdit().AddThemeFontSizeOverride ("font_size", 90);
      spinBox.CustomMinimumSize = new Vector2 (0.0f, 120.0f);
    }
    _password = GetNode <LineEdit> ("PanelContainer/ScrollContainer/MarginContainer/VBoxContainer/Password");
    _serverAddress = GetNode <LineEdit> ("PanelContainer/ScrollContainer/MarginContainer/VBoxContainer/ServerAddress");
    _middleText = GetNode <Label> ("PanelContainer/ScrollContainer/MarginContainer/VBoxContainer/MiddleText");
    _bottomText = GetNode <Label> ("PanelContainer/ScrollContainer/MarginContainer/VBoxContainer/BottomText");
    _hostGameButton.Disabled = true;
    _playerName.TextChanged += OnPlayerNameTextChanged;
    _password.TextChanged += OnPasswordTextChanged;
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
    if (string.IsNullOrEmpty (_password.Text)) _password.Text = Settings.HostPassword;
    _difficulty.Selected = Settings.Difficulty;
    _playerColor.Selected = PlayerColors.NormalizeIndex (Settings.PlayerColor);
    _maxPlayers.Value = Settings.MaxPlayers;
    _gameMode.Selected = Settings.GameMode;
    _roundMinutes.Value = Settings.RoundMinutes;
    _zapLimit.Value = Settings.PointLimit ((core.world.GameMode)_gameMode.Selected); // KOTH & zaps each remember their own limit (issue #44).
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
    Settings.PlayerColor = _playerColor.Selected;
    Settings.MaxPlayers = (int)_maxPlayers.Value;
    Settings.GameMode = _gameMode.Selected; // Read back by World.OnHostGameSuccess (issue #44).
    Settings.RoundMinutes = (int)_roundMinutes.Value; // Read back by World.OnHostGameSuccess (issue #153).
    Settings.SetPointLimit ((core.world.GameMode)_gameMode.Selected, (int)_zapLimit.Value); // Save under the selected mode.
    Settings.HostPassword = _password.Text;
    Hide();
    EmitSignal (SignalName.HostGameSuccess, _playerName.Text, _difficulty.Selected, (int)_maxPlayers.Value, _password.Text, _playerColor.Selected);
  }

  private void OnError (string error)
  {
    _peer?.Close();
    _bottomText.Text = error;
    GD.Print (error);
    UpdateHostGameButtonState();
  }
}
