using System.Linq;
using com.forerunnergames.energyshot.core.world;
using com.forerunnergames.energyshot.players;
using com.forerunnergames.energyshot.ui.dialogs;
using com.forerunnergames.energyshot.ui.hud.messages;
using com.forerunnergames.energyshot.weapons;
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
  private CooldownMeter _shotMeter = null!;
  private CooldownMeter _slideMeter = null!;
  private CooldownMeter _fullAutoMeter = null!;
  private CooldownMeter _bananaMeter = null!;
  private TextureRect _breadIcon = null!;
  private AudioStreamPlayer _munchSound = null!;
  private AudioStreamPlayer _breadDeniedSound = null!;
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
  // Server-operator announcements render gold with a [SERVER] prefix (issue #158).
  private void OnAdminMessageReceived (string message) => PrintMessage ($"[SERVER] {message}", MessageScroller.MessageImportance.Admin);

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
    _leaderboardEntries.Text = string.Join ("\n", players.Select ((player, index) => LeaderboardEntry (player, rank: index + 1)));
    UpdateScoreLabel();
  }

  // 3+ streak entries glow & pulse so the hot player stands out (see issue #77); the
  // current leader wears the crown (issue #107) & every entry shows its ping (issue #100).
  // Entries are ranked by sorted position, ties keep list order (issue #126).
  // Names are tinted with each player's chosen body color (issue #43).
  private static string LeaderboardEntry (players.Player player, int rank)
  {
    var name = $"[color=#{players.PlayerColors.TextHex (player.ColorIndex)}]{player.DisplayName}[/color]";
    var entry = $"{rank}. {name}  {player.Score}  ({Mathf.Max (0, player.PingMs)}ms)";
    if (player.IsOnStreak) entry = $"[pulse freq=1.5 color=#ffd24d ease=-2.0][wave amp=18.0 freq=4.0][b]{entry}[/b][/wave][/pulse]";
    return rank == 1 ? $"\U0001F451 {entry}" : entry;
  }

  // Score can also drop (fall penalty), so the label reads the replicated value.
  private void UpdateScoreLabel() => _scoreLabel.Text = $"Score: {_world.SelfPlayer?.Score ?? 0}";
  private bool IsSelf (string playerName) => _selfPlayerName == playerName;
  private void OnKickedFromServer (string reason) => Hide();
  private void OnServerShutDown() => Hide();
  private void PrintMessage (string message, MessageScroller.MessageImportance importance = MessageScroller.MessageImportance.Medium) => _messageScroller.AddMessage (message, importance);
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
    _shotMeter = GetNode <CooldownMeter> ("CooldownMeters/Shot");
    _slideMeter = GetNode <CooldownMeter> ("CooldownMeters/Slide");
    _fullAutoMeter = GetNode <CooldownMeter> ("CooldownMeters/FullAuto");
    _bananaMeter = GetNode <CooldownMeter> ("CooldownMeters/Banana");
    _breadIcon = GetNode <TextureRect> ("VBoxContainer/Bread/Icon");
    CreateBreadSounds();
    _world.SelfPlayerPunched += OnSelfPlayerPunched;
    _world.SelfPlayerSplattered += OnSelfPlayerSplattered;
    _world.SelfPlayerAteBread += OnSelfPlayerAteBread;
    _world.SelfPlayerBreadDenied += OnSelfPlayerBreadDenied;
    GetNode <Timer> ("LeaderboardTimer").Timeout += UpdateLeaderboard;
    _quitDialog = GetNode <ConfirmationDialog2> ("QuitDialog");
    _quitDialog.Confirmed += () => EmitSignal (SignalName.GameQuit);
    _quitDialog.Canceled += CancelQuit;
    _quitDialog.Closed += CancelQuit;
    _world.NewGameStarted += OnNewGameStarted;
    _world.PlayerJoinedGame += OnPlayerJoinedGame;
    _world.PlayerLeftGame += OnPlayerLeftGame;
    _world.RemoteMessageReceived += OnRemoteMessageReceived;
    _world.AdminMessageReceived += OnAdminMessageReceived;
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
    UpdateCooldownMeters();
    UpdateBreadIcon();
  }

  // Bread munch & soft denied cues are code-generated (issue #160): no downloaded assets.
  private void CreateBreadSounds()
  {
    _munchSound = new AudioStreamPlayer { Stream = ProceduralSounds.Munch() };
    _breadDeniedSound = new AudioStreamPlayer { Stream = ProceduralSounds.Denied() };
    AddChild (_munchSound);
    AddChild (_breadDeniedSound);
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

  // Tiny center-screen meters near the crosshair (issue #177), each visible only
  // while its cooldown is recovering; the fade & ready-flash live in CooldownMeter.
  private void UpdateCooldownMeters()
  {
    var self = _world.SelfPlayer;
    if (self == null || !Visible) return;
    _shotMeter.SetFraction (self.ShotReadyFraction);
    _slideMeter.SetFraction (self.SlideReadyFraction); // The slide meter replaced the punch bar (issue #127).
    _fullAutoMeter.SetFraction (self.FullAutoReadyFraction);
    _bananaMeter.SetFraction (self.BananaReadyFraction);
  }

  // The bread icon dims once the loaf is eaten & brightens when a respawn restocks
  // it (issue #160); polling covers the restock, which has no signal.
  private void UpdateBreadIcon()
  {
    var self = _world.SelfPlayer;
    if (self == null || !Visible) return;
    _breadIcon.Modulate = self.HasBread ? Colors.White : new Color (0.4f, 0.4f, 0.4f, 0.35f);
  }

  private void OnSelfPlayerAteBread()
  {
    _munchSound.Play();
    PrintMessage ("You scarf your bread & feel brand new!", MessageScroller.MessageImportance.High);
  }

  // Soft denied cues (issue #160): a pressed B that can't eat is never silent.
  private void OnSelfPlayerBreadDenied (bool isOut)
  {
    _breadDeniedSound.Play();
    PrintMessage (isOut ? "No bread left this life" : "Already at full health");
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
    _splatter.SetShaderParameter ("seed", GD.Randf() * 100.0f); // Fresh splat layout per hit (issue #165).
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
  // once & broadcasts it to everyone (including the shooter); streak & theft-revenge
  // lines are picked once & shared the same way.
  private void OnPlayerRespawnedShot (string playerName, string shotByPlayerName)
  {
    if (IsSelf (shotByPlayerName))
    {
      ++_zapStreak;
      _zappedStreak = 0;
      if (_world.SelfPlayer?.TookRevengeOn (playerName) == true) Announce (MessageGenerator.OnTheftRevenge (playerName, shotByPlayerName));
      if (_zapStreak >= 3) Announce (MessageGenerator.OnZapStreak (shotByPlayerName, _zapStreak));
      return;
    }

    if (!IsSelf (playerName)) return;
    // Your own death renders red locally (issue #101); everyone else gets the same text in white.
    Announce (MessageGenerator.OnZapped (playerName, shotByPlayerName, BuildDeathContext (shotByPlayerName)), MessageScroller.MessageImportance.Critical);
    ++_zappedStreak;
    _zapStreak = 0;
    if (_zappedStreak >= 3) Announce (MessageGenerator.OnZappedStreak (playerName));
  }

  // The victim knows its own death snapshot; the killer's stance (sliding/airborne/
  // unarmed) is read from the killer's replicated node at message time (issue #84).
  private DeathContext BuildDeathContext (string killerName)
  {
    var self = _world.SelfPlayer;
    var killer = _world.GetPlayers().FirstOrDefault (player => player.DisplayName == killerName);
    return new DeathContext (
      self?.LastDamageKind ?? DamageKind.None,
      self?.LastZapEnergy ?? 0.0f,
      self?.DiedSliding ?? false,
      self?.DiedArmed ?? false,
      self?.DiedHoldingBananaGun ?? false,
      self?.LostStreakCount ?? 0,
      killer?.Sliding ?? false,
      killer?.IsLikelyAirborne() ?? false,
      killer?.HeldWeapon == HeldWeapon.None,
      _splatterSecondsLeft > 0.0f,
      _blurIntensity > 0.0f,
      self?.LastZapThroughBarrier ?? false);
  }

  private void Announce (string message, MessageScroller.MessageImportance importance = MessageScroller.MessageImportance.Medium) => NotifyMessage (message, message, importance);

  private void OnPlayerRespawnedFell (string playerName)
  {
    if (!IsSelf (playerName)) return;
    UpdateScoreLabel(); // Falling costs a point; show it immediately.
    // Same text for every peer (#53); red locally because it's your own death (issue #101).
    Announce (MessageGenerator.OnPlayerRespawnedFell (isSelf: false, playerName, out _), MessageScroller.MessageImportance.Critical);
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

  private void NotifyMessage (string localMessage, string remoteMessage, MessageScroller.MessageImportance importance = MessageScroller.MessageImportance.Medium, string excludedPlayerName = "")
  {
    PrintMessage (localMessage, importance);
    EmitSignal (SignalName.Message, remoteMessage, excludedPlayerName);
  }
}
