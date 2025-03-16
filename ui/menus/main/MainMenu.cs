using com.forerunnergames.energyshot.core.world;
using Godot;

namespace com.forerunnergames.energyshot.ui.menus;

public partial class MainMenu : Control
{
  [Signal] public delegate void HostGameRequestEventHandler();
  [Signal] public delegate void JoinGameRequestEventHandler();
  private World _world = null!;
  private Button _hostButton = null!;
  private Button _joinButton = null!;
  private Button _quitButton = null!;
  private Label _bottomMainMenuText = null!;
  private void OnQuitButtonPressed() => QuitGame();
  private void QuitGame() => GetTree().Quit();

  public override void _Ready()
  {
    _world = GetNode <World> ("/root/World");
    _hostButton = GetNode <Button> ("PanelContainer/MarginContainer/VBoxContainer/Buttons/VBoxContainer/HostButton");
    _joinButton = GetNode <Button> ("PanelContainer/MarginContainer/VBoxContainer/Buttons/VBoxContainer/JoinButton");
    _quitButton = GetNode <Button> ("PanelContainer/MarginContainer/VBoxContainer/Buttons/VBoxContainer/Quit");
    _bottomMainMenuText = GetNode <Label> ("PanelContainer/MarginContainer/VBoxContainer/BottomText");
    _hostButton.Pressed += () => EmitSignal (SignalName.HostGameRequest);
    _joinButton.Pressed += () => EmitSignal (SignalName.JoinGameRequest);
    _quitButton.Pressed += QuitGame;
    _world.NewGameStarted += OnNewGameStarted;
    _world.KickedFromServer += OnKickedFromServer;
    _bottomMainMenuText.Text = string.Empty;
  }

  private void OnNewGameStarted (string selfPlayerName)
  {
    Hide();
    _bottomMainMenuText.Text = string.Empty;
  }

  private void OnKickedFromServer (string reason)
  {
    GD.Print ("Server disconnected");
    _bottomMainMenuText.Text = $"You were kicked from the server, reason: {reason}";
    Input.MouseMode = Input.MouseModeEnum.Visible;
    Show();
  }
}
