using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Minimal reader for KineretMovieConfig.ini (key=value lines).
/// Blank lines, [sections] and lines starting with ';' or '#' are ignored.
/// Keys are case-insensitive. Surrounding quotes on the value are removed.
/// </summary>
public static class KineretConfigReader
{
    /// <summary>Reads every key=value pair. Returns null (and logs) when the file is missing.</summary>
    public static Dictionary<string, string> Load(string iniPath)
    {
        if (!File.Exists(iniPath))
        {
            Debug.LogError($"Config file not found: {iniPath}");
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string rawLine in File.ReadAllLines(iniPath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[')
                continue;

            int equalsIndex = line.IndexOf('=');
            if (equalsIndex <= 0)
                continue;

            string key = line.Substring(0, equalsIndex).Trim();
            string value = line.Substring(equalsIndex + 1).Trim().Trim('"').Trim();
            values[key] = value; // last one wins if a key is repeated
        }

        return values;
    }

    /// <summary>Gets a value; returns false when the key is missing or empty.</summary>
    public static bool TryGetValue(Dictionary<string, string> config, string key, out string value)
    {
        if (config != null && config.TryGetValue(key, out value) && !string.IsNullOrEmpty(value))
            return true;

        value = null;
        return false;
    }
}
