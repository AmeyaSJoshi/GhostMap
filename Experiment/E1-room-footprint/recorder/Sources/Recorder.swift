// RESEARCH SPIKE — NOT PRODUCTION.
//
// Records what E1's offline analysis needs to turn a pixel into a floor point:
// the captured image (sensor orientation, unrotated), the per-frame intrinsics
// for exactly that image size, the ARKit camera transform, tracking state, and
// the floor locked once — exactly like GhostMap's floor lock (center raycast
// against a detected horizontal plane; Ghost forward = camera forward projected
// onto the floor). It records too much on purpose: every ARKit pose while
// recording (poses.jsonl) and an image every 0.2 s (frames.jsonl + frames/),
// so keyframe selection and quality gates can be changed offline.
//
// Matrices are written as ROWS (row-major), converted explicitly from simd's
// column-major storage. Units: meters, seconds.

import ARKit
import CoreImage
import Foundation
import UIKit

final class Recorder: NSObject, ObservableObject, ARSessionDelegate {
    let session = ARSession()

    @Published var trackingText = "starting"
    @Published var floorLocked = false
    @Published var recording = false
    @Published var savedCount = 0
    @Published var coverage = [Bool](repeating: false, count: 36)
    @Published var pitchDeg: Double = 0
    @Published var floorHint = ""
    @Published var cameraHeight: Double = 0
    @Published var status = ""

    var coveredDegrees: Int { coverage.filter { $0 }.count * 10 }

    static let saveInterval: TimeInterval = 0.2

    private let arQueue = DispatchQueue(label: "e1.ar")
    private let ioQueue = DispatchQueue(label: "e1.io")
    private let ciContext = CIContext()
    private let encodeLock = NSLock()
    private var encoding = false

    // Floor lock (touched only on arQueue).
    private var floorY: Float = 0
    private var ghostOrigin = SIMD3<Float>(repeating: 0)
    private var ghostForward = SIMD3<Float>(0, 0, -1)
    private var ghostRight = SIMD3<Float>(1, 0, 0)
    private var floorAnchorId: UUID?
    private var lockRecord: [String: Any] = [:]

    // Recording state (arQueue for decisions, ioQueue for files).
    private var scanDir: URL?
    private var framesHandle: FileHandle?
    private var posesHandle: FileHandle?
    private var lastSaveTime: TimeInterval = 0
    private var frameIndex = 0
    private var poseCount = 0
    private var startedAt = Date()
    private var lastUiUpdate: TimeInterval = 0
    private var videoFormat: [String: Any] = [:]

    func start() {
        session.delegate = self
        session.delegateQueue = arQueue
        let config = ARWorldTrackingConfiguration()
        config.planeDetection = [.horizontal]
        videoFormat = [
            "width": Int(config.videoFormat.imageResolution.width),
            "height": Int(config.videoFormat.imageResolution.height),
            "fps": config.videoFormat.framesPerSecond,
        ]
        session.run(config)
        DispatchQueue.main.async { UIApplication.shared.isIdleTimerDisabled = true }
    }

    // MARK: - Floor lock

    func lockFloor() {
        arQueue.async { [self] in
            guard let frame = session.currentFrame else { return publishStatus("No camera frame yet.") }
            guard case .normal = frame.camera.trackingState else { return publishStatus("Tracking is not normal yet — move the phone slowly.") }
            // Center of the captured image is the center of the screen in any orientation.
            let query = frame.raycastQuery(from: CGPoint(x: 0.5, y: 0.5), allowing: .existingPlaneGeometry, alignment: .horizontal)
            let camPos = frame.camera.transform.columns.3
            let hit = session.raycast(query).first { result in
                guard let plane = result.anchor as? ARPlaneAnchor, plane.alignment == .horizontal else { return false }
                return camPos.y - result.worldTransform.columns.3.y > 0.5  // at least 0.5 m below the camera
            }
            guard let hit, let plane = hit.anchor as? ARPlaneAnchor else {
                return publishStatus("No floor plane under the crosshair. Move the phone a little over open floor, then retry.")
            }
            let p = hit.worldTransform.columns.3
            let origin = SIMD3<Float>(p.x, p.y, p.z)
            let camZ = frame.camera.transform.columns.2
            var fwd = SIMD3<Float>(-camZ.x, 0, -camZ.z)
            guard simd_length(fwd) > 1e-3 else { return publishStatus("Don't point straight down when locking.") }
            fwd = simd_normalize(fwd)
            // Right-handed ARKit world: physical right = forward x up (Unity's cross(up, forward) in its left-handed world).
            let right = simd_cross(fwd, SIMD3<Float>(0, 1, 0))

            floorY = origin.y
            ghostOrigin = origin
            ghostForward = fwd
            ghostRight = right
            floorAnchorId = plane.identifier
            lockRecord = [
                "timestamp": frame.timestamp,
                "hitWorld": [origin.x, origin.y, origin.z],
                "cameraTransform": Self.rows(frame.camera.transform),
                "planeAnchorId": plane.identifier.uuidString,
                "planeClassification": Self.name(plane.classification),
                "planeTransform": Self.rows(plane.transform),
                "planeExtent": [plane.planeExtent.width, plane.planeExtent.height],
                "ghostFrame": [
                    "origin": [origin.x, origin.y, origin.z],
                    "right": [right.x, right.y, right.z],
                    "up": [0, 1, 0],
                    "forward": [fwd.x, fwd.y, fwd.z],
                ],
            ]
            DispatchQueue.main.async {
                self.floorLocked = true
                self.coverage = [Bool](repeating: false, count: 36)
                self.status = "Floor locked. Stand near the room's middle, then Start."
            }
        }
    }

    // MARK: - Recording

    func startRecording() {
        arQueue.async { [self] in
            guard !lockRecord.isEmpty else { return }
            let stamp = DateFormatter.e1.string(from: Date())
            let docs = FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0]
            let dir = docs.appendingPathComponent("scans/scan_\(stamp)", isDirectory: true)
            do {
                try FileManager.default.createDirectory(at: dir.appendingPathComponent("frames"), withIntermediateDirectories: true)
                FileManager.default.createFile(atPath: dir.appendingPathComponent("frames.jsonl").path, contents: nil)
                FileManager.default.createFile(atPath: dir.appendingPathComponent("poses.jsonl").path, contents: nil)
                framesHandle = try FileHandle(forWritingTo: dir.appendingPathComponent("frames.jsonl"))
                posesHandle = try FileHandle(forWritingTo: dir.appendingPathComponent("poses.jsonl"))
            } catch {
                return publishStatus("Could not create scan folder: \(error.localizedDescription)")
            }
            scanDir = dir
            frameIndex = 0
            poseCount = 0
            lastSaveTime = 0
            startedAt = Date()
            writeMetadata(finished: false)
            DispatchQueue.main.async {
                self.recording = true
                self.savedCount = 0
                self.coverage = [Bool](repeating: false, count: 36)
                self.status = "Recording \(dir.lastPathComponent) — turn slowly"
            }
        }
    }

    func stopRecording() {
        arQueue.async { [self] in
            guard let dir = scanDir else { return }
            scanDir = nil
            ioQueue.async { [self] in
                try? framesHandle?.close()
                try? posesHandle?.close()
                framesHandle = nil
                posesHandle = nil
                writeMetadata(finished: true, dir: dir)
                DispatchQueue.main.async {
                    self.recording = false
                    self.status = "Saved \(dir.lastPathComponent) (\(self.savedCount) frames)"
                }
            }
        }
    }

    private func writeMetadata(finished: Bool, dir: URL? = nil) {
        guard let dir = dir ?? scanDir else { return }
        var sys = utsname()
        uname(&sys)
        let machine = withUnsafePointer(to: &sys.machine) {
            $0.withMemoryRebound(to: CChar.self, capacity: 1) { String(cString: $0) }
        }
        let meta: [String: Any] = [
            "format": "ghostmap-e1-scan-v1",
            "note": "RESEARCH SPIKE — NOT PRODUCTION",
            "finished": finished,
            "device": machine,
            "systemVersion": UIDevice.current.systemVersion,
            "startedAt": ISO8601DateFormatter().string(from: startedAt),
            "saveIntervalS": Self.saveInterval,
            "videoFormat": videoFormat,
            "imageOrientation": "sensor (landscape), unrotated; UI held portrait",
            "matrixLayout": "row-major rows; camera: +x right, +y up, +z toward viewer (looks down -z) in sensor orientation",
            "floorLock": lockRecord,
            "framesSaved": frameIndex,
            "posesLogged": poseCount,
        ]
        if let data = try? JSONSerialization.data(withJSONObject: meta, options: [.prettyPrinted, .sortedKeys]) {
            try? data.write(to: dir.appendingPathComponent("metadata.json"))
        }
    }

    // MARK: - Per frame

    func session(_ session: ARSession, didUpdate frame: ARFrame) {
        let cam = frame.camera
        let t = cam.transform
        let fwd = SIMD3<Float>(-t.columns.2.x, -t.columns.2.y, -t.columns.2.z)
        let pitch = Double(asin(max(-1, min(1, fwd.y)))) * 180 / .pi
        let tracking = Self.name(cam.trackingState)

        var yawBin: Int?
        var camHeight: Double = 0
        if !lockRecord.isEmpty {
            camHeight = Double(t.columns.3.y - floorY)
            let gx = simd_dot(fwd, ghostRight)
            let gz = simd_dot(fwd, ghostForward)
            var yaw = atan2(Double(gx), Double(gz)) * 180 / .pi
            if yaw < 0 { yaw += 360 }
            yawBin = min(35, Int(yaw / 10))
        }

        if let dir = scanDir {
            poseCount += 1
            let poseLine = Self.json(["t": frame.timestamp, "T": Self.rows(t), "tracking": tracking])
            ioQueue.async { [self] in posesHandle?.write(poseLine) }

            if frame.timestamp - lastSaveTime >= Self.saveInterval && tryBeginEncode() {
                lastSaveTime = frame.timestamp
                let index = frameIndex
                frameIndex += 1
                let file = String(format: "frames/%06d.jpg", index)
                var meta: [String: Any] = [
                    "i": index,
                    "file": file,
                    "t": frame.timestamp,
                    "w": Int(cam.imageResolution.width),
                    "h": Int(cam.imageResolution.height),
                    "K": Self.rows(cam.intrinsics),
                    "T": Self.rows(t),
                    "tracking": tracking,
                    "exposureDurationS": cam.exposureDuration,
                    "exposureOffset": cam.exposureOffset,
                    "featurePoints": frame.rawFeaturePoints?.points.count ?? 0,
                    "pitchDeg": pitch,
                    "cameraHeightM": camHeight,
                ]
                if let light = frame.lightEstimate {
                    meta["ambientIntensity"] = light.ambientIntensity
                    meta["ambientColorTemperature"] = light.ambientColorTemperature
                }
                if let id = floorAnchorId, let plane = frame.anchors.first(where: { $0.identifier == id }) as? ARPlaneAnchor {
                    meta["floorPlaneTransform"] = Self.rows(plane.transform)
                    meta["floorPlaneExtent"] = [plane.planeExtent.width, plane.planeExtent.height]
                }
                let buffer = frame.capturedImage
                let isNormal = tracking == "normal"
                ioQueue.async { [self] in
                    defer { endEncode() }
                    let image = CIImage(cvPixelBuffer: buffer)
                    let opts = [CIImageRepresentationOption(rawValue: kCGImageDestinationLossyCompressionQuality as String): 0.9]
                    guard let jpg = ciContext.jpegRepresentation(of: image, colorSpace: CGColorSpace(name: CGColorSpace.sRGB)!, options: opts) else { return }
                    try? jpg.write(to: dir.appendingPathComponent(file))
                    framesHandle?.write(Self.json(meta))
                    DispatchQueue.main.async {
                        self.savedCount += 1
                        if isNormal, let b = yawBin { self.coverage[b] = true }
                    }
                }
            }
        }

        if frame.timestamp - lastUiUpdate > 0.1 {
            lastUiUpdate = frame.timestamp
            // Portrait: the image's long side is vertical. Bottom edge of view = pitch - half vertical FOV.
            let halfV = atan(Double(max(cam.imageResolution.width, cam.imageResolution.height)) / 2 / Double(cam.intrinsics[0][0])) * 180 / .pi
            let below = halfV - pitch  // degrees below horizon at the bottom of the image
            var hint = "floor not visible"
            if camHeight > 0.3, below > 1 {
                hint = String(format: "floor visible beyond %.1f m", camHeight / tan(below * .pi / 180))
            }
            DispatchQueue.main.async {
                self.trackingText = tracking
                self.pitchDeg = pitch
                self.cameraHeight = camHeight
                self.floorHint = hint
            }
        }
    }

    func sessionWasInterrupted(_ session: ARSession) { publishStatus("Session interrupted.") }

    func session(_ session: ARSession, didFailWithError error: Error) { publishStatus("AR error: \(error.localizedDescription)") }

    // MARK: - Helpers

    private func tryBeginEncode() -> Bool {
        encodeLock.lock(); defer { encodeLock.unlock() }
        if encoding { return false }  // never hold more than one ARFrame buffer
        encoding = true
        return true
    }

    private func endEncode() {
        encodeLock.lock(); encoding = false; encodeLock.unlock()
    }

    private func publishStatus(_ s: String) { DispatchQueue.main.async { self.status = s } }

    static func rows(_ m: simd_float4x4) -> [[Float]] {
        (0..<4).map { r in [m.columns.0[r], m.columns.1[r], m.columns.2[r], m.columns.3[r]] }
    }

    static func rows(_ m: simd_float3x3) -> [[Float]] {
        (0..<3).map { r in [m.columns.0[r], m.columns.1[r], m.columns.2[r]] }
    }

    static func json(_ obj: [String: Any]) -> Data {
        var d = (try? JSONSerialization.data(withJSONObject: obj, options: [.sortedKeys])) ?? Data()
        d.append(0x0A)
        return d
    }

    static func name(_ state: ARCamera.TrackingState) -> String {
        switch state {
        case .normal: return "normal"
        case .notAvailable: return "notAvailable"
        case .limited(let r):
            switch r {
            case .initializing: return "limited:initializing"
            case .excessiveMotion: return "limited:excessiveMotion"
            case .insufficientFeatures: return "limited:insufficientFeatures"
            case .relocalizing: return "limited:relocalizing"
            @unknown default: return "limited:unknown"
            }
        }
    }

    static func name(_ c: ARPlaneAnchor.Classification) -> String {
        switch c {
        case .floor: return "floor"
        case .wall: return "wall"
        case .ceiling: return "ceiling"
        case .table: return "table"
        case .seat: return "seat"
        case .door: return "door"
        case .window: return "window"
        case .none(let s): return "none:\(s)"
        @unknown default: return "unknown"
        }
    }
}

extension DateFormatter {
    static let e1: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "yyyyMMdd_HHmmss"
        f.locale = Locale(identifier: "en_US_POSIX")
        return f
    }()
}
