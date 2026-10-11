# RESEARCH SPIKE — NOT PRODUCTION

Throwaway scripts used by the 2026-10-03 perception audit
(`../REPORT.md`). Nothing here is referenced by the Scanner, Viewer or shared
package, and nothing here should be.

| Script | Question it answered |
| --- | --- |
| `vision_taxonomy.swift` | Which indoor labels does Apple's built-in `VNClassifyImageRequest` know? (1,303 labels, revision 2; includes desk, cabinet, bookshelf, lamp, closet, sofa, armchair, bed, television, computer_monitor, whiteboard, cardboard_box, classroom, bedroom.) |
| `inspect.swift` | Exact input/output shapes of the two Apple Core ML models, and their latency on the dev Mac's Neural Engine vs GPU. |
| `labels.swift` | The real class list baked into the DETR segmentation `.mlpackage`, and the depth model's output pixel format. |

Run: `swiftc -O <file>.swift -o out && ./out [model.mlpackage ...]` (macOS, Xcode 26.6).

## Model weights used (NOT committed — kept in the session scratchpad)

| Model | Source | Exact file | License | Size | SHA-256 of `weight.bin` |
| --- | --- | --- | --- | --- | --- |
| DETR-ResNet50 semantic segmentation, F16 | `huggingface.co/apple/coreml-detr-semantic-segmentation` (main) | `DETRResnet50SemanticSegmentationF16.mlpackage` | Apache-2.0 (model card); trained on COCO (annotations CC BY 4.0, images Flickr terms) | 85.2 MB weights | `2f4de3dfed1aeced35ad7096d7c8721fcfb59571110a9e9138d73a7e56480408` |
| Depth Anything V2 Small, F16 | `huggingface.co/apple/coreml-depth-anything-v2-small` (main) | `DepthAnythingV2SmallF16.mlpackage` | Apache-2.0 (model card) | 49.4 MB weights | `fa60d9b6a155734f59029ebb882fd54e549bfaee3539c1a9cbd2cbbab64a0fed` |

Both hashes match the Hugging Face LFS object ids.

## Measured on the dev Mac (Apple M5, macOS 26.6.2) — NOT an iPhone number

| Model | Input | Output | `.cpuAndNeuralEngine` p50 | `.cpuAndGPU` p50 | First ANE load |
| --- | --- | --- | --- | --- | --- |
| DETR semantic seg F16 | 448×448 BGRA image | `semanticPredictions` Int32 448×448 (133 COCO-panoptic classes) | 26.3 ms | 37.5 ms | 5.4 s (compile, then cached) |
| Depth Anything V2 Small F16 | 518×392 BGRA image | `depth` Float16 (`OneComponent16Half`) 518×392 | 20.6 ms | 18.2 ms | 5.3 s |

Random-pixel input, 20 timed runs after one warm-up. This measures speed only, not quality.
