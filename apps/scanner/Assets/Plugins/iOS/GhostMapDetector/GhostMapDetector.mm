// Task R4 (ADR-0011): identify furniture on the phone with YOLO11n.
//
// Runs the Core ML model (StreamingAssets/GhostMap/yolo11n.mlpackage, exported
// with NMS, so Vision returns VNRecognizedObjectObservation) on ARKit's camera
// image. This file only runs Vision and hands back raw facts: each box
// normalized in the upright image (Vision's bottom-left origin), its class
// index and confidence, and the frame's displayTransform. Everything else,
// including mapping boxes to the screen, is C# (DetectionGeometry,
// FurnitureIdentificationController) so it is unit-tested.
//
// One frame at a time: Submit refuses while the previous frame is still being
// processed, so frames are dropped, never queued.

#import <ARKit/ARKit.h>
#import <CoreML/CoreML.h>
#import <Vision/Vision.h>

// AR Foundation's XRCameraFrame.nativePtr on ARKit (UnityXRNativeFrame_1).
typedef struct
{
    int version;
    void* framePtr;
} GhostMapUnityNativeFrame;

// Blittable; mirrored by CoreMlObjectDetector.NativeDetection in C#.
typedef struct
{
    int classIndex;
    float confidence;
    float x;
    float y;
    float width;
    float height;
} GhostMapDetection;

enum { GhostMapIdle = 0, GhostMapLoading = 1, GhostMapReady = 2, GhostMapFailed = -1 };
static const int GhostMapMaxDetections = 64;

static volatile int gState = GhostMapIdle;
static VNCoreMLModel* gModel = nil;
static NSArray<NSString*>* gLabels = nil;
static NSString* gError = @"";

static NSLock* gLock = nil;
static BOOL gBusy = NO;
static GhostMapDetection gResults[GhostMapMaxDetections];
static int gCount = 0;
static long long gFrameId = 0;
static float gTransform[6] = { 1, 0, 0, 1, 0, 0 };
static float gElapsedMs = 0;

static dispatch_queue_t GhostMapQueue(void)
{
    static dispatch_queue_t queue;
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        queue = dispatch_queue_create("com.ghostmap.detector", dispatch_queue_attr_make_with_qos_class(DISPATCH_QUEUE_SERIAL, QOS_CLASS_USER_INITIATED, 0));
        gLock = [[NSLock alloc] init];
    });
    return queue;
}

static void GhostMapFail(NSString* message)
{
    gError = message ?: @"unknown error";
    gState = GhostMapFailed;
    NSLog(@"GhostMap yolo: %@", gError);
}

extern "C" int GhostMapDetector_State(void)
{
    return gState;
}

extern "C" void GhostMapDetector_Load(const char* modelPath)
{
    if (gState == GhostMapLoading || gState == GhostMapReady)
    {
        return;
    }

    NSString* path = modelPath != NULL ? [NSString stringWithUTF8String:modelPath] : @"";
    gState = GhostMapLoading;

    dispatch_async(GhostMapQueue(), ^{
        NSError* error = nil;
        NSURL* compiled = [MLModel compileModelAtURL:[NSURL fileURLWithPath:path] error:&error];
        if (compiled == nil)
        {
            GhostMapFail([NSString stringWithFormat:@"could not compile %@: %@", path, error.localizedDescription]);
            return;
        }

        MLModelConfiguration* config = [[MLModelConfiguration alloc] init];
        config.computeUnits = MLComputeUnitsAll;

        MLModel* model = [MLModel modelWithContentsOfURL:compiled configuration:config error:&error];
        VNCoreMLModel* vision = model != nil ? [VNCoreMLModel modelForMLModel:model error:&error] : nil;
        if (vision == nil)
        {
            GhostMapFail([NSString stringWithFormat:@"could not load the model: %@", error.localizedDescription]);
            return;
        }

        gLabels = [model.modelDescription.classLabels isKindOfClass:[NSArray class]] ? model.modelDescription.classLabels : @[];
        gModel = vision;
        gState = GhostMapReady;
        NSLog(@"GhostMap yolo: model ready, %lu classes", (unsigned long)gLabels.count);
    });
}

extern "C" int GhostMapDetector_LabelCount(void)
{
    return gState == GhostMapReady ? (int)gLabels.count : 0;
}

extern "C" int GhostMapDetector_CopyLabel(int index, char* buffer, int size)
{
    if (gState != GhostMapReady || index < 0 || index >= (int)gLabels.count || buffer == NULL || size <= 0)
    {
        return 0;
    }

    return [gLabels[index] getCString:buffer maxLength:(NSUInteger)size encoding:NSUTF8StringEncoding] ? 1 : 0;
}

extern "C" int GhostMapDetector_CopyError(char* buffer, int size)
{
    return buffer != NULL && size > 0 && [gError getCString:buffer maxLength:(NSUInteger)size encoding:NSUTF8StringEncoding] ? 1 : 0;
}

extern "C" int GhostMapDetector_Submit(void* nativeFrame, float viewportWidth, float viewportHeight)
{
    if (gState != GhostMapReady || nativeFrame == NULL)
    {
        return 0;
    }

    dispatch_queue_t queue = GhostMapQueue();

    [gLock lock];
    if (gBusy)
    {
        [gLock unlock];
        return 0;
    }
    gBusy = YES;
    [gLock unlock];

    // The frame is only valid during this call: take what is needed now.
    GhostMapUnityNativeFrame* unityFrame = (GhostMapUnityNativeFrame*)nativeFrame;
    ARFrame* frame = (__bridge ARFrame*)unityFrame->framePtr;
    if (frame == nil || frame.capturedImage == NULL)
    {
        [gLock lock];
        gBusy = NO;
        [gLock unlock];
        return 0;
    }

    CVPixelBufferRef pixels = CVPixelBufferRetain(frame.capturedImage);
    CGAffineTransform display = [frame displayTransformForOrientation:UIInterfaceOrientationPortrait
                                                          viewportSize:CGSizeMake(viewportWidth, viewportHeight)];

    dispatch_async(queue, ^{
        CFAbsoluteTime start = CFAbsoluteTimeGetCurrent();

        VNCoreMLRequest* request = [[VNCoreMLRequest alloc] initWithModel:gModel];
        request.imageCropAndScaleOption = VNImageCropAndScaleOptionScaleFill;

        // .right: the back camera's landscape sensor image turned upright for
        // a portrait phone, so YOLO sees furniture the right way up.
        VNImageRequestHandler* handler = [[VNImageRequestHandler alloc] initWithCVPixelBuffer:pixels
                                                                                  orientation:kCGImagePropertyOrientationRight
                                                                                      options:@{}];
        NSError* error = nil;
        BOOL ok = [handler performRequests:@[ request ] error:&error];
        CVPixelBufferRelease(pixels);

        GhostMapDetection found[GhostMapMaxDetections];
        int count = 0;

        if (ok)
        {
            for (id result in request.results)
            {
                if (![result isKindOfClass:[VNRecognizedObjectObservation class]] || count >= GhostMapMaxDetections)
                {
                    continue;
                }

                VNRecognizedObjectObservation* observation = result;
                VNClassificationObservation* best = observation.labels.firstObject;
                if (best == nil)
                {
                    continue;
                }

                NSUInteger index = [gLabels indexOfObject:best.identifier];
                CGRect box = observation.boundingBox;
                found[count++] = (GhostMapDetection){
                    index == NSNotFound ? -1 : (int)index,
                    best.confidence,
                    (float)box.origin.x, (float)box.origin.y, (float)box.size.width, (float)box.size.height
                };
            }
        }
        else
        {
            NSLog(@"GhostMap yolo: request failed: %@", error.localizedDescription);
        }

        float elapsed = (float)((CFAbsoluteTimeGetCurrent() - start) * 1000.0);

        [gLock lock];
        memcpy(gResults, found, sizeof(GhostMapDetection) * (size_t)count);
        gCount = count;
        gTransform[0] = (float)display.a;
        gTransform[1] = (float)display.b;
        gTransform[2] = (float)display.c;
        gTransform[3] = (float)display.d;
        gTransform[4] = (float)display.tx;
        gTransform[5] = (float)display.ty;
        gElapsedMs = elapsed;
        gFrameId++;
        gBusy = NO;
        [gLock unlock];
    });

    return 1;
}

extern "C" int GhostMapDetector_Latest(GhostMapDetection* out, int maxCount, long long* frameId, float* transform6, float* elapsedMs)
{
    if (gLock == nil)
    {
        return 0;
    }

    [gLock lock];
    int count = gCount < maxCount ? gCount : maxCount;
    if (out != NULL && count > 0)
    {
        memcpy(out, gResults, sizeof(GhostMapDetection) * (size_t)count);
    }
    if (frameId != NULL)
    {
        *frameId = gFrameId;
    }
    if (transform6 != NULL)
    {
        memcpy(transform6, gTransform, sizeof(gTransform));
    }
    if (elapsedMs != NULL)
    {
        *elapsedMs = gElapsedMs;
    }
    [gLock unlock];
    return count;
}
