using System;
using System.IO;
using GhostMap.Shared.Domain;
using GhostMap.Shared.Protocol;
using UnityEngine;

namespace GhostMap.Viewer.Scene
{
    /// <summary>
    /// Loads a Task F3 fixture file for the Task V1 "Load fixture" developer
    /// button, so viewer development never waits on a scanner.
    ///
    /// <para>A fixture file holds a bare <c>SceneSnapshot</c> — the payload,
    /// not the protocol envelope (<c>docs/contracts/protocol-v1.md</c>
    /// section 8) — so this reads it directly with <c>JsonUtility</c> rather
    /// than through <see cref="ProtocolSerializer"/>, which only understands
    /// wrapped wire messages.</para>
    /// </summary>
    public static class FixtureLoader
    {
        public static bool TryLoadFromFile(string path, out SceneSnapshot snapshot, out string error)
        {
            snapshot = null;

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                error = $"Fixture not found: {path}";
                return false;
            }

            string json;
            try
            {
                json = File.ReadAllText(path);
            }
            catch (Exception exception)
            {
                error = $"Could not read fixture '{path}': {exception.Message}";
                return false;
            }

            return TryLoadFromJson(json, out snapshot, out error);
        }

        public static bool TryLoadFromJson(string json, out SceneSnapshot snapshot, out string error)
        {
            snapshot = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Fixture is empty.";
                return false;
            }

            SceneSnapshot parsed;
            try
            {
                parsed = JsonUtility.FromJson<SceneSnapshot>(json);
            }
            catch (Exception exception)
            {
                error = $"Fixture is not valid JSON: {exception.Message}";
                return false;
            }

            if (parsed == null || parsed.room == null)
            {
                error = "Fixture has no room.";
                return false;
            }

            if (parsed.schemaVersion != ProtocolConstants.SchemaVersion)
            {
                error = $"Unsupported scene schema version {parsed.schemaVersion}; " +
                        $"expected {ProtocolConstants.SchemaVersion}.";
                return false;
            }

            snapshot = parsed;
            return true;
        }
    }
}
