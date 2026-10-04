// RESEARCH SPIKE — NOT PRODUCTION.
import CoreML
import Foundation
for p in CommandLine.arguments.dropFirst() {
  let m = try MLModel(contentsOf: try MLModel.compileModel(at: URL(fileURLWithPath: p)))
  let d = m.modelDescription
  for (k, v) in d.outputDescriptionsByName { if let c = v.imageConstraint { print(k, "pixelFormat FourCC:", String(format: "%08x", c.pixelFormatType), c.pixelsWide, c.pixelsHigh) } }
  for (k, v) in d.inputDescriptionsByName { if let c = v.imageConstraint { print(k, "in pixelFormat:", String(format: "%08x", c.pixelFormatType), "sizes:", c.sizeConstraint.type.rawValue) } }
  if let s = (d.metadata[MLModelMetadataKey.creatorDefinedKey] as? [String: String])?["com.apple.coreml.model.preview.params"],
     let j = try? JSONSerialization.jsonObject(with: Data(s.utf8)) as? [String: Any], let labels = j["labels"] as? [String] {
    let real = labels.enumerated().filter { $0.element != "--" }
    print("non-placeholder labels:", real.count, "of", labels.count)
    print(real.map { "\($0.offset):\($0.element)" }.joined(separator: ", "))
  }
}
