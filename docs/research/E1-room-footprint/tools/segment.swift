// RESEARCH SPIKE — NOT PRODUCTION.
//
// Runs Apple's DETRResnet50SemanticSegmentationF16 on every 448x448 PNG in an
// input folder and writes the raw class-id map as an 8-bit grayscale PNG with
// the same name (class ids are 0...200, so they fit). All orientation, tiling
// and geometry lives in the Python analysis, where it is unit-tested; this tool
// only does the one thing Python can't: Core ML on the Neural Engine.
//
// Build: swiftc -O segment.swift -o ../build/segment
// Use:   segment <model.mlpackage> <in_dir> <out_dir>
import CoreML
import Foundation
import ImageIO
import UniformTypeIdentifiers

let args = CommandLine.arguments
guard args.count == 4 else {
    FileHandle.standardError.write("usage: segment <model.mlpackage> <in_dir> <out_dir>\n".data(using: .utf8)!)
    exit(2)
}
let modelURL = URL(fileURLWithPath: args[1])
let inDir = URL(fileURLWithPath: args[2])
let outDir = URL(fileURLWithPath: args[3])
try FileManager.default.createDirectory(at: outDir, withIntermediateDirectories: true)

let cfg = MLModelConfiguration()
cfg.computeUnits = .cpuAndNeuralEngine
let model = try MLModel(contentsOf: try MLModel.compileModel(at: modelURL), configuration: cfg)
let side = 448

func pixelBuffer(from url: URL) -> CVPixelBuffer? {
    guard let src = CGImageSourceCreateWithURL(url as CFURL, nil),
          let img = CGImageSourceCreateImageAtIndex(src, 0, nil),
          img.width == side, img.height == side else { return nil }
    var pb: CVPixelBuffer?
    CVPixelBufferCreate(nil, side, side, kCVPixelFormatType_32BGRA,
                        [kCVPixelBufferCGImageCompatibilityKey: true, kCVPixelBufferCGBitmapContextCompatibilityKey: true] as CFDictionary, &pb)
    guard let pb else { return nil }
    CVPixelBufferLockBaseAddress(pb, [])
    defer { CVPixelBufferUnlockBaseAddress(pb, []) }
    let ctx = CGContext(data: CVPixelBufferGetBaseAddress(pb), width: side, height: side, bitsPerComponent: 8,
                        bytesPerRow: CVPixelBufferGetBytesPerRow(pb), space: CGColorSpace(name: CGColorSpace.sRGB)!,
                        bitmapInfo: CGImageAlphaInfo.premultipliedFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue)
    ctx?.draw(img, in: CGRect(x: 0, y: 0, width: side, height: side))
    return pb
}

func writeLabels(_ arr: MLMultiArray, to url: URL) {
    var bytes = [UInt8](repeating: 0, count: side * side)
    let p = arr.dataPointer.bindMemory(to: Int32.self, capacity: side * side)
    for i in 0..<(side * side) { bytes[i] = UInt8(clamping: p[i]) }
    let provider = CGDataProvider(data: Data(bytes) as CFData)!
    let img = CGImage(width: side, height: side, bitsPerComponent: 8, bitsPerPixel: 8, bytesPerRow: side,
                      space: CGColorSpaceCreateDeviceGray(), bitmapInfo: CGBitmapInfo(rawValue: 0),
                      provider: provider, decode: nil, shouldInterpolate: false, intent: .defaultIntent)!
    let dest = CGImageDestinationCreateWithURL(url as CFURL, UTType.png.identifier as CFString, 1, nil)!
    CGImageDestinationAddImage(dest, img, nil)
    CGImageDestinationFinalize(dest)
}

let files = try FileManager.default.contentsOfDirectory(at: inDir, includingPropertiesForKeys: nil)
    .filter { $0.pathExtension.lowercased() == "png" }
    .sorted { $0.lastPathComponent < $1.lastPathComponent }
var done = 0
let start = Date()
for f in files {
    guard let pb = pixelBuffer(from: f) else {
        print("skip (not \(side)x\(side)): \(f.lastPathComponent)")
        continue
    }
    let out = try model.prediction(from: MLDictionaryFeatureProvider(dictionary: ["image": MLFeatureValue(pixelBuffer: pb)]))
    guard let labels = out.featureValue(for: "semanticPredictions")?.multiArrayValue,
          labels.dataType == .int32, labels.count == side * side else {
        print("unexpected output for \(f.lastPathComponent)")
        exit(1)
    }
    writeLabels(labels, to: outDir.appendingPathComponent(f.lastPathComponent))
    done += 1
}
print("segmented \(done) tiles in \(String(format: "%.1f", Date().timeIntervalSince(start))) s")
