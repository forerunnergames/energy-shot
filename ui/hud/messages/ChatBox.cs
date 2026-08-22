using System.Collections.Generic;
using Godot;

namespace com.forerunnergames.energyshot.ui.hud.messages;

// Player chat (issue #188): a dedicated bottom-left box that shows ONLY chat lines -
// "Name: message" in the sender's color - each lingering ~9s then gone, invisible when
// empty, so talk never drowns in the auto-message feed. T opens the input line &
// deliberately takes the keyboard (the Hud disables player input for the duration);
// Enter sends, Esc cancels, the mouse stays captured throughout.
public partial class ChatBox : VBoxContainer
{
  [Signal] public delegate void ChatSubmittedEventHandler (string text);
  [Signal] public delegate void OpenedEventHandler();
  [Signal] public delegate void ClosedEventHandler();
  public const int MaxChars = 120;
  private const float LingerSeconds = 9.0f;
  private const int MaxLines = 6;
  private const float WidthPixels = 520.0f;
  private readonly List <(string Bbcode, ulong ExpiresAtMs)> _lines = new();
  private RichTextLabel _label = null!;
  private LineEdit _input = null!;

  public bool IsOpen => _input.Visible;

  public override void _Ready()
  {
    MouseFilter = MouseFilterEnum.Ignore;
    CustomMinimumSize = new Vector2 (WidthPixels, 0.0f);
    _label = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2 (WidthPixels, 0.0f) };
    _input = new LineEdit { Visible = false, MaxLength = MaxChars, PlaceholderText = "Say something (Enter sends, Esc cancels)", CustomMinimumSize = new Vector2 (WidthPixels, 0.0f) };
    _input.TextSubmitted += OnSubmitted;
    _input.GuiInput += OnInputLineEvent;
    AddChild (_label);
    AddChild (_input);
    Visible = false;
  }

  public override void _Process (double delta)
  {
    var now = Time.GetTicksMsec();
    var pruned = _lines.RemoveAll (line => line.ExpiresAtMs <= now);
    if (pruned > 0) Render();
    Visible = _lines.Count > 0 || IsOpen;
  }

  public void Open()
  {
    if (IsOpen) return;
    _input.Clear();
    _input.Visible = true;
    // The T-does-nothing bug: this container starts hidden when no lines linger, & a
    // hidden container can't hand focus to its child - the box opened invisibly &
    // silently ate the keyboard. Show FIRST, then grab focus a frame later, once
    // visibility has propagated.
    Visible = true;
    _input.CallDeferred (Control.MethodName.GrabFocus);
    EmitSignal (SignalName.Opened);
  }

  // Playtest surface: the phase asserts what a player actually experiences.
  public bool InputFocused => _input.HasFocus();
  public string VisibleText => _label.Text;
  public string InputText { get => _input.Text; set => _input.Text = value; }

  public void Close()
  {
    if (!IsOpen) return;
    _input.ReleaseFocus();
    _input.Visible = false;
    EmitSignal (SignalName.Closed);
  }

  // colorHex is the sender's lightened palette color (PlayerColors.TextHex, issue #43).
  public void AddLine (string senderName, string colorHex, string text)
  {
    _lines.Add (($"[color=#{colorHex}]{EscapeBbcode (senderName)}:[/color] {EscapeBbcode (text)}", Time.GetTicksMsec() + (ulong)(LingerSeconds * 1000.0f)));
    if (_lines.Count > MaxLines) _lines.RemoveAt (0);
    Render();
  }

  // Chat text is player-typed & lands in a BBCode label, so an opening bracket must
  // render as itself - never as a tag somebody smuggled in. Pure & unit-tested.
  public static string EscapeBbcode (string text) => text.Replace ("[", "[lb]");

  private void Render() => _label.Text = string.Join ("\n", _lines.ConvertAll (line => line.Bbcode));

  private void OnSubmitted (string text)
  {
    Close();
    if (text.Trim().Length == 0) return;
    EmitSignal (SignalName.ChatSubmitted, text);
  }

  // Esc cancels the line instead of reaching the quit dialog (issue #188).
  private void OnInputLineEvent (InputEvent @event)
  {
    if (!@event.IsActionPressed ("quit")) return;
    Close();
    GetViewport().SetInputAsHandled();
  }
}
