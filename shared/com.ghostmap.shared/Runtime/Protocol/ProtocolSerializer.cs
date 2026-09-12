using System;
using System.Text;
using UnityEngine;

namespace GhostMap.Shared.Protocol
{
    /// <summary>
    /// Protocol v1 serialization over newline-delimited JSON.
    ///
    /// Deserialization follows a fixed algorithm:
    /// <list type="number">
    /// <item>parse JSON into <see cref="WireMessageHeader"/>;</item>
    /// <item>validate <c>protocolVersion</c>;</item>
    /// <item>switch on <c>type</c>;</item>
    /// <item>parse again into the concrete message class;</item>
    /// <item>validate fields;</item>
    /// <item>return the message for dispatch.</item>
    /// </list>
    ///
    /// Nothing here throws on bad input. A malformed line is a normal event on a
    /// local network, and the viewer must log and keep its current scene rather
    /// than fail.
    /// </summary>
    public static class ProtocolSerializer
    {
        /// <summary>
        /// Serializes a message to a single-line JSON string, with no trailing
        /// terminator. JsonUtility escapes any newline inside a string value, so
        /// the result is always safe for newline framing.
        /// </summary>
        public static string Serialize(object message)
        {
            if (message == null)
            {
                return string.Empty;
            }

            return JsonUtility.ToJson(message);
        }

        /// <summary>
        /// Serializes a message and appends the line terminator, ready to write
        /// straight to the socket.
        /// </summary>
        public static string SerializeLine(object message)
            => Serialize(message) + ProtocolConstants.LineTerminator;

        /// <summary>
        /// Attempts to deserialize one line of JSON into a concrete message.
        /// </summary>
        /// <param name="json">One line, without the terminator.</param>
        /// <param name="message">The concrete message on success, otherwise null.</param>
        /// <param name="error">A human-readable reason on failure, otherwise empty.</param>
        public static bool TryDeserialize(string json, out object message, out string error)
        {
            message = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Message is empty.";
                return false;
            }

            int byteCount = Encoding.UTF8.GetByteCount(json);

            if (byteCount > ProtocolConstants.MaxLineLengthBytes)
            {
                error = $"Message length {byteCount} bytes exceeds the maximum " +
                        $"{ProtocolConstants.MaxLineLengthBytes} bytes.";
                return false;
            }

            // 1. Header.
            WireMessageHeader header;

            try
            {
                header = JsonUtility.FromJson<WireMessageHeader>(json);
            }
            catch (Exception exception)
            {
                error = $"Malformed JSON: {exception.Message}";
                return false;
            }

            if (header == null)
            {
                error = "Malformed JSON: could not read message header.";
                return false;
            }

            // 2. Protocol version.
            if (header.protocolVersion != ProtocolConstants.ProtocolVersion)
            {
                error = $"Unsupported protocol version {header.protocolVersion}; " +
                        $"expected {ProtocolConstants.ProtocolVersion}.";
                return false;
            }

            if (string.IsNullOrEmpty(header.type))
            {
                error = "Message type is missing.";
                return false;
            }

            // 3, 4, 5. Switch on type, re-parse, validate.
            try
            {
                switch (header.type)
                {
                    case ProtocolConstants.TypeHello:
                        return Accept(JsonUtility.FromJson<HelloMessage>(json), out message, out error);

                    case ProtocolConstants.TypeHeartbeat:
                        return Accept(JsonUtility.FromJson<HeartbeatMessage>(json), out message, out error);

                    case ProtocolConstants.TypePhonePose:
                        return Accept(JsonUtility.FromJson<PhonePoseMessage>(json), out message, out error);

                    case ProtocolConstants.TypeSceneSnapshot:
                        return AcceptSnapshot(
                            JsonUtility.FromJson<SceneSnapshotMessage>(json), out message, out error);

                    case ProtocolConstants.TypeScanFinalized:
                        return Accept(JsonUtility.FromJson<ScanFinalizedMessage>(json), out message, out error);

                    default:
                        error = $"Unknown message type '{header.type}'.";
                        return false;
                }
            }
            catch (Exception exception)
            {
                error = $"Malformed JSON for type '{header.type}': {exception.Message}";
                return false;
            }
        }

        private static bool Accept(WireMessageHeader parsed, out object message, out string error)
        {
            if (parsed == null)
            {
                message = null;
                error = "Message body could not be parsed.";
                return false;
            }

            message = parsed;
            error = string.Empty;
            return true;
        }

        private static bool AcceptSnapshot(
            SceneSnapshotMessage parsed,
            out object message,
            out string error)
        {
            message = null;
            error = string.Empty;

            if (parsed == null)
            {
                error = "Message body could not be parsed.";
                return false;
            }

            // JsonUtility materializes a default instance for a missing nested
            // object rather than leaving it null, so an absent "snapshot" field
            // arrives as a zeroed SceneSnapshot. Detect that explicitly, or the
            // caller gets a misleading "schema version 0" error instead of being
            // told the snapshot is simply absent.
            bool snapshotAbsent =
                parsed.snapshot == null
                || (parsed.snapshot.schemaVersion == 0
                    && parsed.snapshot.room == null
                    && string.IsNullOrEmpty(parsed.snapshot.sessionId));

            if (snapshotAbsent)
            {
                error = "scene.snapshot message carries no snapshot.";
                return false;
            }

            if (parsed.snapshot.schemaVersion != ProtocolConstants.SchemaVersion)
            {
                error = $"Unsupported scene schema version {parsed.snapshot.schemaVersion}; " +
                        $"expected {ProtocolConstants.SchemaVersion}.";
                return false;
            }

            if (parsed.snapshot.room == null)
            {
                error = "scene.snapshot carries no room.";
                return false;
            }

            message = parsed;
            return true;
        }
    }
}
