using System.Text.RegularExpressions;
using Godot;

namespace com.forerunnergames.energyshot;

public static partial class Tools
{
  // @formatter:off
  public static bool IsValidServerAddress (string address) => IsValidIPv4 (address) || IsValidIPv6 (address) || IsValidHostname (address);
  public static bool IsValidPlayerName (string name) => ValidPlayerNameRegex().IsMatch (name);
  [GeneratedRegex (@"^(25[0-5]|2[0-4][0-9]|[0-1]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[0-1]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[0-1]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[0-1]?[0-9][0-9]?)$")]
  private static partial Regex ValidIPv4Regex();
  [GeneratedRegex (@"^(([0-9a-fA-F]{1,4}:){7,7}[0-9a-fA-F]{1,4}|([0-9a-fA-F]{1,4}:){1,7}:|([0-9a-fA-F]{1,4}:){1,6}:[0-9a-fA-F]{1,4}|([0-9a-fA-F]{1,4}:){1,5}(:[0-9a-fA-F]{1,4}){1,2}|([0-9a-fA-F]{1,4}:){1,4}(:[0-9a-fA-F]{1,4}){1,3}|([0-9a-fA-F]{1,4}:){1,3}(:[0-9a-fA-F]{1,4}){1,4}|([0-9a-fA-F]{1,4}:){1,2}(:[0-9a-fA-F]{1,4}){1,5}|[0-9a-fA-F]{1,4}:((:[0-9a-fA-F]{1,4}){1,6})|:((:[0-9a-fA-F]{1,4}){1,7}|:)|fe80:(:[0-9a-fA-F]{0,4}){0,4}%[0-9a-zA-Z]{1,}|::(ffff(:0{1,4}){0,1}:){0,1}(([0-9]{1,3}\.){3,3}[0-9]{1,3})|([0-9a-fA-F]{1,4}:){1,4}:(([0-9]{1,3}\.){3,3}[0-9]{1,3}))$")]
  private static partial Regex ValidIPv6Regex();
  [GeneratedRegex (@"^(?!-)[A-Za-z0-9-]{1,63}(?<!-)(\.[A-Za-z]{2,})+$")]
  private static partial Regex ValidHostnameRegex();
  [GeneratedRegex ("^[A-Za-z0-9]{1,16}$")]
  private static partial Regex ValidPlayerNameRegex();
  private static bool IsValidIPv4 (string address) => ValidIPv4Regex().IsMatch (address);
  private static bool IsValidIPv6 (string address) => ValidIPv6Regex().IsMatch (address);
  private static bool IsValidHostname (string address) => ValidHostnameRegex().IsMatch (address);
  // @formatter:on

  public static (bool success, string address, string error) FindServerAddress (int port)
  {
    var uPnp = new Upnp();
    var discoverResult = (Upnp.UpnpResult)uPnp.Discover();

    if (discoverResult != Upnp.UpnpResult.Success)
    {
      var error = $"UPNP discovery failed, error [{discoverResult}]";
      GD.Print (error);
      return (false, string.Empty, error);
    }

    if (uPnp.GetGateway() == null || !uPnp.GetGateway().IsValidGateway())
    {
      const string error = "UPNP invalid gateway";
      GD.Print (error);
      return (false, string.Empty, error);
    }

    var removeMapResult = (Upnp.UpnpResult)uPnp.DeletePortMapping (port);

    GD.Print (removeMapResult == Upnp.UpnpResult.Success
      ? $"UPNP successfully removed existing port mapping on port [{port}]"
      : $"No existing port mapping to remove on port [{port}], or an error occurred: [{removeMapResult}]");

    var mapResult = (Upnp.UpnpResult)uPnp.AddPortMapping (port);

    if (mapResult != Upnp.UpnpResult.Success)
    {
      var error = $"UPNP port mapping failed, error [{mapResult}]";
      GD.Print (error);
      return (false, string.Empty, error);
    }

    var address = uPnp.QueryExternalAddress();
    GD.Print ($"UPNP setup successfully, server address [{address}]");
    return (true, address, string.Empty);
  }
}
