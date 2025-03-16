using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace com.forerunnergames.energyshot.ui.hud.messages;

public partial class MessageScroller : Control
{
  [Signal] public delegate void OnMessageScrollerExpandedEventHandler();
  [Signal] public delegate void OnMessageScrollerCollapsedEventHandler();
  [Export] public float LowMessageImportanceDisplayTimeSeconds = 0.1f;
  [Export] public float MediumMessageImportanceDisplayTimeSeconds = 1.0f;
  [Export] public float HighMessageImportanceDisplayTimeSeconds = 2.0f;
  [Export] public float CriticalMessageImportanceDisplayTimeSeconds = 3.0f;
  [Export] public float LoadingCompleteMessageImportanceDisplayTimeSeconds = 1.0f;
  [Export] public Color LowMessageImportanceColor = Colors.White;
  [Export] public Color MediumMessageImportanceColor = Colors.White;
  [Export] public Color HighMessageImportanceColor = Colors.LightSkyBlue;
  [Export] public Color CriticalMessageImportanceColor = Colors.IndianRed;
  [Export] public Color LoadingStartMessageImportanceColor = Colors.White;
  [Export] public Color LoadingCompleteMessageImportanceColor = Colors.White;
  [Export] public int MaxMessageHistoryLines = 1000;
  [Export] public int MessageHistoryPercentToRemoveWhenOverMax = 10;
  [Export] public int MaxMessageHistoryContainerHeight = 900;
  private Label _messageLabel1 = null!;
  private Label _messageLabel2 = null!;
  private Label _messageLabel3 = null!;
  private Label _messageLabel4 = null!;
  private TextureButton _expandMessagesButton = null!;
  private TextureButton _collapseMessagesButton = null!;
  private MarginContainer _messageContainer = null!;
  private MarginContainer _messageHistoryContainer = null!;
  private RichTextLabel _messageHistoryLabel = null!;
  private Timer _messageTimer = null!;
  private readonly List <string> _messageHistory = new();
  private readonly ConcurrentQueue <(MessageImportance, string)> _messageQueue = new();
  private Dictionary <MessageImportance, float> _messageImportanceToDisplayTimes = null!;
  private Dictionary <MessageImportance, Color> _messageImportanceToColors = null!;
  private int _messageHistoryLineRemovalAmount;
  private void _OnMessageTimerTimeout() => DisplayNextMessage();
  private bool IsMessageHistoryVisible() => _messageHistoryContainer.Visible;
  private void StartMessageTimer (MessageImportance importance, bool isInstant = false) => _messageTimer.Start (isInstant ? 0.01f : _messageImportanceToDisplayTimes[importance]);

  public enum MessageImportance
  {
    Low,
    Medium,
    High,
    Critical,
    Stop,
    Resume
  }

  public override void _Ready()
  {
    _messageHistoryLineRemovalAmount = Mathf.RoundToInt (MaxMessageHistoryLines / (float)MessageHistoryPercentToRemoveWhenOverMax);

    _messageImportanceToDisplayTimes = new Dictionary <MessageImportance, float>
    {
      { MessageImportance.Low, LowMessageImportanceDisplayTimeSeconds },
      { MessageImportance.Medium, MediumMessageImportanceDisplayTimeSeconds },
      { MessageImportance.High, HighMessageImportanceDisplayTimeSeconds },
      { MessageImportance.Critical, CriticalMessageImportanceDisplayTimeSeconds },
      { MessageImportance.Stop, float.MaxValue }, // Shows message immediately & indefinitely until a 'Resume' message is received
      { MessageImportance.Resume, LoadingCompleteMessageImportanceDisplayTimeSeconds }
    };

    _messageImportanceToColors = new Dictionary <MessageImportance, Color>
    {
      { MessageImportance.Low, LowMessageImportanceColor },
      { MessageImportance.Medium, MediumMessageImportanceColor },
      { MessageImportance.High, HighMessageImportanceColor },
      { MessageImportance.Critical, CriticalMessageImportanceColor },
      { MessageImportance.Stop, LoadingStartMessageImportanceColor },
      { MessageImportance.Resume, LoadingCompleteMessageImportanceColor }
    };

    _messageLabel1 = GetNode <Label> ("MarginContainer/VBoxContainer/Label1");
    _messageLabel2 = GetNode <Label> ("MarginContainer/VBoxContainer/Label2");
    _messageLabel3 = GetNode <Label> ("MarginContainer/VBoxContainer/Label3");
    _messageLabel4 = GetNode <Label> ("MarginContainer/VBoxContainer/Label4");
    _messageContainer = GetNode <MarginContainer> ("MarginContainer");
    _messageHistoryContainer = GetNode <MarginContainer> ("History");
    _messageHistoryLabel = GetNode <RichTextLabel> ("History/VBoxContainer/MarginContainer/RichTextLabel");
    _expandMessagesButton = GetNode <TextureButton> ("MarginContainer/VBoxContainer/Expand/TextureButton");
    _collapseMessagesButton = GetNode <TextureButton> ("History/VBoxContainer/Collapse/TextureButton");
    _messageTimer = GetNode <Timer> ("MessageTimer");
    _expandMessagesButton.Pressed += _OnExpandMessagesButtonPressed;
    _collapseMessagesButton.Pressed += _OnCollapseMessagesButtonPressed;
    _messageTimer.Timeout += _OnMessageTimerTimeout;
    Reset();
  }

  public void AddMessage (string message, MessageImportance importance = MessageImportance.Medium)
  {
    GD.Print ($"Message: {message}");

    if (importance is MessageImportance.Stop or MessageImportance.Resume)
    {
      _messageTimer.Stop();
      DisplayMessage (importance, message);
      return;
    }

    _messageQueue.Enqueue ((importance, message));

    if (!_messageTimer.IsStopped()) return;
    StartMessageTimer (importance, isInstant: _messageQueue.Count == 1);
  }

  public void Reset()
  {
    ClearMessages();
    HideMessageHistory();
  }

  private void _OnExpandMessagesButtonPressed()
  {
    ShowMessageHistory();
    EmitSignal (SignalName.OnMessageScrollerExpanded);
  }

  private void _OnCollapseMessagesButtonPressed()
  {
    HideMessageHistory();
    EmitSignal (SignalName.OnMessageScrollerCollapsed);
  }

  private void DisplayNextMessage()
  {
    if (_messageQueue.Count == 0) return;
    _messageQueue.TryDequeue (out var messageItem);
    DisplayMessage (messageItem.Item1, messageItem.Item2);
  }

  private void DisplayMessage (MessageImportance importance, string unsplitMessage)
  {
    if (string.IsNullOrWhiteSpace (unsplitMessage)) return;
    var lines = unsplitMessage.Split ('\n');
    _ = lines.Select ((line, i) => DisplaySingleLineMessage (importance, line, isInstant: i < lines.Length - 1)).ToList();
  }

  private bool DisplaySingleLineMessage (MessageImportance importance, string singleLineMessage, bool isInstant = false)
  {
    if (string.IsNullOrWhiteSpace (singleLineMessage)) return false;
    ShiftMessagesUp();
    UpdateBottomMessage (_messageImportanceToColors[importance], singleLineMessage);
    ModulateMessages();
    AddMessageToHistory (singleLineMessage);
    StartMessageTimer (importance, isInstant);
    return true;
  }

  private void UpdateBottomMessage (Color color, string singleLineMessage)
  {
    _messageLabel4.Text = singleLineMessage;
    _messageLabel4.AddThemeColorOverride ("font_color", color);
  }

  private void ShiftMessagesUp()
  {
    _messageLabel1.Text = _messageLabel2.Text;
    _messageLabel2.Text = _messageLabel3.Text;
    _messageLabel3.Text = _messageLabel4.Text;
    _messageLabel1.AddThemeColorOverride ("font_color", _messageLabel2.GetThemeColor ("font_color"));
    _messageLabel2.AddThemeColorOverride ("font_color", _messageLabel3.GetThemeColor ("font_color"));
    _messageLabel3.AddThemeColorOverride ("font_color", _messageLabel4.GetThemeColor ("font_color"));
    _messageLabel4.RemoveThemeColorOverride ("font_color");
    _messageLabel4.Text = string.Empty;
  }

  private void ModulateMessages()
  {
    var tween = CreateTween().SetParallel();
    tween.TweenProperty (_messageLabel1, "modulate:a", 0.4f, 0.5f).From (0.6f);
    tween.TweenProperty (_messageLabel2, "modulate:a", 0.6f, 0.5f).From (0.8f);
    tween.TweenProperty (_messageLabel3, "modulate:a", 0.8f, 0.5f).From (1.0f);
  }

  private void AddMessageToHistory (string singleLineMessage)
  {
    if (_messageHistory.Count >= MaxMessageHistoryLines) _messageHistory.RemoveRange (0, _messageHistoryLineRemovalAmount);
    _messageHistory.Add (singleLineMessage);
    if (!IsMessageHistoryVisible()) return;
    UpdateMessageHistory();
  }

  private void ClearMessages()
  {
    // @formatter:off
    _messageLabel1.Text = string.Empty;
    _messageLabel2.Text = string.Empty;
    _messageLabel3.Text = string.Empty;
    _messageLabel4.Text = string.Empty;
    while (_messageQueue.TryDequeue (out _)) { }
    ClearMessageHistory();
    // @formatter:on
  }

  private void UpdateMessageHistory()
  {
    _messageHistoryLabel.Text = $"[center]{string.Join ("\n", _messageHistory)}[/center]";
    _messageHistoryLabel.CustomMinimumSize = new Vector2 (_messageHistoryLabel.CustomMinimumSize.X, Mathf.Min (MaxMessageHistoryContainerHeight, _messageHistoryLabel.GetContentHeight()));
  }

  private void ShowMessageHistory()
  {
    UpdateMessageHistory();
    _messageContainer.Hide();
    _messageHistoryContainer.Show();
  }

  private void HideMessageHistory()
  {
    _messageHistoryContainer.Hide();
    _messageContainer.Show();
  }

  private void ClearMessageHistory()
  {
    _messageHistory.Clear();
    UpdateMessageHistory();
  }
}
