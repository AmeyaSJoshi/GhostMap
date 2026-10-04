// RESEARCH SPIKE — NOT PRODUCTION. Deliberately plain research UI.
import ARKit
import SceneKit
import SwiftUI

struct ARViewContainer: UIViewRepresentable {
    let recorder: Recorder

    func makeUIView(context: Context) -> ARSCNView {
        let view = ARSCNView()
        view.session = recorder.session
        view.automaticallyUpdatesLighting = false
        recorder.start()
        return view
    }

    func updateUIView(_ uiView: ARSCNView, context: Context) {}
}

struct ContentView: View {
    @StateObject private var recorder = Recorder()

    var body: some View {
        ZStack {
            ARViewContainer(recorder: recorder).ignoresSafeArea()

            // Crosshair: aim at open floor when locking.
            Image(systemName: "plus").font(.system(size: 28, weight: .light)).foregroundStyle(.white)

            VStack(alignment: .leading, spacing: 4) {
                Text("Tracking: \(recorder.trackingText)")
                Text(recorder.floorLocked
                     ? String(format: "Floor: LOCKED · camera %.2f m above floor", recorder.cameraHeight)
                     : "Floor: not locked — aim crosshair at open floor, tap Lock Floor")
                Text(String(format: "Pitch %+.0f°  ·  %@", recorder.pitchDeg, recorder.floorHint))
                Text("Frames saved: \(recorder.savedCount)  ·  coverage \(recorder.coveredDegrees)°/360°")
                CoverageBar(bins: recorder.coverage)
                if !recorder.status.isEmpty { Text(recorder.status).foregroundStyle(.yellow) }
            }
            .font(.system(size: 13, design: .monospaced))
            .foregroundStyle(.white)
            .padding(10)
            .background(.black.opacity(0.55), in: RoundedRectangle(cornerRadius: 10))
            .frame(maxHeight: .infinity, alignment: .top)
            .padding(.top, 8)
            .padding(.horizontal, 12)

            HStack(spacing: 12) {
                Button("Lock Floor") { recorder.lockFloor() }
                    .disabled(recorder.recording)
                Button(recorder.recording ? "Stop" : "Start") {
                    recorder.recording ? recorder.stopRecording() : recorder.startRecording()
                }
                .disabled(!recorder.floorLocked)
                .tint(recorder.recording ? .red : .green)
            }
            .buttonStyle(.borderedProminent)
            .controlSize(.large)
            .frame(maxHeight: .infinity, alignment: .bottom)
            .padding(.bottom, 30)
        }
    }
}

/// 36 cells, one per 10 degrees of heading around the locked floor's forward.
struct CoverageBar: View {
    let bins: [Bool]

    var body: some View {
        HStack(spacing: 1) {
            ForEach(bins.indices, id: \.self) { i in
                Rectangle().fill(bins[i] ? Color.green : Color.white.opacity(0.25)).frame(width: 7, height: 10)
            }
        }
    }
}
