// Task R7 (ADR-0013 section 2): Send to Computer is the iOS share sheet.
//
// One C function, called from C# (IosShareSheet) through [DllImport("__Internal")].
// It presents UIActivityViewController with one file (the saved room's zip) and
// reports how it ended back to a Unity GameObject with UnitySendMessage, as
//
//   "<1 if completed, else 0>|<activity type, or empty>|<error, or empty>"
//
// which ShareResult.TryParse reads. Written in this repository on purpose
// rather than pulled in as a package (AGENTS.md rule 10).

#import <UIKit/UIKit.h>

extern "C" UIViewController* UnityGetGLViewController(void);
extern "C" void UnitySendMessage(const char* obj, const char* method, const char* msg);

static NSString* GhostMapString(const char* value)
{
    return value != NULL ? [NSString stringWithUTF8String:value] : @"";
}

extern "C" void GhostMapShare_ShareFile(const char* path, const char* callbackObject, const char* callbackMethod)
{
    // Copy the C strings now: Unity frees them when this call returns.
    NSString* filePath = GhostMapString(path);
    NSString* object = GhostMapString(callbackObject);
    NSString* method = GhostMapString(callbackMethod);

    void (^report)(BOOL, NSString*, NSString*) = ^(BOOL completed, NSString* activity, NSString* error) {
        NSString* message = [NSString stringWithFormat:@"%d|%@|%@",
                             completed ? 1 : 0, activity ?: @"", error ?: @""];
        UnitySendMessage(object.UTF8String, method.UTF8String, message.UTF8String);
    };

    dispatch_async(dispatch_get_main_queue(), ^{
        if (![[NSFileManager defaultManager] fileExistsAtPath:filePath])
        {
            report(NO, nil, @"The saved room file is missing.");
            return;
        }

        UIViewController* presenter = UnityGetGLViewController();
        while (presenter.presentedViewController != nil)
        {
            presenter = presenter.presentedViewController;
        }

        if (presenter == nil)
        {
            report(NO, nil, @"There is no screen to show the share sheet on.");
            return;
        }

        NSURL* url = [NSURL fileURLWithPath:filePath];
        UIActivityViewController* sheet =
            [[UIActivityViewController alloc] initWithActivityItems:@[ url ] applicationActivities:nil];

        sheet.completionWithItemsHandler = ^(UIActivityType activityType, BOOL completed, NSArray* returnedItems, NSError* activityError) {
            report(completed, activityType, activityError.localizedDescription);
        };

        // On iPad the sheet is a popover and must be anchored, or UIKit throws.
        UIPopoverPresentationController* popover = sheet.popoverPresentationController;
        if (popover != nil)
        {
            CGRect bounds = presenter.view.bounds;
            popover.sourceView = presenter.view;
            popover.sourceRect = CGRectMake(CGRectGetMidX(bounds), CGRectGetMidY(bounds), 1.0, 1.0);
            popover.permittedArrowDirections = 0;
        }

        [presenter presentViewController:sheet animated:YES completion:nil];
    });
}
