using Godot;

namespace com.forerunnergames.energyshot.utilities;

// Always-on server-side event log (issue #111): journald captures the official
// server's stdout, so consistently prefixed GD.Print lines with a timestamp & the
// relevant peer id are enough for live debugging. Client-side logging is unchanged.
public static class ServerLog
{
  public static void Event (long peerId, string message) => GD.Print ($"{Prefix()} [peer {peerId}] {message}");
  public static void Event (string message) => GD.Print ($"{Prefix()} {message}");
  private static string Prefix() => $"[{Time.GetDatetimeStringFromSystem (utc: true)}] Server:";
}
