using Godot;

public partial class World : Node3D
{
  private const int ServerPort = 55556;
  private LineEdit _serverAddress = null!;
  private ENetMultiplayerPeer _peer = new();
  private PanelContainer _mainMenu = null!;
  private Control _hud = null!;
  private PackedScene _playerScene = null!;
  private ProgressBar _healthBar = null!;
  private HostGameDialog _hostGameDialog = null!;
  private JoinGameDialog _joinGameDialog = null!;
  private Button _hostButton = null!;
  private Button _joinButton = null!;
  private Button _quitButton = null!;
  private void OnJoinButtonPressed() => _joinGameDialog.Show (_peer, ServerPort);
  private void OnHostButtonPressed() => _hostGameDialog.Show (_peer, ServerPort);
  private void OnQuitButtonPressed() => GetTree().Quit();
  private void RemovePlayer (long peerId) => GetNodeOrNull <Player> ($"{peerId}")?.QueueFree();

  public override void _Ready()
  {
    _playerScene = ResourceLoader.Load <PackedScene> ("res://Player.tscn");
    _mainMenu = GetNode <PanelContainer> ("UI/MainMenu");
    _hud = GetNode <Control> ("UI/HUD");
    _healthBar = GetNode <ProgressBar> ("UI/HUD/HealthBar");
    _hostGameDialog = GetNode <HostGameDialog> ("UI/HostGameDialog");
    _joinGameDialog = GetNode <JoinGameDialog> ("UI/JoinGameDialog");
    _hostButton = GetNode <Button> ("UI/MainMenu/MarginContainer/VBoxContainer/MarginContainer/VBoxContainer/HostButton");
    _joinButton = GetNode <Button> ("UI/MainMenu/MarginContainer/VBoxContainer/MarginContainer/VBoxContainer/JoinButton");
    _quitButton = GetNode <Button> ("UI/MainMenu/MarginContainer/VBoxContainer/MarginContainer/VBoxContainer/Quit");
    _hostButton.Pressed += OnHostButtonPressed;
    _joinButton.Pressed += OnJoinButtonPressed;
    _quitButton.Pressed += OnQuitButtonPressed;
    _hostGameDialog.HostGameSuccess += OnHostGameSuccess;
    _joinGameDialog.JoinGameSuccess += OnJoinGameSuccess;
  }

  public override void _UnhandledInput (InputEvent @event)
  {
    if (!Input.IsActionJustPressed ("quit")) return;
    GetTree().Quit();
  }

  private void OnHostGameSuccess()
  {
    _mainMenu.Hide();
    _hud.Show();
    Multiplayer.MultiplayerPeer = _peer;
    Multiplayer.PeerConnected += OnPeerConnected;
    Multiplayer.PeerDisconnected += OnPeerDisconnected;
    AddPlayer (Multiplayer.GetUniqueId());
  }

  private void OnJoinGameSuccess()
  {
    _mainMenu.Hide();
    _hud.Show();
    Multiplayer.MultiplayerPeer = _peer;
    Multiplayer.ServerDisconnected += OnServerDisconnected;
  }

  private void OnServerDisconnected()
  {
    GD.Print ("Server disconnected");
    _hud.Hide();
    _mainMenu.Show();
  }

  private void OnPeerConnected (long id)
  {
    AddPlayer (id);
    GD.Print ($"Player [{id}] connected");
  }

  private void OnPeerDisconnected (long id)
  {
    RemovePlayer (id);
    GD.Print ($"Player [{id}] disconnected");
  }

  private void _OnMultiplayerSpawnerSpawned (Node node)
  {
    if (node is not Player player || !player.IsMultiplayerAuthority()) return;
    player.HealthChanged += value => _healthBar.Value = value;
  }

  private void AddPlayer (long peerId)
  {
    var player = _playerScene.Instantiate <Player>();
    player.Name = $"{peerId}";
    AddChild (player);
    if (!player.IsMultiplayerAuthority()) return;
    player.HealthChanged += value => _healthBar.Value = value;
  }
}
