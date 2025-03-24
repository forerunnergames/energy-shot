using com.forerunnergames.energyshot.core.world;
using com.forerunnergames.energyshot.ui.dialogs;
using com.forerunnergames.energyshot.ui.hud.messages;
using Godot;

namespace com.forerunnergames.energyshot.ui.hud;

public partial class Hud : Control
{
  // @formatter:off
  [Signal] public delegate void MessageEventHandler (string message, string excludedPlayerName);
  [Signal] public delegate void GamePausedEventHandler();
  [Signal] public delegate void GameResumedEventHandler();
  [Signal] public delegate void GameQuitEventHandler();
  private World _world = null!;
  private ProgressBar _healthBar = null!;
  private MessageScroller _messageScroller = null!;
  private ConfirmationDialog2 _quitDialog = null!;
  private Label _scoreLabel = null!;
  private string _selfPlayerName = string.Empty;
  private void OnRemoteMessageReceived (string message) => _messageScroller.AddMessage (message);
  private void OnSelfPlayerHealthChanged (string playerName, int health) => _healthBar.Value = health;
  private bool IsSelf (string playerName) => _selfPlayerName == playerName;
  private void OnKickedFromServer (string reason) => Hide();
  private void OnServerShutDown() => Hide();
  private void PrintMessage (string message) => _messageScroller.AddMessage (message);
  // @formatter:on

  public override void _Ready()
  {
    _world = GetNode <World> ("/root/World");
    _healthBar = GetNode <ProgressBar> ("VBoxContainer/Health/ProgressBar");
    _messageScroller = GetNode <MessageScroller> ("MessageScroller");
    _scoreLabel = GetNode <Label> ("VBoxContainer/Score/Label");
    _quitDialog = GetNode <ConfirmationDialog2> ("QuitDialog");
    _quitDialog.Confirmed += () => EmitSignal (SignalName.GameQuit);
    _quitDialog.Canceled += CancelQuit;
    _quitDialog.Closed += CancelQuit;
    _world.NewGameStarted += OnNewGameStarted;
    _world.PlayerJoinedGame += OnPlayerJoinedGame;
    _world.PlayerLeftGame += OnPlayerLeftGame;
    _world.RemoteMessageReceived += OnRemoteMessageReceived;
    _world.PlayerScored += OnPlayerScored;
    _world.PlayerRespawnedShot += OnPlayerRespawnedShot;
    _world.PlayerRespawnedFell += OnPlayerRespawnedFell;
    _world.SelfPlayerHealthChanged += OnSelfPlayerHealthChanged;
    _world.KickedFromServer += OnKickedFromServer;
    _world.ServerShutDown += OnServerShutDown;
  }

  public override void _UnhandledInput (InputEvent @event)
  {
    if (!Input.IsActionJustPressed ("quit")) return;
    ToggleQuitDialog();
  }

  private void OnNewGameStarted (string selfPlayerName)
  {
    _selfPlayerName = selfPlayerName;
    _messageScroller.Reset();
    Show();
  }

  private void OnPlayerJoinedGame (string playerName)
  {
    if (IsSelf (playerName)) return;
    PrintMessage ($"{playerName} joined the game");
  }

  private void OnPlayerLeftGame (string playerName)
  {
    if (IsSelf (playerName)) return;
    PrintMessage ($"{playerName} left the game");
  }

  private void OnPlayerRespawnedShot (string playerName, string shotByPlayerName)
  {
    if (IsSelf (shotByPlayerName))
    {
      PrintMessage (MessageGenerator.OnShotPlayer (isSelf: true, shotByPlayerName, playerName));
      return;
    }

    if (!IsSelf (playerName)) return;
    NotifyMessage (MessageGenerator.OnPlayerRespawnedShot (isSelf: true, playerName, shotByPlayerName), MessageGenerator.OnPlayerRespawnedShot (isSelf: false, playerName, shotByPlayerName), excludedPlayerName: shotByPlayerName);
  }

  private void OnPlayerRespawnedFell (string playerName)
  {
    if (!IsSelf (playerName)) return;
    NotifyMessage (MessageGenerator.OnPlayerRespawnedFell (isSelf: true, playerName, out var messageIndex), MessageGenerator.OnPlayerRespawnedFell (isSelf: false, playerName, messageIndex));
  }

  private void OnPlayerScored (int score, string playerName, string shotPlayerName)
  {
    if (!IsSelf (playerName)) return;
    _scoreLabel.Text = $"Score: {score}";
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
    EmitSignal (SignalName.GamePaused);
  }

  private void CancelQuit()
  {
    Input.MouseMode = Input.MouseModeEnum.Captured;
    EmitSignal (SignalName.GameResumed);
  }

  private void NotifyMessage (string localMessage, string remoteMessage, string excludedPlayerName = "")
  {
    PrintMessage (localMessage);
    EmitSignal (SignalName.Message, remoteMessage, excludedPlayerName);
  }
}
