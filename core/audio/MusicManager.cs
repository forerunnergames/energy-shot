using System.Collections.Generic;
using System.Linq;
using com.forerunnergames.energyshot.core.world;
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
  [Signal] public delegate void OwnVoteChangedEventHandler (int vote); // +1 up, -1 down, 0 none (issue #162).
  public const string BusName = "Music";
  // Thumbs-down votes in the current play that trigger a skip; the dedicated
  // server overrides this with --skip-votes N (issue #137).
  [Export] public int SkipVotes = 2;
  public string CurrentTrackTitle { get; private set; } = string.Empty;
  public int CurrentUpVotes { get; private set; }
  public int CurrentDownVotes { get; private set; }
  // The local player's remembered vote for the current track (issue #162), recalled
  // by the server on each track start & confirmed after every vote.
  public int CurrentOwnVote { get; private set; }
  private const float MusicVolumeDb = -12.0f;
  private const float SilentDb = -80.0f;
  private const float FadeSeconds = 2.0f;
  private const string VotesFilePath = "user://music-votes.json";
  // Menu music (issue #137): plays locally while no game session is active; it's
  // outside the voted arena rotation.
  private const string MenuTrackFile = "res://assets/music/track-14-main-menu.ogg";
  // The soundtrack's stable filename stems: track-NN-<intensity>. The stem is the
  // permanent vote-persistence key; the display title is derived from it.
  private static readonly string[] TrackStems =
  [
    "track-01-light", "track-02-medium", "track-03-medium", "track-04-medium", "track-05-medium",
    "track-06-light", "track-07-light", "track-08-light", "track-09-light", "track-10-medium",
    "track-11-intense", "track-12-intense", "track-13-medium"
  ];
  private readonly AudioStreamPlayer _musicPlayer = new();
  private readonly RandomNumberGenerator _rng = new();
  private readonly Dictionary <long, int> _playVotes = new(); // Peer id -> +1 (up) / -1 (down) for the current play.
  private Dictionary <string, (int Up, int Down)> _allTimeVotes = new();
  // Own-vote memory (issue #162): per-track voter map keyed by DisplayName - the best
  // identity available without accounts, so rename collisions are an accepted limitation.
  private Dictionary <string, Dictionary <string, int>> _voterVotes = new();
  private World _world = null!;
  private Timer _trackTimer = null!;
  private Tween? _fadeTween;
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

  // A connected ENet peer, checked before Multiplayer.IsServer(): polling IsServer()
  // on an inactive peer (e.g. between the playtest's wrong-password kick & the
  // rejoin) spams "multiplayer instance isn't currently active" errors.
  private bool IsSessionActive() => Multiplayer.MultiplayerPeer is ENetMultiplayerPeer peer && peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;

  // Only a live ENet server session runs the playlist (same guard as World, issue #111).
  private bool IsActiveServer() => IsSessionActive() && Multiplayer.IsServer();

  public override void _EnterTree() => CreateMusicBus();

  public override void _Ready()
  {
    SkipVotes = ParseSkipVotes (SkipVotes);
    AddPlayer (_musicPlayer);
    _trackTimer = new Timer { OneShot = true };
    _trackTimer.Timeout += OnTrackFinished;
    AddChild (_trackTimer);
    Multiplayer.PeerConnected += OnPeerConnected;
    _world = GetParent <World>();
    // A joiner's spawn is when its DisplayName exists server-side, so that's the
    // moment to recall its remembered vote for the playing track (issue #162).
    _world.PlayerJoinedGame += OnPlayerJoinedGame;
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
  // running the dedicated server - & the menu track covers every idle moment
  // outside a session (issue #137).
  private void StartPlaylistPoller()
  {
    var timer = new Timer { WaitTime = 1.0, Autostart = true };
    timer.Timeout += StartPlaylistWhenServing;
    timer.Timeout += PlayMenuMusicWhenIdle;
    AddChild (timer);
  }

  private void StartPlaylistWhenServing()
  {
    if (_playlistStarted || !IsActiveServer()) return;
    _playlistStarted = true;
    LoadVotes();
    StartNextTrack();
  }

  // Outside a session, the menu track fades in locally; joining crossfades to the
  // server's synced pick, & leaving/kicks crossfade back here on the next poll.
  private void PlayMenuMusicWhenIdle()
  {
    if (OS.HasFeature ("dedicated_server") || IsSessionActive()) return;
    _currentTrack = -1;
    if (_musicPlayer.Playing && _musicPlayer.Stream?.ResourcePath == MenuTrackFile) return;
    CrossfadeToFile (MenuTrackFile, fromPosition: 0.0f);
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
    ResetPlayVotes();
    RecallRememberedVotes();
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

  private void ResetPlayVotes()
  {
    _playVotes.Clear();
    Rpc (MethodName.OnVoteCounts, 0, 0);
  }

  // Own-vote memory (issue #162): on each track start the server tells every spawned
  // player which way it last voted on this track, so the mini player can render that
  // thumb as already pressed.
  private void RecallRememberedVotes()
  {
    foreach (var player in _world.GetPlayers()) SendOwnVote (player.NetworkId, RememberedVoteFor (player.DisplayName));
  }

  private int RememberedVoteFor (string displayName) => _voterVotes[TrackStems[_currentTrack]].GetValueOrDefault (displayName, 0);
  private string DisplayNameFor (long peerId) => _world.GetPlayers().FirstOrDefault (player => player.NetworkId == peerId)?.DisplayName ?? string.Empty;

  // RpcId to our own peer id doesn't loop back locally, so the hosting player's
  // recall is a direct call (issue #162).
  private void SendOwnVote (long peerId, int vote)
  {
    if (peerId == Multiplayer.GetUniqueId()) OnOwnVote (vote);
    else RpcId (peerId, MethodName.OnOwnVote, vote);
  }

  private void OnPlayerJoinedGame (string playerName)
  {
    if (!IsActiveServer() || _currentTrack == -1) return;
    var player = _world.GetPlayers().FirstOrDefault (candidate => candidate.DisplayName == playerName);
    if (player == null) return;
    SendOwnVote (player.NetworkId, RememberedVoteFor (playerName));
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
    OnOwnVote (0); // Clear the pressed thumb until the server recalls a remembered vote (issue #162).
    EmitSignal (SignalName.TrackChanged, CurrentTrackTitle);
  }

  // The server's word on which way this player voted on the current track (issue
  // #162): recalled memory on track start & join, or confirmation of a fresh vote.
  [Rpc]
  private void OnOwnVote (int vote)
  {
    CurrentOwnVote = vote;
    EmitSignal (SignalName.OwnVoteChanged, vote);
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

  // One vote per player per play; re-voting switches the vote (issue #137). The
  // permanent totals track one remembered vote per player per track (issue #162).
  private void ApplyVote (long peerId, bool isUpVote)
  {
    if (_currentTrack == -1) return;
    _playVotes[peerId] = isUpVote ? 1 : -1;
    var upCount = _playVotes.Values.Count (vote => vote == 1);
    var downCount = _playVotes.Values.Count (vote => vote == -1);
    UpdateRememberedVote (peerId, isUpVote ? 1 : -1);
    ServerLog.Event (peerId, $"music vote {(isUpVote ? "up" : "down")} for [{CurrentTrackTitle}]: play now {upCount} up / {downCount} down");
    Rpc (MethodName.OnVoteCounts, upCount, downCount);
    SendOwnVote (peerId, isUpVote ? 1 : -1); // Instant pressed-thumb feedback for the voter (issue #162).
    if (downCount < SkipVotes) return;
    ServerLog.Event ($"music skip: [{CurrentTrackTitle}] reached {downCount} thumbs-down (threshold {SkipVotes})");
    StartNextTrack();
  }

  // Own-vote memory (issue #162): the all-time totals hold one vote per player per
  // track - switching stance removes the old vote & adds the new one, & a repeat of
  // the same vote changes nothing, so nothing ever double-counts.
  private void UpdateRememberedVote (long peerId, int vote)
  {
    var name = DisplayNameFor (peerId);
    if (name.Length == 0) return; // No spawned player yet: nothing durable to key the memory on.
    var stem = TrackStems[_currentTrack];
    var previous = _voterVotes[stem].GetValueOrDefault (name, 0);
    if (previous == vote) return;
    var (up, down) = _allTimeVotes[stem];
    _allTimeVotes[stem] = (up - (previous == 1 ? 1 : 0) + (vote == 1 ? 1 : 0), down - (previous == -1 ? 1 : 0) + (vote == -1 ? 1 : 0));
    _voterVotes[stem][name] = vote;
    SaveVotes();
  }

  private void CrossfadeTo (int track, float fromPosition) => CrossfadeToFile (FileFor (track), fromPosition);

  // One persistent main player; each crossfade hands the outgoing track to a
  // throwaway sibling player (seeked to the same position) that fades to silence
  // & frees itself, while the main player fades the new track in. Interrupted
  // fades can orphan a sibling mid-fade, so each crossfade sweeps them first.
  private void CrossfadeToFile (string file, float fromPosition)
  {
    KillFadeTween();
    CleanupOrphanedPlayers();
    if (_musicPlayer.Playing) HandOffToFadeOutPlayer();
    _musicPlayer.Stream = ResourceLoader.Load <AudioStream> (file);
    _musicPlayer.VolumeDb = SilentDb;
    _musicPlayer.Play (fromPosition);
    _fadeTween = CreateTween();
    _fadeTween.TweenProperty (_musicPlayer, "volume_db", MusicVolumeDb, FadeSeconds);
  }

  private void HandOffToFadeOutPlayer()
  {
    var fadeOut = new AudioStreamPlayer { Stream = _musicPlayer.Stream, VolumeDb = _musicPlayer.VolumeDb, Bus = BusName };
    AddChild (fadeOut);
    fadeOut.Play (_musicPlayer.GetPlaybackPosition());
    _musicPlayer.Stop();
    var tween = CreateTween();
    tween.TweenProperty (fadeOut, "volume_db", SilentDb, FadeSeconds);
    tween.TweenCallback (Callable.From (fadeOut.QueueFree));
  }

  private void KillFadeTween()
  {
    if (_fadeTween == null || !IsInstanceValid (_fadeTween)) return;
    _fadeTween.Kill();
    _fadeTween = null;
  }

  private void CleanupOrphanedPlayers()
  {
    foreach (var child in GetChildren())
    {
      if (child is not AudioStreamPlayer player || player == _musicPlayer || !IsInstanceValid (player)) continue;
      player.Stop();
      player.QueueFree();
    }
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

  // All-time per-track vote totals & per-track voter memory (issues #137/#162):
  // loaded on start, saved on every vote, so rankings, weights, & each player's
  // remembered stance survive server restarts.
  private void LoadVotes()
  {
    _allTimeVotes = TrackStems.ToDictionary (name => name, _ => (Up: 0, Down: 0));
    _voterVotes = TrackStems.ToDictionary (name => name, _ => new Dictionary <string, int>());
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
    LoadTrackVoters (entry, name);
  }

  // Per-track voter map keyed by DisplayName (issue #162); rename collisions are an
  // accepted limitation of account-less identity.
  private void LoadTrackVoters (Godot.Collections.Dictionary entry, string name)
  {
    if (!entry.TryGetValue ("voters", out var value) || value.Obj is not Godot.Collections.Dictionary voters) return;
    foreach (var key in voters.Keys) _voterVotes[name][(string)key] = (int)voters[key];
  }

  private void SaveVotes()
  {
    var root = new Godot.Collections.Dictionary();
    foreach (var (name, votes) in _allTimeVotes) root[name] = new Godot.Collections.Dictionary { { "up", votes.Up }, { "down", votes.Down }, { "voters", VotersFor (name) } };
    using var file = FileAccess.Open (VotesFilePath, FileAccess.ModeFlags.Write);
    file?.StoreString (Json.Stringify (root, "  "));
  }

  private Godot.Collections.Dictionary VotersFor (string name)
  {
    var voters = new Godot.Collections.Dictionary();
    foreach (var (voter, vote) in _voterVotes[name]) voters[voter] = vote;
    return voters;
  }
}
