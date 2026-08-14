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

  // First-time players get the official dedicated server pre-filled. A hostname
  // instead of a raw IP (issue #138): ENet resolves it, & the client survives any
  // future server IP change.
  public const string OfficialServerAddress = "es1.forerunner.games";

  public static string LastJoinAddress
  {
    get
    {
      var address = Get ("last_join_address");
      return string.IsNullOrEmpty (address) ? OfficialServerAddress : address;
    }
    set => Set ("last_join_address", value);
  }

  public static int Difficulty
  {
    get => GetInt ("difficulty");
    set => Set ("difficulty", value);
  }

  // Selected body color (issue #43): an index into PlayerColors, remembered like the name.
  public static int PlayerColor
  {
    get => GetInt ("player_color");
    set => Set ("player_color", value);
  }

  // Host-chosen player cap (issue #73), remembered like difficulty.
  public static int MaxPlayers
  {
    get => Mathf.Clamp (GetInt ("max_players", core.world.World.MaxPlayers), 2, core.world.World.MaxPlayers);
    set => Set ("max_players", value);
  }

  // Game passwords (issue #90), remembered like the other dialog fields.
  public static string HostPassword
  {
    get => Get ("host_password");
    set => Set ("host_password", value);
  }

  public static string LastJoinPassword
  {
    get => Get ("last_join_password");
    set => Set ("last_join_password", value);
  }

  // Third-person chase view (issue #119), remembered so the chosen view survives restarts.
  public static bool ThirdPersonView
  {
    get => GetBool ("third_person_view", false);
    set => Set ("third_person_view", value);
  }

  // Crouch as hold-while-pressed instead of the default toggle (issue #147).
  public static bool HoldToCrouch
  {
    get => GetBool ("hold_to_crouch", false);
    set => Set ("hold_to_crouch", value);
  }

  // Mini music player visibility (issue #137): hiding it never stops the music.
  public static bool ShowMusicPlayer
  {
    get => GetBool ("show_music_player", true);
    set => Set ("show_music_player", value);
  }

  private static string Get (string key)
  {
    var config = new ConfigFile();
    config.Load (FilePath);
    return (string)config.GetValue (Section, key, string.Empty);
  }

  private static int GetInt (string key, int defaultValue = 0)
  {
    var config = new ConfigFile();
    config.Load (FilePath);
    return (int)config.GetValue (Section, key, defaultValue);
  }

  private static bool GetBool (string key, bool defaultValue)
  {
    var config = new ConfigFile();
    config.Load (FilePath);
    return (bool)config.GetValue (Section, key, defaultValue);
  }

  private static void Set (string key, Variant value)
  {
    var config = new ConfigFile();
    config.Load (FilePath);
    config.SetValue (Section, key, value);
    config.Save (FilePath);
  }
}
