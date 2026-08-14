using com.forerunnergames.energyshot.core.audio;
using com.forerunnergames.energyshot.ui.hud.messages;
using com.forerunnergames.energyshot.utilities;
using Godot;

namespace com.forerunnergames.energyshot.ui.hud;

// Mini music player (issue #137): a mostly transparent bottom-right HUD panel
// showing the current song, this play's thumbs up/down counts (with bigger,
// brighter "." & "," key hints, issue #155) & a small spectrum visualizer. Your
// own remembered vote renders as a pressed thumb (issue #162). Never captures the
// mouse; a "Show music player" toggle in the pause dialog hides it without
// stopping music.
public partial class MusicPlayer : Control
{
  private MusicManager _music = null!;
  private Control _panel = null!;
  private Label _title = null!;
  private Label _upVote = null!;
  private Label _downVote = null!;
  private StyleBoxFlat _pressedStyle = null!;
  private StyleBoxFlat _restStyle = null!;
  private bool _hintShown;

  public override void _Ready()
  {
    _music = GetNode <MusicManager> ("/root/World/MusicManager");
    _panel = GetNode <Control> ("Panel");
    _title = GetNode <Label> ("Panel/MarginContainer/VBoxContainer/Title");
    _upVote = GetNode <Label> ("Panel/MarginContainer/VBoxContainer/Votes/UpVote");
    _downVote = GetNode <Label> ("Panel/MarginContainer/VBoxContainer/Votes/DownVote");
    _pressedStyle = ChipStyle (new Color (1.0f, 0.84f, 0.3f, 0.3f));
    _restStyle = ChipStyle (Colors.Transparent);
    OnOwnVoteChanged (0); // Both chips start unpressed with identical margins, so toggling never shifts layout.
    _music.TrackChanged += OnTrackChanged;
    _music.VoteCountsChanged += OnVoteCountsChanged;
    _music.OwnVoteChanged += OnOwnVoteChanged;
    AddShowToggleToPauseDialog();
    OnVoteCountsChanged (0, 0);
    _panel.Visible = false; // Nothing to show until the first track starts.
  }

  // Keyboard-only voting (issue #137): the mouse stays captured during play.
  public override void _UnhandledInput (InputEvent @event)
  {
    if (Input.IsActionJustPressed ("music_vote_up")) _music.SubmitVote (isUpVote: true);
    if (Input.IsActionJustPressed ("music_vote_down")) _music.SubmitVote (isUpVote: false);
  }

  private void OnVoteCountsChanged (int upCount, int downCount)
  {
    _upVote.Text = $". ▲ {upCount}";
    _downVote.Text = $", ▼ {downCount}";
  }

  // Own-vote memory (issue #162): the thumb you've cast - now or in any earlier
  // play the server remembers - renders as a clearly pressed amber chip.
  private void OnOwnVoteChanged (int vote)
  {
    ApplyPressedStyle (_upVote, vote == 1);
    ApplyPressedStyle (_downVote, vote == -1);
  }

  private void ApplyPressedStyle (Label label, bool isPressed)
  {
    label.AddThemeStyleboxOverride ("normal", isPressed ? _pressedStyle : _restStyle);
    if (isPressed) label.AddThemeColorOverride ("font_color", new Color (1.0f, 0.92f, 0.55f));
    else label.RemoveThemeColorOverride ("font_color");
  }

  private static StyleBoxFlat ChipStyle (Color background)
  {
    var style = new StyleBoxFlat { BgColor = background };
    style.SetCornerRadiusAll (10);
    style.SetContentMarginAll (8.0f);
    return style;
  }

  private void OnTrackChanged (string title)
  {
    _title.Text = title;
    _panel.Visible = Settings.ShowMusicPlayer;
    ShowVoteHintOnce();
  }

  // Vote-key discoverability (issue #155): a one-time hint when the session's first
  // track starts, since the keys were invisible to new players.
  private void ShowVoteHintOnce()
  {
    if (_hintShown) return;
    _hintShown = true;
    GetNodeOrNull <MessageScroller> ("../MessageScroller")?.AddMessage ("Vote on the music: . up / , down", MessageScroller.MessageImportance.High);
  }

  // The pause (quit) dialog is the only in-game UI with a visible mouse, so the
  // persisted "Show music player" toggle lives there; added in code to keep the
  // Hud scene edits minimal (issue #137).
  private void AddShowToggleToPauseDialog()
  {
    var container = GetNodeOrNull <BoxContainer> ("../QuitDialog/VBoxContainer/HBoxContainer");
    if (container == null) return;
    var toggle = new CheckButton { Text = "Show music player", ButtonPressed = Settings.ShowMusicPlayer };
    toggle.AddThemeFontSizeOverride ("font_size", 40);
    toggle.Toggled += OnShowToggled;
    container.Alignment = BoxContainer.AlignmentMode.Center;
    container.AddChild (toggle);
  }

  private void OnShowToggled (bool isEnabled)
  {
    Settings.ShowMusicPlayer = isEnabled;
    _panel.Visible = isEnabled && _title.Text.Length > 0;
  }
}
