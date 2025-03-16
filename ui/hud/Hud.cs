using com.forerunnergames.energyshot.core.world;
using com.forerunnergames.energyshot.ui.dialogs;
using com.forerunnergames.energyshot.ui.hud.messages;
using Godot;

namespace com.forerunnergames.energyshot.ui.hud;

public partial class Hud : Control
{
  // @formatter:off
  [Signal] public delegate void GamePausedEventHandler();
  [Signal] public delegate void GameResumedEventHandler();
  [Signal] public delegate void GameQuitEventHandler();
  private World _world = null!;
  private ProgressBar _healthBar = null!;
  private MessageScroller _messageScroller = null!;
  private ConfirmationDialog2 _quitDialog = null!;
  private Label _scoreLabel = null!;
  private string _selfPlayerName = string.Empty;
  private bool IsSelf (string playerName) => _selfPlayerName == playerName;
  private string YouOrName (string playerName) => IsSelf (playerName) ? "You" : playerName;
  private string WasOrWere (string playerName) => IsSelf (playerName) ? "were" : "was";
  private void OnPlayerRespawned (string shotPlayerName, string shooterPlayerName) => _messageScroller.AddMessage ($"{YouOrName (shotPlayerName)} {WasOrWere (shotPlayerName)} shot by {shooterPlayerName}");
  private void OnSelfPlayerHealthChanged (string playerName, int health) => _healthBar.Value = health;
  private void OnKickedFromServer (string reason) => Hide();
  private void OnServerShutDown() => Hide();
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
    _world.PlayerScored += OnPlayerScored;
    _world.PlayerRespawned += OnPlayerRespawned;
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
    _messageScroller.AddMessage ($"{playerName} joined the game");
  }

  private void OnPlayerLeftGame (string playerName)
  {
    if (IsSelf (playerName)) return;
    _messageScroller.AddMessage ($"{playerName} left the game");
  }

  private void OnPlayerScored (int score, string shooterPlayerName, string shotPlayerName)
  {
    _messageScroller.AddMessage ($"{YouOrName (shooterPlayerName)} shot {shotPlayerName}");
    if (!IsSelf (shooterPlayerName)) return;
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
}
