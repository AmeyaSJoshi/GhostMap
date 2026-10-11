// RESEARCH SPIKE — NOT PRODUCTION. Inspects and times Core ML models on this Mac.
import CoreML
import Foundation
func describe(_ path: String, units: MLComputeUnits) throws {
  let url = URL(fileURLWithPath: path)
  let compiled = try MLModel.compileModel(at: url)
  let cfg = MLModelConfiguration(); cfg.computeUnits = units
  let t0 = Date()
  let model = try MLModel(contentsOf: compiled, configuration: cfg)
  print("== \(url.lastPathComponent) load \(String(format: "%.2f", Date().timeIntervalSince(t0)))s units=\(units.rawValue)")
  let d = model.modelDescription
  for (k, v) in d.inputDescriptionsByName { print(" in ", k, v) }
  for (k, v) in d.outputDescriptionsByName { print(" out", k, v) }
  if let labels = d.classLabels { print(" classLabels:", labels.count) }
  let md = d.metadata[MLModelMetadataKey.creatorDefinedKey] as? [String: String] ?? [:]
  for (k, v) in md { print(" meta", k, ":", v.prefix(3000)) }
  // Build a random input matching the first image input and time 20 runs.
  guard let (name, desc) = d.inputDescriptionsByName.first, let c = desc.imageConstraint else { return }
  var pb: CVPixelBuffer?
  CVPixelBufferCreate(nil, c.pixelsWide, c.pixelsHigh, kCVPixelFormatType_32BGRA, nil, &pb)
  let input = try MLDictionaryFeatureProvider(dictionary: [name: MLFeatureValue(pixelBuffer: pb!)])
  _ = try model.prediction(from: input)
  var ts: [Double] = []
  for _ in 0..<20 { let s = Date(); _ = try model.prediction(from: input); ts.append(Date().timeIntervalSince(s) * 1000) }
  ts.sort(); print(" p50 \(String(format: "%.1f", ts[10])) ms  min \(String(format: "%.1f", ts[0])) ms")
}
for p in CommandLine.arguments.dropFirst() {
  for u in [MLComputeUnits.cpuAndNeuralEngine, .cpuAndGPU] { try describe(p, units: u) }
}
