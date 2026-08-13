using Godot;

namespace com.forerunnergames.energyshot.utilities;

// Server-operator announcement channel (issue #158): with --admin-message-file
// <path>, the server polls the file once a second & hands any new non-empty
// content to the broadcast callback, then truncates the file - so the same text
// can be re-sent later & nothing re-broadcasts on restart. Sending a message
// from SSH is one line: echo "Back in a minute!" > /path/to/admin-message
public partial class AdminMessageFileWatcher : Node
{
  private readonly string _path;
  private readonly System.Action <string> _broadcast;

  public AdminMessageFileWatcher (string path, System.Action <string> broadcast)
  {
    _path = path;
    _broadcast = broadcast;
  }

  public override void _Ready()
  {
    var timer = new Timer { WaitTime = 1.0, Autostart = true };
    timer.Timeout += Poll;
    AddChild (timer);
  }

  private void Poll()
  {
    var message = TakePendingMessage();
    if (message.Length == 0) return;
    _broadcast (message);
  }

  private string TakePendingMessage()
  {
    if (!System.IO.File.Exists (_path)) return string.Empty;
    var message = System.IO.File.ReadAllText (_path).Trim();
    if (message.Length == 0) return string.Empty;
    System.IO.File.WriteAllText (_path, string.Empty);
    return message;
  }
}
