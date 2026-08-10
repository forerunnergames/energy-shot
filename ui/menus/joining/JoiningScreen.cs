using com.forerunnergames.energyshot.core.world;
using Godot;

namespace com.forerunnergames.energyshot.ui.menus;

// Animated overlay for the whole join flow (issue #91): connect, password check, &
// spawn. Pulsing "Joining <address>..." text with cycling dots, plus a Cancel button
// that aborts the attempt & returns to the menu. Hides itself when the game starts
// (HUD shows via NewGameStarted) or on kick/shutdown, where errors surface as before.
public partial class JoiningScreen : Control
{
  [Signal] public delegate void CancelPressedEventHandler();
  private const float PulseSeconds = 0.6f;
  private const float PulseMinAlpha = 0.35f;
  private World _world = null!;
  private Label _joiningText = null!;
  private Button _cancelButton = null!;
  private Timer _dotsTimer = null!;
  private Tween? _pulse;
  private string _address = string.Empty;
  private int _dots;
  private void UpdateJoiningText() => _joiningText.Text = $"Joining {_address}{new string ('.', _dots)}";

  public override void _Ready()
  {
    _world = GetNode <World> ("/root/World");
    _joiningText = GetNode <Label> ("PanelContainer/MarginContainer/VBoxContainer/JoiningText");
    _cancelButton = GetNode <Button> ("PanelContainer/MarginContainer/VBoxContainer/Buttons/VBoxContainer/CancelButton");
    _dotsTimer = GetNode <Timer> ("DotsTimer");
    _dotsTimer.Timeout += OnDotsTimerTimeout;
    _cancelButton.Pressed += OnCancelButtonPressed;
    _world.NewGameStarted += (_, _) => Close();
    _world.KickedFromServer += _ => Close();
    _world.ServerShutDown += Close;
  }

  public void Open (string address)
  {
    _address = address;
    _dots = 3;
    UpdateJoiningText();
    _dotsTimer.Start();
    StartPulse();
    Show();
  }

  public void Close()
  {
    if (!Visible) return;
    _dotsTimer.Stop();
    StopPulse();
    Hide();
  }

  private void OnCancelButtonPressed()
  {
    Close();
    EmitSignal (SignalName.CancelPressed);
  }

  private void OnDotsTimerTimeout()
  {
    _dots = (_dots + 1) % 4;
    UpdateJoiningText();
  }

  private void StartPulse()
  {
    StopPulse();
    _pulse = CreateTween().SetLoops();
    _pulse.TweenProperty (_joiningText, "modulate:a", PulseMinAlpha, PulseSeconds).SetTrans (Tween.TransitionType.Sine).SetEase (Tween.EaseType.InOut);
    _pulse.TweenProperty (_joiningText, "modulate:a", 1.0f, PulseSeconds).SetTrans (Tween.TransitionType.Sine).SetEase (Tween.EaseType.InOut);
  }

  private void StopPulse()
  {
    _pulse?.Kill();
    _pulse = null;
    _joiningText.Modulate = Colors.White;
  }
}
