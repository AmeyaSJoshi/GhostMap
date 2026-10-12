// Task R9: a light tap when a capture lands. Unity's only built-in is
// Handheld.Vibrate, which is a long buzz; this is UIKit's light impact.
// Called from C# (IosHaptics) on Unity's main thread, which is UIKit's.

#import <UIKit/UIKit.h>

static UIImpactFeedbackGenerator* gGhostMapImpact = nil;

extern "C" void GhostMapHaptics_LightImpact(void)
{
    if (gGhostMapImpact == nil)
    {
        gGhostMapImpact = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
    }

    [gGhostMapImpact impactOccurred];
    [gGhostMapImpact prepare];
}
