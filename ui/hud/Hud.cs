using System.Linq;
using com.forerunnergames.energyshot.core.audio;
using com.forerunnergames.energyshot.core.world;
using com.forerunnergames.energyshot.players;
using com.forerunnergames.energyshot.ui.dialogs;
using com.forerunnergames.energyshot.ui.hud.messages;
using com.forerunnergames.energyshot.utilities;
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
  private Label _roundClock = null!; // Issue #153.
  private Label _modeLabel = null!; // "King of the Hill", above the clock (issue #419).
  private ScopeView _scopeView = null!; // Issue #236.
  private Label _dartAmmo = null!;
  private ColorRect _roundOverlay = null!;
  private RichTextLabel _roundBoard = null!;
  private StyleBoxFlat? _poisonFill; // Green fill swapped in while poisoned (issue #194).
  private bool _poisonTintShown;
  private MessageScroller _messageScroller = null!;
  private ChatBox _chatBox = null!; // Player chat (issue #188).
  private ConfirmationDialog2 _quitDialog = null!;
  private Label _scoreLabel = null!;
  private RichTextLabel _leaderboardEntries = null!;
  private ShaderMaterial _vignette = null!;
  private ShaderMaterial _blur = null!;
  private ShaderMaterial _splatter = null!;
  // The overlay nodes themselves, so they can be hidden while idle (issue #200):
  // a visible full-screen ColorRect runs its fragment shader over every pixel of
  // every frame even when it draws nothing at all.
  private ColorRect _blurRect = null!;
  private ColorRect _splatterRect = null!;
  private ColorRect _vignetteRect = null!;
  private CooldownMeter _shotMeter = null!;
  private CooldownMeter _slideMeter = null!;
  private CooldownMeter _fullAutoMeter = null!;
  private CooldownMeter _bananaMeter = null!;
  // Runs in reverse (issue #192): the eating ritual drains it instead of filling it.
  private CooldownMeter _eatMeter = null!;
  private TextureRect _breadIcon = null!;
  private Label _breadNotice = null!;
  private ulong _breadNoticeEndMs;
  private AudioStreamPlayer _lockOnSound = null!;
  private AudioStreamPlayer _breadDeniedSound = null!;
  private AudioStreamPlayer _breadInterruptedSound = null!;
  private ColorRect _deathOverlay = null!;
  private Label _deathCountdown = null!;
  private TargetRing _targetRing = null!;
  private ulong _deathOverlayEndMs;
  private float _blurIntensity;
  private float _splatterSecondsLeft;
  private float _splatterSlide;
  private const float SplatterSeconds = 5.0f; // Matches the banana stun window (issue #70).
  private const float SplatterSlidePerSecond = 0.06f;
  private const float BreadNoticeSeconds = 2.0f; // How long a bread refusal/interruption line lingers (issue #192).
  private static readonly Color BreadNoticeColor = new(1.0f, 0.85f, 0.5f);
  private static readonly Color BreadInterruptColor = new(1.0f, 0.4f, 0.35f);
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
    var intensity = Mathf.Clamp (1.0f - health / threshold, 0.0f, 1.0f);
    _vignette.SetShaderParameter ("intensity", intensity);
    // Above the threshold it draws nothing, so stop drawing it: three full-screen
    // blended quads per frame is real fill-rate at retina resolutions (issue #200).
    _vignetteRect.Visible = intensity > 0.0f || (_world.SelfPlayer?.IsPoisoned ?? false); // The green layer needs it too (issue #261).
  }

  private void UpdateLeaderboard()
  {
    var players = _world.GetPlayers().OrderByDescending (player => player.Score).ThenBy (player => player.DisplayName);
    _leaderboardEntries.Text = string.Join ("\n", players.Select ((player, index) => LeaderboardEntry (player, rank: index + 1)));
    UpdateScoreLabel();
  }

  // 3+ streak entries glow & pulse so the hot player stands out (see issue #77); the
  // crown holder wears the crown here too - only above zero points, & ties don't
  // steal it (issues #107/#178) - & every entry shows its ping (issue #100).
  // Entries are ranked by sorted position; equal scores order by ascending display
  // name (issue #126), matching World.PickCrownHolder's tie ordering (issue #178).
  // Names are tinted with each player's chosen body color (issue #43). The crown
  // hangs in a fixed-width left gutter (issue #189), never in the entry itself.
  private static string LeaderboardEntry (players.Player player, int rank)
  {
    var name = $"[color=#{players.PlayerColors.TextHex (player.ColorIndex)}]{player.DisplayName}[/color]";
    var entry = $"{rank}. {name}  {player.Score}  ({Mathf.Max (0, player.PingMs)}ms)";
    if (player.IsOnStreak) entry = $"[pulse freq=1.5 color=#ffd24d ease=-2.0][wave amp=18.0 freq=4.0][b]{entry}[/b][/wave][/pulse]";
    return $"{CrownGutter (player.IsCrowned)}{entry}";
  }

  // Fixed-width left gutter (issue #189): prepending the crown to the leader's row
  // alone shoved that one entry right & knocked the rank/name/score/ping columns out
  // of line with every other row. Now EVERY row carries the glyph - drawn fully
  // transparent for non-leaders - so the gutter is always exactly one crown wide &
  // the columns stay put whether or not a crown is showing, wherever it moves to.
  // The same glyph reserves the space, so the width can never disagree with itself.
  // Kept outside the streak-pulse wrap so a hot leader's crown doesn't wave with the
  // text, & outside the name tint so it stays gold (issues #43 & #77).
  private const string CrownGlyph = "\U0001F451";
  private static string CrownGutter (bool isCrowned) => isCrowned ? $"{CrownGlyph} " : $"[color=#00000000]{CrownGlyph}[/color] ";

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
    _messageScroller.ApplyVisibilitySetting(); // Issue #297: also closes any stuck Tab backdrop.
    _scoreLabel = GetNode <Label> ("VBoxContainer/Score/Label");
    _leaderboardEntries = GetNode <RichTextLabel> ("Leaderboard/MarginContainer/VBoxContainer/Entries");
    _vignette = (ShaderMaterial)GetNode <ColorRect> ("Vignette").Material;
    _vignetteRect = GetNode <ColorRect> ("Vignette");
    _blurRect = GetNode <ColorRect> ("Blur");
    _splatterRect = GetNode <ColorRect> ("Splatter");
    _blur = (ShaderMaterial)_blurRect.Material;
    _splatter = (ShaderMaterial)_splatterRect.Material;
    _blurRect.Visible = false;
    _splatterRect.Visible = false;
    _vignetteRect.Visible = false; // Shown by UpdateVignette once health drops (issue #200).
    _shotMeter = GetNode <CooldownMeter> ("CooldownMeters/Shot");
    _slideMeter = GetNode <CooldownMeter> ("CooldownMeters/Slide");
    _fullAutoMeter = GetNode <CooldownMeter> ("CooldownMeters/FullAuto");
    _bananaMeter = GetNode <CooldownMeter> ("CooldownMeters/Banana");
    _eatMeter = GetNode <CooldownMeter> ("CooldownMeters/Eat"); // Reverse meter (issue #192).
    _breadIcon = GetNode <TextureRect> ("VBoxContainer/Bread/Icon");
    CreateBreadSounds();
    CreateBreadNotice();
    _lockOnSound = new AudioStreamPlayer { Stream = ProceduralSounds.LockOn(), MaxPolyphony = 2, Bus = AudioBuses.SfxBusName }; // Issue #211.
    AddChild (_lockOnSound);
    _world.SelfPlayerPunched += OnSelfPlayerPunched;
    _world.SelfPlayerPoisonTicked += () => _poisonPulse = 1.0f; // Issue #261.
    _world.SelfPlayerSplattered += OnSelfPlayerSplattered;
    _world.SelfPlayerBreaded += OnSelfPlayerBreaded; // Issue #247.
    _world.SelfPlayerAirplaneCaught += OnSelfPlayerAirplaneCaught;
    _world.SelfPlayerAirplaneLockAcquired += () => _lockOnSound.Play(); // Issue #211.
    _world.SelfPlayerAteBread += OnSelfPlayerAteBread;
    _world.SelfPlayerBreadDenied += OnSelfPlayerBreadDenied;
    _world.SelfPlayerBreadInterrupted += OnSelfPlayerBreadInterrupted;
    GetNode <Timer> ("LeaderboardTimer").Timeout += UpdateLeaderboard;
    _quitDialog = GetNode <ConfirmationDialog2> ("QuitDialog");
    _quitDialog.GetNode <Label> ("VBoxContainer/Title/Label").Text = "Leave the game?"; // It returns to the menu, it doesn't kill the app (Aaron, 2026-08-24).
    _quitDialog.Confirmed += () => EmitSignal (SignalName.GameQuit);
    _quitDialog.Canceled += CancelQuit;
    _quitDialog.Closed += CancelQuit;
    CreateSettingsDialog(); // Issue #297.
    _world.NewGameStarted += OnNewGameStarted;
    _world.LeftGame += OnLeftGame; // Back to the menu (Aaron, 2026-08-24): the HUD goes with the session.
    _world.PlayerJoinedGame += OnPlayerJoinedGame;
    _world.PlayerLeftGame += OnPlayerLeftGame;
    _world.RemoteMessageReceived += OnRemoteMessageReceived;
    _world.AdminMessageReceived += OnAdminMessageReceived;
    _world.RoundClockUpdated += OnRoundClockUpdated; // Issue #153.
    _world.HillCleared += message => PrintMessage (message, MessageScroller.MessageImportance.High); // The bounty (issue #420).
    _world.RoundEnded += OnRoundEnded;
    _world.RoundStarted += OnRoundStarted;
    CreateRoundUi();
    CreateScopeUi(); // Issue #236.
    _world.ChatReceived += OnChatReceived; // Issue #188.
    CreateChatBox();
    _world.PlayerScored += OnPlayerScored;
    _world.PlayerRespawnedShot += OnPlayerRespawnedShot;
    _world.PlayerRespawnedFell += OnPlayerRespawnedFell;
    _world.SelfPlayerHealthChanged += OnSelfPlayerHealthChanged;
    _world.KickedFromServer += OnKickedFromServer;
    _world.ServerShutDown += OnServerShutDown;
    CreateDeathOverlay();
    CreateTargetRing();
  }

  // Paper airplane warning ring (issue #191): built in code like the death overlay,
  // & only ever visible on the screen of the player the airplane is locked onto.
  private void CreateTargetRing()
  {
    _targetRing = new TargetRing();
    AddChild (_targetRing);
    MoveChild (_targetRing, 1); // Above the death wash, under the messages & leaderboard.
  }

  // Death overlay (issue #152): a dim wash + countdown while the body lies at the
  // death spot; built in code to keep Hud scene edits minimal, like the music
  // player's pause toggle (issue #137).
  private void CreateDeathOverlay()
  {
    _deathOverlay = new ColorRect { Color = new Color (0.0f, 0.0f, 0.0f, 0.45f), MouseFilter = MouseFilterEnum.Ignore, Visible = false };
    _deathOverlay.SetAnchorsPreset (LayoutPreset.FullRect);
    AddChild (_deathOverlay);
    MoveChild (_deathOverlay, 0); // Under the health bar, messages, & leaderboard.
    _deathCountdown = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
    _deathCountdown.AddThemeFontSizeOverride ("font_size", 48);
    _deathCountdown.SetAnchorsPreset (LayoutPreset.FullRect);
    _deathOverlay.AddChild (_deathCountdown);
  }

  // The blowgun's scope view & dart count (issue #236): the scope draws over
  // everything while scoped; the ammo readout sits under the crosshair whenever the
  // blowgun is out. Both poll the local player, like the bread icon.
  private void CreateScopeUi()
  {
    _scopeView = new ScopeView();
    AddChild (_scopeView);
    _dartAmmo = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
    _dartAmmo.AddThemeFontSizeOverride ("font_size", 34);
    _dartAmmo.SetAnchorsPreset (LayoutPreset.Center);
    _dartAmmo.GrowHorizontal = GrowDirection.Both;
    _dartAmmo.OffsetTop = 48.0f;
    AddChild (_dartAmmo);
  }

  private void UpdateDartAmmo()
  {
    var self = _world.SelfPlayer;
    _scopeView.Track (self);
    var show = self != null && Visible && self.IsBlowgunSelected && self.HasBlowgun;
    _dartAmmo.Visible = show;
    if (!show) return;
    _dartAmmo.Text = self!.BlowgunDarts > 0 ? $"darts: {self.BlowgunDarts}" : "darts: 0 - find ammo";
    _dartAmmo.Modulate = self.BlowgunDarts > 0 ? Colors.White : new Color (1.0f, 0.5f, 0.4f);
  }

  // Rounds (issue #153), code-built like the death overlay: a top-center clock while
  // a round runs & a full-screen scoreboard between rounds. Neither captures input.
  private void CreateRoundUi()
  {
    // The clock & mode ride ABOVE the leaderboard (Aaron, 2026-08-22): the old
    // top-center label overlapped the in-game message feed.
    _roundClock = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
    _roundClock.AddThemeFontSizeOverride ("font_size", 40);
    var leaderboardColumn = GetNode <BoxContainer> ("Leaderboard/MarginContainer/VBoxContainer");
    leaderboardColumn.AddChild (_roundClock);
    leaderboardColumn.MoveChild (_roundClock, 0);
    // The box says WHICH game it is scoring (Aaron, 2026-08-28, issue #419): "first
    // to 100" never named King of the Hill. Its own line, so the clock line can't
    // outgrow the box.
    _modeLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
    _modeLabel.AddThemeFontSizeOverride ("font_size", 44);
    leaderboardColumn.AddChild (_modeLabel);
    leaderboardColumn.MoveChild (_modeLabel, 0);
    _roundOverlay = new ColorRect { Color = new Color (0.0f, 0.0f, 0.0f, 0.7f), MouseFilter = MouseFilterEnum.Ignore, Visible = false };
    _roundOverlay.SetAnchorsPreset (LayoutPreset.FullRect);
    AddChild (_roundOverlay);
    _roundBoard = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, MouseFilter = MouseFilterEnum.Ignore };
    // Twice the old size (Aaron, 2026-08-22): the between-rounds screen read tiny.
    _roundBoard.AddThemeFontSizeOverride ("normal_font_size", 72);
    _roundBoard.AddThemeFontSizeOverride ("bold_font_size", 80);
    _roundBoard.SetAnchorsPreset (LayoutPreset.Center);
    _roundBoard.GrowHorizontal = GrowDirection.Both;
    _roundBoard.GrowVertical = GrowDirection.Both;
    _roundBoard.CustomMinimumSize = new Vector2 (2200.0f, 0.0f);
    _roundOverlay.AddChild (_roundBoard);
  }

  private void OnRoundClockUpdated (int secondsLeft, int zapLimit, int mode, string hillHolder)
  {
    var clock = secondsLeft >= 0 ? $"{secondsLeft / 60}:{secondsLeft % 60:00}" : string.Empty;
    var target = zapLimit > 0 ? $"first to {zapLimit}" : string.Empty;
    // King of the Hill (issue #44): who holds it, or that it's contested, or nobody.
    var hill = (GameMode)mode != GameMode.KingOfTheHill ? string.Empty : hillHolder.Length == 0 ? "hill: empty" : hillHolder == "contested" ? "hill: CONTESTED" : $"hill: {hillHolder}";
    _roundClock.Text = string.Join ("   ·   ", new[] { clock, target, hill }.Where (part => part.Length > 0));
    _roundClock.Visible = _roundClock.Text.Length > 0 && !_roundOverlay.Visible;
    _modeLabel.Text = (GameMode)mode == GameMode.KingOfTheHill ? "King of the Hill" : "Zaps"; // Issue #419.
    _modeLabel.Visible = _roundClock.Visible;
    if ((GameMode)mode == GameMode.KingOfTheHill) UpdateKingBanner (hillHolder);
  }

  // Crowning hits BIG (issue #376, thepro & Caleb: 'so underwhelming'): the moment
  // YOU take the hill, a huge gold center-screen banner; losing it gets a DETHRONED
  // beat; everyone else gets a scroller line on the handover. Contested & empty are
  // not a change of king - the last sole holder stays remembered through them.
  private string _lastKing = string.Empty;
  private Label? _kingBanner;
  private Tween? _kingBannerTween;

  private void UpdateKingBanner (string hillHolder)
  {
    var isSoleHolder = hillHolder.Length > 0 && hillHolder != "contested";
    if (!isSoleHolder || hillHolder == _lastKing) return;
    var previousKing = _lastKing;
    _lastKing = hillHolder;
    var selfName = Player.Local?.DisplayName ?? string.Empty;
    if (hillHolder == selfName) ShowKingBanner ("\U0001F451 YOU ARE THE KING! \U0001F451", fontSize: 170, holdSeconds: 2.5f);
    else if (previousKing == selfName) ShowKingBanner ("DETHRONED!", fontSize: 110, holdSeconds: 1.8f);
    else PrintMessage ($"{hillHolder} took the hill!", MessageScroller.MessageImportance.High);
  }

  private void ShowKingBanner (string text, int fontSize, float holdSeconds)
  {
    if (_kingBanner == null)
    {
      _kingBanner = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
      _kingBanner.AddThemeColorOverride ("font_color", new Color (1.0f, 0.85f, 0.2f));
      _kingBanner.AddThemeColorOverride ("font_outline_color", new Color (0.2f, 0.1f, 0.0f));
      _kingBanner.AddThemeConstantOverride ("outline_size", 24);
      _kingBanner.SetAnchorsPreset (LayoutPreset.CenterTop);
      _kingBanner.GrowHorizontal = GrowDirection.Both;
      _kingBanner.Position = new Vector2 (_kingBanner.Position.X, 320.0f);
      AddChild (_kingBanner);
    }

    _kingBanner.AddThemeFontSizeOverride ("font_size", fontSize);
    _kingBanner.Text = text;
    _kingBanner.Modulate = Colors.White;
    _kingBanner.Visible = true;
    _kingBannerTween?.Kill();
    _kingBannerTween = CreateTween();
    _kingBannerTween.TweenInterval (holdSeconds);
    _kingBannerTween.TweenProperty (_kingBanner, "modulate:a", 0.0f, 0.6f);
    _kingBannerTween.TweenCallback (Callable.From (() => _kingBanner.Visible = false));
  }

  private void OnRoundEnded (string scoreboardBbcode)
  {
    _roundBoard.Text = scoreboardBbcode;
    _roundOverlay.Visible = true;
    _roundClock.Visible = false;
    _modeLabel.Visible = false; // The mode line rides the clock's visibility everywhere (CodeRabbit on #432).
  }

  private void OnRoundStarted() => _roundOverlay.Visible = false;

  // The Settings dialog (issue #297): the pause dialog gains a SETTINGS button &
  // every persisted preference lives in one place, in the same custom dialog style
  // (the ConfirmationDialog2 skeleton the host/join dialogs share). Toggles apply
  // immediately; volume sliders follow with the audio-bus work (issue #301).
  private ConfirmationDialog2 _settingsDialog = null!;

  private void CreateSettingsDialog()
  {
    _settingsDialog = ResourceLoader.Load <PackedScene> ("res://ui/dialogs/confirm/ConfirmationDialog2.tscn").Instantiate <ConfirmationDialog2>();
    AddChild (_settingsDialog);
    _settingsDialog.GetNode <Label> ("VBoxContainer/Title/Label").Text = "Settings";
    _settingsDialog.GetNode <Control> ("VBoxContainer/MarginContainer").Visible = false; // No OK/CANCEL: the X closes.
    _settingsDialog.Closed += CloseSettings;
    var rows = _settingsDialog.GetNode <BoxContainer> ("VBoxContainer/HBoxContainer");
    rows.Alignment = BoxContainer.AlignmentMode.Center;
    var column = new VBoxContainer();
    column.AddThemeConstantOverride ("separation", 36);
    column.CustomMinimumSize = new Vector2 (1100.0f, 0.0f);
    rows.AddChild (column);
    column.AddChild (SettingToggle ("Hold to crouch", Settings.HoldToCrouch, isEnabled => { Settings.HoldToCrouch = isEnabled; Player.Local?.RefreshCrouchMode(); }));
    column.AddChild (SettingToggle ("Hold to scope", Settings.HoldToScope, isEnabled => { Settings.HoldToScope = isEnabled; Player.Local?.RefreshScopeMode(); }));
    column.AddChild (SettingToggle ("Hold to emote", Settings.HoldToDance, isEnabled => { Settings.HoldToDance = isEnabled; Player.Local?.RefreshDanceMode(); }));
    // Audio (issue #301, Escendrix: the effects are too loud): two live sliders.
    column.AddChild (SettingSlider ("Sound effects volume", Settings.SfxVolume, percent => { Settings.SfxVolume = percent; AudioBuses.ApplyVolumes (Settings.SfxVolume, Settings.MusicVolume); }));
    column.AddChild (SettingSlider ("Music volume", Settings.MusicVolume, percent => { Settings.MusicVolume = percent; AudioBuses.ApplyVolumes (Settings.SfxVolume, Settings.MusicVolume); }));
    column.AddChild (SettingToggle ("Show music player", Settings.ShowMusicPlayer, isEnabled => { Settings.ShowMusicPlayer = isEnabled; GetNode <MusicPlayer> ("MusicPlayer").ApplyVisibilitySetting(); }));
    column.AddChild (SettingToggle ("Show chat", Settings.ShowChat, isEnabled => Settings.ShowChat = isEnabled));
    column.AddChild (SettingToggle ("Show game messages", Settings.ShowMessages, isEnabled => { Settings.ShowMessages = isEnabled; _messageScroller.ApplyVisibilitySetting(); }));
    _settingsDialog.Hide();
    // The pause dialog's middle row hosts the way in.
    var pauseRow = _quitDialog.GetNode <BoxContainer> ("VBoxContainer/HBoxContainer");
    pauseRow.Alignment = BoxContainer.AlignmentMode.Center;
    var settingsButton = new Button { Text = " SETTINGS " };
    settingsButton.AddThemeFontSizeOverride ("font_size", 50);
    settingsButton.Pressed += OpenSettings;
    pauseRow.AddChild (settingsButton);
  }

  // Full-size rows (Aaron, 2026-08-22: code-built widgets keep coming out tiny):
  // a big label & a real ON/OFF toggle button - no theme-sized check icons, which
  // render microscopic against the 4K design viewport no matter the font.
  // A 0-100 slider row at 4K size (the UI rule): label, a wide slider, & the live value.
  private static Control SettingSlider (string text, int initial, System.Action <int> apply)
  {
    var row = new HBoxContainer();
    row.AddThemeConstantOverride ("separation", 40);
    var label = new Label { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
    label.AddThemeFontSizeOverride ("font_size", 60);
    var value = new Label { Text = $"{initial}%", CustomMinimumSize = new Vector2 (200.0f, 0.0f), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    value.AddThemeFontSizeOverride ("font_size", 60);
    var slider = new HSlider { MinValue = 0, MaxValue = 100, Step = 5, Value = initial, CustomMinimumSize = new Vector2 (700.0f, 80.0f), SizeFlagsVertical = SizeFlags.ShrinkCenter };
    slider.ValueChanged += newValue => { value.Text = $"{(int)newValue}%"; apply ((int)newValue); };
    row.AddChild (label);
    row.AddChild (slider);
    row.AddChild (value);
    return row;
  }

  private static Control SettingToggle (string text, bool initial, System.Action <bool> apply)
  {
    var row = new HBoxContainer();
    row.AddThemeConstantOverride ("separation", 40);
    var label = new Label { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center };
    label.AddThemeFontSizeOverride ("font_size", 60);
    var toggle = new Button { ToggleMode = true, ButtonPressed = initial, Text = initial ? "  ON  " : "  OFF  ", CustomMinimumSize = new Vector2 (260.0f, 0.0f) };
    toggle.AddThemeFontSizeOverride ("font_size", 60);
    toggle.Toggled += isEnabled => { toggle.Text = isEnabled ? "  ON  " : "  OFF  "; apply (isEnabled); };
    row.AddChild (label);
    row.AddChild (toggle);
    return row;
  }

  private void OpenSettings()
  {
    _quitDialog.Hide();
    _settingsDialog.Show(); // The mouse is already visible: we came from the pause dialog.
  }

  private void CloseSettings()
  {
    _settingsDialog.Hide();
    Input.MouseMode = Input.MouseModeEnum.Captured;
    EmitSignal (SignalName.GameResumed);
  }

  public override void _UnhandledInput (InputEvent @event)
  {
    if (_chatBox.IsOpen) return; // The chat line owns the keyboard (issue #188): Esc cancels it, not the game.
    if (@event.IsActionPressed ("chat") && Visible && !_quitDialog.Visible) { _chatBox.Open(); return; }
    if (!Input.IsActionJustPressed ("quit")) return;
    if (_settingsDialog.Visible) { CloseSettings(); return; } // Esc backs out of Settings first (issue #297).
    ToggleQuitDialog();
  }

  // Code-built (issue #188): bottom-left, grows upward, sits beside nothing else.
  // Opening it disables player input for the duration - deliberate capture, the
  // opposite of the #118 history bug - & never touches the mouse mode.
  private void CreateChatBox()
  {
    _chatBox = new ChatBox();
    AddChild (_chatBox);
    _chatBox.SetAnchorsAndOffsetsPreset (LayoutPreset.BottomLeft);
    _chatBox.GrowVertical = GrowDirection.Begin;
    _chatBox.OffsetLeft = 16.0f;
    _chatBox.OffsetBottom = -16.0f;
    _chatBox.Opened += () => _world.SelfPlayer?.SetInputEnabled (isEnabled: false);
    _chatBox.Closed += () => _world.SelfPlayer?.SetInputEnabled (isEnabled: true);
    _chatBox.ChatSubmitted += _world.SendChat;
  }

  // The sender's identity & color come from the server-relayed peer id (issue #188),
  // tinted exactly like leaderboard names (issue #43). The Tab history archives the
  // same line in the same color.
  private void OnChatReceived (int senderId, string senderName, string text)
  {
    var colorIndex = _world.GetPlayers().FirstOrDefault (player => player.NetworkId == senderId)?.ColorIndex ?? 0;
    var hex = players.PlayerColors.TextHex (colorIndex);
    _chatBox.AddLine (senderName, hex, text);
    _messageScroller.AddToHistoryOnly ($"{ChatBox.EscapeBbcode (senderName)}: {ChatBox.EscapeBbcode (text)}", Color.FromHtml (hex));
  }

  public override void _Process (double delta)
  {
    UpdateBlur (delta);
    UpdateSplatter (delta);
    UpdateCooldownMeters();
    UpdateBreadIcon();
    UpdatePoisonTint(); // Issue #194.
    UpdatePoisonVignette (delta); // Issue #261.
    UpdateDartAmmo(); // Issue #236.
    UpdateBreadNotice();
    UpdateDeathOverlay();
    UpdateTargetRing();
  }

  // The ring only ever reads OUR own incoming-airplane threat (issue #191), so no
  // other player's screen can be lit up by somebody else's hazard.
  private void UpdateTargetRing()
  {
    var self = Visible ? _world.SelfPlayer : null;
    _targetRing.SetThreat (self?.AirplaneThreatFraction ?? 0.0f);
    _targetRing.SetLocked (self?.HasAirplaneLock ?? false); // Thrower's lock-on (issue #205).
  }

  // Bread cues are code-generated (issue #160): no downloaded assets. The munching
  // itself is positional now & lives on the eater (issue #192), so only the refusal
  // & the interruption are HUD-local.
  private void CreateBreadSounds()
  {
    _breadDeniedSound = new AudioStreamPlayer { Stream = ProceduralSounds.Denied(), Bus = AudioBuses.SfxBusName };
    _breadInterruptedSound = new AudioStreamPlayer { Stream = ProceduralSounds.Interrupted(), Bus = AudioBuses.SfxBusName };
    AddChild (_breadDeniedSound);
    AddChild (_breadInterruptedSound);
  }

  // A short line centered just above the cooldown meters (issue #192): why the eat
  // was refused, or that a hit just cost you the loaf. Built in code like the death
  // overlay & target ring, to keep Hud scene edits minimal.
  private void CreateBreadNotice()
  {
    _breadNotice = new Label { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, MouseFilter = MouseFilterEnum.Ignore, Visible = false };
    _breadNotice.AddThemeFontSizeOverride ("font_size", 22);
    _breadNotice.AddThemeColorOverride ("font_outline_color", Colors.Black);
    _breadNotice.AddThemeConstantOverride ("outline_size", 4);
    _breadNotice.SetAnchorsPreset (LayoutPreset.Center);
    _breadNotice.OffsetLeft = -240.0f;
    _breadNotice.OffsetRight = 240.0f;
    _breadNotice.OffsetTop = 4.0f;
    _breadNotice.OffsetBottom = 32.0f;
    AddChild (_breadNotice);
  }

  private void ShowBreadNotice (string text, Color color)
  {
    _breadNotice.Text = text;
    _breadNotice.Modulate = color;
    _breadNotice.Visible = true;
    _breadNoticeEndMs = Time.GetTicksMsec() + (ulong)(BreadNoticeSeconds * 1000.0f);
  }

  private void UpdateBreadNotice()
  {
    if (!_breadNotice.Visible || Time.GetTicksMsec() < _breadNoticeEndMs) return;
    _breadNotice.Visible = false;
  }

  private void ShowDeathOverlay()
  {
    var seconds = _world.SelfPlayer?.DeathSequenceSeconds ?? 5.0f;
    _deathOverlayEndMs = Time.GetTicksMsec() + (ulong)(seconds * 1000.0f);
    _deathOverlay.Visible = true;
  }

  // Countdown feel (issue #152): whole seconds ticking down to the auto-respawn;
  // clears once the wait is over & the replicated Fallen state has dropped.
  private void UpdateDeathOverlay()
  {
    if (!_deathOverlay.Visible) return;
    var now = Time.GetTicksMsec();
    var secondsLeft = now >= _deathOverlayEndMs ? 0 : (int)Mathf.Ceil ((_deathOverlayEndMs - now) / 1000.0f);
    _deathCountdown.Text = $"Zapped out!\nBack in {Mathf.Max (1, secondsLeft)}...";
    if (secondsLeft > 0 || _world.SelfPlayer?.Fallen == true) return;
    _deathOverlay.Visible = false;
  }

  // Punch blur stacks per hit & fades back to sharp; a heavy stack fades slower, so
  // being near-blind lingers (issue #68).
  private void UpdateBlur (double delta)
  {
    // Poison holds a blur floor that grows with the dart count (issue #261); punches
    // still stack on top & fade back down to that floor, not to sharp.
    var self = _world.SelfPlayer;
    var poisonFloor = self == null ? 0.0f : Mathf.Min (0.6f, 0.25f * self.PoisonDarts);
    _blurIntensity = Mathf.Max (_blurIntensity, poisonFloor);
    if (_blurIntensity <= 0.0f) return;
    var fadePerSecond = Mathf.Lerp (0.25f, 0.1f, _blurIntensity);
    _blurIntensity = Mathf.Max (poisonFloor, _blurIntensity - fadePerSecond * (float)delta);
    _blur.SetShaderParameter ("intensity", _blurIntensity);
    // Sampling the screen texture forces a back-buffer copy, so the node only
    // stays up while there's actually blur to draw (issue #200).
    _blurRect.Visible = _blurIntensity > 0.0f;
  }

  // The splat slides slowly down the screen & fades out with the banana stun (issue #70).
  private void UpdateSplatter (double delta)
  {
    if (_splatterSecondsLeft <= 0.0f) return;
    _splatterSecondsLeft = Mathf.Max (0.0f, _splatterSecondsLeft - (float)delta);
    _splatterSlide += SplatterSlidePerSecond * (float)delta;
    _splatter.SetShaderParameter ("slide", _splatterSlide);
    _splatter.SetShaderParameter ("intensity", _splatterSecondsLeft / SplatterSeconds);
    // 24 wobbling goo chunks per pixel is a real cost, & it was being paid every
    // frame of every match - the node only stays up while goo is on screen (#200).
    _splatterRect.Visible = _splatterSecondsLeft > 0.0f;
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
    _eatMeter.SetDraining (self.BreadEatRemainingFraction, self.Eating); // Reverse: it drains to empty (issue #192).
  }

  // The green vignette (issue #261): a baseline while poisoned, & a pulse on every
  // tick that fades over a second - so the edges of the screen breathe with the damage.
  private float _poisonPulse;

  private void UpdatePoisonVignette (double delta)
  {
    var self = _world.SelfPlayer;
    _poisonPulse = Mathf.Max (0.0f, _poisonPulse - (float)delta);
    var baseline = self != null && self.IsPoisoned ? 0.25f : 0.0f;
    var poison = self != null && self.IsPoisoned ? Mathf.Min (1.0f, baseline + 0.7f * _poisonPulse) : 0.0f;
    _vignette.SetShaderParameter ("poison", poison);
    if (poison > 0.0f) _vignetteRect.Visible = true;
  }

  // The health bar turns sickly green while darts are embedded (issue #194); polling
  // like the bread icon, since the dart count changes with no HUD-facing signal.
  private void UpdatePoisonTint()
  {
    var self = _world.SelfPlayer;
    if (self == null || !Visible) return;
    if (self.IsPoisoned == _poisonTintShown) return;
    _poisonTintShown = self.IsPoisoned;
    if (_poisonFill == null && _healthBar.GetThemeStylebox ("fill") is StyleBoxFlat fill) { _poisonFill = (StyleBoxFlat)fill.Duplicate(); _poisonFill.BgColor = Player.PoisonGreen; }
    if (_poisonTintShown && _poisonFill != null) { _healthBar.AddThemeStyleboxOverride ("fill", _poisonFill); return; }
    _healthBar.RemoveThemeStyleboxOverride ("fill");
  }

  // The bread icon dims once the loaf is eaten & brightens when a respawn restocks
  // it (issue #160); polling covers the restock, which has no signal.
  private void UpdateBreadIcon()
  {
    var self = _world.SelfPlayer;
    if (self == null || !Visible) return;
    _breadIcon.Modulate = self.HasBread ? Colors.White : new Color (0.4f, 0.4f, 0.4f, 0.35f);
  }

  // The munching itself was heard positionally during the ritual (issue #192); this
  // is the payoff line for finishing the whole three seconds.
  private void OnSelfPlayerAteBread() => PrintMessage ("You scarf your bread & feel brand new!", MessageScroller.MessageImportance.High);

  // Soft denied cues (issue #160): an eat attempt that can't start is never silent,
  // & the reason now lands centered above the meter (issue #192).
  private void OnSelfPlayerBreadDenied (string reason)
  {
    _breadDeniedSound.Play();
    ShowBreadNotice (reason, BreadNoticeColor);
    PrintMessage (reason);
  }

  // Unmistakable interruption (issue #192): a sour cue, a red line above the meter,
  // & the reverse meter visibly dying - the loaf is gone & you were not healed.
  private void OnSelfPlayerBreadInterrupted()
  {
    _breadInterruptedSound.Play();
    _eatMeter.Interrupt();
    ShowBreadNotice ("Your bread!", BreadInterruptColor);
    PrintMessage ("That hit cost you your bread!", MessageScroller.MessageImportance.High);
  }

  // ~3 hits reach max blur (issue #68): the shader's top LOD is a near-whiteout.
  private void OnSelfPlayerPunched()
  {
    _blurIntensity = Mathf.Min (1.0f, _blurIntensity + 0.4f);
    _blurRect.Visible = true; // Idle-hidden until something actually blurs (issue #200).
    _blur.SetShaderParameter ("intensity", _blurIntensity);
  }

  // The thrower saw its own airplane get punch-caught (issue #102): it picks the
  // line once & broadcasts it, so every peer sees the same text (#53).
  private void OnSelfPlayerAirplaneCaught (string catcherName, string throwerName) => Announce (MessageGenerator.OnAirplaneCatch (throwerName, catcherName));

  private void OnSelfPlayerSplattered() => Splat (crumbs: false);

  // Slung bread (issue #247): the same overlay machinery, a different splat - small
  // jagged brown crumbs & crusts, not recolored banana goo.
  private void OnSelfPlayerBreaded() => Splat (crumbs: true);

  private void Splat (bool crumbs)
  {
    _splatter.SetShaderParameter ("crumbs", crumbs ? 1.0f : 0.0f);
    _splatterSecondsLeft = SplatterSeconds;
    _splatterRect.Visible = true; // Idle-hidden until there's goo to draw (issue #200).
    _splatterSlide = 0.0f;
    _splatter.SetShaderParameter ("slide", 0.0f);
    _splatter.SetShaderParameter ("intensity", 1.0f);
    _splatter.SetShaderParameter ("seed", GD.Randf() * 100.0f); // Fresh splat layout per hit (issue #165).
  }

  private void OnNewGameStarted (string selfPlayerName, int selfMaxHealth)
  {
    _selfPlayerName = selfPlayerName;
    _messageScroller.Reset();
    _deathOverlay.Visible = false; // No stale countdown from a previous session (issue #152).
    // A rejoin must not inherit the LAST session's round furniture (CodeRabbit on
    // #432): the label & clock stay hidden until this game's own clock broadcast.
    _roundClock.Visible = false;
    _modeLabel.Visible = false;
    _healthBar.MaxValue = selfMaxHealth;
    _healthBar.Value = selfMaxHealth;
    UpdateVignette (selfMaxHealth);
    Show();
  }

  private void OnLeftGame()
  {
    _quitDialog.Hide();
    _roundOverlay.Visible = false;
    Hide();
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
    ShowDeathOverlay(); // The message now lands at death time, opening the lie-down wait (issue #152).
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
      self?.DiedEating ?? false, // Zapped out mid-ritual (issue #192).
      self?.LostStreakCount ?? 0,
      killer?.Sliding ?? false,
      killer?.IsLikelyAirborne() ?? false,
      killer?.IsUnarmed ?? false, // Bread doesn't count as being armed (issue #190).
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
