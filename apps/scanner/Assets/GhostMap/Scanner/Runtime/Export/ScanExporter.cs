using System;
using System.Diagnostics;
using GhostMap.Shared.Domain;

namespace GhostMap.Scanner.Export
{
    /// <summary>The outcome of exporting one finalized room.</summary>
    public readonly struct ExportResult
    {
        public ExportResult(bool succeeded, SavedScan saved, string error, long elapsedMs)
        {
            Succeeded = succeeded;
            Saved = saved;
            Error = error ?? string.Empty;
            ElapsedMs = elapsedMs;
        }

        public bool Succeeded { get; }
        public SavedScan Saved { get; }
        public string Error { get; }

        /// <summary>Time to build and save the bundle (spec: under 2 s on the phone).</summary>
        public long ElapsedMs { get; }
    }

    /// <summary>
    /// Task R6: on finalize, builds the room's export bundle and keeps it on
    /// the phone. Plain C#: the template, the store and the clock are injected,
    /// so the whole path runs in EditMode tests.
    ///
    /// A failed export never touches the scan itself: the finalized room stays
    /// in memory and the user can try again.
    /// </summary>
    public sealed class ScanExporter
    {
        private readonly Func<string> templateProvider;
        private readonly SavedScanStore store;
        private readonly Func<DateTime> clock;

        public ScanExporter(Func<string> templateProvider, SavedScanStore store, Func<DateTime> clock = null)
        {
            this.templateProvider = templateProvider ?? throw new ArgumentNullException(nameof(templateProvider));
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            this.clock = clock ?? (() => DateTime.Now);
        }

        public SavedScanStore Store => store;

        public ExportResult Export(SceneSnapshot finalized)
        {
            Stopwatch timer = Stopwatch.StartNew();

            string template;
            try
            {
                template = templateProvider();
            }
            catch (Exception exception)
            {
                return new ExportResult(false, default, $"The room viewer template could not be loaded: {exception.Message}", timer.ElapsedMilliseconds);
            }

            if (!ExportBundleBuilder.TryBuild(finalized, template, clock(), out byte[] zip, out string fileName, out string error))
            {
                return new ExportResult(false, default, error, timer.ElapsedMilliseconds);
            }

            if (!store.TrySave(zip, fileName, out SavedScan saved, out error))
            {
                return new ExportResult(false, default, error, timer.ElapsedMilliseconds);
            }

            return new ExportResult(true, saved, string.Empty, timer.ElapsedMilliseconds);
        }
    }
}
