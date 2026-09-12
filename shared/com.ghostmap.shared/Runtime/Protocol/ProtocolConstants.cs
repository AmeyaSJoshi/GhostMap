namespace GhostMap.Shared.Protocol
{
    /// <summary>
    /// Frozen protocol v1 constants. Changing any value here is a breaking wire
    /// change requiring an ADR, a protocol version increment, and contract tests.
    /// </summary>
    public static class ProtocolConstants
    {
        public const int ProtocolVersion = 1;

        /// <summary>The scene schema version this protocol version carries.</summary>
        public const int SchemaVersion = 1;

        public const int Port = 47831;

        /// <summary>
        /// Maximum accepted line length. A longer line is rejected rather than
        /// buffered, so a malformed or hostile peer cannot exhaust memory.
        /// </summary>
        public const int MaxLineLengthBytes = 262144;

        /// <summary>Every serialized message is terminated with this.</summary>
        public const string LineTerminator = "\n";

        // Message types.
        public const string TypeHello = "hello";
        public const string TypeHeartbeat = "heartbeat";
        public const string TypePhonePose = "phone.pose";
        public const string TypeSceneSnapshot = "scene.snapshot";
        public const string TypeScanFinalized = "scan.finalized";

        // Timing.
        public const float HeartbeatIntervalSeconds = 2f;
        public const float ReconnectIntervalSeconds = 2f;

        /// <summary>Pose messages are debug only and must not exceed this rate.</summary>
        public const float MaxPoseHz = 5f;
    }
}
