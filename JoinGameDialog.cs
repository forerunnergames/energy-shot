using energyshot;
using Godot;

public partial class JoinGameDialog : Control
{
  [Signal] public delegate void JoinGameSuccessEventHandler();
  [Signal] public delegate void ClosedEventHandler();
  private Button _closeButton = null!;
  private Button _joinGameButton = null!;
  private LineEdit _serverAddress = null!;
  private Label _topText = null!;
  private Label _bottomText = null!;
  private Timer _connectionTimer = null!;
  private Callable _onConnectedToServerCallable;
  private Callable _onConnectionFailedCallable;
  private Callable _onServerDisconnectedCallable;
  private ENetMultiplayerPeer? _peer;
  private int _serverPort = -1;
  private void OnServerAddressTextChanged (string newText) => _joinGameButton.Disabled = !Tools.IsValidServerAddress (newText);
  private void OnConnectionTimeout() => OnError ("Failed to connect to server, timed out.");
  private void OnConnectionFailed() => OnError ("Failed to connect to server.");
  private void OnServerDisconnected() => OnError ("Disconnected from server.");

  public override void _Ready()
  {
    _onConnectedToServerCallable = Callable.From (OnConnectedToServer);
    _onConnectionFailedCallable = Callable.From (OnConnectionFailed);
    _onServerDisconnectedCallable = Callable.From (OnServerDisconnected);
    _closeButton = GetNode <Button> ("PanelContainer/MarginContainer/VBoxContainer/HBoxContainer/MarginContainer/CloseButton");
    _joinGameButton = GetNode <Button> ("PanelContainer/MarginContainer/VBoxContainer/JoinGameButton");
    _serverAddress = GetNode <LineEdit> ("PanelContainer/MarginContainer/VBoxContainer/ServerAddress");
    _topText = GetNode <Label> ("PanelContainer/MarginContainer/VBoxContainer/TopText");
    _bottomText = GetNode <Label> ("PanelContainer/MarginContainer/VBoxContainer/BottomText");
    _connectionTimer = GetNode <Timer> ("ConnectionTimer");
    _joinGameButton.Disabled = true;
    _bottomText.Text = string.Empty;
    _closeButton.Pressed += OnCloseButtonPressed;
    _joinGameButton.Pressed += OnJoinGameButtonPressed;
    _serverAddress.TextChanged += OnServerAddressTextChanged;
    _connectionTimer.Timeout += OnConnectionTimeout;
  }

  public void Show (ENetMultiplayerPeer peer, int serverPort)
  {
    _peer = peer;
    _serverPort = serverPort;
    _bottomText.Text = string.Empty;
    _joinGameButton.Disabled = true;
    OnServerAddressTextChanged (_serverAddress.Text);
    Show();
  }

  private void OnJoinGameButtonPressed()
  {
    ConnectSignals();
    _connectionTimer.Start();
    var message = $"Connecting to server at [{_serverAddress.Text}:{_serverPort}]...";
    GD.Print (message);
    _bottomText.Text = message;
    _joinGameButton.Disabled = true;

    if (_peer == null)
    {
      OnError ("Failed to join game, error [ENetMultiplayerPeer not set]");
      return;
    }

    if (_serverPort == -1)
    {
      OnError ("Failed to join game, error [server port not set]");
      return;
    }

    var error = _peer.CreateClient (_serverAddress.Text, _serverPort);
    Multiplayer.MultiplayerPeer = _peer;

    // ReSharper disable once InvertIf
    if (error != Error.Ok)
    {
      OnError ($"Failed to join game, error [{error}]");
      return; // ReSharper disable once RedundantJumpStatement
    }
  }

  private void OnCloseButtonPressed()
  {
    StopConnecting();
    Hide();
  }

  private void OnConnectedToServer()
  {
    _connectionTimer.Stop();
    DisconnectSignals();
    Hide();
    GD.Print ($"Successfully joined game at [{_serverAddress.Text}:{_serverPort}]");
    EmitSignal (SignalName.JoinGameSuccess);
  }

  private void OnError (string error)
  {
    StopConnecting();
    _bottomText.Text = error;
    GD.Print (error);
  }

  private void StopConnecting()
  {
    _connectionTimer.Stop();
    DisconnectSignals();
    _peer?.Close();
    _joinGameButton.Disabled = false;
  }

  private void ConnectSignals()
  {
    // @formatter:off
    if (!Multiplayer.IsConnected (MultiplayerApi.SignalName.ConnectedToServer, _onConnectedToServerCallable)) Multiplayer.Connect (MultiplayerApi.SignalName.ConnectedToServer, _onConnectedToServerCallable);
    if (!Multiplayer.IsConnected (MultiplayerApi.SignalName.ConnectionFailed, _onConnectionFailedCallable)) Multiplayer.Connect (MultiplayerApi.SignalName.ConnectionFailed, _onConnectionFailedCallable);
    if (!Multiplayer.IsConnected (MultiplayerApi.SignalName.ServerDisconnected, _onServerDisconnectedCallable)) Multiplayer.Connect (MultiplayerApi.SignalName.ServerDisconnected, _onServerDisconnectedCallable);
    // @formatter:on
  }

  private void DisconnectSignals()
  {
    // @formatter:off
    if (Multiplayer.IsConnected (MultiplayerApi.SignalName.ConnectedToServer, _onConnectedToServerCallable)) Multiplayer.Disconnect (MultiplayerApi.SignalName.ConnectedToServer, _onConnectedToServerCallable);
    if (Multiplayer.IsConnected (MultiplayerApi.SignalName.ConnectionFailed, _onConnectionFailedCallable)) Multiplayer.Disconnect (MultiplayerApi.SignalName.ConnectionFailed, _onConnectionFailedCallable);
    if (Multiplayer.IsConnected (MultiplayerApi.SignalName.ServerDisconnected, _onServerDisconnectedCallable)) Multiplayer.Disconnect (MultiplayerApi.SignalName.ServerDisconnected, _onServerDisconnectedCallable);
    // @formatter:on
  }
}
