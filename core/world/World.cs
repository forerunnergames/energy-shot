using System.Linq;
using com.forerunnergames.energyshot.players;
using com.forerunnergames.energyshot.ui;
using com.forerunnergames.energyshot.utilities;
using Godot;

namespace com.forerunnergames.energyshot.core.world;

public partial class World : Node3D
{
  [Signal] public delegate void NewGameStartedEventHandler (string selfPlayerName, int selfMaxHealth);
  [Signal] public delegate void PlayerJoinedGameEventHandler (string playerName);
  [Signal] public delegate void PlayerLeftGameEventHandler (string playerName);
  [Signal] public delegate void PlayerScoredEventHandler (int score, string playerName, string shotPlayerName);
  [Signal] public delegate void PlayerRespawnedShotEventHandler (string playerName, string shotByPlayerName);
  [Signal] public delegate void PlayerRespawnedFellEventHandler (string playerName);
  [Signal] public delegate void SelfPlayerHealthChangedEventHandler (string playerName, int health);
  [Signal] public delegate void SelfPlayerPunchedEventHandler();
  [Signal] public delegate void SelfPlayerSplatteredEventHandler();
  [Signal] public delegate void RemoteMessageReceivedEventHandler (string message);
  [Signal] public delegate void AdminMessageReceivedEventHandler (string message);
  [Signal] public delegate void KickedFromServerEventHandler (string reason);
  [Signal] public delegate void ServerShutDownEventHandler();
  private const int DefaultServerPort = 55556;
  // Hard engine limit on players per game (issue #73); hosts can choose fewer.
  public const int MaxPlayers = 12;
  private NetworkManager _networkManager = null!;
  private int _maxPlayers = MaxPlayers;
  // Required to join (issue #90); empty only on a dedicated server whose owner set no --password.
  private string _serverPassword = string.Empty;
  // Running build tag from --version-file (issue #158); empty = no version line on join.
  private string _serverVersion = string.Empty;
  // Peers already told the version line, so the ready handshake & its fallback can't double-send (PR #166 review).
  private readonly System.Collections.Generic.HashSet <int> _versionLinePeerIds = new();
  private UI _ui = null!;
  private Callable _onServerDisconnectedCallable;
  private PackedScene _playerScene = null!;
  private Player? _selfPlayer;
  private string _selfPlayerName = string.Empty;
  private int _selfDifficulty = 2;
  private int _selfColorIndex; // Chosen body color (issue #43), applied on our own spawned node.
  private int _score;
  private int FindPlayerId (string displayName) => FindPlayer (displayName)?.NetworkId ?? 0;
  private Player? FindPlayer (string displayName) => GetChildren().OfType <Player>().FirstOrDefault (player => player.DisplayName == displayName);
  private Player? FindPlayer (int peerId) => GetChildren().OfType <Player>().FirstOrDefault (player => player.NetworkId == peerId);
  public System.Collections.Generic.IEnumerable <Player> GetPlayers() => GetChildren().OfType <Player>();
  public Player? SelfPlayer => _selfPlayer;
  private void OnGamePaused() => _selfPlayer?.SetInputEnabled (isEnabled: false);
  private void OnGameResumed() => _selfPlayer?.SetInputEnabled (isEnabled: true);
  private void OnGameQuit() => GetTree().Quit();
  private static void OnClientConnectedToServer (long peerId) => ServerLog.Event (peerId, "connected");
  // Only a live ENet server session logs (issue #111): the engine's default
  // OfflineMultiplayerPeer also reports IsServer, which would spam clients in menus.
  // Connected check first: polling IsServer() on an inactive peer (e.g. right after
  // a kick) logs "multiplayer instance isn't currently active" errors.
  private bool IsActiveServer() => Multiplayer.MultiplayerPeer is ENetMultiplayerPeer peer && peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected && Multiplayer.IsServer();

  // Being kicked already explains the follow-up disconnect: unhooking first keeps the
  // kick reason (e.g. "Wrong password.") from being overwritten by a bogus
  // "server was shut down" moments later (issue #109).
  [Rpc]
  private void OnKickedFromServer (string reason)
  {
    OnJoinCanceled();
    EmitSignal (SignalName.KickedFromServer, reason);
  }

  public override void _Ready()
  {
    _onServerDisconnectedCallable = Callable.From (OnServerDisconnected);
    _playerScene = ResourceLoader.Load <PackedScene> ("res://core/players/Player.tscn");
    _networkManager = GetNode <NetworkManager> ("NetworkManager");
    _ui = GetNode <UI> ("UI");
    _ui.Message += (message, excludedPlayerName) => _networkManager.NotifyMessage (message, FindPlayerId (excludedPlayerName));
    _ui.HostGameSuccess += OnHostGameSuccess;
    _ui.JoinGameSuccess += OnJoinGameSuccess;
    _ui.JoinCanceled += OnJoinCanceled;
    _ui.GamePaused += OnGamePaused;
    _ui.GameResumed += OnGameResumed;
    _ui.GameQuit += OnGameQuit;
    _networkManager.PlayerRespawnedShot += (playerName, shotByPlayerName) => OnPlayerRespawned (playerName, SignalName.PlayerRespawnedShot, shotByPlayerName);
    _networkManager.PlayerRespawnedFell += playerName => OnPlayerRespawned (playerName, SignalName.PlayerRespawnedFell);
    // Deaths reach the server through these notifications on every path (issue #111).
    _networkManager.PlayerRespawnedShot += (playerName, shotByPlayerName) => { if (IsActiveServer()) ServerLog.Event (FindPlayerId (playerName), $"death: {playerName} zapped out by {shotByPlayerName}"); };
    _networkManager.PlayerRespawnedFell += playerName => { if (IsActiveServer()) ServerLog.Event (FindPlayerId (playerName), $"death: {playerName} fell off the world"); };
    _networkManager.RemoteMessageReceived += message => EmitSignal (SignalName.RemoteMessageReceived, message);
    _networkManager.AdminMessageReceived += message => EmitSignal (SignalName.AdminMessageReceived, message);
    _networkManager.PlayerJoinGame += playerName => EmitSignal (SignalName.PlayerJoinedGame, playerName);
    _networkManager.PlayerLeftGame += playerName => EmitSignal (SignalName.PlayerLeftGame, playerName);
    if (OS.GetCmdlineUserArgs().Contains ("--playtest")) CallDeferred (MethodName.StartPlaytest);
    if (IsDedicatedServer()) StartDedicatedServer();
    StartAdminMessagePolling();
    _serverVersion = ReadServerVersion();
    StartCrownTicker();
  }

  // Admin announcements (issue #158): only active with --admin-message-file; no
  // polling otherwise. Broadcasts only run on a live server session.
  private void StartAdminMessagePolling()
  {
    var path = ParseArgValue ("--admin-message-file");
    if (path.Length == 0) return;
    AddChild (new AdminMessageFileWatcher (path, BroadcastAdminMessage));
  }

  private void BroadcastAdminMessage (string message)
  {
    if (!IsActiveServer()) return;
    ServerLog.Event ($"admin announcement: {message}");
    _networkManager.NotifyAdminMessage (message);
  }

  // Release announcement (issue #158): --version-file <path> names the running
  // build; read once at startup & shown to each joining peer as "Running <tag>".
  // Absent file/flag = skipped silently (self-hosted games don't show it).
  private static string ReadServerVersion()
  {
    var path = ParseArgValue ("--version-file");
    if (path.Length == 0 || !System.IO.File.Exists (path)) return string.Empty;
    return System.IO.File.ReadAllText (path).Trim();
  }

  // The version line goes to the joining peer only - not a broadcast, so it
  // doubles as version info without spamming everyone on every join (issue #158).
  // Sent on the client's readiness confirmation, not a guessed delay (PR #166
  // review): NewGameStarted resets the message scroller, which would wipe a line
  // that arrives before the HUD is up.
  private void SendVersionTo (int peerId)
  {
    if (_serverVersion.Length == 0) return;
    if (!_versionLinePeerIds.Add (peerId)) return; // Already sent (ready + fallback paths).
    _networkManager.SendAdminMessageTo (peerId, $"Running {_serverVersion}");
  }

  // A joined client confirmed its HUD is up: safe to send its version line now (PR #166 review).
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void ConfirmClientReady()
  {
    if (!Multiplayer.IsServer()) return;
    var senderId = Multiplayer.GetRemoteSenderId();
    if (FindPlayer (senderId) == null) return; // Only joined players get the line.
    SendVersionTo (senderId);
  }

  // Tell the server our HUD finished NewGameStarted; the host is the server
  // itself & needs no confirmation (PR #166 review).
  private void NotifyServerClientReady()
  {
    if (Multiplayer.GetUniqueId() == 1) return;
    RpcId (1, MethodName.ConfirmClientReady);
  }

  // Fallback (PR #166 review): a client whose readiness confirmation is lost
  // still gets its version line, just on the old fixed-delay timing.
  private async void SendVersionToAfterFallbackDelay (int peerId)
  {
    if (_serverVersion.Length == 0) return;
    await ToSignal (GetTree().CreateTimer (10.0), SceneTreeTimer.SignalName.Timeout);
    if (!Multiplayer.GetPeers().Contains (peerId)) return; // Already left.
    SendVersionTo (peerId);
  }

  private void StartPlaytest() => AddChild (new playtest.PlaytestDriver());

  // Any respawn ends that player's streak (issue #88): clear the local streak display
  // on this reliable broadcast, so the glow & pulsing leaderboard entry can't outlive
  // a death even if a peer missed the one-off ZapStreakCount reset delta.
  private void OnPlayerRespawned (string playerName, StringName signal, string? shotByPlayerName = null)
  {
    FindPlayer (playerName)?.ClearStreakDisplayLocally();
    if (shotByPlayerName == null) EmitSignal (signal, playerName);
    else EmitSignal (signal, playerName, shotByPlayerName);
  }

  // Session entry points for the automated playtest harness: same code paths the
  // host/join dialogs use, minus the UI & UPnP.
  public void StartHostSession (string playerName, int difficulty, int port, string password, int colorIndex = 0)
  {
    var peer = new ENetMultiplayerPeer();
    var error = peer.CreateServer (port);
    if (error != Error.Ok) GD.PrintErr ($"Playtest host failed: {error}");
    Multiplayer.MultiplayerPeer = peer;
    OnHostGameSuccess (playerName, difficulty, MaxPlayers, password, colorIndex);
  }

  public void StartClientSession (string playerName, int difficulty, string address, int port, string password, int colorIndex = 0)
  {
    var peer = new ENetMultiplayerPeer();
    var error = peer.CreateClient (address, port);
    if (error != Error.Ok) GD.PrintErr ($"Playtest join failed: {error}");
    Multiplayer.MultiplayerPeer = peer;
    // One-shot: a retried session (e.g. the playtest's wrong-password probe, issue
    // #109) must not replay stale credentials from an earlier attempt's handler.
    Multiplayer.Connect (MultiplayerApi.SignalName.ConnectedToServer, Callable.From (() => OnJoinGameSuccess (playerName, difficulty, password, colorIndex)), (uint)ConnectFlags.OneShot);
  }

  // Dedicated-server exports carry the feature tag, so the server binary needs no flag;
  // --server also works for running from a normal build (e.g. local testing).
  private static bool IsDedicatedServer() => OS.HasFeature ("dedicated_server") || OS.GetCmdlineUserArgs().Contains ("--server");

  private static int ParseServerPort()
  {
    var args = OS.GetCmdlineUserArgs();
    var index = System.Array.IndexOf (args, "--port");
    if (index == -1 || index + 1 >= args.Length) return DefaultServerPort;
    if (!int.TryParse (args[index + 1], out var port) || port is <= 0 or > 65535) return DefaultServerPort;
    return port;
  }

  // Dedicated-server password (issue #90): --password <p>; empty means no password
  // required, so the official server keeps working until its owner sets one.
  private static string ParseServerPassword() => ParseArgValue ("--password");

  private static string ParseArgValue (string name)
  {
    var args = OS.GetCmdlineUserArgs();
    var index = System.Array.IndexOf (args, name);
    if (index == -1 || index + 1 >= args.Length) return string.Empty;
    return args[index + 1];
  }

  // Headless dedicated server (issue #27): no UI, no local player; clients join via the existing RequestPlayerSlot RPC flow.
  private void StartDedicatedServer()
  {
    _ui.Hide();
    var port = ParseServerPort();
    var peer = new ENetMultiplayerPeer();
    var error = peer.CreateServer (port);

    if (error != Error.Ok)
    {
      GD.PrintErr ($"Server: Failed to create dedicated server on port [{port}], error [{error}]");
      GetTree().Quit (1);
      return;
    }

    Multiplayer.MultiplayerPeer = peer;
    Multiplayer.PeerConnected += OnClientConnectedToServer;
    Multiplayer.PeerDisconnected += OnClientDisconnectedFromServer;
    _serverPassword = ParseServerPassword();
    GD.Print ($"Server: Dedicated server listening on port [{port}], password {(_serverPassword.Length > 0 ? "required" : "not required")}");
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestPlayerSlot (string playerName, int difficulty, string password, int colorIndex)
  {
    if (!Multiplayer.IsServer()) return;
    var senderId = Multiplayer.GetRemoteSenderId();
    ServerLog.Event (senderId, $"join request: [{playerName}] (difficulty {difficulty})");

    // Server-enforced game password (issue #90); empty server password = open server.
    if (_serverPassword.Length > 0 && password != _serverPassword)
    {
      ServerLog.Event (senderId, $"join denied: [{playerName}] wrong password");
      Kick (senderId, "Wrong password.");
      return;
    }

    ServerLog.Event (senderId, $"password {(_serverPassword.Length > 0 ? "accepted" : "not required")} for [{playerName}]");
    var duplicateId = FindPlayer (senderId);
    var duplicateName = FindPlayer (playerName);

    if (duplicateId != null)
    {
      ServerLog.Event (senderId, $"join denied: duplicate ID, [{duplicateId.DisplayName} (ID: {duplicateId.NetworkId})] is already in game");
      Kick (senderId, "You're already in the game.");
      return;
    }

    if (duplicateName != null)
    {
      ServerLog.Event (senderId, $"join denied: duplicate display name, [{duplicateName.DisplayName} (ID: {duplicateName.NetworkId})] is already in game");
      Kick (senderId, "Your name is already in use by another player.");
      return;
    }

    // Host-chosen player cap (issue #73).
    if (GetPlayers().Count() >= _maxPlayers)
    {
      ServerLog.Event (senderId, $"join denied: game is full ({_maxPlayers} players)");
      Kick (senderId, "Game is full.");
      return;
    }

    AddPlayer (senderId, playerName, Player.MaxHealthFor (difficulty), colorIndex);
    SendVersionToAfterFallbackDelay (senderId);
  }

  // Delay the disconnect so the kick-reason RPC isn't dropped by an immediate peer disconnect (see issue #23).
  private async void Kick (int peerId, string reason)
  {
    ServerLog.Event (peerId, $"kicked: {reason}");
    RpcId (peerId, MethodName.OnKickedFromServer, reason);
    await ToSignal (GetTree().CreateTimer (0.5), SceneTreeTimer.SignalName.Timeout);
    if (!Multiplayer.GetPeers().Contains (peerId)) return;
    Multiplayer.MultiplayerPeer.DisconnectPeer (peerId);
  }

  private void OnHostGameSuccess (string playerName, int difficulty, int maxPlayers, string password, int colorIndex)
  {
    _maxPlayers = Mathf.Clamp (maxPlayers, 2, MaxPlayers);
    _serverPassword = password;
    Multiplayer.PeerConnected += OnClientConnectedToServer;
    Multiplayer.PeerDisconnected += OnClientDisconnectedFromServer;
    AddPlayer (Multiplayer.GetUniqueId(), playerName, Player.MaxHealthFor (difficulty), colorIndex);
  }

  private void OnJoinGameSuccess (string playerName, int difficulty, string password, int colorIndex)
  {
    _selfPlayerName = playerName;
    _selfDifficulty = difficulty;
    _selfColorIndex = colorIndex;
    if (!Multiplayer.IsConnected (MultiplayerApi.SignalName.ServerDisconnected, _onServerDisconnectedCallable)) Multiplayer.Connect (MultiplayerApi.SignalName.ServerDisconnected, _onServerDisconnectedCallable);
    RpcId (1, MethodName.RequestPlayerSlot, playerName, difficulty, password, colorIndex);
  }

  private void OnServerDisconnected() => EmitSignal (SignalName.ServerShutDown);

  // A canceled join (issue #91) drops the connection on purpose; unhooking first
  // keeps the resulting disconnect from reading as a server shutdown.
  private void OnJoinCanceled()
  {
    if (!Multiplayer.IsConnected (MultiplayerApi.SignalName.ServerDisconnected, _onServerDisconnectedCallable)) return;
    Multiplayer.Disconnect (MultiplayerApi.SignalName.ServerDisconnected, _onServerDisconnectedCallable);
  }

  private void OnClientDisconnectedFromServer (long id)
  {
    RemovePlayer (id);
    _versionLinePeerIds.Remove ((int)id); // A rejoin counts as a fresh join (PR #166 review).
    ServerLog.Event (id, "disconnected");
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
    // This node is the replication authority, so the difficulty health pool must be
    // set here (client-side) - values set on the server copy get overwritten by sync.
    spawnedPlayer.MaxHealth = Player.MaxHealthFor (_selfDifficulty);
    spawnedPlayer.ColorIndex = _selfColorIndex; // Chosen body color (issue #43), same authority rule.
    spawnedPlayer.Health = spawnedPlayer.MaxHealth;
    spawnedPlayer.RespawnedShot += (playerName, shotByPlayerName) => _networkManager.NotifyPlayerRespawnedShot (playerName, shotByPlayerName);
    spawnedPlayer.RespawnedFell += playerName => _networkManager.NotifyPlayerRespawnedFell (playerName);
    RegisterSelf (spawnedPlayer);
  }

  private void AddPlayer (int peerId, string playerName, int maxHealth, int colorIndex)
  {
    var player = _playerScene.Instantiate <Player>();
    player.Name = $"{peerId}";
    player.MaxHealth = maxHealth;
    player.ColorIndex = colorIndex; // Chosen body color (issue #43), carried into spawn state for every peer.
    player.RespawnedShot += (respawnedPlayerName, shotByPlayerName) => _networkManager.NotifyPlayerRespawnedShot (respawnedPlayerName, shotByPlayerName);
    player.RespawnedFell += respawnedPlayerName => _networkManager.NotifyPlayerRespawnedFell (respawnedPlayerName);
    AddChild (player);
    player.DisplayName = playerName;
    ServerLog.Event (player.NetworkId, $"spawn: [{player.DisplayName}] joined the game (max health {maxHealth})");
    _networkManager.NotifyPlayerJoinGame (player.DisplayName);
    if (!player.IsMultiplayerAuthority()) return;
    RegisterSelf (player);
  }

  private void RegisterSelf (Player selfPlayer)
  {
    if (!selfPlayer.IsMultiplayerAuthority()) return;
    _selfPlayer = selfPlayer;
    selfPlayer.HealthChanged += value => EmitSignal (SignalName.SelfPlayerHealthChanged, selfPlayer.DisplayName, value);
    selfPlayer.Punched += () => EmitSignal (SignalName.SelfPlayerPunched);
    selfPlayer.Splattered += () => EmitSignal (SignalName.SelfPlayerSplattered);
    selfPlayer.Scored += (playerName, shotPlayerName) => EmitSignal (SignalName.PlayerScored, ++_score, playerName, shotPlayerName);
    GD.Print ($"{_selfPlayer.NetworkId}: Registered my player {_selfPlayer.DisplayName}");
    EmitSignal (SignalName.NewGameStarted, _selfPlayer.DisplayName, _selfPlayer.MaxHealth);
    // NewGameStarted handlers (incl. the HUD's scroller reset) ran synchronously
    // above, so the version line can't be wiped after this (PR #166 review).
    NotifyServerClientReady();
  }

  private void RemovePlayer (long peerId)
  {
    var player = GetNodeOrNull <Player> ($"{peerId}");
    if (player == null) return;
    _networkManager.NotifyPlayerLeftGame (player.DisplayName);
    player.QueueFree();
  }

  // Golden crown (issue #89): every peer computes the score leader locally from the
  // replicated Scores each second - no new networking; ties break by the
  // leaderboard's sort order (highest score, then name). The same tick samples pings
  // server-side (issue #100).
  private void StartCrownTicker()
  {
    var timer = new Timer { WaitTime = 1.0, Autostart = true };
    timer.Timeout += UpdateCrownHolder;
    timer.Timeout += UpdatePings;
    AddChild (timer);
  }

  private void UpdateCrownHolder()
  {
    var players = GetPlayers().ToList();
    var leader = players.OrderByDescending (player => player.Score).ThenBy (player => player.DisplayName).FirstOrDefault();
    players.ForEach (player => player.SetCrowned (player == leader));
  }

  // Per-player ping (issue #100): the server samples each peer's ENet round-trip time
  // once a second & tells the owning client, which writes the replicated PingMs that
  // every peer renders on the leaderboard.
  private void UpdatePings()
  {
    if (Multiplayer.MultiplayerPeer is not ENetMultiplayerPeer peer || !IsActiveServer()) return;
    foreach (var player in GetPlayers()) UpdatePing (peer, player);
  }

  private void UpdatePing (ENetMultiplayerPeer peer, Player player)
  {
    if (player.NetworkId == Multiplayer.GetUniqueId())
    {
      player.PingMs = 0; // The host is the server: its own ping is always 0.
      return;
    }

    if (!Multiplayer.GetPeers().Contains (player.NetworkId)) return; // Mid-disconnect.
    var pingMs = (int)peer.GetPeer (player.NetworkId).GetStatistic (ENetPacketPeer.PeerStatistic.RoundTripTime);
    player.RpcId (player.NetworkId, Player.MethodName.ReceivePingMeasurement, pingMs);
  }
}
