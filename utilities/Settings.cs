using Godot;

namespace com.forerunnergames.energyshot.utilities;

// Persists small player preferences (name, last joined server) to user://settings.cfg
// so host/join dialogs come pre-filled & players can rejoin with two clicks.
public static class Settings
{
  private const string FilePath = "user://settings.cfg";
  private const string Section = "player";

  public static string PlayerName
  {
    get => Get ("name");
    set => Set ("name", value);
  }

  public static string LastJoinAddress
  {
    get => Get ("last_join_address");
    set => Set ("last_join_address", value);
  }

  private static string Get (string key)
  {
    var config = new ConfigFile();
    config.Load (FilePath);
    return (string)config.GetValue (Section, key, string.Empty);
  }

  private static void Set (string key, string value)
  {
    var config = new ConfigFile();
    config.Load (FilePath);
    config.SetValue (Section, key, value);
    config.Save (FilePath);
  }
}
