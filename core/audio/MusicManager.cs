using System.Collections.Generic;
using System.Linq;
using com.forerunnergames.energyshot.utilities;
using Godot;

namespace com.forerunnergames.energyshot.core.audio;

// In-game music (issue #137): a server-authoritative playlist synced to every peer.
// The server picks each next track by weighted random (all-time votes shape the
// weights), RPCs the choice to everyone, tallies per-play thumbs up/down votes,
// skips a track when enough thumbs-down arrive, & persists all-time totals to
// user://music-votes.json. Peers crossfade between tracks on a dedicated "Music"
// bus so the HUD visualizer can tap its spectrum. Track timing comes from a
// track-length timer (not audible playback), so the headless dedicated server
// drives the playlist without needing audio output.
public partial class MusicManager : Node
{
  [Signal] public delegate void TrackChangedEventHandler (string title);
  [Signal] public delegate void VoteCountsChangedEventHandler (int upCount, int downCount);
  public const string BusName = "Music";
  // Thumbs-down votes in the current play that trigger a skip; the dedicated
  // server overrides this with --skip-votes N (issue #137).
  [Export] public int SkipVotes = 2;
  public string CurrentTrackTitle { get; private set; } = string.Empty;
  public int CurrentUpVotes { get; private set; }
  public int CurrentDownVotes { get; private set; }
  private const float MusicVolumeDb = -12.0f;
  private const float SilentDb = -60.0f;
  private const float FadeSeconds = 2.0f;
  private const string VotesFilePath = "user://music-votes.json";
  // The soundtrack's stable filename stems: track-NN-<intensity>. The stem is the
  // permanent vote-persistence key; the display title is derived from it.
  private static readonly string[] TrackStems =
  [
    "track-01-light", "track-02-medium", "track-03-medium", "track-04-medium", "track-05-medium",
    "track-06-light", "track-07-light", "track-08-light", "track-09-light", "track-10-medium",
    "track-11-intense", "track-12-intense", "track-13-medium"
  ];
  private readonly AudioStreamPlayer[] _players = [new(), new()];
  private readonly RandomNumberGenerator _rng = new();
  private readonly Dictionary <long, int> _playVotes = new(); // Peer id -> +1 (up) / -1 (down) for the current play.
  private Dictionary <string, (int Up, int Down)> _allTimeVotes = new();
  private (int Up, int Down) _playBaseVotes; // All-time totals when the current play began.
  private Timer _trackTimer = null!;
  private Tween? _fadeTween;
  private int _activePlayer;
  private int _currentTrack = -1;
  private bool _playlistStarted;
  private static string FileFor (int track) => $"res://assets/music/{TrackStems[track]}.ogg";
  private static float LengthOf (int track) => (float)ResourceLoader.Load <AudioStream> (FileFor (track)).GetLength();

  // "track-11-intense" -> "Track 11 (Intense)": the intensity rides along as a
  // subtle suffix so players know what mood just queued up (issue #137).
  private static string TitleFor (int track)
  {
    var parts = TrackStems[track].Split ('-');
    return $"Track {int.Parse (parts[1])} ({char.ToUpper (parts[2][0])}{parts[2][1..]})";
  }

  // Only a live ENet server session runs the playlist (same guard as World, issue #111).
  private bool IsActiveServer() => Multiplayer.MultiplayerPeer is ENetMultiplayerPeer && Multiplayer.IsServer();

  public override void _EnterTree() => CreateMusicBus();

  public override void _Ready()
  {
    SkipVotes = ParseSkipVotes (SkipVotes);
    foreach (var player in _players) AddPlayer (player);
    _trackTimer = new Timer { OneShot = true };
    _trackTimer.Timeout += OnTrackFinished;
    AddChild (_trackTimer);
    Multiplayer.PeerConnected += OnPeerConnected;
    StartPlaylistPoller();
  }

  // Liked songs play more often, disliked ones less - never zero (issue #137).
  private float WeightFor (int track)
  {
    var votes = _allTimeVotes[TrackStems[track]];
    return Mathf.Clamp (1.0f + (votes.Up - votes.Down) * 0.1f, 0.25f, 3.0f);
  }

  // Voting entry point for the local player: the server applies its own votes
  // directly; clients send theirs to the server (issue #137).
  public void SubmitVote (bool isUpVote)
  {
    if (Multiplayer.IsServer())
    {
      ApplyVote (Multiplayer.GetUniqueId(), isUpVote);
      return;
    }

    RpcId (1, MethodName.OnVoteSubmitted, isUpVote);
  }

  // The visualizer taps this bus's spectrum analyzer (issue #137); created in code
  // so no bus layout resource is needed & the headless server stays happy.
  private static void CreateMusicBus()
  {
    if (AudioServer.GetBusIndex (BusName) != -1) return;
    var busIndex = AudioServer.BusCount;
    AudioServer.AddBus (busIndex);
    AudioServer.SetBusName (busIndex, BusName);
    AudioServer.AddBusEffect (busIndex, new AudioEffectSpectrumAnalyzer());
  }

  private void AddPlayer (AudioStreamPlayer player)
  {
    player.Bus = BusName;
    player.VolumeDb = SilentDb;
    AddChild (player);
  }

  // The playlist begins once a live server session exists - hosting a game or
  // running the dedicated server - & stops mattering when the session ends.
  private void StartPlaylistPoller()
  {
    var timer = new Timer { WaitTime = 1.0, Autostart = true };
    timer.Timeout += StartPlaylistWhenServing;
    AddChild (timer);
  }

  private void StartPlaylistWhenServing()
  {
    if (_playlistStarted || !IsActiveServer()) return;
    _playlistStarted = true;
    LoadVotes();
    StartNextTrack();
  }

  private void OnTrackFinished()
  {
    if (!IsActiveServer()) return;
    StartNextTrack();
  }

  // Server-side: weighted random pick, never the same track twice in a row.
  private void StartNextTrack()
  {
    var next = PickNextTrack();
    _trackTimer.Start (LengthOf (next));
    Rpc (MethodName.OnTrackStarted, next, 0.0f);
    ResetPlayVotes (next);
  }

  private int PickNextTrack()
  {
    var candidates = Enumerable.Range (0, TrackStems.Length).Where (track => track != _currentTrack).ToList();
    var roll = _rng.RandfRange (0.0f, candidates.Sum (WeightFor));

    foreach (var candidate in candidates)
    {
      roll -= WeightFor (candidate);
      if (roll <= 0.0f) return candidate;
    }

    return candidates[^1];
  }

  private void ResetPlayVotes (int track)
  {
    _playVotes.Clear();
    _playBaseVotes = _allTimeVotes[TrackStems[track]];
    Rpc (MethodName.OnVoteCounts, 0, 0);
  }

  // Late joiners pick up the current track mid-song & the current play's counts.
  private void OnPeerConnected (long peerId)
  {
    if (!IsActiveServer() || _currentTrack == -1) return;
    var elapsed = Mathf.Max (0.0f, LengthOf (_currentTrack) - (float)_trackTimer.TimeLeft);
    RpcId (peerId, MethodName.OnTrackStarted, _currentTrack, elapsed);
    RpcId (peerId, MethodName.OnVoteCounts, CurrentUpVotes, CurrentDownVotes);
  }

  // Every peer (including the server, for the hosting player's ears) starts the
  // same track at roughly the same position, crossfading over ~2s.
  [Rpc (CallLocal = true)]
  private void OnTrackStarted (int track, float fromPosition)
  {
    _currentTrack = track;
    CurrentTrackTitle = TitleFor (track);
    CrossfadeTo (track, fromPosition);
    EmitSignal (SignalName.TrackChanged, CurrentTrackTitle);
  }

  [Rpc (CallLocal = true)]
  private void OnVoteCounts (int upCount, int downCount)
  {
    CurrentUpVotes = upCount;
    CurrentDownVotes = downCount;
    EmitSignal (SignalName.VoteCountsChanged, upCount, downCount);
  }

  [Rpc (MultiplayerApi.RpcMode.AnyPeer)]
  private void OnVoteSubmitted (bool isUpVote)
  {
    if (!Multiplayer.IsServer()) return;
    ApplyVote (Multiplayer.GetRemoteSenderId(), isUpVote);
  }

  // One vote per player per play; re-voting switches the vote (issue #137). Every
  // vote lands in the permanent per-track totals that drive the weighting.
  private void ApplyVote (long peerId, bool isUpVote)
  {
    if (_currentTrack == -1) return;
    _playVotes[peerId] = isUpVote ? 1 : -1;
    var upCount = _playVotes.Values.Count (vote => vote == 1);
    var downCount = _playVotes.Values.Count (vote => vote == -1);
    _allTimeVotes[TrackStems[_currentTrack]] = (_playBaseVotes.Up + upCount, _playBaseVotes.Down + downCount);
    SaveVotes();
    ServerLog.Event (peerId, $"music vote {(isUpVote ? "up" : "down")} for [{CurrentTrackTitle}]: play now {upCount} up / {downCount} down");
    Rpc (MethodName.OnVoteCounts, upCount, downCount);
    if (downCount < SkipVotes) return;
    ServerLog.Event ($"music skip: [{CurrentTrackTitle}] reached {downCount} thumbs-down (threshold {SkipVotes})");
    StartNextTrack();
  }

  private void CrossfadeTo (int track, float fromPosition)
  {
    _fadeTween?.Kill();
    var fadeOut = _players[_activePlayer];
    _activePlayer = 1 - _activePlayer;
    var fadeIn = _players[_activePlayer];
    fadeIn.Stream = ResourceLoader.Load <AudioStream> (FileFor (track));
    fadeIn.VolumeDb = SilentDb;
    fadeIn.Play (fromPosition);
    _fadeTween = CreateTween();
    _fadeTween.TweenProperty (fadeIn, "volume_db", MusicVolumeDb, FadeSeconds);
    _fadeTween.Parallel().TweenProperty (fadeOut, "volume_db", SilentDb, FadeSeconds);
    _fadeTween.TweenCallback (Callable.From (fadeOut.Stop));
  }

  // Dedicated-server skip threshold (issue #137): --skip-votes N, minimum 1.
  private static int ParseSkipVotes (int fallback)
  {
    var args = OS.GetCmdlineUserArgs();
    var index = System.Array.IndexOf (args, "--skip-votes");
    if (index == -1 || index + 1 >= args.Length) return fallback;
    if (!int.TryParse (args[index + 1], out var threshold) || threshold < 1) return fallback;
    return threshold;
  }

  // All-time per-track vote totals (issue #137): loaded on start, saved on every
  // vote, so rankings & weights survive server restarts.
  private void LoadVotes()
  {
    _allTimeVotes = TrackStems.ToDictionary (name => name, _ => (Up: 0, Down: 0));
    if (!FileAccess.FileExists (VotesFilePath)) return;
    using var file = FileAccess.Open (VotesFilePath, FileAccess.ModeFlags.Read);
    if (file == null) return;
    if (Json.ParseString (file.GetAsText()).Obj is not Godot.Collections.Dictionary parsed) return;
    foreach (var name in TrackStems) LoadTrackVotes (parsed, name);
  }

  private void LoadTrackVotes (Godot.Collections.Dictionary parsed, string name)
  {
    if (!parsed.TryGetValue (name, out var value) || value.Obj is not Godot.Collections.Dictionary entry) return;
    var up = entry.TryGetValue ("up", out var upValue) ? (int)upValue : 0;
    var down = entry.TryGetValue ("down", out var downValue) ? (int)downValue : 0;
    _allTimeVotes[name] = (up, down);
  }

  private void SaveVotes()
  {
    var root = new Godot.Collections.Dictionary();
    foreach (var (name, votes) in _allTimeVotes) root[name] = new Godot.Collections.Dictionary { { "up", votes.Up }, { "down", votes.Down } };
    using var file = FileAccess.Open (VotesFilePath, FileAccess.ModeFlags.Write);
    file?.StoreString (Json.Stringify (root, "  "));
  }
}
