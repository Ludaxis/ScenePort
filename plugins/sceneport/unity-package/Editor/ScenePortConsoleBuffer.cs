using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace ScenePort.McpBridge.Editor
{
    /// <summary>
    /// Thread-safe, bounded ring buffer of recent Unity console messages. O(1) eviction
    /// (the original used List.RemoveAt(0), which is O(n)). Instantiable so tests can
    /// exercise it without touching the global bridge buffer polluted by the test runner.
    /// </summary>
    internal sealed class ScenePortConsoleBuffer
    {
        private readonly int capacity;
        private readonly object gate = new object();
        private readonly Queue<LogEntryDto> entries;

        internal ScenePortConsoleBuffer(int capacity = 500)
        {
            this.capacity = Math.Max(1, capacity);
            entries = new Queue<LogEntryDto>(this.capacity);
        }

        internal void Add(string message, string stackTrace, string type)
        {
            var entry = new LogEntryDto
            {
                Message = message ?? string.Empty,
                StackTrace = stackTrace ?? string.Empty,
                Type = type ?? string.Empty,
                Utc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            };

            lock (gate)
            {
                entries.Enqueue(entry);
                while (entries.Count > capacity)
                {
                    entries.Dequeue();
                }
            }
        }

        internal int Count
        {
            get
            {
                lock (gate)
                {
                    return entries.Count;
                }
            }
        }

        /// <summary>Newest-first snapshot, optionally filtered by log type ("all" or a specific type).</summary>
        internal List<LogEntryDto> Snapshot(int limit, string type)
        {
            limit = Mathf.Clamp(limit, 1, capacity);
            var typeFilter = (type ?? "all").ToLowerInvariant();
            var result = new List<LogEntryDto>();

            lock (gate)
            {
                var array = entries.ToArray();
                for (var i = array.Length - 1; i >= 0 && result.Count < limit; i--)
                {
                    var entry = array[i];
                    if (typeFilter != "all" && !string.Equals(typeFilter, entry.Type.ToLowerInvariant(), StringComparison.Ordinal))
                    {
                        continue;
                    }

                    result.Add(entry);
                }
            }

            return result;
        }

        /// <summary>Newest-first snapshot of error/exception/assert entries (for compilation status).</summary>
        internal List<LogEntryDto> ErrorSnapshot(int limit)
        {
            var result = new List<LogEntryDto>();

            lock (gate)
            {
                var array = entries.ToArray();
                for (var i = array.Length - 1; i >= 0 && result.Count < limit; i--)
                {
                    var entry = array[i];
                    if (entry.Type != "Error" && entry.Type != "Exception" && entry.Type != "Assert")
                    {
                        continue;
                    }

                    result.Add(entry);
                }
            }

            return result;
        }
    }
}
