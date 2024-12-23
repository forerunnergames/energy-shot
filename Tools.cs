using System.Text.RegularExpressions;
using Godot;

namespace energyshot;

public static partial class Tools
{
  // @formatter:off
  [GeneratedRegex (@"^(25[0-5]|2[0-4][0-9]|[0-1]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[0-1]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[0-1]?[0-9][0-9]?)\.(25[0-5]|2[0-4][0-9]|[0-1]?[0-9][0-9]?)$")]
  private static partial Regex ValidIPv4Regex();
  [GeneratedRegex (@"^(([0-9a-fA-F]{1,4}:){7,7}[0-9a-fA-F]{1,4}|([0-9a-fA-F]{1,4}:){1,7}:|([0-9a-fA-F]{1,4}:){1,6}:[0-9a-fA-F]{1,4}|([0-9a-fA-F]{1,4}:){1,5}(:[0-9a-fA-F]{1,4}){1,2}|([0-9a-fA-F]{1,4}:){1,4}(:[0-9a-fA-F]{1,4}){1,3}|([0-9a-fA-F]{1,4}:){1,3}(:[0-9a-fA-F]{1,4}){1,4}|([0-9a-fA-F]{1,4}:){1,2}(:[0-9a-fA-F]{1,4}){1,5}|[0-9a-fA-F]{1,4}:((:[0-9a-fA-F]{1,4}){1,6})|:((:[0-9a-fA-F]{1,4}){1,7}|:)|fe80:(:[0-9a-fA-F]{0,4}){0,4}%[0-9a-zA-Z]{1,}|::(ffff(:0{1,4}){0,1}:){0,1}(([0-9]{1,3}\.){3,3}[0-9]{1,3})|([0-9a-fA-F]{1,4}:){1,4}:(([0-9]{1,3}\.){3,3}[0-9]{1,3}))$")]
  private static partial Regex ValidIPv6Regex();
  [GeneratedRegex (@"^(?!-)[A-Za-z0-9-]{1,63}(?<!-)(\.[A-Za-z]{2,})+$")] private static partial Regex ValidHostnameRegex();
  public static bool IsValidServerAddress (string address) => IsValidIPv4 (address) || IsValidIPv6 (address) || IsValidHostname (address);
  // @formatter:on

  public static (bool success, string address) FindServerAddress (int port)
  {
    var uPnp = new Upnp();
    var discoverResult = (Upnp.UpnpResult)uPnp.Discover();

    if (discoverResult != Upnp.UpnpResult.Success)
    {
      GD.Print ($"UPNP discover failed, error [{discoverResult}]");
      return (false, string.Empty);
    }

    if (uPnp.GetGateway() == null || !uPnp.GetGateway().IsValidGateway())
    {
      GD.Print ("UPNP invalid gateway");
      return (false, string.Empty);
    }

    var mapResult = (Upnp.UpnpResult)uPnp.AddPortMapping (port);

    if (mapResult != Upnp.UpnpResult.Success)
    {
      GD.Print ($"UPNP port mapping failed, error [{mapResult}]");
      return (false, string.Empty);
    }

    var address = uPnp.QueryExternalAddress();
    GD.Print ($"UPNP setup successfully, server address [{address}]");
    return (true, address);
  }

  private static bool IsValidIPv4 (string address)
  {
    var regex = ValidIPv4Regex();
    return regex.IsMatch (address);
  }

  private static bool IsValidIPv6 (string address)
  {
    var regex = ValidIPv6Regex();
    return regex.IsMatch (address);
  }

  private static bool IsValidHostname (string address)
  {
    var regex = ValidHostnameRegex();
    return regex.IsMatch (address);
  }
}
