using System;
using System.Collections.Generic;
using GhostMap.Scanner.Share;

namespace GhostMap.Scanner.Tests.EditMode
{
    /// <summary>Records what was shared; the test finishes the sheet by hand.</summary>
    internal sealed class FakeShareSheet : IShareSheet
    {
        private Action<ShareResult> pending;

        public bool IsAvailable { get; set; } = true;

        public List<string> SharedPaths { get; } = new List<string>();

        public bool IsOpen => pending != null;

        public void Share(string filePath, Action<ShareResult> onFinished)
        {
            SharedPaths.Add(filePath);
            pending = onFinished;
        }

        public void Finish(ShareResult result)
        {
            Action<ShareResult> callback = pending;
            pending = null;
            callback?.Invoke(result);
        }
    }
}
