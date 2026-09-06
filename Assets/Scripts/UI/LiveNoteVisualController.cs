using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Holds the chart's COLORV / SIZEV / ALPHAV timeline and says, at any moment,
/// what a note should be wearing.
/// </summary>
/// <remarks>
/// This used to push: it snapshotted the notes once, walked a cursor, and wrote
/// the new look onto every note in the snapshot as each command came due. Every
/// note that was not in the snapshot, or that built a renderer of its own after
/// the write - a slide rebuilding its bars, which happens on its first frames -
/// was simply missed, and nothing ever went back for it. Scrubbing hid that,
/// because moving the clock backwards re-applied everything; playing straight
/// through did not, which is why a live colour could show in the editor's preview
/// and then not during playback, and why a slide's arc could stay its old colour
/// while its guide star changed.
///
/// So the notes ask instead. This side only tracks a version that changes when the
/// answer changes, and a note re-asks when it sees a version it has not applied -
/// including the frame it is born and any frame it tells us its renderers changed.
/// A note cannot be missed, because nothing here has a list of notes.
/// </remarks>
[DefaultExecutionOrder(-9000)]
public sealed class LiveNoteVisualController : MonoBehaviour
{
    public static LiveNoteVisualController Active { get; private set; }

    private enum ChangeKind
    {
        Color,
        Size,
        Alpha
    }

    private sealed class LiveChange
    {
        public double Time;
        public int SourcePosition;
        public int StreamIndex;
        public ChangeKind Kind;
    }

    private readonly List<LiveChange> changes = new();
    /// <summary>Which stream and kind the chart writes at all, asked once per note.</summary>
    private readonly HashSet<(int Stream, ChangeKind Kind)> written = new();
    private JsonDataLoader loader;
    private AudioTimeProvider timeProvider;
    private int cursor;
    private float previousTime = float.NegativeInfinity;

    /// <summary>
    /// Changes whenever the live state does. A note that has applied this version
    /// is up to date; any other value means it has to ask again.
    /// </summary>
    public int Version { get; private set; } = 1;

    private void OnEnable() => Active = this;

    private void OnDisable()
    {
        if (Active == this)
            Active = null;
    }

    internal void Configure(
        List<ColorChange> colorChanges,
        List<SizeChange> sizeChanges,
        List<AlphaChange> alphaChanges,
        JsonDataLoader chartLoader,
        AudioTimeProvider clock)
    {
        changes.Clear();
        Add(colorChanges?.Where(item => item.live).Select(item =>
            (item.time, item.sourcePosition, item.streamIndex)), ChangeKind.Color);
        Add(sizeChanges?.Where(item => item.live).Select(item =>
            (item.time, item.sourcePosition, item.streamIndex)), ChangeKind.Size);
        Add(alphaChanges?.Where(item => item.live).Select(item =>
            (item.time, item.sourcePosition, item.streamIndex)), ChangeKind.Alpha);
        changes.Sort((left, right) =>
        {
            var byTime = left.Time.CompareTo(right.Time);
            return byTime != 0
                ? byTime
                : left.SourcePosition.CompareTo(right.SourcePosition);
        });

        written.Clear();
        foreach (var change in changes)
            written.Add((change.StreamIndex, change.Kind));

        loader = chartLoader;
        timeProvider = clock;
        cursor = 0;
        previousTime = float.NegativeInfinity;
        // A new chart is a new answer for every note, including "no live commands
        // at all", which is what puts the previous chart's colours back.
        Version++;
        enabled = true;
    }

    private void Add(
        IEnumerable<(double Time, int SourcePosition, int StreamIndex)> source,
        ChangeKind kind)
    {
        if (source == null)
            return;
        foreach (var item in source)
            changes.Add(new LiveChange
            {
                Time = item.Time,
                SourcePosition = item.SourcePosition,
                StreamIndex = item.StreamIndex,
                Kind = kind
            });
    }

    private void Update()
    {
        if (timeProvider == null || changes.Count == 0)
            return;

        var now = timeProvider.AudioTime;
        if (now + 0.0001f < previousTime)
        {
            // The clock went back, so commands that had come due may not have.
            var rewound = cursor;
            cursor = 0;
            while (cursor < changes.Count && changes[cursor].Time <= now + 0.000001d)
                cursor++;
            if (cursor != rewound)
                Version++;
            previousTime = now;
            return;
        }

        var before = cursor;
        while (cursor < changes.Count && changes[cursor].Time <= now + 0.000001d)
            cursor++;
        if (cursor != before)
            Version++;
        previousTime = now;
    }

    /// <summary>
    /// Dresses one note the way the live commands say at this moment.
    /// </summary>
    /// <remarks>
    /// A kind with no live command anywhere in the chart is left alone entirely,
    /// so a chart that never writes COLORV keeps the colours its notes were built
    /// with. A kind that does have commands is resolved and applied every time,
    /// including to nothing: that is what a COLORV*NULL, or a clock sitting before
    /// the first command, has to look like.
    /// </remarks>
    public void ApplyCurrent(NoteDrop note)
    {
        if (note == null || loader == null || timeProvider == null)
            return;

        var time = timeProvider.AudioTime;
        var stream = note.VisualStreamIndex;
        if (written.Contains((stream, ChangeKind.Color)))
        {
            note.ApplyLiveColor(ToColor(loader.ResolveLiveColor(note, time)));
            note.ApplyLiveGuideStarColor(
                ToColor(loader.ResolveLiveGuideStarColor(note, time)));
        }
        if (written.Contains((stream, ChangeKind.Size)))
        {
            note.ApplyLiveScale(loader.ResolveLiveSize(note, time));
            note.ApplyLiveGuideStarScale(loader.ResolveLiveGuideStarSize(note, time));
        }
        if (written.Contains((stream, ChangeKind.Alpha)))
        {
            note.ApplyLiveAlpha(loader.ResolveLiveAlpha(note, time));
            note.ApplyLiveGuideStarAlpha(loader.ResolveLiveGuideStarAlpha(note, time));
        }
    }

    private static Color? ToColor(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return ColorUtility.TryParseHtmlString("#" + value.TrimStart('#'), out var color)
            ? new Color(color.r, color.g, color.b, 1f)
            : new Color(1f, 1f, 1f, 0f);
    }
}
