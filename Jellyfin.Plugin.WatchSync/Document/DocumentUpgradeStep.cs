using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;

namespace Jellyfin.Plugin.WatchSync.Document;

/// <summary>
/// One step of the ladder: the members of a document at one version, rewritten into the members
/// of the next.
///
/// A step is from one version to the very next one and never further, which is what makes a
/// document three versions behind an ordinary case rather than a special one. #71 asks for that
/// in those words, and the cost of the other shape is what the words are about: a ladder holding
/// a direct step from 1 to 4 beside the three single steps has two routes to the same document
/// and only one of them is exercised by a fixture.
///
/// The members a step may touch are declared, and the declaration is the whole of its surface in
/// both directions. It is handed an object holding only those members and its answer is merged
/// back over only those members, so a member the step never heard of cannot be dropped by it,
/// and a member it writes without declaring is refused rather than quietly added. That is how
/// "an upgrade never loses a field it does not understand" is a property of the mechanism and not
/// a rule each step keeps: the step is never given the chance.
/// </summary>
public sealed class DocumentUpgradeStep
{
    private readonly HashSet<string> _members;
    private readonly Action<JsonObject> _carry;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentUpgradeStep"/> class.
    /// </summary>
    /// <param name="from">The version the step reads, which it carries to the one above.</param>
    /// <param name="members">
    /// The members this step may read, write and remove. Everything else in the document is
    /// carried across untouched and is not shown to the step at all.
    /// </param>
    /// <param name="carry">
    /// What the step does, over an object holding its declared members and nothing else. It
    /// leaves behind the members the next version holds, and what it leaves is what replaces
    /// them.
    /// </param>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The version read is not a whole number above zero, which no document can carry.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// A declared member is empty, is declared twice, or is the version itself. The version is
    /// not a member and a step that moved it would be deciding the ladder's own arithmetic.
    /// </exception>
    public DocumentUpgradeStep(int from, IReadOnlyList<string> members, Action<JsonObject> carry)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(carry);
        ArgumentOutOfRangeException.ThrowIfLessThan(from, 1);

        _members = new HashSet<string>(StringComparer.Ordinal);

        foreach (var member in members)
        {
            if (string.IsNullOrEmpty(member))
            {
                throw new ArgumentException(
                    "A step declares the members it may touch by name, and one of the names is empty.",
                    nameof(members));
            }

            if (string.Equals(member, StoredDocument.VersionMember, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "The version is not one of the members beside it, so a step may not declare it.",
                    nameof(members));
            }

            if (!_members.Add(member))
            {
                throw new ArgumentException(
                    "A step declares each member it may touch once, and one of them is declared twice.",
                    nameof(members));
            }
        }

        From = from;
        _carry = carry;
    }

    /// <summary>
    /// Gets the version this step reads.
    /// </summary>
    public int From { get; }

    /// <summary>
    /// Gets the version this step produces, which is always the next one.
    /// </summary>
    public int To => From + 1;

    /// <summary>
    /// Gets the members this step may read, write and remove, in no particular order.
    /// </summary>
    public IReadOnlyCollection<string> Members => _members;

    /// <summary>
    /// Runs the step over the members of a document and answers the members of the next version.
    ///
    /// The order of the members that were already there is kept, and a member the step added
    /// arrives at the end, so a document that goes up the ladder and one written at the top by
    /// hand are the same bytes rather than the same values in a different order.
    /// </summary>
    /// <param name="fields">The members of the document at <see cref="From"/>.</param>
    /// <returns>The members of the document at <see cref="To"/>.</returns>
    /// <exception cref="InvalidOperationException">
    /// The step left behind a member it did not declare. Merging it would widen the step's
    /// surface past what the declaration says, and the declaration is the only thing standing
    /// between a step and a member belonging to some other part of the document.
    /// </exception>
    internal JsonObject Apply(JsonObject fields)
    {
        var mine = new JsonObject();

        foreach (var member in fields.Where(member => _members.Contains(member.Key)))
        {
            mine[member.Key] = member.Value?.DeepClone();
        }

        _carry(mine);

        if (mine.Any(left => !_members.Contains(left.Key)))
        {
            throw new InvalidOperationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "The step from version {0} left behind a member it did not declare, so"
                    + " what it may touch and what it touched disagree.",
                    From));
        }

        var carried = new JsonObject();

        foreach (var member in fields)
        {
            if (!_members.Contains(member.Key))
            {
                carried[member.Key] = member.Value?.DeepClone();
                continue;
            }

            if (mine.TryGetPropertyValue(member.Key, out var kept))
            {
                carried[member.Key] = kept?.DeepClone();
            }
        }

        foreach (var left in mine.Where(left => !carried.ContainsKey(left.Key)))
        {
            carried[left.Key] = left.Value?.DeepClone();
        }

        return carried;
    }
}
