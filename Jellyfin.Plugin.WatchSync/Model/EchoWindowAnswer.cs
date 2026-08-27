namespace Jellyfin.Plugin.WatchSync.Model;

/// <summary>
/// What the suppression window answers about one field of one subject, which is #16.
///
/// Three answers and no fourth. Either nothing is outstanding, in which case the first mechanism
/// has already decided and the window was not asked; or something is outstanding and it is this
/// plugin's own write coming back changed by the server; or something is outstanding and it
/// happened here.
/// </summary>
public enum EchoWindowAnswer
{
    /// <summary>
    /// The value is what the two sides last agreed, so there is nothing to send and nothing to
    /// suppress.
    ///
    /// This is the first of the two mechanisms having answered, which is the agreed record in #14
    /// read through <c>OutstandingChanges.Since</c>. The window is not consulted at all here, and
    /// that is the property this answer exists to make decidable rather than a detail of the
    /// order the rule asks its questions in.
    /// </summary>
    NothingIsOutstanding,

    /// <summary>
    /// Something is outstanding and this plugin wrote this field itself inside the window, so
    /// what this server holds is its own write as the server stored it.
    ///
    /// The difference is the server's normalisation of the value that arrived, and what the
    /// caller does with it is agree what is stored rather than send it back. Sending it back is
    /// the endless exchange this issue exists against; leaving it outstanding is the same
    /// exchange one round slower, because the difference never goes away on its own.
    /// </summary>
    TheServerNormalisedThisPluginsOwnWrite,

    /// <summary>
    /// Something is outstanding and no write of this plugin's stands behind it, so it happened on
    /// this server and it leaves.
    /// </summary>
    TheChangeIsLocal,
}
