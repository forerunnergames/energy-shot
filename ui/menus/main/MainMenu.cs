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
#if GODOT_WINDOWS
    // The Windows fullscreen fix (issue #302), ported from another Forerunner title:
    // Windows needs the mode FORCED at boot - ugly but proven; without it the window
    // comes up in a state players can't play in.
    DisplayServer.WindowSetMode (DisplayServer.WindowMode.ExclusiveFullscreen);
#endif
    _world = GetNode <World> ("/root/World");
    // Jonathan's redesign (issue #436) flattened the tree: art + wedge shader behind,
    // absolutely-placed controls in the 4K design space in front.
    _hostButton = GetNode <Button> ("HostButton");
    _joinButton = GetNode <Button> ("JoinButton");
    _quitButton = GetNode <Button> ("Quit");
    _bottomMainMenuText = GetNode <Label> ("BottomText");
    _hostButton.Pressed += () => EmitSignal (SignalName.HostGameRequest);
    _joinButton.Pressed += () => EmitSignal (SignalName.JoinGameRequest);
    _quitButton.Pressed += QuitGame;
    _world.NewGameStarted += OnNewGameStarted;
    _world.KickedFromServer += OnKickedFromServer;
    _world.ServerShutDown += OnServerShutDown;
    _world.LeftGame += OnLeftGame;
    _bottomMainMenuText.Text = string.Empty;
    _hostButton.GrabFocus(); // Keyboard & controller nav from boot (CodeRabbit on #437).
  }

  private void OnNewGameStarted (string selfPlayerName, int selfMaxHealth)
  {
    Hide();
    _bottomMainMenuText.Text = string.Empty;
  }

  // A voluntary exit is not a failure: no red text, just the menu (Aaron, 2026-08-24).
  private void OnLeftGame()
  {
    _bottomMainMenuText.Text = string.Empty;
    Input.MouseMode = Input.MouseModeEnum.Visible;
    Show();
    _hostButton.GrabFocus(); // Reopened menus stay navigable (CodeRabbit on #437).
  }

  private void OnServerShutDown()
  {
    GD.Print ("Server shut down");
    _bottomMainMenuText.Text = "The server was shut down.";
    Input.MouseMode = Input.MouseModeEnum.Visible;
    Show();
    _hostButton.GrabFocus(); // Reopened menus stay navigable (CodeRabbit on #437).
  }

  private void OnKickedFromServer (string reason)
  {
    GD.Print ("Server disconnected");
    _bottomMainMenuText.Text = $"You were kicked from the server, reason: {reason}";
    Input.MouseMode = Input.MouseModeEnum.Visible;
    Show();
    _hostButton.GrabFocus(); // Reopened menus stay navigable (CodeRabbit on #437).
  }
}
