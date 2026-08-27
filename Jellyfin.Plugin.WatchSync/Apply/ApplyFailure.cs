using System;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Apply;

/// <summary>
/// One item the apply path could not write, and what it was refused with.
///
/// A failure on one item is a normal outcome rather than an exceptional one, which is #54's
/// opening sentence: there is no transaction across two servers, so an exchange that half
/// succeeded is the ordinary shape of a bad evening. What makes it survivable is that the item is
/// named, so the next exchange offers exactly it again, and that nothing about the rest of the
/// walk depends on it.
///
/// The reason is the name of the type the server threw and never its message. A message is
/// assembled by somebody else's code out of whatever was to hand, which on this surface is a
/// path, an item title or a peer's own text, and this record is meant to be counted on a page,
/// #62, and written into a log, #67. Both of those refuse exactly those three. A type name is
/// chosen by the runtime, names no work and no person, and is what an operator needs in order to
/// tell a database that is down from an item the library no longer holds.
/// </summary>
public sealed class ApplyFailure
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ApplyFailure"/> class.
    /// </summary>
    /// <param name="subject">The mapped user and the leaf item that was not written.</param>
    /// <param name="reason">The name of the type the write was refused with.</param>
    /// <exception cref="ArgumentNullException">The subject is null.</exception>
    /// <exception cref="ArgumentException">
    /// The reason is empty or blank. A failure carrying no reason is one an operator can see the
    /// count of and nothing else, and the count alone is what a support thread starts from.
    /// </exception>
    public ApplyFailure(TransferSubject subject, string reason)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        Subject = subject;
        Reason = reason;
    }

    /// <summary>
    /// Gets the mapped user and the leaf item that was not written.
    /// </summary>
    public TransferSubject Subject { get; }

    /// <summary>
    /// Gets the name of the type the write was refused with.
    /// </summary>
    public string Reason { get; }
}
