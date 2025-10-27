using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

public static class EnvConfig
{
    private static Dictionary<string, string> _vars;

    static EnvConfig()
    {
        _vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Look in project root or Application.dataPath parent
        string rootPath = Directory.GetParent(Application.dataPath).FullName;
        string envPath = Path.Combine(rootPath, ".env");

        if (File.Exists(envPath))
        {
            foreach (var line in File.ReadAllLines(envPath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                    continue;

                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                {
                    _vars[parts[0].Trim()] = parts[1].Trim();
                }
            }
        }
        else
        {
            Debug.LogWarning(".env file not found. Using default keys.");
        }
    }

    public static string Get(string key, string fallback = "")
    {
        if (_vars.TryGetValue(key, out var value))
            return value;
        return fallback;
    }
}
