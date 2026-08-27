using System;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.WatchSync.Tests.Apply;

/// <summary>
/// One write a side was asked for, whether it answered it or refused it.
///
/// The item and the reason, because those are the two a case asserts about: which items were
/// written and in what order, and under which of the server's own reasons the write was recorded.
/// </summary>
/// <param name="ItemId">The item the write was against.</param>
/// <param name="Reason">The reason the server was asked to record.</param>
internal sealed record RecordedWrite(Guid ItemId, UserDataSaveReason Reason);
