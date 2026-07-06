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

            // Write a header on the first measurement so the perf file appears immediately
            // when PerfMode is active - makes "is it working?" instantly visible.
            if (!_headerWritten)
            {
                _headerWritten = true;
                Write($"PerfMode enabled (plugin v{Instance.ModuleVersion})");
            }

            return Stopwatch.GetTimestamp();
        }

        // One-shot measurement: logs "label took X.XXms" when the elapsed time reaches the threshold.
        public static void End(string label, long startTimestamp, double thresholdMs = 1.0)
        {
            if (startTimestamp == 0 || !Enabled) return;

            double ms = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
            if (ms < thresholdMs) return;

            Write($"{label} took {ms:F2}ms");
        }

        private sealed class Aggregate
        {
            public double TotalMs;
            public double MaxMs;
            public int Count;
            public DateTime WindowStart = DateTime.Now;
        }

        private static readonly ConcurrentDictionary<string, Aggregate> _aggregates = new();

        // Per-tick measurement: accumulates and logs an avg/max summary every few seconds,
        // so tick paths do not produce one log line per tick.
        public static void Sample(string label, long startTimestamp, double reportSeconds = 5.0, double maxThresholdMs = 0.5)
        {
            if (startTimestamp == 0 || !Enabled) return;

            double ms = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
            var agg = _aggregates.GetOrAdd(label, _ => new Aggregate());
            lock (agg)
            {
                agg.TotalMs += ms;
                agg.Count++;
                if (ms > agg.MaxMs) agg.MaxMs = ms;

                if ((DateTime.Now - agg.WindowStart).TotalSeconds < reportSeconds) return;

                if (agg.MaxMs >= maxThresholdMs)
                    Write($"{label} avg={agg.TotalMs / agg.Count:F2}ms max={agg.MaxMs:F2}ms samples={agg.Count}");

                agg.TotalMs = 0;
                agg.MaxMs = 0;
                agg.Count = 0;
                agg.WindowStart = DateTime.Now;
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
