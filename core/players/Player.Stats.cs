using Godot;

namespace com.forerunnergames.energyshot.players;

// Round stats (issue #153): alongside Score, each player owns & replicates its own
// zap-outs, falls, & assists, the way Score already works - the dying or scoring
// player's authority writes, everyone reads for the scoreboard. An assist is damage
// dealt to the victim within AssistWindowSeconds before somebody ELSE zapped them.
public partial class Player
{
  [Export] public int ZapOuts { get; set; }
  [Export] public int Falls { get; set; }
  [Export] public int Assists { get; set; }
  [Export] public float AssistWindowSeconds = 10.0f;
  private int _lastDamagerId;
  private ulong _lastDamagerAtMs;

  // Victim-side bookkeeping from ApplyDamageFrom: the most recent non-lethal damager.
  private void RememberDamager (int attackerId)
  {
    if (attackerId == 0) return; // Ownerless hazards (a landmine) assist nobody.
    _lastDamagerId = attackerId;
    _lastDamagerAtMs = Time.GetTicksMsec();
  }

  // Called from the lethal branch: credit the previous damager, if fresh & not the zapper.
  private void CreditAssist (int zapperId)
  {
    if (_lastDamagerId == 0 || _lastDamagerId == zapperId) return;
    if (Time.GetTicksMsec() - _lastDamagerAtMs > (ulong)(AssistWindowSeconds * 1000.0f)) return;
    GetParent().GetNodeOrNull <Player> ($"{_lastDamagerId}")?.RpcId (_lastDamagerId, MethodName.NotifyAssisted, DisplayName);
  }

  private void ForgetDamager() => _lastDamagerId = 0;

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void NotifyAssisted (string zappedPlayerName)
  {
    if (!IsMultiplayerAuthority()) return;
    ++Assists;
    GD.Print ($"{DisplayName}: assist on {zappedPlayerName} ({Assists} total)");
  }

  // A new round (issue #153): the server tells every player to zero its counters &
  // respawn fresh. Peer-1-only, the admin-message rule; a direct (non-RPC) call is
  // honored only inside the server process - the host resetting its own player
  // (CodeRabbit on #226).
  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  public void ResetForNewRound()
  {
    var sender = Multiplayer.GetRemoteSenderId();
    var authorized = sender == 1 || (sender == 0 && Multiplayer.IsServer());
    if (!authorized) return;
    if (!IsMultiplayerAuthority()) return;
    Score = 0;
    ZapOuts = 0;
    Falls = 0;
    Assists = 0;
    ForgetDamager();
    if (!Fallen) Respawn();
  }
}
