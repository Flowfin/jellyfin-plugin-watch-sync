using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.WatchSync.Model;

/// <summary>
/// Every version of the transfer envelope this plugin speaks, and what each of them requires
/// an envelope to carry.
///
/// This is the one place the set is declared, which is the last of the five rules in #18.
/// <see cref="Supported"/> is the declaration and everything else is derived from it: the
/// version this code sends is the newest entry, the refusal names the whole set, and the
/// required members are looked up per entry rather than kept beside the set. A version added
/// here without its required members is refused by <see cref="Requires"/> rather than
/// accepted with nothing to check.
///
/// The envelope's version is not the pairing plugin's. Two paired servers do not upgrade at
/// the same moment and the two numbers move independently, which is the sentence #18 opens
/// with, so nothing here reads or derives from a contract version.
///
/// There is no ladder. A document in this plugin's store is carried forward one version at a
/// time, because the alternative is refusing an operator their own data; an envelope has no
/// such claim on anybody. It arrives from a machine that can be asked again, so a version
/// outside this set is refused whole and the exchange stops, and there is no upgrade step for
/// something that will be resent in the shape both sides agree on.
/// </summary>
public static class EnvelopeVersions
{
    private static readonly Dictionary<int, IReadOnlyList<string>> _requiredMembers =
        new Dictionary<int, IReadOnlyList<string>>
        {
            [1] = new[] { Envelope.ChangesMember },
        };

    /// <summary>
    /// Gets every version of the envelope this plugin speaks, oldest first.
    ///
    /// There is one today, because nothing has sent an envelope yet and a second entry would
    /// be a version nobody has spoken. What a second entry costs is written at
    /// <see cref="Requires"/> and in <c>docs/changelog.md</c>, which already carries dropping
    /// an envelope version as something that is always an entry, because dropping one strands
    /// a peer.
    /// </summary>
    public static IReadOnlyList<int> Supported { get; } = new[] { 1 };

    /// <summary>
    /// Gets the version this code sends, which is the newest supported one.
    /// </summary>
    public static int Current => Supported[Supported.Count - 1];

    /// <summary>
    /// The members an envelope of a version has to carry to be read at all.
    ///
    /// A missing member is a refusal and never a default, which is the third rule in #18: a
    /// permissive default is how a newer server silently accepts a truncated message, and the
    /// truncation that matters here reads as an exchange in which nothing had changed.
    ///
    /// Version 1 requires one member, and that is the state of the tree rather than a judgement
    /// that one is enough. What else an answer names is being decided elsewhere: the point a
    /// watermark advances to is #51 and the pairing and mapped user one exchange is about are
    /// #40 and #42, and none of the three is in this tree. A member declared here for one of
    /// them would be this issue deciding another issue's shape, and it would be declared in the
    /// place hardest to undo, because a required member cannot be added later without every
    /// peer speaking the older version being refused.
    /// </summary>
    /// <param name="version">The version to ask about.</param>
    /// <returns>The members that version requires.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A version this plugin does not speak, which is a caller that skipped the refusal rather
    /// than a state an envelope can be in.
    /// </exception>
    public static IReadOnlyList<string> Requires(int version)
    {
        if (!_requiredMembers.TryGetValue(version, out var members))
        {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                version,
                "No required members are declared for that envelope version, so there is nothing to hold an envelope of it to.");
        }

        return members;
    }
}
