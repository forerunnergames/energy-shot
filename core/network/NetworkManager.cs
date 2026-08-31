using System;
using System.Linq;
using Godot;

namespace com.forerunnergames.energyshot.utilities;

public partial class NetworkManager : Node
{
  // @formatter:off
  public event Action <string>? RemoteMessageReceived;
  public event Action <string>? AdminMessageReceived;
  public event Action <string, string>? PlayerRespawnedShot;
  public event Action <string>? PlayerRespawnedFell;
  public event Action <string>? PlayerJoinGame;
  public event Action <string>? PlayerLeftGame;
  private int LocalNetworkId => Multiplayer.GetUniqueId();
  private bool IsServer => Multiplayer.IsServer();
  [Rpc] private void OnRemoteMessageReceived (string message) => RemoteMessageReceived?.Invoke (message);
  [Rpc] private void OnRemotePlayerJoinGame (string playerName) => PlayerJoinGame?.Invoke (playerName);
  [Rpc] private void OnRemotePlayerLeftGame (string playerName) => PlayerLeftGame?.Invoke (playerName);
  [Rpc] private void OnRemotePlayerRespawnedFell (string playerName) => PlayerRespawnedFell?.Invoke (playerName);
  [Rpc] private void OnRemotePlayerRespawnedShot (string playerName, string shotByPlayerName) => PlayerRespawnedShot?.Invoke (playerName, shotByPlayerName);
  public void NotifyMessage (string message, int excludingId) => Broadcast (excludingId1: LocalNetworkId, excludingId2: excludingId, nameof (OnMessageReceived), message, excludingId);
  private void SendToServer (string method, params Variant[] args) => RpcId (1, method, args);
  private void SendToAllClients (string method, params Variant[] args) => Multiplayer.GetPeers().ToList().ForEach (id => RpcId (id, method, args));
  private void SendToAllClientsExcept (int excludingId, string method, params Variant[] args) => Multiplayer.GetPeers().Where (id => id != excludingId).ToList().ForEach (id => RpcId (id, method, args));
  private void SendToAllClientsExcept (int excludingId1, int excludingId2, string method, params Variant[] args) => Multiplayer.GetPeers().Where (id => id != excludingId1 && id != excludingId2).ToList().ForEach (id => RpcId (id, method, args));
  // @formatter:on

  // Admin announcements (issue #158): server-only broadcast, shown on every peer
  // including the host. Same text everywhere (#53). The server guard runs before
  // local delivery, so a misused call on a client can neither display nor send.
  public void NotifyAdminMessage (string message)
  {
    if (!IsServer) return;
    AdminMessageReceived?.Invoke (message);
    SendToAllClients (nameof (OnAdminMessageReceived), message);
  }

  // Version line for a single joining peer (issue #158): targeted, not a broadcast,
  // so it doubles as version info without spamming everyone on every join.
  public void SendAdminMessageTo (int peerId, string message)
  {
    if (!IsServer) return;
    RpcId (peerId, nameof (OnAdminMessageReceived), message);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void OnAdminMessageReceived (string message)
  {
    // Admin messages are only honored from peer 1, the server (issue #158);
    // a forged send from any other peer is dropped.
    if (Multiplayer.GetRemoteSenderId() != 1) return;
    AdminMessageReceived?.Invoke (message);
  }

  public void NotifyPlayerJoinGame (string playerName)
  {
    PlayerJoinGame?.Invoke (playerName);
    Broadcast (nameof (OnPlayerJoinGame), playerName);
  }

  public void NotifyPlayerLeftGame (string playerName)
  {
    PlayerLeftGame?.Invoke (playerName);
    Broadcast (nameof (OnPlayerLeftGame), playerName);
  }

  public void NotifyPlayerRespawnedShot (string playerName, string shotByPlayerName)
  {
    PlayerRespawnedShot?.Invoke (playerName, shotByPlayerName);
    Broadcast (excludingId: LocalNetworkId, nameof (OnPlayerRespawnedShot), playerName, shotByPlayerName);
  }

  public void NotifyPlayerRespawnedFell (string playerName)
  {
    PlayerRespawnedFell?.Invoke (playerName);
    Broadcast (excludingId: LocalNetworkId, nameof (OnPlayerRespawnedFell), playerName);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void OnMessageReceived (string message, int excludingId)
  {
    var senderId = Multiplayer.GetRemoteSenderId();
    if (LocalNetworkId != senderId && LocalNetworkId != excludingId) RemoteMessageReceived?.Invoke (message);
    if (!IsServer) return;
    Broadcast (excludingId1: senderId, excludingId2: excludingId, nameof (OnRemoteMessageReceived), message);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void OnPlayerJoinGame (string playerName)
  {
    var senderId = Multiplayer.GetRemoteSenderId();
    if (LocalNetworkId != senderId) PlayerJoinGame?.Invoke (playerName);
    if (!IsServer) return;
    Broadcast (nameof (OnRemotePlayerJoinGame), playerName);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void OnPlayerLeftGame (string playerName)
  {
    var senderId = Multiplayer.GetRemoteSenderId();
    if (LocalNetworkId != senderId) PlayerLeftGame?.Invoke (playerName);
    if (!IsServer) return;
    Broadcast (nameof (OnRemotePlayerLeftGame), playerName);
  }

  // World supplies the name-to-peer check; every peer holds the replicated roster.
  public System.Func <string, int, bool>? IsVictimOwnedBy;

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void OnPlayerRespawnedShot (string playerName, string shotByPlayerName)
  {
    var senderId = Multiplayer.GetRemoteSenderId();
    // Only the victim's own peer may report its zap-out (CodeRabbit on #431): the
    // hill-clear bounty made this notification a SCORING input, so a forged victim
    // name could bank points per message. Deaths are victim-authoritative (#111) -
    // a report about somebody else's player is a forgery, dropped on every peer.
    if (IsVictimOwnedBy != null && !IsVictimOwnedBy (playerName, senderId))
    {
      ServerLog.Event (senderId, $"death notification deny: [{playerName}] is not the sender's player");
      return;
    }
    if (LocalNetworkId != senderId) PlayerRespawnedShot?.Invoke (playerName, shotByPlayerName);
    if (!IsServer) return;
    Broadcast (excludingId: senderId, nameof (OnRemotePlayerRespawnedShot), playerName, shotByPlayerName);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void OnPlayerRespawnedFell (string playerName)
  {
    var senderId = Multiplayer.GetRemoteSenderId();
    if (LocalNetworkId != senderId) PlayerRespawnedFell?.Invoke (playerName);
    if (!IsServer) return;
    Broadcast (excludingId: senderId, nameof (OnRemotePlayerRespawnedFell), playerName);
  }

  private void Broadcast (string method, params Variant[] args)
  {
    if (IsServer)
    {
      SendToAllClients (method, args);
      return;
    }

    SendToServer (method, args);
  }

  private void Broadcast (int excludingId, string method, params Variant[] args)
  {
    if (IsServer)
    {
      SendToAllClientsExcept (excludingId, method, args);
      return;
    }

    SendToServer (method, args);
  }

  private void Broadcast (int excludingId1, int excludingId2, string method, params Variant[] args)
  {
    if (IsServer)
    {
      SendToAllClientsExcept (excludingId1, excludingId2, method, args);
      return;
    }

    SendToServer (method, args);
  }
}
