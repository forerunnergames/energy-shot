using com.forerunnergames.energyshot.core.audio;
using com.forerunnergames.energyshot.utilities;
using Godot;

namespace com.forerunnergames.energyshot.ui.hud;

// Mini music player (issue #137): a mostly transparent bottom-right HUD panel
// showing the current song, this play's thumbs up/down counts (with the "." &
// "," key hints) & a small spectrum visualizer. Never captures the mouse; a
// "Show music player" toggle in the pause dialog hides it without stopping music.
public partial class MusicPlayer : Control
{
  private MusicManager _music = null!;
  private Control _panel = null!;
  private Label _title = null!;
  private Label _votes = null!;
  private void OnVoteCountsChanged (int upCount, int downCount) => _votes.Text = $". ▲ {upCount}      , ▼ {downCount}";

  public override void _Ready()
  {
    _music = GetNode <MusicManager> ("/root/World/MusicManager");
    _panel = GetNode <Control> ("Panel");
    _title = GetNode <Label> ("Panel/MarginContainer/VBoxContainer/Title");
    _votes = GetNode <Label> ("Panel/MarginContainer/VBoxContainer/Votes");
    _music.TrackChanged += OnTrackChanged;
    _music.VoteCountsChanged += OnVoteCountsChanged;
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

  private void OnTrackChanged (string title)
  {
    _title.Text = title;
    _panel.Visible = Settings.ShowMusicPlayer;
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
