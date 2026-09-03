using System;
using Jellyfin.Plugin.WatchSync.Model;

namespace Jellyfin.Plugin.WatchSync.Apply;

/// <summary>
/// One item of a stopped run that an approval did not write, and why.
///
/// It names the item and carries no title, no path and no value, for the reason
/// <see cref="ApplyFailure"/> gives: it is counted on a page and written into a log, and both of
/// those refuse a work's title next to a person. The values themselves are in the plan and in
/// the server, where somebody with the right to read them can.
/// </summary>
public sealed class ItemSetAside
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ItemSetAside"/> class.
    /// </summary>
    /// <param name="subject">The mapped user and the leaf item that was not written.</param>
    /// <param name="reason">Why it was not.</param>
    /// <exception cref="ArgumentNullException">The subject is null.</exception>
    public ItemSetAside(TransferSubject subject, SetAsideReason reason)
    {
        ArgumentNullException.ThrowIfNull(subject);

        Subject = subject;
        Reason = reason;
    }

    /// <summary>
    /// Gets the mapped user and the leaf item that was not written.
    /// </summary>
    public TransferSubject Subject { get; }

    /// <summary>
    /// Gets why it was not written.
    /// </summary>
    public SetAsideReason Reason { get; }
}
