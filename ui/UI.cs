using com.forerunnergames.energyshot.ui.dialogs;
using com.forerunnergames.energyshot.ui.hud;
using com.forerunnergames.energyshot.ui.menus;
using Godot;

namespace com.forerunnergames.energyshot.ui;

// ReSharper disable once InconsistentNaming
public partial class UI : CanvasLayer
{
  [Signal] public delegate void MessageEventHandler (string message, string excludedPlayerName);
  [Signal] public delegate void HostGameSuccessEventHandler (string playerName);
  [Signal] public delegate void JoinGameSuccessEventHandler (string playerName);
  [Signal] public delegate void GamePausedEventHandler();
  [Signal] public delegate void GameResumedEventHandler();
  [Signal] public delegate void GameQuitEventHandler();
  [Export] public int ServerPort = 55556;
  private ENetMultiplayerPeer _peer = new();
  private MainMenu _mainMenu = null!;
  private Hud _hud = null!;
  private HostGameDialog _hostGameDialog = null!;
  private JoinGameDialog _joinGameDialog = null!;
  private void OnHostGameRequest() => _hostGameDialog.Show (_peer, ServerPort);
  private void OnJoinGameRequest() => _joinGameDialog.Show (_peer, ServerPort);

  public override void _Ready()
  {
    _mainMenu = GetNode <MainMenu> ("MainMenu");
    _hud = GetNode <Hud> ("Hud");
    _hostGameDialog = GetNode <HostGameDialog> ("HostGameDialog");
    _joinGameDialog = GetNode <JoinGameDialog> ("JoinGameDialog");
    _mainMenu.HostGameRequest += OnHostGameRequest;
    _mainMenu.JoinGameRequest += OnJoinGameRequest;
    _hud.Message += (message, excludedPlayerName) => EmitSignal (SignalName.Message, message, excludedPlayerName);
    _hud.GamePaused += () => EmitSignal (SignalName.GamePaused);
    _hud.GameResumed += () => EmitSignal (SignalName.GameResumed);
    _hud.GameQuit += () => EmitSignal (SignalName.GameQuit);
    _hostGameDialog.HostGameSuccess += playerName => EmitSignal (SignalName.HostGameSuccess, playerName);
    _joinGameDialog.JoinGameSuccess += playerName => EmitSignal (SignalName.JoinGameSuccess, playerName);
  }
}
