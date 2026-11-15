using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Plays the videos listed in Movie.json, from MoviesPath/VideoFolder set in StreamingAssets/KineretMovieConfig.ini.
/// One click = next video. A finished video holds its last frame (or loops) until the next click.
/// After the last video, the next click goes back to the first one.
///
/// Input (presentation clicker sends PageDown / PageUp):
///   Next video:      left mouse, Right Arrow, PageDown
///   Previous video:  right mouse, Left Arrow, PageUp
///   Next scene:      Period (only if Movie.json has scenes)
///   Restart show:    Space, or F5 while Period is held (re-reads the ini and JSON)
/// </summary>
public class VideoHandler : MonoBehaviour
{
    private const string ConfigFileName = "KineretMovieConfig.ini";
    private const string MoviesPathKey = "MoviesPath";
    private const string VideoFolderKey = "VideoFolder";
    private const string ShowJsonFileName = "Movie.json";
    private const float LoopRestartDelay = 0.1f;

    [SerializeField] private VideoPlayer _videoPlayer;
    [SerializeField] private RawImage _screen;

    private readonly VideoDataReaderFromJson _videoData = new VideoDataReaderFromJson();
    private readonly List<PlaylistEntry> _playlist = new List<PlaylistEntry>();
    private RenderTexture _renderTexture;
    private Coroutine _playbackRoutine;
    private int _currentIndex;
    private string _loadedFilePath; // own copy; VideoPlayer.url may be normalized by Unity
    private bool _isPreparing;      // true while a new clip loads; end events then belong to the old clip
    private bool _prepareFailed;    // set by errorReceived so the prepare wait can stop

    private readonly struct PlaylistEntry
    {
        public readonly string FilePath;
        public readonly int SceneIndex;
        public readonly bool LoopAtTheEnd;
        public readonly double LoopStartSeconds;

        public PlaylistEntry(string filePath, int sceneIndex, VideoDataReaderFromJson.Movie movie)
        {
            FilePath = filePath;
            SceneIndex = sceneIndex;
            LoopAtTheEnd = movie.loopAtTheEnd;
            LoopStartSeconds = VideoDataReaderFromJson.TimeAsSeconds(movie.loopStartTime);
        }
    }

    private void Awake()
    {
        if (_videoPlayer == null || _screen == null)
        {
            Debug.LogError("VideoHandler: assign Video Player and Screen (RawImage) in the Inspector.");
            enabled = false; // Start and Update will not run
            return;
        }

        // Black until the first video is loaded; the render texture is sized per video in EnsureRenderTexture.
        _screen.texture = Texture2D.blackTexture;

        _videoPlayer.source = VideoSource.Url;
        _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        _videoPlayer.playOnAwake = false;
        _videoPlayer.waitForFirstFrame = true;
        _videoPlayer.skipOnDrop = true;
        _videoPlayer.isLooping = false;
        _videoPlayer.loopPointReached += OnVideoFinished;
        _videoPlayer.errorReceived += OnVideoError;
    }

    private void Start()
    {
        RestartShow();
    }

    private void OnDestroy()
    {
        if (_videoPlayer != null)
        {
            _videoPlayer.loopPointReached -= OnVideoFinished;
            _videoPlayer.errorReceived -= OnVideoError;
        }

        if (_renderTexture != null)
        {
            _renderTexture.Release();
            Destroy(_renderTexture);
        }
    }

    private void Update()
    {
        HandleInput();
    }

    private void HandleInput()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        bool restart = keyboard != null &&
            (keyboard.spaceKey.wasPressedThisFrame ||
             (keyboard.periodKey.isPressed && keyboard.f5Key.wasPressedThisFrame));
        if (restart)
        {
            RestartShow();
            return;
        }

        if (_playlist.Count == 0)
            return;

        bool next =
            (mouse != null && mouse.leftButton.wasPressedThisFrame) ||
            (keyboard != null && (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.pageDownKey.wasPressedThisFrame));
        bool previous =
            (mouse != null && mouse.rightButton.wasPressedThisFrame) ||
            (keyboard != null && (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.pageUpKey.wasPressedThisFrame));
        // Only when Movie.json groups clips into scenes; with a flat "movies" list Period does nothing.
        bool nextScene = _videoData.HasScenes && keyboard != null && keyboard.periodKey.wasPressedThisFrame;

        if (next) PlayIndex(Wrap(_currentIndex + 1));
        else if (previous) PlayIndex(Wrap(_currentIndex - 1));
        else if (nextScene) PlayIndex(FirstIndexOfNextScene());
    }

    private void RestartShow()
    {
        if (_playbackRoutine != null)
            StopCoroutine(_playbackRoutine);

        if (LoadShow())
        {
            PlayIndex(0);
        }
        else
        {
            _videoPlayer.Stop();
            _loadedFilePath = null;
        }
    }

    private bool LoadShow()
    {
        _playlist.Clear();

        string configPath = Path.Combine(Application.streamingAssetsPath, ConfigFileName);
        Dictionary<string, string> config = KineretConfigReader.Load(configPath);
        if (config == null)
            return false;

        if (!KineretConfigReader.TryGetValue(config, VideoFolderKey, out string folderName))
        {
            Debug.LogError($"'{VideoFolderKey}=' is missing or empty in {configPath}");
            return false;
        }

        string folderPath = Path.Combine(GetMoviesRoot(config), folderName);
        if (!Directory.Exists(folderPath))
        {
            Debug.LogError($"Video folder not found: {folderPath}");
            return false;
        }

        if (!_videoData.Load(Path.Combine(folderPath, ShowJsonFileName)))
            return false;

        VideoDataReaderFromJson.Scene[] scenes = _videoData.LoadedScenes;
        for (int sceneIndex = 0; sceneIndex < scenes.Length; sceneIndex++)
        {
            VideoDataReaderFromJson.Movie[] movies = scenes[sceneIndex]?.movies;
            if (movies == null)
                continue;

            foreach (VideoDataReaderFromJson.Movie movie in movies)
            {
                if (movie == null || string.IsNullOrWhiteSpace(movie.movName))
                {
                    Debug.LogWarning($"Scene {sceneIndex} has a movie without movName, skipping it");
                    continue;
                }

                string filePath = Path.Combine(folderPath, movie.movName + ".mp4");
                if (!File.Exists(filePath))
                {
                    Debug.LogError($"Video listed in JSON not found, skipping it: {filePath}");
                    continue;
                }

                _playlist.Add(new PlaylistEntry(filePath, sceneIndex, movie));
            }
        }

        if (_playlist.Count == 0)
        {
            Debug.LogError($"No playable videos in {folderPath}");
            return false;
        }

        Debug.Log($"Loaded {_playlist.Count} videos from '{folderPath}'");
        return true;
    }

    /// <summary>
    /// Folder that holds the video folders. MoviesPath in the ini; empty or missing = StreamingAssets.
    /// A full path (e.g. D:\KineretMovies) is used as is; a relative one is relative to StreamingAssets.
    /// </summary>
    private static string GetMoviesRoot(Dictionary<string, string> config)
    {
        if (!KineretConfigReader.TryGetValue(config, MoviesPathKey, out string moviesPath))
            return Application.streamingAssetsPath;

        // Path.Combine returns moviesPath unchanged when it is already a full path.
        return Path.Combine(Application.streamingAssetsPath, moviesPath);
    }

    private void PlayIndex(int index)
    {
        _currentIndex = index;

        if (_playbackRoutine != null)
            StopCoroutine(_playbackRoutine);

        _playbackRoutine = StartCoroutine(PrepareAndPlay(_playlist[index]));
    }

    private IEnumerator PrepareAndPlay(PlaylistEntry entry)
    {
        // Loop from 0: native looping (seamless). Loop from a later time: handled in OnVideoFinished.
        _videoPlayer.isLooping = entry.LoopAtTheEnd && entry.LoopStartSeconds <= 0;

        // Same file again (e.g. show with one video): just rewind, no need to prepare again.
        if (_loadedFilePath == entry.FilePath && _videoPlayer.isPrepared)
        {
            _videoPlayer.time = 0;
            _videoPlayer.Play();
            yield break;
        }

        _isPreparing = true;
        _prepareFailed = false;
        _loadedFilePath = entry.FilePath;
        _videoPlayer.url = entry.FilePath;
        _videoPlayer.Prepare();
        while (!_videoPlayer.isPrepared && !_prepareFailed)
            yield return null;

        _isPreparing = false;
        if (_prepareFailed)
        {
            _loadedFilePath = null;
            Debug.LogError($"Could not play {entry.FilePath}. Click to go to the next video.");
            yield break;
        }

        EnsureRenderTexture((int)_videoPlayer.width, (int)_videoPlayer.height);
        _videoPlayer.Play();
        Debug.Log($"Playing {_currentIndex + 1}/{_playlist.Count}: {Path.GetFileName(entry.FilePath)}");
    }

    private void OnVideoFinished(VideoPlayer source)
    {
        if (_playlist.Count == 0 || _isPreparing)
            return; // while the next clip loads, this event came from the previous clip

        PlaylistEntry entry = _playlist[_currentIndex];

        // Not looping: player stops and keeps the last frame until the next click.
        // Looping from 0: isLooping already restarts it.
        if (!entry.LoopAtTheEnd || entry.LoopStartSeconds <= 0)
            return;

        if (_playbackRoutine != null)
            StopCoroutine(_playbackRoutine);

        _playbackRoutine = StartCoroutine(RestartLoop(entry.LoopStartSeconds));
    }

    private IEnumerator RestartLoop(double loopStartSeconds)
    {
        _videoPlayer.time = loopStartSeconds;
        yield return new WaitForSeconds(LoopRestartDelay);
        _videoPlayer.Play();
    }

    private void OnVideoError(VideoPlayer source, string message)
    {
        Debug.LogError($"Video error ({source.url}): {message}");
        _prepareFailed = true;
    }

    private int FirstIndexOfNextScene()
    {
        int currentScene = _playlist[_currentIndex].SceneIndex;
        for (int i = _currentIndex + 1; i < _playlist.Count; i++)
        {
            if (_playlist[i].SceneIndex != currentScene)
                return i;
        }

        return 0; // last scene: back to the start
    }

    private int Wrap(int index)
    {
        int count = _playlist.Count;
        return ((index % count) + count) % count;
    }

    /// <summary>
    /// Makes the render texture match the video's own resolution (1080p video = 1080p texture, 4K = 4K).
    /// Reuses the current texture when the size is unchanged. The RawImage stretches it to the panel.
    /// </summary>
    private void EnsureRenderTexture(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            Debug.LogWarning($"Video reported size {width}x{height}, keeping the current texture");
            return;
        }

        if (_renderTexture != null && _renderTexture.width == width && _renderTexture.height == height)
            return;

        RenderTexture previous = _renderTexture;

        _renderTexture = new RenderTexture(width, height, 0);
        _renderTexture.Create();
        ClearToBlack(_renderTexture);
        _videoPlayer.targetTexture = _renderTexture;
        _screen.texture = _renderTexture;

        if (previous != null)
        {
            previous.Release();
            Destroy(previous);
        }

        Debug.Log($"Render texture set to {width}x{height}");
    }

    private static void ClearToBlack(RenderTexture texture)
    {
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = texture;
        GL.Clear(true, true, Color.black);
        RenderTexture.active = previous;
    }
}
