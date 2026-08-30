using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.WatchSync.Undo;

/// <summary>
/// What an undo of one pairing's writes would put back, and what it leaves standing.
///
/// It is an answer rather than a run, which is what #44's last condition asks for: whether the
/// undo happens at revocation, on request, or not at all follows the decision on the pairing
/// board, and this issue only makes all three possible. A type that decided and wrote would have
/// taken that decision by existing.
///
/// The two lists together account for every field of every item the record of provenance names,
/// so a caller can subtract one from the other and find nothing missing. That is what makes a
/// count of skips readable: an operator told that eleven values were put back and four were left
/// standing has been told about fifteen writes, and there is no fifth outcome the answer is quiet
/// about.
/// </summary>
public sealed class UndoAnswer
{
    private readonly List<ItemToRestore> _restore;
    private readonly List<SkippedValue> _skipped;

    /// <summary>
    /// Initializes a new instance of the <see cref="UndoAnswer"/> class.
    /// </summary>
    /// <param name="restore">The items to write, with the fields being put back.</param>
    /// <param name="skipped">The values left standing, with the reason for each.</param>
    /// <exception cref="ArgumentNullException">Either list is null.</exception>
    public UndoAnswer(IReadOnlyList<ItemToRestore> restore, IReadOnlyList<SkippedValue> skipped)
    {
        ArgumentNullException.ThrowIfNull(restore);
        ArgumentNullException.ThrowIfNull(skipped);

        _restore = new List<ItemToRestore>(restore);
        _skipped = new List<SkippedValue>(skipped);
    }

    /// <summary>
    /// Gets the items to write, with the fields being put back.
    /// </summary>
    public IReadOnlyList<ItemToRestore> Restore => _restore;

    /// <summary>
    /// Gets the values left standing, with the reason for each.
    /// </summary>
    public IReadOnlyList<SkippedValue> Skipped => _skipped;

    /// <summary>
    /// Gets the number of fields this undo would put back, across every item.
    ///
    /// It is a count of fields rather than of items because that is the unit the record of
    /// provenance is kept in and the unit a skip is reported in, and a page comparing a count of
    /// items against a count of skipped fields would be comparing two different things.
    /// </summary>
    public int Restoring
    {
        get
        {
            var fields = 0;

            foreach (var item in _restore)
            {
                fields += item.Fields.Count;
            }

            return fields;
        }
    }
}
