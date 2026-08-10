using System.Linq;
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
  private RichTextLabel _leaderboardEntries = null!;
  private ShaderMaterial _vignette = null!;
  private ShaderMaterial _blur = null!;
  private ShaderMaterial _splatter = null!;
  private ProgressBar _shotBar = null!;
  private ProgressBar _punchBar = null!;
  private ProgressBar _fullAutoBar = null!;
  private ProgressBar _bananaBar = null!;
  private float _blurIntensity;
  private float _splatterSecondsLeft;
  private float _splatterSlide;
  private const float SplatterSeconds = 5.0f; // Matches the banana stun window (issue #70).
  private const float SplatterSlidePerSecond = 0.06f;
  private int _zapStreak;
  private int _zappedStreak;
  private int _fallStreak;
  private string _selfPlayerName = string.Empty;
  private void OnRemoteMessageReceived (string message) => _messageScroller.AddMessage (message);

  private void OnSelfPlayerHealthChanged (string playerName, int health)
  {
    _healthBar.Value = health;
    UpdateVignette (health);
  }

  // Red vignette fades in below 40% health, fully saturated at death's door.
  private void UpdateVignette (int health)
  {
    var threshold = 0.4f * (float)_healthBar.MaxValue;
    _vignette.SetShaderParameter ("intensity", Mathf.Clamp (1.0f - health / threshold, 0.0f, 1.0f));
  }

  private void UpdateLeaderboard()
  {
    var players = _world.GetPlayers().OrderByDescending (player => player.Score).ThenBy (player => player.DisplayName);
    _leaderboardEntries.Text = string.Join ("\n", players.Select (LeaderboardEntry));
    UpdateScoreLabel();
  }

  // 3+ streak entries glow & pulse so the hot player stands out (see issue #77).
  private static string LeaderboardEntry (players.Player player) =>
    player.IsOnStreak
      ? $"[pulse freq=1.5 color=#ffd24d ease=-2.0][wave amp=18.0 freq=4.0][b]{player.DisplayName}  {player.Score}[/b][/wave][/pulse]"
      : $"{player.DisplayName}  {player.Score}";

  // Score can also drop (fall penalty), so the label reads the replicated value.
  private void UpdateScoreLabel() => _scoreLabel.Text = $"Score: {_world.SelfPlayer?.Score ?? 0}";
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
    _leaderboardEntries = GetNode <RichTextLabel> ("Leaderboard/MarginContainer/VBoxContainer/Entries");
    _vignette = (ShaderMaterial)GetNode <ColorRect> ("Vignette").Material;
    _blur = (ShaderMaterial)GetNode <ColorRect> ("Blur").Material;
    _splatter = (ShaderMaterial)GetNode <ColorRect> ("Splatter").Material;
    _shotBar = GetNode <ProgressBar> ("VBoxContainer/Cooldowns/Shot/Bar");
    _punchBar = GetNode <ProgressBar> ("VBoxContainer/Cooldowns/Punch/Bar");
    _fullAutoBar = GetNode <ProgressBar> ("VBoxContainer/Cooldowns/FullAuto/Bar");
    _bananaBar = GetNode <ProgressBar> ("VBoxContainer/Cooldowns/Banana/Bar");
    _world.SelfPlayerPunched += OnSelfPlayerPunched;
    _world.SelfPlayerSplattered += OnSelfPlayerSplattered;
    GetNode <Timer> ("LeaderboardTimer").Timeout += UpdateLeaderboard;
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

  public override void _Process (double delta)
  {
    UpdateBlur (delta);
    UpdateSplatter (delta);
    UpdateCooldownBars();
  }

  // Punch blur stacks per hit & fades back to sharp; a heavy stack fades slower, so
  // being near-blind lingers (issue #68).
  private void UpdateBlur (double delta)
  {
    if (_blurIntensity <= 0.0f) return;
    var fadePerSecond = Mathf.Lerp (0.25f, 0.1f, _blurIntensity);
    _blurIntensity = Mathf.Max (0.0f, _blurIntensity - fadePerSecond * (float)delta);
    _blur.SetShaderParameter ("intensity", _blurIntensity);
  }

  // The splat slides slowly down the screen & fades out with the banana stun (issue #70).
  private void UpdateSplatter (double delta)
  {
    if (_splatterSecondsLeft <= 0.0f) return;
    _splatterSecondsLeft = Mathf.Max (0.0f, _splatterSecondsLeft - (float)delta);
    _splatterSlide += SplatterSlidePerSecond * (float)delta;
    _splatter.SetShaderParameter ("slide", _splatterSlide);
    _splatter.SetShaderParameter ("intensity", _splatterSecondsLeft / SplatterSeconds);
  }

  private void UpdateCooldownBars()
  {
    var self = _world.SelfPlayer;
    if (self == null || !Visible) return;
    _shotBar.Value = self.ShotReadyFraction;
    _punchBar.Value = self.PunchReadyFraction;
    _fullAutoBar.Value = self.FullAutoReadyFraction;
    _bananaBar.Value = self.BananaReadyFraction;
  }

  // ~3 hits reach max blur (issue #68): the shader's top LOD is a near-whiteout.
  private void OnSelfPlayerPunched()
  {
    _blurIntensity = Mathf.Min (1.0f, _blurIntensity + 0.4f);
    _blur.SetShaderParameter ("intensity", _blurIntensity);
  }

  private void OnSelfPlayerSplattered()
  {
    _splatterSecondsLeft = SplatterSeconds;
    _splatterSlide = 0.0f;
    _splatter.SetShaderParameter ("slide", 0.0f);
    _splatter.SetShaderParameter ("intensity", 1.0f);
  }

  private void OnNewGameStarted (string selfPlayerName, int selfMaxHealth)
  {
    _selfPlayerName = selfPlayerName;
    _messageScroller.Reset();
    _healthBar.MaxValue = selfMaxHealth;
    _healthBar.Value = selfMaxHealth;
    UpdateVignette (selfMaxHealth);
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

  // Every peer sees the exact same message text (#53): the victim picks the zap line
  // once & broadcasts it to everyone (including the shooter); streak lines are picked
  // once & shared the same way.
  private void OnPlayerRespawnedShot (string playerName, string shotByPlayerName)
  {
    if (IsSelf (shotByPlayerName))
    {
      ++_zapStreak;
      _zappedStreak = 0;
      if (_zapStreak >= 3) Announce (MessageGenerator.OnZapStreak (shotByPlayerName));
      return;
    }

    if (!IsSelf (playerName)) return;
    var fullCharge = _world.SelfPlayer?.LastZapEnergy >= 0.95f;
    Announce (MessageGenerator.OnZapped (playerName, shotByPlayerName, selfIsVictim: false, selfIsZapper: false, fullCharge));
    ++_zappedStreak;
    _zapStreak = 0;
    if (_zappedStreak >= 3) Announce (MessageGenerator.OnZappedStreak (playerName));
  }

  private void Announce (string message) => NotifyMessage (message, message);

  private void OnPlayerRespawnedFell (string playerName)
  {
    if (!IsSelf (playerName)) return;
    UpdateScoreLabel(); // Falling costs a point; show it immediately.
    Announce (MessageGenerator.OnPlayerRespawnedFell (isSelf: false, playerName, out _)); // Same text for every peer (#53).
    ++_fallStreak;
    if (_fallStreak >= 3) Announce (MessageGenerator.OnFallStreak (playerName));
  }

  private void OnPlayerScored (int score, string playerName, string shotPlayerName)
  {
    if (!IsSelf (playerName)) return;
    UpdateScoreLabel();
    _fallStreak = 0;
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
