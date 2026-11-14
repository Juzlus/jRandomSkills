using src.utils;
using System.Collections.Concurrent;
using System.Diagnostics;
using static src.jRandomSkills;

namespace src.player
{
    public static class PerfLog
    {
        private static readonly string logsFolder = Path.Combine(Instance.ModuleDirectory, "logs");
        private static readonly string sessionId = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
        private static StreamWriter? _writer;
        private static readonly object _writeLock = new();

        public static bool Enabled => Config.LoadedConfig.PerfMode;

        private static bool _headerWritten;

        public static long Start()
        {
            if (!Enabled) return 0;

            // First write creates the file so an active PerfMode is immediately visible.
            if (!_headerWritten)
            {
                _headerWritten = true;
                Write($"PerfMode enabled (plugin v{Instance.ModuleVersion})");
            }

            long timestamp = Stopwatch.GetTimestamp();
            _starts ??= [];
            if (_starts.Count > 256) _starts.Clear();
            _starts[timestamp] = (GC.GetAllocatedBytesForCurrentThread(), GC.CollectionCount(0));
            return timestamp;
        }

        [ThreadStatic] private static Dictionary<long, (long Alloc, int Gc)>? _starts;

        private static (long Alloc, bool Gc) TakeStart(long startTimestamp)
        {
            if (_starts == null || !_starts.Remove(startTimestamp, out var start)) return (0, false);
            return (GC.GetAllocatedBytesForCurrentThread() - start.Alloc, GC.CollectionCount(0) != start.Gc);
        }

        public static void Info(string message)
        {
            if (!Enabled) return;
            Write(message);
        }

        // One-shot measurement: logs "label took X.XXms" when the elapsed time reaches the threshold.
        public static void End(string label, long startTimestamp, double thresholdMs = 1.0)
        {
            if (startTimestamp == 0 || !Enabled) return;

            double ms = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
            var (alloc, gc) = TakeStart(startTimestamp);
            if (ms < thresholdMs) return;

            Write($"{label} took {ms:F2}ms alloc={alloc / 1024}KB{(gc ? " gc=yes" : "")}");
        }

        private static readonly double[] bucketBoundsMs = [0.05, 0.1, 0.25, 0.5, 1, 2, 4, 8, 16, 32, double.MaxValue];

        private const int maxRawSamples = 4096;

        private sealed class Aggregate
        {
            public double TotalMs;
            public double MaxMs;
            public double MaxMsNoGc;
            public long AllocBytes;
            public int GcSamples;
            public int Count;
            public readonly int[] Buckets = new int[bucketBoundsMs.Length];
            public readonly List<double> Raw = [];
            public DateTime WindowStart = DateTime.Now;

            private bool rawSorted;

            public void Add(double ms, long alloc, bool gc)
            {
                AllocBytes += alloc;
                if (gc) GcSamples++;
                else if (ms > MaxMsNoGc) MaxMsNoGc = ms;
                TotalMs += ms;
                Count++;
                if (ms > MaxMs) MaxMs = ms;

                if (Raw.Count < maxRawSamples) Raw.Add(ms);

                for (int i = 0; i < bucketBoundsMs.Length; i++)
                    if (ms <= bucketBoundsMs[i]) { Buckets[i]++; break; }
            }

            public double Percentile(double fraction)
            {
                if (Count == 0) return 0;

                double value = Raw.Count == Count ? ExactPercentile(fraction) : BucketPercentile(fraction);
                return value > MaxMs ? MaxMs : value;
            }

            private double ExactPercentile(double fraction)
            {
                if (!rawSorted)
                {
                    Raw.Sort();
                    rawSorted = true;
                }

                int index = (int)Math.Ceiling(Raw.Count * fraction) - 1;
                if (index < 0) index = 0;
                if (index >= Raw.Count) index = Raw.Count - 1;
                return Raw[index];
            }

            private double BucketPercentile(double fraction)
            {
                int target = (int)Math.Ceiling(Count * fraction);
                if (target < 1) target = 1;

                int seen = 0;
                double lower = 0;
                for (int i = 0; i < bucketBoundsMs.Length; i++)
                {
                    double upper = bucketBoundsMs[i] == double.MaxValue || bucketBoundsMs[i] > MaxMs ? MaxMs : bucketBoundsMs[i];

                    if (seen + Buckets[i] >= target)
                    {
                        if (Buckets[i] <= 0) return upper;
                        return lower + (upper - lower) * ((target - seen) / (double)Buckets[i]);
                    }

                    seen += Buckets[i];
                    lower = upper;
                }
                return MaxMs;
            }

            public void Reset()
            {
                TotalMs = 0;
                MaxMs = 0;
                MaxMsNoGc = 0;
                AllocBytes = 0;
                GcSamples = 0;
                Count = 0;
                Array.Clear(Buckets);
                Raw.Clear();
                rawSorted = false;
                WindowStart = DateTime.Now;
            }
        }

        private static readonly ConcurrentDictionary<string, Aggregate> _aggregates = new();

        // Per-tick measurement: accumulates and logs an avg/percentile summary every few seconds,
        // so tick paths do not produce one log line per tick.
        public static void Sample(string label, long startTimestamp, double reportSeconds = 5.0, double maxThresholdMs = 0.5)
        {
            if (startTimestamp == 0 || !Enabled) return;
            long endTimestamp = Stopwatch.GetTimestamp();
            var (alloc, gc) = TakeStart(startTimestamp);

            var agg = _aggregates.GetOrAdd(label, _ => new Aggregate());

            if (PlayerManager.IsServerIdle())
            {
                lock (agg) agg.Reset();
                return;
            }

            double ms = (endTimestamp - startTimestamp) * 1000.0 / Stopwatch.Frequency;
            lock (agg)
            {
                agg.Add(ms, alloc, gc);

                if ((DateTime.Now - agg.WindowStart).TotalSeconds < reportSeconds) return;

                if (agg.MaxMs >= maxThresholdMs)
                    Write($"{label} avg={agg.TotalMs / agg.Count:F2}ms p50={agg.Percentile(.5):F2}ms " +
                          $"p95={agg.Percentile(.95):F2}ms p99={agg.Percentile(.99):F2}ms max={agg.MaxMs:F2}ms " +
                          $"maxNoGc={agg.MaxMsNoGc:F2}ms gcHits={agg.GcSamples} alloc/call={agg.AllocBytes / agg.Count}B " +
                          $"samples={agg.Count}{ContextSuffix()}");

                agg.Reset();
            }
        }

        private static long _lastProcessTimestamp;
        private static long _lastAllocatedBytes;
        private static TimeSpan _lastGcPause;
        private static TimeSpan _lastCpuTime;

        public static string ProcessStats()
        {
            long now = Stopwatch.GetTimestamp();
            long allocated = GC.GetTotalAllocatedBytes();
            TimeSpan pause = GC.GetTotalPauseDuration();
            TimeSpan cpu;
            using (var process = Process.GetCurrentProcess())
                cpu = process.TotalProcessorTime;

            double seconds = _lastProcessTimestamp == 0 ? 0 : (now - _lastProcessTimestamp) / (double)Stopwatch.Frequency;
            string result = seconds <= 0
                ? $"PROCESS heap={GC.GetTotalMemory(false) / 1048576}MB"
                : $"PROCESS cpu={(cpu - _lastCpuTime).TotalSeconds / seconds * 100:F0}% alloc={(allocated - _lastAllocatedBytes) / seconds / 1024:F0}KB/s " +
                  $"gcPause={(pause - _lastGcPause).TotalMilliseconds:F1}ms gen0={GC.CollectionCount(0)} gen1={GC.CollectionCount(1)} gen2={GC.CollectionCount(2)} " +
                  $"heap={GC.GetTotalMemory(false) / 1048576}MB";

            _lastProcessTimestamp = now;
            _lastAllocatedBytes = allocated;
            _lastGcPause = pause;
            _lastCpuTime = cpu;
            return result;
        }

        public static void MapHeap()
        {
            if (!Enabled) return;
            var info = GC.GetGCMemoryInfo();
            Write($"MAPHEAP heap={GC.GetTotalMemory(false) / 1048576}MB gcHeap={info.HeapSizeBytes / 1048576}MB frag={info.FragmentedBytes / 1048576}MB " +
                  $"gen0={GC.CollectionCount(0)} gen1={GC.CollectionCount(1)} gen2={GC.CollectionCount(2)}");
        }

        public static Func<string>? ContextProvider;

        private static string ContextSuffix()
        {
            var provider = ContextProvider;
            if (provider == null) return string.Empty;

            try
            {
                string context = provider();
                return string.IsNullOrEmpty(context) ? string.Empty : $" {context}";
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void Write(string message)
        {
            lock (_writeLock)
            {
                try
                {
                    if (_writer == null)
                    {
                        Directory.CreateDirectory(logsFolder);
                        _writer = new StreamWriter(Path.Combine(logsFolder, $"perf_{sessionId}.txt"), append: true, System.Text.Encoding.UTF8) { AutoFlush = true };
                    }
                    _writer.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [PERF] {message}");
                }
                catch
                {
                }
            }
        }
    }
}
