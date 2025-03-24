using System;
using System.Linq;
using Godot;

namespace com.forerunnergames.energyshot.utilities;

public partial class NetworkManager : Node
{
  // @formatter:off
  public event Action <string, string>? PlayerRespawnedShot;
  public event Action <string>? PlayerRespawnedFell;
  public event Action <string>? RemoteMessageReceived;
  private int LocalNetworkId => Multiplayer.GetUniqueId();
  private bool IsServer => Multiplayer.IsServer();
  [Rpc] private void OnRemoteMessageReceived (string message) => RemoteMessageReceived?.Invoke (message);
  [Rpc] private void OnRemotePlayerRespawnedFell (string playerName) => PlayerRespawnedFell?.Invoke (playerName);
  [Rpc] private void OnRemotePlayerRespawnedShot (string playerName, string shotByPlayerName) => PlayerRespawnedShot?.Invoke (playerName, shotByPlayerName);
  public void NotifyMessage (string message, int excludingId) => Broadcast (excludingId1: LocalNetworkId, excludingId2: excludingId, nameof (OnMessageReceived), message, excludingId);
  private void SendToServer (string method, params Variant[] args) => RpcId (1, method, args);
  private void SendToClientsExcept (int excludingId, string method, params Variant[] args) => Multiplayer.GetPeers().Where (id => id != excludingId).ToList().ForEach (id => RpcId (id, method, args));
  private void SendToClientsExcept (int excludingId1, int excludingId2, string method, params Variant[] args) => Multiplayer.GetPeers().Where (id => id != excludingId1 && id != excludingId2).ToList().ForEach (id => RpcId (id, method, args));
  // @formatter:on

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
  private void OnPlayerRespawnedShot (string playerName, string shotByPlayerName)
  {
    var senderId = Multiplayer.GetRemoteSenderId();
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

  private void Broadcast (int excludingId, string method, params Variant[] args)
  {
    if (IsServer)
    {
      SendToClientsExcept (excludingId, method, args);
      return;
    }

    SendToServer (method, args);
  }

  private void Broadcast (int excludingId1, int excludingId2, string method, params Variant[] args)
  {
    if (IsServer)
    {
      SendToClientsExcept (excludingId1, excludingId2, method, args);
      return;
    }

    SendToServer (method, args);
  }
}
