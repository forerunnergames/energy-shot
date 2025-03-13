using System.Linq;
using Godot;

namespace com.forerunnergames.energyshot;

public partial class World : Node3D
{
  // @formatter:off
  [Export] public int ServerPort = 55556;
  private LineEdit _serverAddress = null!;
  private ENetMultiplayerPeer _peer = new();
  private PanelContainer _mainMenu = null!;
  private Control _hud = null!;
  private ConfirmationDialog2 _quitDialog = null!;
  private PackedScene _playerScene = null!;
  private ProgressBar _healthBar = null!;
  private Label _scoreLabel = null!;
  private Label _bottomMainMenuText = null!;
  private HostGameDialog _hostGameDialog = null!;
  private JoinGameDialog _joinGameDialog = null!;
  private Button _hostButton = null!;
  private Button _joinButton = null!;
  private Button _quitButton = null!;
  private MessageScroller _messageScroller = null!;
  private Player? _selfPlayer;
  private string _selfPlayerName = string.Empty;
  private int _score;
  [Rpc] private void KickedFromServer (string reason) => GoToMainMenu (bottomText: $"You were kicked from the server, reason: {reason}");
  private void QuitGame() => GetTree().Quit();
  private void OnJoinButtonPressed() => _joinGameDialog.Show (_peer, ServerPort);
  private void OnHostButtonPressed() => _hostGameDialog.Show (_peer, ServerPort);
  private void OnQuitButtonPressed() => QuitGame();
  private Player? FindPlayer (string displayName) => GetChildren().OfType <Player>().FirstOrDefault (player => player.DisplayName == displayName);
  private Player? FindPlayer (int peerId) => GetChildren().OfType <Player>().FirstOrDefault (player => player.NetworkId == peerId);
  private static void OnClientConnectedToServer (long peerId) => GD.Print ($"Server: Client ID [{peerId}] connected");
  // @formatter:on

  public override void _Ready()
  {
    _playerScene = ResourceLoader.Load <PackedScene> ("res://Player.tscn");
    _mainMenu = GetNode <PanelContainer> ("UI/MainMenu");
    _hud = GetNode <Control> ("UI/HUD");
    _quitDialog = GetNode <ConfirmationDialog2> ("UI/QuitDialog");
    _healthBar = GetNode <ProgressBar> ("UI/HUD/VBoxContainer/Health/ProgressBar");
    _scoreLabel = GetNode <Label> ("UI/HUD/VBoxContainer/Score/Label");
    _bottomMainMenuText = GetNode <Label> ("UI/MainMenu/MarginContainer/VBoxContainer/BottomText");
    _hostGameDialog = GetNode <HostGameDialog> ("UI/HostGameDialog");
    _joinGameDialog = GetNode <JoinGameDialog> ("UI/JoinGameDialog");
    _hostButton = GetNode <Button> ("UI/MainMenu/MarginContainer/VBoxContainer/MarginContainer/VBoxContainer/HostButton");
    _joinButton = GetNode <Button> ("UI/MainMenu/MarginContainer/VBoxContainer/MarginContainer/VBoxContainer/JoinButton");
    _quitButton = GetNode <Button> ("UI/MainMenu/MarginContainer/VBoxContainer/MarginContainer/VBoxContainer/Quit");
    _messageScroller = GetNode <MessageScroller> ("UI/HUD/MessageScroller");
    _bottomMainMenuText.Text = string.Empty;
    _quitDialog.Confirmed += QuitGame;
    _quitDialog.Canceled += CancelQuit;
    _quitDialog.Closed += CancelQuit;
    _hostButton.Pressed += OnHostButtonPressed;
    _joinButton.Pressed += OnJoinButtonPressed;
    _quitButton.Pressed += OnQuitButtonPressed;
    _hostGameDialog.HostGameSuccess += OnHostGameSuccess;
    _joinGameDialog.JoinGameSuccess += OnJoinGameSuccess;
  }

  public override void _UnhandledInput (InputEvent @event)
  {
    if (!Input.IsActionJustPressed ("quit")) return;
    ToggleQuitDialog();
  }

  private void ToggleQuitDialog()
  {
    if (_quitDialog.Visible)
    {
      _quitDialog.Hide();
      CancelQuit();
      return;
    }

    _quitDialog.Show();
    Input.MouseMode = Input.MouseModeEnum.Visible;
    _selfPlayer?.SetInputEnabled (isEnabled: false);
  }

  private void CancelQuit()
  {
    Input.MouseMode = Input.MouseModeEnum.Captured;
    _selfPlayer?.SetInputEnabled (isEnabled: true);
  }

  private void OnHostGameSuccess (string playerName)
  {
    GoToGameScreen();
    Multiplayer.PeerConnected += OnClientConnectedToServer;
    Multiplayer.PeerDisconnected += OnClientDisconnectedFromServer;
    AddPlayer (Multiplayer.GetUniqueId(), playerName);
  }

  private void OnJoinGameSuccess (string playerName)
  {
    _selfPlayerName = playerName;
    GoToGameScreen();
    Multiplayer.ServerDisconnected += OnServerDisconnected;
    RpcId (1, MethodName.RequestPlayerSlot, playerName);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestPlayerSlot (string playerName)
  {
    if (!Multiplayer.IsServer()) return;
    var senderId = Multiplayer.GetRemoteSenderId();
    GD.Print ($"Server: {senderId} {playerName} is requesting to join the game");
    var duplicateId = FindPlayer (senderId);
    var duplicateName = FindPlayer (playerName);

    if (duplicateId != null)
    {
      RpcId (senderId, MethodName.KickedFromServer, "You're already in the game.");
      Multiplayer.MultiplayerPeer.DisconnectPeer (senderId);
      GD.PrintErr ($"Server: Disconnected client ID [{senderId}], duplicate ID, [{duplicateId.DisplayName} (ID: {duplicateId.NetworkId})] is already in game");
      return;
    }

    if (duplicateName != null)
    {
      RpcId (senderId, MethodName.KickedFromServer, "Your name is already in use by another player.");
      Multiplayer.MultiplayerPeer.DisconnectPeer (senderId);
      GD.PrintErr ($"Server: Disconnected client ID [{senderId}], duplicate display name, [{duplicateName.DisplayName} (ID: {duplicateName.NetworkId})] is already in game");
      return;
    }

    AddPlayer (senderId, playerName);
  }

  private void OnServerDisconnected()
  {
    GD.Print ("Server disconnected");
    GoToMainMenu (bottomText: "The host shut down the server.");
  }

  private void OnClientDisconnectedFromServer (long id)
  {
    RemovePlayer (id);
    GD.Print ($"Server: Player [{id}] disconnected");
  }

  private void _OnMultiplayerSpawnerSpawned (Node node)
  {
    if (node is not Player player || !player.IsMultiplayerAuthority()) return;
    player.DisplayName = _selfPlayerName;
    RegisterSelf (player);
  }

  private void AddPlayer (int peerId, string playerName)
  {
    var player = _playerScene.Instantiate <Player>();
    player.Name = $"{peerId}";
    player.DisplayName = playerName;
    AddChild (player);
    GD.Print ($"Server: [{player.DisplayName} {player.NetworkId}] joined the game");
    _messageScroller.AddMessage ($"{player.DisplayName} joined the game");
    if (!player.IsMultiplayerAuthority()) return;
    RegisterSelf (player);
  }

  private void RegisterSelf (Player player)
  {
    if (!player.IsMultiplayerAuthority()) return;
    _selfPlayer = player;
    _selfPlayer.HealthChanged += value => _healthBar.Value = value;
    _selfPlayer.Scored += OnSelfPlayerScored;
    _selfPlayer.Respawned += OnSelfPlayerRespawned;
    GD.Print ($"{player.NetworkId}: Registered my player {player.DisplayName}");
  }

  private void OnSelfPlayerScored (string shooterDisplayName, string shotDisplayName)
  {
    _scoreLabel.Text = $"Score: {++_score}";
    GD.Print ($"{_selfPlayer?.DisplayName}: Score: {_score}");
    _messageScroller.AddMessage ($"You shot {shotDisplayName}");
  }

  private void OnSelfPlayerRespawned (string shotDisplayName, string shooterDisplayName)
  {
    GD.Print ($"{_selfPlayer?.DisplayName} respawned");
    _messageScroller.AddMessage ($"You were shot by {shooterDisplayName}");
  }

  private void GoToMainMenu (string bottomText = "")
  {
    _bottomMainMenuText.Text = bottomText;
    _hud.Hide();
    _mainMenu.Show();
    Input.MouseMode = Input.MouseModeEnum.Visible;
  }

  private void GoToGameScreen()
  {
    _bottomMainMenuText.Text = string.Empty;
    _messageScroller.Reset();
    _mainMenu.Hide();
    _hud.Show();
  }

  private void RemovePlayer (long peerId)
  {
    var player = GetNodeOrNull <Player> ($"{peerId}");
    if (player == null) return;
    _messageScroller.AddMessage ($"{player.DisplayName} left the game");
    player.QueueFree();
  }
}
