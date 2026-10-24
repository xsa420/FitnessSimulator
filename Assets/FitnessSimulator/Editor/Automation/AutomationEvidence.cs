using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FitnessSimulator.Automation
{
    // Test evidence only. Never changes the production checkpoint writer or discards evidence.
    public static class AutomationEvidence
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static readonly HashSet<string> Roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public static string OutputRoot => Environment.GetEnvironmentVariable("FITNESS_RUN_OUTPUT_ROOT") ?? Path.GetTempPath();
        public static long TraceLimit => ReadLimit("FITNESS_TRACE_MAX_BYTES", 16L * 1024 * 1024);
        public static long SnapshotLimit => ReadLimit("FITNESS_SNAPSHOT_MAX_BYTES", 8L * 1024 * 1024);
        public static long SnapshotSetLimit => ReadLimit("FITNESS_SNAPSHOT_SET_MAX_BYTES", 64L * 1024 * 1024);

        public static long ReadLimit(string name, long fallback)
        {
            string value = Environment.GetEnvironmentVariable(name);
            if (value == null) return fallback;
            if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long limit) || limit <= 0)
                throw new InvalidOperationException("InvalidEvidenceBudget: " + name);
            return limit;
        }

        public static void RegisterDirectory(string directory)
        {
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            Directory.CreateDirectory(root);
            lock (Roots) Roots.Add(root);
            ValidateDirectory(root);
        }

        private static string RootFor(string path)
        {
            string full = Path.GetFullPath(path), result = null;
            lock (Roots) foreach (string root in Roots)
                if ((full.Equals(root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) && (result == null || root.Length < result.Length)) result = root;
            if (result == null) throw new InvalidOperationException("UnregisteredEvidenceDirectory: " + path);
            return result;
        }

        public static void ValidateFor(string path) => ValidateDirectory(RootFor(path));

        public static long ValidateDirectory(string directory, string replacingPath = null, long replacementBytes = 0)
        {
            long single = SnapshotLimit, totalLimit = SnapshotSetLimit, total = replacementBytes;
            if (replacementBytes > single) throw new InvalidOperationException("SnapshotByteBudgetExceeded: " + replacingPath);
            string replacement = replacingPath == null ? null : Path.GetFullPath(replacingPath);
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(file) == "trace.jsonl" || string.Equals(Path.GetFullPath(file), replacement, StringComparison.OrdinalIgnoreCase)) continue;
                long bytes = new FileInfo(file).Length;
                if (bytes > single) throw new InvalidOperationException("SnapshotByteBudgetExceeded: " + file);
                total = checked(total + bytes);
                if (total > totalLimit) throw new InvalidOperationException("SnapshotSetByteBudgetExceeded: " + directory);
            }
            if (total > totalLimit) throw new InvalidOperationException("SnapshotSetByteBudgetExceeded: " + directory);
            return total;
        }

        public static void WriteAllText(string path, string contents)
        {
            ValidateDirectory(RootFor(path), path, Utf8.GetByteCount(contents));
            File.WriteAllText(path, contents, Utf8);
        }

        public static void Copy(string source, string destination, bool overwrite = false)
        {
            ValidateDirectory(RootFor(destination), destination, new FileInfo(source).Length);
            File.Copy(source, destination, overwrite);
        }

        public static void WriteAllBytes(string path, byte[] contents)
        {
            ValidateDirectory(RootFor(path), path, contents.LongLength);
            File.WriteAllBytes(path, contents);
        }
    }

    public sealed class EvidenceTrace : IDisposable
    {
        private readonly StreamWriter writer;
        private readonly string path;
        private readonly HashSet<string> views = new HashSet<string>();
        public long Bytes { get; private set; }
        public long ViewReferences { get; private set; }
        public long Limit { get; }

        public EvidenceTrace(string path)
        {
            this.path = path;
            Limit = AutomationEvidence.TraceLimit;
            AutomationEvidence.ValidateFor(path);
            writer = new StreamWriter(path, false, new UTF8Encoding(false));
        }

        public void WriteLine(string line)
        {
            if (line == null) line = "";
            string hash = null;
            bool reference = false;
            if (line.StartsWith("{\"RunId\":", StringComparison.Ordinal) && line.Contains("\"ScreenId\":"))
            {
                using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(line))).Replace("-", "");
                reference = views.Contains(hash);
                line = reference ? "{\"viewRef\":\"" + hash + "\"}" : "{\"viewHash\":\"" + hash + "\",\"view\":" + line + "}";
            }
            long bytes = Encoding.UTF8.GetByteCount(line) + Encoding.UTF8.GetByteCount(writer.NewLine);
            if (bytes > Limit - Bytes) throw new InvalidOperationException("TraceByteBudgetExceeded: " + path + "; limit=" + Limit);
            writer.WriteLine(line);
            Bytes += bytes;
            if (hash != null) { views.Add(hash); if (reference) ViewReferences++; }
        }

        public void Flush() => writer.Flush();
        public void Dispose()
        {
            writer.Dispose();
            AutomationEvidence.ValidateFor(path);
        }
    }
}
