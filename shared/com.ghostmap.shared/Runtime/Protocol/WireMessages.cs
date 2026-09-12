using System;
using GhostMap.Shared.Domain;

namespace GhostMap.Shared.Protocol
{
    /// <summary>
    /// Common header on every protocol v1 message.
    ///
    /// Deserialization parses this first to read <c>protocolVersion</c> and
    /// <c>type</c>, then re-parses into the concrete message class. There is no
    /// reflection-based polymorphic serialization: JsonUtility does not support
    /// it, and an explicit switch is easier to debug from a wire log.
    /// </summary>
    [Serializable]
    public class WireMessageHeader
    {
        public int protocolVersion = ProtocolConstants.ProtocolVersion;
        public string type;
        public string sessionId;
        public long sequence;
        public long unixTimeMs;
    }

    /// <summary>Sent immediately on connect, and again after every reconnect.</summary>
    [Serializable]
    public sealed class HelloMessage : WireMessageHeader
    {
        public HelloMessage()
        {
            type = ProtocolConstants.TypeHello;
        }

        public string appVersion;
        public string deviceName;
    }

    /// <summary>Liveness only. Carries no scene state.</summary>
    [Serializable]
    public sealed class HeartbeatMessage : WireMessageHeader
    {
        public HeartbeatMessage()
        {
            type = ProtocolConstants.TypeHeartbeat;
        }
    }

    /// <summary>
    /// Debug and display only, capped at 5 Hz.
    ///
    /// The room is never reconstructed from pose messages. They exist so the
    /// viewer can show tracking quality and where the phone is.
    /// </summary>
    [Serializable]
    public sealed class PhonePoseMessage : WireMessageHeader
    {
        public PhonePoseMessage()
        {
            type = ProtocolConstants.TypePhonePose;
        }

        public Vec3Dto position;
        public float yawDeg;
        public string trackingState;
        public string notTrackingReason;
    }

    /// <summary>
    /// The entire current scene. Sent after every structural mutation: floor
    /// lock, corner add/remove, room height update, opening add/remove/edit,
    /// furniture add/remove/edit, and finalization.
    /// </summary>
    [Serializable]
    public sealed class SceneSnapshotMessage : WireMessageHeader
    {
        public SceneSnapshotMessage()
        {
            type = ProtocolConstants.TypeSceneSnapshot;
        }

        public SceneSnapshot snapshot;
    }

    /// <summary>
    /// Marks the handover of authority from scanner to viewer.
    ///
    /// The scanner must send the final snapshot immediately BEFORE this message,
    /// so the viewer's last received state is complete when it takes ownership.
    /// </summary>
    [Serializable]
    public sealed class ScanFinalizedMessage : WireMessageHeader
    {
        public ScanFinalizedMessage()
        {
            type = ProtocolConstants.TypeScanFinalized;
        }

        public int finalRevision;
    }
}
