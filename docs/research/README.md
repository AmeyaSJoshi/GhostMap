# Research

Throwaway experiments from the 2026-10-03 hackathon. **Not production code.**
Nothing in the Scanner, Viewer or shared package references anything here, and
nothing should.

| Folder | Question it answered |
| --- | --- |
| `E1-room-footprint/` | Can walls be found from the floor boundary in camera images? A recorder app, a Core ML segmentation tool and a Python analysis pipeline. Finding: in a furnished lab only 6-8% of floor-visible columns show an unobstructed wall/floor junction, so floor-boundary walls cannot be the primary signal. This is why the automatic room scan uses ARKit wall planes instead |
| `perception-audit/` | What can Apple's on-device models see? Apple's built-in image classifier labels, DETR-ResNet50 segmentation and Depth Anything V2 Small, timed on the dev Mac. Useful background for the YOLO-n work (ADR-0011) |

Model weights were never committed. `perception-audit/spikes/README.md` refers
to a `REPORT.md` that was also never committed.
