using System.Linq;
using com.forerunnergames.energyshot.players;
using com.forerunnergames.energyshot.ui;
using com.forerunnergames.energyshot.utilities;
using Godot;

namespace com.forerunnergames.energyshot.core.world;

public partial class World : Node3D
{
  [Signal] public delegate void NewGameStartedEventHandler (string selfPlayerName);
  [Signal] public delegate void PlayerJoinedGameEventHandler (string playerName);
  [Signal] public delegate void PlayerLeftGameEventHandler (string playerName);
  [Signal] public delegate void PlayerScoredEventHandler (int score, string playerName, string shotPlayerName);
  [Signal] public delegate void PlayerRespawnedShotEventHandler (string playerName, string shotByPlayerName);
  [Signal] public delegate void PlayerRespawnedFellEventHandler (string playerName);
  [Signal] public delegate void SelfPlayerHealthChangedEventHandler (string playerName, int health);
  [Signal] public delegate void RemoteMessageReceivedEventHandler (string message);
  [Signal] public delegate void KickedFromServerEventHandler (string reason);
  [Signal] public delegate void ServerShutDownEventHandler();
  private NetworkManager _networkManager = null!;
  private UI _ui = null!;
  private PackedScene _playerScene = null!;
  private Player? _selfPlayer;
  private string _selfPlayerName = string.Empty;
  private int _score;
  [Rpc] private void OnKickedFromServer (string reason) => EmitSignal (SignalName.KickedFromServer, reason);
  private int FindPlayerId (string displayName) => FindPlayer (displayName)?.NetworkId ?? 0;
  private Player? FindPlayer (string displayName) => GetChildren().OfType <Player>().FirstOrDefault (player => player.DisplayName == displayName);
  private Player? FindPlayer (int peerId) => GetChildren().OfType <Player>().FirstOrDefault (player => player.NetworkId == peerId);
  private void OnGamePaused() => _selfPlayer?.SetInputEnabled (isEnabled: false);
  private void OnGameResumed() => _selfPlayer?.SetInputEnabled (isEnabled: true);
  private void OnGameQuit() => GetTree().Quit();
  private static void OnClientConnectedToServer (long peerId) => GD.Print ($"Server: Client ID [{peerId}] connected");

  public override void _Ready()
  {
    _playerScene = ResourceLoader.Load <PackedScene> ("res://core/players/Player.tscn");
    _networkManager = GetNode <NetworkManager> ("NetworkManager");
    _ui = GetNode <UI> ("UI");
    _ui.Message += (message, excludedPlayerName) => _networkManager.NotifyMessage (message, FindPlayerId (excludedPlayerName));
    _ui.HostGameSuccess += OnHostGameSuccess;
    _ui.JoinGameSuccess += OnJoinGameSuccess;
    _ui.GamePaused += OnGamePaused;
    _ui.GameResumed += OnGameResumed;
    _ui.GameQuit += OnGameQuit;
    _networkManager.PlayerRespawnedShot += (playerName, shotByPlayerName) => EmitSignal (SignalName.PlayerRespawnedShot, playerName, shotByPlayerName);
    _networkManager.PlayerRespawnedFell += playerName => EmitSignal (SignalName.PlayerRespawnedFell, playerName);
    _networkManager.RemoteMessageReceived += message => EmitSignal (SignalName.RemoteMessageReceived, message);
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
      RpcId (senderId, MethodName.OnKickedFromServer, "You're already in the game.");
      Multiplayer.MultiplayerPeer.DisconnectPeer (senderId);
      GD.PrintErr ($"Server: Disconnected client ID [{senderId}], duplicate ID, [{duplicateId.DisplayName} (ID: {duplicateId.NetworkId})] is already in game");
      return;
    }

    if (duplicateName != null)
    {
      RpcId (senderId, MethodName.OnKickedFromServer, "Your name is already in use by another player.");
      Multiplayer.MultiplayerPeer.DisconnectPeer (senderId);
      GD.PrintErr ($"Server: Disconnected client ID [{senderId}], duplicate display name, [{duplicateName.DisplayName} (ID: {duplicateName.NetworkId})] is already in game");
      return;
    }

    AddPlayer (senderId, playerName);
  }

  private void OnHostGameSuccess (string playerName)
  {
    Multiplayer.PeerConnected += OnClientConnectedToServer;
    Multiplayer.PeerDisconnected += OnClientDisconnectedFromServer;
    AddPlayer (Multiplayer.GetUniqueId(), playerName);
  }

  private void OnJoinGameSuccess (string playerName)
  {
    _selfPlayerName = playerName;
    Multiplayer.ServerDisconnected += () => EmitSignal (SignalName.ServerShutDown);
    RpcId (1, MethodName.RequestPlayerSlot, playerName);
  }

  private void OnClientDisconnectedFromServer (long id)
  {
    RemovePlayer (id);
    GD.Print ($"Server: Player [{id}] disconnected");
  }

  private void _OnMultiplayerSpawnerSpawned (Node node)
  {
    if (node is not Player player) return;
    if (!player.IsMultiplayerAuthority()) return;
    CallDeferred (MethodName.RegisterSpawnedSelfPlayerDeferred, player);
  }

  private void RegisterSpawnedSelfPlayerDeferred (Player spawnedPlayer)
  {
    spawnedPlayer.DisplayName = _selfPlayerName;
    spawnedPlayer.RespawnedShot += (playerName, shotByPlayerName) => _networkManager.NotifyPlayerRespawnedShot (playerName, shotByPlayerName);
    spawnedPlayer.RespawnedFell += playerName => _networkManager.NotifyPlayerRespawnedFell (playerName);
    RegisterSelf (spawnedPlayer);
  }

  private void AddPlayer (int peerId, string playerName)
  {
    var player = _playerScene.Instantiate <Player>();
    player.Name = $"{peerId}";
    player.RespawnedShot += (respawnedPlayerName, shotByPlayerName) => _networkManager.NotifyPlayerRespawnedShot (respawnedPlayerName, shotByPlayerName);
    player.RespawnedFell += respawnedPlayerName => _networkManager.NotifyPlayerRespawnedFell (respawnedPlayerName);
    AddChild (player);
    player.DisplayName = playerName;
    GD.Print ($"Server: [{player.DisplayName} {player.NetworkId}] joined the game");
    EmitSignal (SignalName.PlayerJoinedGame, player.DisplayName);
    if (!player.IsMultiplayerAuthority()) return;
    RegisterSelf (player);
  }

  private void RegisterSelf (Player selfPlayer)
  {
    if (!selfPlayer.IsMultiplayerAuthority()) return;
    _selfPlayer = selfPlayer;
    selfPlayer.HealthChanged += value => EmitSignal (SignalName.SelfPlayerHealthChanged, selfPlayer.DisplayName, value);
    selfPlayer.Scored += (playerName, shotPlayerName) => EmitSignal (SignalName.PlayerScored, ++_score, playerName, shotPlayerName);
    GD.Print ($"{_selfPlayer.NetworkId}: Registered my player {_selfPlayer.DisplayName}");
    EmitSignal (SignalName.NewGameStarted, _selfPlayer.DisplayName);
  }

  private void RemovePlayer (long peerId)
  {
    var player = GetNodeOrNull <Player> ($"{peerId}");
    if (player == null) return;
    EmitSignal (SignalName.PlayerLeftGame, player.DisplayName);
    player.QueueFree();
  }
}
