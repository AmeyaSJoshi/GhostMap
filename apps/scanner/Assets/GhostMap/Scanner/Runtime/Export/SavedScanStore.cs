using System;
using System.Collections.Generic;
using System.IO;

namespace GhostMap.Scanner.Export
{
    /// <summary>One saved room on the phone.</summary>
    public readonly struct SavedScan
    {
        public SavedScan(string fileName, string path, long sizeBytes, DateTime savedUtc)
        {
            FileName = fileName;
            Path = path;
            SizeBytes = sizeBytes;
            SavedUtc = savedUtc;
        }

        public string FileName { get; }
        public string Path { get; }
        public long SizeBytes { get; }
        public DateTime SavedUtc { get; }
    }

    /// <summary>
    /// Task R6: keeps every finalized room's export bundle on the phone so it
    /// can be sent again (ADR-0013 section 1). The real directory is
    /// <c>Application.persistentDataPath/scans/</c>; tests inject a temporary
    /// one.
    ///
    /// Writes are atomic: the zip is written to a temporary file and moved into
    /// place, so a full disk or a crash never leaves a half-written room that
    /// looks complete.
    /// </summary>
    public sealed class SavedScanStore
    {
        public const string Extension = ".zip";
        private const string TempSuffix = ".tmp";

        public SavedScanStore(string directory)
        {
            if (string.IsNullOrEmpty(directory))
            {
                throw new ArgumentException("A directory is required.", nameof(directory));
            }

            Directory = directory;
        }

        public string Directory { get; }

        /// <summary>
        /// Saves a bundle. If a file of that name exists (two exports in the
        /// same minute), a numeric suffix keeps both.
        /// </summary>
        public bool TrySave(byte[] zip, string fileName, out SavedScan saved, out string error)
        {
            saved = default;

            if (zip == null || zip.Length == 0)
            {
                error = "There is nothing to save.";
                return false;
            }

            if (!IsPlainFileName(fileName) || !fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            {
                error = $"'{fileName}' is not a valid saved-room name.";
                return false;
            }

            string temp = null;

            try
            {
                System.IO.Directory.CreateDirectory(Directory);

                string path = UniquePath(fileName);
                temp = path + TempSuffix;

                File.WriteAllBytes(temp, zip);
                File.Move(temp, path);
                temp = null;

                var info = new FileInfo(path);
                saved = new SavedScan(info.Name, info.FullName, info.Length, info.LastWriteTimeUtc);
                error = string.Empty;
                return true;
            }
            catch (IOException exception)
            {
                error = IsDiskFull(exception)
                    ? "Not enough space on the iPhone to save this room."
                    : $"The room could not be saved: {exception.Message}";
                return false;
            }
            catch (UnauthorizedAccessException exception)
            {
                error = $"The room could not be saved: {exception.Message}";
                return false;
            }
            finally
            {
                if (temp != null)
                {
                    TryDelete(temp);
                }
            }
        }

        /// <summary>Saved rooms, newest first. Never throws; an unreadable folder lists as empty.</summary>
        public IReadOnlyList<SavedScan> List()
        {
            var scans = new List<SavedScan>();

            try
            {
                if (!System.IO.Directory.Exists(Directory))
                {
                    return scans;
                }

                foreach (string path in System.IO.Directory.GetFiles(Directory, "*" + Extension))
                {
                    var info = new FileInfo(path);
                    scans.Add(new SavedScan(info.Name, info.FullName, info.Length, info.LastWriteTimeUtc));
                }
            }
            catch (IOException)
            {
                return scans;
            }
            catch (UnauthorizedAccessException)
            {
                return scans;
            }

            scans.Sort((a, b) =>
            {
                int byTime = b.SavedUtc.CompareTo(a.SavedUtc);
                return byTime != 0 ? byTime : string.CompareOrdinal(b.FileName, a.FileName);
            });

            return scans;
        }

        /// <summary>Deletes one saved room by file name. False if it was not there.</summary>
        public bool Delete(string fileName)
        {
            if (!IsPlainFileName(fileName))
            {
                return false;
            }

            string path = Path.Combine(Directory, fileName);

            if (!File.Exists(path))
            {
                return false;
            }

            return TryDelete(path);
        }

        private string UniquePath(string fileName)
        {
            string stem = Path.GetFileNameWithoutExtension(fileName);
            string path = Path.Combine(Directory, fileName);

            for (int n = 2; File.Exists(path); n++)
            {
                path = Path.Combine(Directory, $"{stem}-{n}{Extension}");
            }

            return path;
        }

        /// <summary>A bare file name: no folders, no "..", nothing that escapes the store.</summary>
        private static bool IsPlainFileName(string fileName)
        {
            return !string.IsNullOrEmpty(fileName)
                && fileName == Path.GetFileName(fileName)
                && fileName != "." && fileName != ".."
                && fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        private static bool IsDiskFull(IOException exception)
        {
            // ENOSPC on iOS/macOS/Linux; ERROR_DISK_FULL / ERROR_HANDLE_DISK_FULL on Windows.
            int code = exception.HResult & 0xFFFF;
            return code == 28 || code == 0x70 || code == 0x27
                || exception.Message.IndexOf("space", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool TryDelete(string path)
        {
            try
            {
                File.Delete(path);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
