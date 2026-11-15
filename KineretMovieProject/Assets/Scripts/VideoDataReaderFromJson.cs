using System;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Reads Movie.json (the play order of one video folder). Two layouts are supported:
///   { "movies": [ ... ] }                                   - flat list, no scenes
///   { "scenes": [ { "sceneName": "...", "movies": [ ... ] } ] } - clips grouped into scenes
/// Plain C# class, not a component: VideoHandler owns one instance.
/// </summary>
public class VideoDataReaderFromJson
{
    [Serializable]
    public class Scenes
    {
        public Scene[] scenes; // used when the JSON groups clips into scenes
        public Movie[] movies; // used when the JSON is a flat list (no scenes)
    }

    [Serializable]
    public class Scene
    {
        public string sceneName;
        public Movie[] movies;
    }

    [Serializable]
    public class Movie
    {
        public string movName;                       // file name without ".mp4"
        public bool loopAtTheEnd;                    // false = hold last frame until the next click
        public string loopStartTime = "00:00:00:00"; // hh:mm:ss:ms, where the loop jumps back to
    }

    public Scene[] LoadedScenes { get; private set; } = Array.Empty<Scene>();

    /// <summary>False when the JSON is a flat "movies" list; LoadedScenes then holds one scene with all clips.</summary>
    public bool HasScenes { get; private set; }

    public bool Load(string jsonPath)
    {
        LoadedScenes = Array.Empty<Scene>();
        HasScenes = false;

        if (!File.Exists(jsonPath))
        {
            Debug.LogError($"Show JSON not found: {jsonPath}");
            return false;
        }

        Scenes data;
        try
        {
            data = JsonUtility.FromJson<Scenes>(File.ReadAllText(jsonPath));
        }
        catch (ArgumentException e)
        {
            Debug.LogError($"Show JSON is not valid JSON: {jsonPath}\n{e.Message}");
            return false;
        }

        bool hasScenes = data?.scenes != null && data.scenes.Length > 0;
        bool hasMovies = data?.movies != null && data.movies.Length > 0;

        if (hasScenes && hasMovies)
            Debug.LogWarning($"Show JSON has both \"scenes\" and \"movies\"; using \"scenes\": {jsonPath}");

        if (hasScenes)
        {
            LoadedScenes = data.scenes;
            HasScenes = true;
            return true;
        }

        if (hasMovies)
        {
            LoadedScenes = new[] { new Scene { sceneName = null, movies = data.movies } };
            return true;
        }

        Debug.LogError($"Show JSON has no \"movies\" or \"scenes\": {jsonPath}");
        return false;
    }

    /// <summary>Converts "hh:mm:ss:ms" to seconds. Returns 0 (and warns) on bad input.</summary>
    public static double TimeAsSeconds(string time)
    {
        if (string.IsNullOrWhiteSpace(time))
            return 0;

        string[] parts = time.Split(':');
        if (parts.Length != 4)
        {
            Debug.LogWarning($"Time '{time}' is not hh:mm:ss:ms, using 0");
            return 0;
        }

        int[] values = new int[4];
        for (int i = 0; i < 4; i++)
        {
            if (!int.TryParse(parts[i].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out values[i]))
            {
                Debug.LogWarning($"Time '{time}' is not hh:mm:ss:ms, using 0");
                return 0;
            }
        }

        return values[0] * 3600 + values[1] * 60 + values[2] + values[3] / 1000.0;
    }
}
