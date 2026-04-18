using System;
using System.Collections.Generic;
using OSE.Content;

namespace OSE.Runtime
{
    /// <summary>
    /// Info payload for <see cref="TaskCursor.TaskSpanOpened"/> — describes the
    /// span that just became the currently-open cursor position.
    /// </summary>
    public readonly struct TaskSpanOpenedInfo
    {
        /// <summary>Entries in this span. Singleton = length 1; unordered set = length N ≥ 2.</summary>
        public readonly IReadOnlyList<TaskOrderEntry> Entries;

        /// <summary>Authored label for unordered sets. <c>null</c> for singletons.</summary>
        public readonly string Label;

        /// <summary>0-based index of this span among all spans in the step.</summary>
        public readonly int SpanIndex;

        /// <summary>Total span count in the step.</summary>
        public readonly int TotalSpans;

        public TaskSpanOpenedInfo(IReadOnlyList<TaskOrderEntry> entries, string label, int spanIndex, int totalSpans)
        {
            Entries    = entries;
            Label      = label;
            SpanIndex  = spanIndex;
            TotalSpans = totalSpans;
        }
    }

    /// <summary>
    /// Phase I runtime sequencer for a step's <see cref="StepDefinition.taskOrder"/>.
    /// Walks entries left-to-right, opening one "span" at a time — either a
    /// singleton (single entry with no <c>unorderedSet</c> label) or an unordered
    /// set (contiguous entries sharing a non-empty label, all openable at once,
    /// completable in any order).
    ///
    /// <para><b>Phase I.b scope (this file):</b> pure data structure + read-only
    /// API + <see cref="TaskSpanOpened"/> event. No controller consumes the cursor
    /// yet — runtime behavior is unchanged. Phase I.c / I.d wire
    /// <see cref="PartRuntimeController"/> / <see cref="ToolRuntimeController"/>
    /// to gate their availability on cursor membership; Phase I.e unifies step
    /// completion on <see cref="IsComplete"/>.</para>
    ///
    /// <para><b>Contract for callers:</b> call <see cref="Start"/> once after
    /// construction + subscription to fire the initial <see cref="TaskSpanOpened"/>.
    /// Call <see cref="NotifyTaskCompleted"/> whenever a task the cursor cares
    /// about finishes. The cursor ignores completions for tasks outside the
    /// current span (stale signals from navigation, etc.).</para>
    ///
    /// <para><b>Required-only advancement:</b> the cursor advances past a span
    /// when all non-optional members are complete. Optional members may be
    /// completed any time up to advancement but do not block it.</para>
    /// </summary>
    public sealed class TaskCursor
    {
        private readonly struct Span
        {
            public readonly IReadOnlyList<TaskOrderEntry> Entries;
            public readonly string Label;

            public Span(IReadOnlyList<TaskOrderEntry> entries, string label)
            {
                Entries = entries;
                Label   = label;
            }
        }

        private static readonly IReadOnlyList<TaskOrderEntry> EmptyEntries = Array.Empty<TaskOrderEntry>();

        private readonly List<Span> _spans;
        private int _currentSpanIndex;
        private readonly HashSet<(string kind, string id)> _completedInCurrentSpan = new HashSet<(string, string)>();

        /// <summary>Fires whenever a new span opens, including the initial one (via <see cref="Start"/>).</summary>
        public event Action<TaskSpanOpenedInfo> TaskSpanOpened;

        /// <summary>Currently-open tasks. Empty when <see cref="IsComplete"/>.</summary>
        public IReadOnlyList<TaskOrderEntry> OpenTasks =>
            IsComplete ? EmptyEntries : _spans[_currentSpanIndex].Entries;

        /// <summary>Label of the current unordered set. <c>null</c> for singleton spans or when complete.</summary>
        public string CurrentSetLabel =>
            IsComplete ? null : _spans[_currentSpanIndex].Label;

        /// <summary>
        /// Count of required (non-optional) members in the current span that are
        /// not yet completed. 0 when <see cref="IsComplete"/> or when the current
        /// span has only optional members remaining.
        /// </summary>
        public int RemainingInCurrentSet
        {
            get
            {
                if (IsComplete) return 0;
                var span = _spans[_currentSpanIndex];
                int required = 0;
                for (int i = 0; i < span.Entries.Count; i++)
                {
                    var e = span.Entries[i];
                    if (e == null || e.isOptional) continue;
                    if (!_completedInCurrentSpan.Contains((e.kind ?? string.Empty, e.id ?? string.Empty)))
                        required++;
                }
                return required;
            }
        }

        /// <summary>True after the cursor has walked past the last span (step fully done).</summary>
        public bool IsComplete => _spans.Count == 0 || _currentSpanIndex >= _spans.Count;

        /// <summary>0-based index of the currently-open span among all spans.</summary>
        public int SpanIndex => _currentSpanIndex;

        /// <summary>Total span count for the step.</summary>
        public int TotalSpans => _spans.Count;

        public TaskCursor(StepDefinition step)
        {
            _spans = BuildSpans(step?.taskOrder);
            _currentSpanIndex = 0;
        }

        /// <summary>
        /// Fires the initial <see cref="TaskSpanOpened"/>. Call once after
        /// construction + subscription so subscribers see the opening. No-op
        /// when the cursor starts already complete (empty taskOrder).
        /// </summary>
        public void Start()
        {
            if (IsComplete) return;
            FireSpanOpened();
        }

        /// <summary>
        /// Returns true when a Part task for <paramref name="partId"/> is in the
        /// currently-open span and not yet completed.
        /// </summary>
        public bool IsPartTaskOpen(string partId)
            => IsComplete ? false : EntryIsOpenAndUncompleted("part", partId);

        /// <summary>
        /// Returns true when a toolAction task for <paramref name="actionId"/> is
        /// in the currently-open span and not yet completed.
        /// </summary>
        public bool IsToolActionOpen(string actionId)
            => IsComplete ? false : EntryIsOpenAndUncompleted("toolAction", actionId);

        /// <summary>
        /// Record completion of a task. When all required members of the
        /// currently-open span are complete, advances to the next span and
        /// fires <see cref="TaskSpanOpened"/>. Completions for tasks outside
        /// the current span are silently ignored (stale signals from earlier
        /// spans or from non-cursor-tracked events).
        /// </summary>
        public void NotifyTaskCompleted(string kind, string id)
        {
            if (IsComplete) return;
            if (!SpanContainsTask(_spans[_currentSpanIndex], kind, id)) return;

            _completedInCurrentSpan.Add((kind ?? string.Empty, id ?? string.Empty));

            if (AllRequiredCompletedInCurrentSpan())
            {
                _currentSpanIndex++;
                _completedInCurrentSpan.Clear();
                if (!IsComplete) FireSpanOpened();
            }
        }

        // ── internals ────────────────────────────────────────────────────

        private bool EntryIsOpenAndUncompleted(string kind, string id)
        {
            var span = _spans[_currentSpanIndex];
            if (!SpanContainsTask(span, kind, id)) return false;
            return !_completedInCurrentSpan.Contains((kind ?? string.Empty, id ?? string.Empty));
        }

        private static bool SpanContainsTask(Span span, string kind, string id)
        {
            for (int i = 0; i < span.Entries.Count; i++)
            {
                var e = span.Entries[i];
                if (e == null) continue;
                if (string.Equals(e.kind, kind, StringComparison.Ordinal) &&
                    string.Equals(e.id,   id,   StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private bool AllRequiredCompletedInCurrentSpan()
        {
            var span = _spans[_currentSpanIndex];
            for (int i = 0; i < span.Entries.Count; i++)
            {
                var e = span.Entries[i];
                if (e == null || e.isOptional) continue;
                if (!_completedInCurrentSpan.Contains((e.kind ?? string.Empty, e.id ?? string.Empty)))
                    return false;
            }
            return true;
        }

        private void FireSpanOpened()
        {
            var span = _spans[_currentSpanIndex];
            TaskSpanOpened?.Invoke(new TaskSpanOpenedInfo(
                span.Entries, span.Label, _currentSpanIndex, _spans.Count));
        }

        private static List<Span> BuildSpans(TaskOrderEntry[] taskOrder)
        {
            var list = new List<Span>();
            if (taskOrder == null || taskOrder.Length == 0) return list;

            int i = 0;
            while (i < taskOrder.Length)
            {
                var entry = taskOrder[i];
                string label = string.IsNullOrEmpty(entry?.unorderedSet) ? null : entry.unorderedSet;

                if (label == null)
                {
                    list.Add(new Span(new[] { entry }, null));
                    i++;
                }
                else
                {
                    int j = i;
                    while (j < taskOrder.Length)
                    {
                        var e = taskOrder[j];
                        string el = string.IsNullOrEmpty(e?.unorderedSet) ? null : e.unorderedSet;
                        if (!string.Equals(el, label, StringComparison.Ordinal)) break;
                        j++;
                    }
                    var members = new TaskOrderEntry[j - i];
                    Array.Copy(taskOrder, i, members, 0, j - i);
                    list.Add(new Span(members, label));
                    i = j;
                }
            }
            return list;
        }
    }
}
