using energyshot;
using Godot;

public partial class World : Node3D
{
  private const int ServerPort = 9999;
  private LineEdit _serverAddress = null!;
  private ENetMultiplayerPeer _peer = new();
  private PanelContainer _mainMenu = null!;
  private Control _hud = null!;
  private PackedScene _playerScene = null!;
  private ProgressBar _healthBar = null!;
  private HostGameStartDialog _hostStartGameDialog = null!;
  private Button _hostButton = null!;
  private Button _joinButton = null!;
  private Button _quitButton = null!;
  private void RemovePlayer (long peerId) => GetNodeOrNull <Player> ($"{peerId}")?.QueueFree();
  private void OnServerAddressTextChanged (string newText) => _joinButton.Disabled = !Tools.IsValidServerAddress (_serverAddress.Text);
  private void OnHostButtonPressed() => _hostStartGameDialog.Show (ServerPort);
  private void OnQuitButtonPressed() => GetTree().Quit();

  public override void _Ready()
  {
    _playerScene = ResourceLoader.Load <PackedScene> ("res://Player.tscn");
    _mainMenu = GetNode <PanelContainer> ("UI/MainMenu");
    _hud = GetNode <Control> ("UI/HUD");
    _healthBar = GetNode <ProgressBar> ("UI/HUD/HealthBar");
    _hostStartGameDialog = GetNode <HostGameStartDialog> ("UI/HostGameStartDialog");
    _serverAddress = GetNode <LineEdit> ("UI/MainMenu/MarginContainer/VBoxContainer/MarginContainer/VBoxContainer/ServerAddress");
    _hostButton = GetNode <Button> ("UI/MainMenu/MarginContainer/VBoxContainer/MarginContainer/VBoxContainer/HostButton");
    _joinButton = GetNode <Button> ("UI/MainMenu/MarginContainer/VBoxContainer/MarginContainer/VBoxContainer/JoinButton");
    _quitButton = GetNode <Button> ("UI/MainMenu/MarginContainer/VBoxContainer/MarginContainer/VBoxContainer/Quit");
    _joinButton.Disabled = true;
    _hostButton.Pressed += OnHostButtonPressed;
    _joinButton.Pressed += OnJoinButtonPressed;
    _quitButton.Pressed += OnQuitButtonPressed;
    _serverAddress.TextChanged += OnServerAddressTextChanged;
    _hostStartGameDialog.StartGamePressed += OnHostStartGame;
  }

  public override void _UnhandledInput (InputEvent @event)
  {
    if (!Input.IsActionJustPressed ("quit")) return;
    GetTree().Quit();
  }

  private void OnHostStartGame()
  {
    _mainMenu.Hide();
    _hud.Show();
    var error = _peer.CreateServer (ServerPort);

    if (error != Error.Ok)
    {
      GD.Print ($"Error hosting game: [{error}]");
      return;
    }

    Multiplayer.MultiplayerPeer = _peer;
    Multiplayer.PeerConnected += OnPeerConnected;
    Multiplayer.PeerDisconnected += OnPeerDisconnected;
    AddPlayer (Multiplayer.GetUniqueId());
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

  private void OnJoinButtonPressed()
  {
    _mainMenu.Hide();
    _hud.Show();
    var error = _peer.CreateClient (_serverAddress.Text, ServerPort);

    if (error != Error.Ok)
    {
      GD.Print ($"Error joining game: [{error}]");
      return;
    }

    Multiplayer.MultiplayerPeer = _peer;
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
