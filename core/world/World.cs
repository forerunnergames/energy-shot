using System.Collections.Generic;
using System.Linq;
using com.forerunnergames.energyshot.players;
using com.forerunnergames.energyshot.ui;
using com.forerunnergames.energyshot.ui.hud;
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
  // Someone caught our own thrown paper airplane (issue #102); the thrower's HUD announces it.
  [Signal] public delegate void SelfPlayerAirplaneCaughtEventHandler (string catcherName, string throwerName);
  // Our own airplane lock landing on somebody (issue #211); the HUD chirps.
  [Signal] public delegate void SelfPlayerAirplaneLockAcquiredEventHandler();
  // Bread feedback (issues #160 & #192): eaten, refused, & interrupted cues, forwarded to the HUD.
  [Signal] public delegate void SelfPlayerAteBreadEventHandler();
  [Signal] public delegate void SelfPlayerBreadDeniedEventHandler (string reason);
  [Signal] public delegate void SelfPlayerBreadInterruptedEventHandler();
  [Signal] public delegate void RemoteMessageReceivedEventHandler (string message);
  [Signal] public delegate void AdminMessageReceivedEventHandler (string message);
  [Signal] public delegate void SelfPlayerPoisonTickedEventHandler(); // Issue #261.
  [Signal] public delegate void RoundClockUpdatedEventHandler (int secondsLeft, int zapLimit, int mode, string hillHolder);
  [Signal] public delegate void RoundEndedEventHandler (string scoreboardBbcode);
  [Signal] public delegate void RoundStartedEventHandler();
  [Signal] public delegate void ChatReceivedEventHandler (int senderId, string senderName, string text);
  [Signal] public delegate void KickedFromServerEventHandler (string reason);
  [Signal] public delegate void ServerShutDownEventHandler();
  private const int DefaultServerPort = 55556;
  // Build version (issue #170): the release workflow stamps the tag (without the
  // leading "v") into project.godot at export time; dev builds keep the "-dev" value.
  public static string GameVersion => (string)ProjectSettings.GetSetting ("application/config/version", "unknown");
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
    DressTheRing(); // The spawn box is a boxing ring (issue #174).
    BackstopTheRing(); // Nobody tunnels out of it (issue #276).
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
    // Crown rules (issue #178): every peer sees these death broadcasts, so the
    // tied-incumbent handover stays consistent everywhere without new networking.
    _networkManager.PlayerRespawnedShot += (playerName, _) => OnCrownIncumbentDied (playerName);
    _networkManager.PlayerRespawnedFell += playerName => OnCrownIncumbentDied (playerName);
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

  // Boxing-ring ropes (issue #174): the spawn box's walls read as red ropes. Bounce
  // physics lives in Player.BoxingRing; this is the look, shared by every peer.
  private void DressTheRing()
  {
    var rope = new StandardMaterial3D { AlbedoColor = new Color (0.85f, 0.1f, 0.12f), Roughness = 0.4f };
    foreach (var wall in GetNode <Node3D> ("SpawnRoom").GetChildren().OfType <CsgBox3D>().Where (box => box.Name.ToString().StartsWith ("Wall"))) wall.Material = rope;
  }

  // The rope walls are only 0.3m thick, & a chained slide or a hard rope bounce moves
  // up to ~1m per physics tick - one tick can step clean past a wall & drop a player
  // 30m to the arena (issue #276, thepro). Invisible 2m backstops hug each wall's
  // outer face (corners sealed, rope height only - over-the-top exits stay open) &
  // count as ropes in Player.IsRope, so even a tunneled player just bounces.
  public const float BackstopThicknessMeters = 2.0f;

  private void BackstopTheRing()
  {
    var room = GetNode <Node3D> ("SpawnRoom");
    var reach = 6.0f + 0.15f + BackstopThicknessMeters / 2.0f; // Wall outer face + half a backstop.
    AddBackstop (room, "WallBackstopNorth", new Vector3 (0.0f, 1.0f, -reach), new Vector3 (16.0f, 1.5f, BackstopThicknessMeters));
    AddBackstop (room, "WallBackstopSouth", new Vector3 (0.0f, 1.0f, reach), new Vector3 (16.0f, 1.5f, BackstopThicknessMeters));
    AddBackstop (room, "WallBackstopEast", new Vector3 (reach, 1.0f, 0.0f), new Vector3 (BackstopThicknessMeters, 1.5f, 16.0f));
    AddBackstop (room, "WallBackstopWest", new Vector3 (-reach, 1.0f, 0.0f), new Vector3 (BackstopThicknessMeters, 1.5f, 16.0f));
  }

  private static void AddBackstop (Node3D room, string name, Vector3 position, Vector3 size)
  {
    var body = new StaticBody3D { Name = name, Position = position };
    body.AddChild (new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
    room.AddChild (body);
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

  // Player chat (issue #188): the server relays every line, deriving the sender from
  // the RPC itself - never from client-supplied text - & logs it. A length cap & a
  // light per-peer rate limit (2/s) keep it from becoming a spam channel.
  public const int MaxChatChars = 120;
  private const ulong ChatMinIntervalMs = 500;
  private readonly System.Collections.Generic.Dictionary <long, ulong> _lastChatMs = new();

  public void SendChat (string text)
  {
    if (Multiplayer.IsServer()) { RequestChat (text); return; }
    RpcId (1, MethodName.RequestChat, text);
  }

  // Flattens to one line & caps the length. Every control character & Unicode line
  // or paragraph separator (CodeRabbit on #224: U+0085, U+2028, U+2029 would split
  // the display & the server log) becomes a space. Pure & unit-tested.
  public static string SanitizeChat (string text)
  {
    var single = new string (text.Select (c => char.IsControl (c) || c is '\u0085' or '\u2028' or '\u2029' ? ' ' : c).ToArray()).Trim();
    return single.Length <= MaxChatChars ? single : single[..MaxChatChars];
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestChat (string text)
  {
    if (!IsActiveServer()) return;
    var senderId = Multiplayer.GetRemoteSenderId();
    if (senderId == 0) senderId = Multiplayer.GetUniqueId(); // The host typing locally.
    var sender = FindPlayer (senderId);
    if (sender == null) return;
    var clean = SanitizeChat (text);
    if (clean.Length == 0) return;
    var now = Time.GetTicksMsec();

    if (_lastChatMs.TryGetValue (senderId, out var last) && now - last < ChatMinIntervalMs)
    {
      ServerLog.Event (senderId, "chat drop: rate limit");
      return;
    }

    _lastChatMs[senderId] = now;
    ServerLog.Event (senderId, $"chat: [{sender.DisplayName}] {clean}");
    Rpc (MethodName.ReceiveChat, senderId, sender.DisplayName, clean);
  }

  // Only peer 1 may put words in anyone's mouth - the admin-message rule (issue #158).
  [Rpc (MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
  private void ReceiveChat (int senderId, string senderName, string text)
  {
    if (Multiplayer.GetRemoteSenderId() != 1) return;
    EmitSignal (SignalName.ChatReceived, senderId, senderName, text);
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

  // The version override exists only for the playtest's wrong-version probe (issue
  // #170); real joins always report this build's own version.
  public void StartClientSession (string playerName, int difficulty, string address, int port, string password, int colorIndex = 0, string? version = null)
  {
    var peer = new ENetMultiplayerPeer();
    var error = peer.CreateClient (address, port);
    if (error != Error.Ok) GD.PrintErr ($"Playtest join failed: {error}");
    Multiplayer.MultiplayerPeer = peer;
    // One-shot: a retried session (e.g. the playtest's wrong-password probe, issue
    // #109) must not replay stale credentials from an earlier attempt's handler.
    Multiplayer.Connect (MultiplayerApi.SignalName.ConnectedToServer, Callable.From (() => RequestSlot (playerName, difficulty, password, colorIndex, version ?? GameVersion)), (uint)ConnectFlags.OneShot);
  }

  // Playtest-only probe (issue #170): joins exactly the way a pre-#170 client does -
  // the legacy 4-argument RequestPlayerSlot RPC that carries no version.
  public void StartLegacyClientSession (string playerName, int difficulty, string address, int port, string password)
  {
    var peer = new ENetMultiplayerPeer();
    var error = peer.CreateClient (address, port);
    if (error != Error.Ok) GD.PrintErr ($"Playtest join failed: {error}");
    Multiplayer.MultiplayerPeer = peer;
    Multiplayer.Connect (MultiplayerApi.SignalName.ConnectedToServer, Callable.From (() => RpcId (1, MethodName.RequestPlayerSlot, playerName, difficulty, password, 0)), (uint)ConnectFlags.OneShot);
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

  private static int ParseArgInt (string name, int fallback) => int.TryParse (ParseArgValue (name), out var value) && value >= 0 ? value : fallback;

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
    // Rounds (issue #153): --round-minutes N & --zap-limit N, 0 = no limit on that
    // axis. Playtest runs assert exact scores & crowns across ~3 minutes, so a round
    // end mid-run would break them: the harness flag turns rounds off.
    var isPlaytest = OS.GetCmdlineUserArgs().Contains ("--playtest");
    // --mode koth picks King of the Hill (issue #44); anything else is classic zaps.
    var mode = !isPlaytest && ParseArgValue ("--mode").ToLowerInvariant() == "koth" ? GameMode.KingOfTheHill : GameMode.Zaps;
    ConfigureRound (isPlaytest ? 0 : Mathf.Clamp (ParseArgInt ("--round-minutes", Match.DefaultRoundMinutes), 0, Match.MaxRoundMinutes) * 60, isPlaytest ? 0 : Mathf.Clamp (ParseArgInt ("--zap-limit", Match.DefaultZapLimit), 0, Match.MaxZapLimit), mode);
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
    GD.Print ($"Server: Dedicated server v{GameVersion} listening on port [{port}], password {(_serverPassword.Length > 0 ? "required" : "not required")}");
  }

  // Legacy pre-#170 join entry point: old clients send this 4-argument RPC, which
  // carries no version. Kept at its original name & arity - Godot drops RPCs whose
  // argument count doesn't match, so removing it would strand old clients with a
  // silently dropped join instead of the readable update prompt their own
  // kick-reason display (#109) can already show.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestPlayerSlot (string playerName, int difficulty, string password, int colorIndex)
  {
    if (!Multiplayer.IsServer()) return;
    var senderId = Multiplayer.GetRemoteSenderId();
    ServerLog.Event (senderId, $"join denied: [{playerName}] legacy versionless join (server {GameVersion})");
    Kick (senderId, $"Update required: server is v{GameVersion}, you have an older version.");
  }

  // Versioned join handshake (issue #170); the version parameter is why this can't
  // share the legacy RPC's name - see RequestPlayerSlot above.
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void RequestPlayerSlotV2 (string playerName, int difficulty, string password, int colorIndex, string version)
  {
    if (!Multiplayer.IsServer()) return;
    var senderId = Multiplayer.GetRemoteSenderId();
    ServerLog.Event (senderId, $"join request: [{playerName}] (difficulty {difficulty}, version {version})");

    // Mixed client versions silently break RPCs between peers (issue #170): block
    // any mismatch up front with an update prompt, same UX as the password kick (#109).
    if (version != GameVersion)
    {
      ServerLog.Event (senderId, $"join denied: [{playerName}] version mismatch (server {GameVersion}, client {version})");
      Kick (senderId, $"Update required: server is v{GameVersion}, you have v{version}.");
      return;
    }

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
    ConfigureRound (Settings.RoundMinutes * 60, Settings.ZapLimit, (GameMode)Settings.GameMode); // Issues #153 & #44.
    Multiplayer.PeerConnected += OnClientConnectedToServer;
    Multiplayer.PeerDisconnected += OnClientDisconnectedFromServer;
    AddPlayer (Multiplayer.GetUniqueId(), playerName, Player.MaxHealthFor (difficulty), colorIndex);
  }

  // The UI join path always reports this build's own version (issue #170).
  private void OnJoinGameSuccess (string playerName, int difficulty, string password, int colorIndex) => RequestSlot (playerName, difficulty, password, colorIndex, GameVersion);

  private void RequestSlot (string playerName, int difficulty, string password, int colorIndex, string version)
  {
    _selfPlayerName = playerName;
    _selfDifficulty = difficulty;
    _selfColorIndex = colorIndex;
    if (!Multiplayer.IsConnected (MultiplayerApi.SignalName.ServerDisconnected, _onServerDisconnectedCallable)) Multiplayer.Connect (MultiplayerApi.SignalName.ServerDisconnected, _onServerDisconnectedCallable);
    RpcId (1, MethodName.RequestPlayerSlotV2, playerName, difficulty, password, colorIndex, version);
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
    _lastChatMs.Remove (id); // Chat rate-limit state leaves with the peer (CodeRabbit on #224).
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
    // Joining during the intermission (CodeRabbit on #226): you get the frozen
    // scoreboard too, not a private head start; ReceiveRoundStarted releases everyone.
    if (_intermission && IsActiveServer() && peerId != Multiplayer.GetUniqueId()) RpcId (peerId, MethodName.ReceiveRoundEnded, _lastBoard);
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
    // Fired only from the server-confirmed handoff now (CodeRabbit on #198), never
    // from the thrower's own prediction - a denied catch used to announce anyway.
    selfPlayer.AirplaneCaught += catcherName => EmitSignal (SignalName.SelfPlayerAirplaneCaught, catcherName, selfPlayer.DisplayName); // Issue #102.
    selfPlayer.AirplaneLockAcquired += () => EmitSignal (SignalName.SelfPlayerAirplaneLockAcquired); // Issue #211.
    selfPlayer.BreadEaten += _ => EmitSignal (SignalName.SelfPlayerAteBread); // Bread feedback (issue #160).
    selfPlayer.BreadDenied += reason => EmitSignal (SignalName.SelfPlayerBreadDenied, reason);
    selfPlayer.BreadInterrupted += () => EmitSignal (SignalName.SelfPlayerBreadInterrupted); // A hit ended the ritual (issue #192).
    selfPlayer.PoisonTicked += () => EmitSignal (SignalName.SelfPlayerPoisonTicked); // Issue #261.
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
  // replicated Scores each second - no new networking; every peer sees the same
  // replicated scores & death broadcasts, so the incumbent state (issue #178) stays
  // consistent everywhere. The same tick samples pings server-side (issue #100).
  private string _crownHolderName = string.Empty;

  private void StartCrownTicker()
  {
    var timer = new Timer { WaitTime = 1.0, Autostart = true };
    timer.Timeout += UpdateCrownHolder;
    timer.Timeout += UpdatePings;
    timer.Timeout += TickRound; // Issue #153.
    AddChild (timer);
  }

  // ------------------------------------------------------- rounds (issue #153)

  // Server-owned: the clock only runs while a round is live with at least two
  // players, so an empty server never "ends" rounds to nobody. Each tick broadcasts
  // the seconds left (late joiners catch up on the next tick - no separate sync).
  private int _roundSeconds;
  private int _zapLimit;
  private float _roundElapsed;
  private bool _intermission;
  private string _lastBoard = string.Empty; // Replayed to anyone who joins mid-intermission (CodeRabbit on #226).

  private GameMode _mode = GameMode.Zaps;
  private Hill? _hill;

  private void ConfigureRound (int roundSeconds, int zapLimit, GameMode mode)
  {
    _roundSeconds = roundSeconds;
    _zapLimit = zapLimit;
    _mode = mode;
    _roundElapsed = 0.0f;
    EnsureHill();
    ServerLog.Event ($"rounds: {mode}, {(roundSeconds > 0 ? $"{roundSeconds / 60} min" : "no time limit")}, {(zapLimit > 0 ? $"first to {zapLimit}" : "no point limit")}");
  }

  // The hill ring exists only in King of the Hill (issue #44); clients learn the mode
  // from the round clock broadcast & build theirs on the first tick.
  private void EnsureHill()
  {
    if (_mode != GameMode.KingOfTheHill || _hill != null) return;
    _hill = Hill.Create();
    AddChild (_hill);
  }

  // Sole occupant of the hill earns a point per tick (issue #44); a contest pays nobody.
  private string AwardHillPoint (List <Player> players)
  {
    var inside = players.Where (player => !player.Fallen && Hill.Contains (player.GlobalPosition)).ToList();
    if (inside.Count != 1) return inside.Count == 0 ? string.Empty : "contested";
    var king = inside[0];
    if (king.NetworkId == Multiplayer.GetUniqueId()) king.NotifyHillPoint();
    else king.RpcId (king.NetworkId, Player.MethodName.NotifyHillPoint);
    return king.DisplayName;
  }

  private bool RoundsEnabled => _roundSeconds > 0 || _zapLimit > 0;

  private void TickRound()
  {
    if (!IsActiveServer() || !RoundsEnabled || _intermission) return;
    var players = GetPlayers().ToList();
    if (players.Count < 2) return;
    _roundElapsed += 1.0f;
    var holder = _mode == GameMode.KingOfTheHill ? AwardHillPoint (players) : string.Empty;
    var secondsLeft = _roundSeconds > 0 ? Mathf.Max (0, _roundSeconds - (int)_roundElapsed) : -1;
    Rpc (MethodName.ReceiveRoundClock, secondsLeft, _zapLimit, (int)_mode, holder);
    if (!Match.IsOver (_roundElapsed, _roundSeconds, players.Max (player => player.Score), _zapLimit)) return;
    EndRound (players);
  }

  private async void EndRound (List <Player> players)
  {
    _intermission = true;
    var ordered = players.OrderByDescending (player => player.Score).ThenBy (player => player.DisplayName).ToList();
    var stats = ordered.Select (player => new RoundStats (player.DisplayName, PlayerColors.TextHex (player.ColorIndex), player.Score, player.ZapOuts, player.Assists, player.Falls)).ToList();
    var board = Match.BuildScoreboard (stats, Match.AwardTitles (stats), MessageGenerator.RoundTitle, _mode);
    ServerLog.Event ($"round over: {string.Join (", ", stats.Select (s => $"{s.Name} {s.Zaps}/{s.ZapOuts}/{s.Assists}/{s.Falls}"))}");
    _lastBoard = board;
    Rpc (MethodName.ReceiveRoundEnded, board);
    await ToSignal (GetTree().CreateTimer (Match.IntermissionSeconds), SceneTreeTimer.SignalName.Timeout);
    if (!IsInsideTree() || !IsActiveServer()) return;
    StartNewRound();
  }

  private void StartNewRound()
  {
    foreach (var player in GetPlayers())
    {
      if (player.NetworkId == Multiplayer.GetUniqueId()) { player.ResetForNewRound(); continue; }
      player.RpcId (player.NetworkId, Player.MethodName.ResetForNewRound);
    }

    _roundElapsed = 0.0f;
    _intermission = false;
    ServerLog.Event ("round start");
    Rpc (MethodName.ReceiveRoundStarted);
  }

  // Only peer 1 runs the match - the admin-message rule (issue #158).
  [Rpc (MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
  private void ReceiveRoundClock (int secondsLeft, int zapLimit, int mode, string hillHolder)
  {
    if (Multiplayer.GetRemoteSenderId() != 1) return;
    _mode = (GameMode)mode;
    EnsureHill(); // Clients build the ring the first time the clock says it's that kind of round.
    EmitSignal (SignalName.RoundClockUpdated, secondsLeft, zapLimit, mode, hillHolder);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
  private void ReceiveRoundEnded (string scoreboardBbcode)
  {
    if (Multiplayer.GetRemoteSenderId() != 1) return;
    _selfPlayer?.SetInputEnabled (isEnabled: false); // Everyone stands still & reads (issue #153).
    EmitSignal (SignalName.RoundEnded, scoreboardBbcode);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
  private void ReceiveRoundStarted()
  {
    if (Multiplayer.GetRemoteSenderId() != 1) return;
    _selfPlayer?.SetInputEnabled (isEnabled: true);
    EmitSignal (SignalName.RoundStarted);
  }

  // Crown rules (issue #178): no crown anywhere until the leader's score is above
  // zero, & in ties the incumbent keeps it until strictly surpassed.
  private void UpdateCrownHolder()
  {
    var players = GetPlayers().ToList();
    var holder = PickCrownHolder (players);
    _crownHolderName = holder?.DisplayName ?? string.Empty;
    players.ForEach (player => player.SetCrowned (player == holder));
  }

  private Player? PickCrownHolder (System.Collections.Generic.List <Player> players)
  {
    var top = players.OrderByDescending (player => player.Score).ThenBy (player => player.DisplayName).FirstOrDefault();
    if (top == null || top.Score <= 0) return null; // Nobody's earned it yet (issue #178).
    var incumbent = players.FirstOrDefault (player => player.DisplayName == _crownHolderName);
    if (incumbent != null && incumbent.Score >= top.Score) return incumbent; // Tying alone never steals the crown (issue #178).
    return top;
  }

  // Crown rules (issue #178): the incumbent dying while tied at the top hands the
  // crown to the tied player immediately; dying while strictly ahead changes nothing.
  private void OnCrownIncumbentDied (string playerName)
  {
    if (playerName != _crownHolderName) return;
    var players = GetPlayers().ToList();
    var incumbent = players.FirstOrDefault (player => player.DisplayName == playerName);
    var rival = players.Where (player => player != incumbent).OrderByDescending (player => player.Score).ThenBy (player => player.DisplayName).FirstOrDefault();
    if (incumbent == null || rival == null || rival.Score < incumbent.Score) return;
    _crownHolderName = rival.DisplayName;
    UpdateCrownHolder();
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
