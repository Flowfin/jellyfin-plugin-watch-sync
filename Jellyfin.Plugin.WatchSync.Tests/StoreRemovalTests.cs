using System;
using System.IO;
using System.Linq;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.WatchSync.Agreement;
using Jellyfin.Plugin.WatchSync.Model;
using Jellyfin.Plugin.WatchSync.Storage;
using MediaBrowser.Common.Configuration;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// The removal an operator takes before uninstalling, which is #73.
///
/// Two properties are what this set is written against. It removes this plugin's store and
/// nothing beside it, compared as the whole tree under the data path before and after. And the
/// two ways of reinstalling are different: without the removal the next install resumes from the
/// store that is there, and after it the next install has no agreement to resume from and starts
/// as a first exchange.
///
/// Nothing here reads a clock, which is the rule in
/// <c>Jellyfin.Plugin.WatchSync.Tests/headless-rule.md</c>.
/// </summary>
public sealed class StoreRemovalTests : IDisposable
{
    private static readonly Guid _pairing = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _user = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid _film = new("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset _evening = new(2026, 8, 24, 20, 0, 0, TimeSpan.Zero);

    private readonly TemporaryDirectory _programData;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreRemovalTests"/> class, with a directory
    /// of its own standing in for what a server would hand over.
    /// </summary>
    public StoreRemovalTests()
    {
        _programData = TemporaryDirectory.Create("removal");
        Directory.CreateDirectory(DataPath);
    }

    /// <inheritdoc />
    public void Dispose() => _programData.Dispose();

    /// <summary>
    /// The second condition of #73. The removal takes the store and nothing else under the data
    /// path, compared as the tree before and after.
    ///
    /// The neighbours are what the comparison is for. One is another plugin's folder, which is
    /// what actually sits beside this store on a real server. One is a file whose name begins
    /// with the store's own, which is what a removal written as a pattern rather than as a path
    /// takes with it. One is a file directly under the data path, which is what a removal that
    /// walked upwards one level too far would reach.
    /// </summary>
    [Fact]
    public void TheRemovalTakesTheStoreAndNothingBesideIt()
    {
        WriteARecord();

        var neighbours = new[]
        {
            Path.Combine(DataPath, "another-plugin", "its-document.json"),
            Path.Combine(DataPath, "watch-sync-notes.txt"),
            Path.Combine(DataPath, "server-owned.db"),
        };

        foreach (var neighbour in neighbours)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(neighbour)!);
            File.WriteAllText(neighbour, "not this plugin's");
        }

        var before = TreeUnderTheDataPath();

        Assert.Equal(StoreRemovalAnswer.Removed, StoreRemoval.Remove(Folder()));

        var after = TreeUnderTheDataPath();

        Assert.Contains(before, each => each == Relative(StorePath));
        Assert.DoesNotContain(after, each => each == Relative(StorePath));
        Assert.Equal(
            neighbours.Select(Relative)
                .Concat(new[] { Relative(Path.GetDirectoryName(neighbours[0])!) })
                .OrderBy(each => each, StringComparer.Ordinal),
            after);
        Assert.All(neighbours, each => Assert.Equal("not this plugin's", File.ReadAllText(each)));
    }

    /// <summary>
    /// The third condition of #73. Reinstalling without taking the removal first resumes from the
    /// store that is there, which is the case an operator who removed the plugin to upgrade it is
    /// in.
    ///
    /// A new store object over the same folder is what a reinstall looks like from this side:
    /// nothing in memory survives, and everything that does is on disk.
    /// </summary>
    [Fact]
    public void ReinstallingWithoutTheRemovalResumesFromTheStoreThatIsThere()
    {
        WriteARecord();

        var reading = AgreedRecords.Read(
            new DocumentStore(Folder()).Read(AgreedRecords.DocumentName(_pairing, _user))!.Document!);

        Assert.False(reading.IsRefused);
        Assert.NotNull(reading.Records!.For(_film));
        Assert.True(reading.Records!.Watermark.IsAt("page-4"));
        Assert.Equal(NextExchange.SinceTheWatermark, reading.Records!.Watermark.Asks);
    }

    /// <summary>
    /// The fourth condition of #73. Reinstalling after the removal has no agreement to resume
    /// from, so the pairing and the person start as a first exchange rather than as a pair that
    /// has settled a library.
    ///
    /// This is the direction that would be expensive to get wrong. An install that assumed an
    /// agreement no longer on disk would treat every item as already settled and move nothing,
    /// and the operator who asked for the records to go would get a plugin that is running and
    /// silent.
    /// </summary>
    [Fact]
    public void ReinstallingAfterTheRemovalStartsAsAFirstExchange()
    {
        WriteARecord();

        StoreRemoval.Remove(Folder());

        var store = new DocumentStore(Folder());

        Assert.Null(store.Read(AgreedRecords.DocumentName(_pairing, _user)));
        Assert.Empty(store.Names());

        var afterwards = AgreedRecords.NoneYet(_pairing, _user);

        Assert.Equal(0, afterwards.Count);
        Assert.Null(afterwards.For(_film));
        Assert.True(afterwards.Watermark.IsNoneYet);
        Assert.Equal(NextExchange.FullReconciliation, afterwards.Watermark.Asks);
    }

    /// <summary>
    /// Removing a store that is not there is answered rather than thrown, and it says which of
    /// the two happened.
    ///
    /// An operator pressing the action twice, or pressing it on a server that never synced, has
    /// asked for a state that already holds. Reporting that as a removal would tell them
    /// something was deleted when nothing was, and reporting it as a failure would send them
    /// looking for a problem that is not there.
    /// </summary>
    [Fact]
    public void RemovingAStoreThatIsNotThereIsAnsweredAndNotThrown()
    {
        Assert.Equal(StoreRemovalAnswer.NothingToRemove, StoreRemoval.Remove(Folder()));

        WriteARecord();

        Assert.Equal(StoreRemovalAnswer.Removed, StoreRemoval.Remove(Folder()));
        Assert.Equal(StoreRemovalAnswer.NothingToRemove, StoreRemoval.Remove(Folder()));
    }

    /// <summary>
    /// The removal takes everything the store holds and not only the documents one walk knows the
    /// names of.
    ///
    /// A store carries what this plugin wrote and whatever a crash left beside it, and both are
    /// about people. A removal written as a loop over the names the store can read would leave
    /// the half it cannot read, which is the half an operator has the least way of finding.
    /// </summary>
    [Fact]
    public void TheRemovalTakesWhatTheStoreCannotReadAsWellAsWhatItCan()
    {
        WriteARecord();

        File.WriteAllText(Path.Combine(StorePath, "half-written.json.tmp"), "{");
        Directory.CreateDirectory(Path.Combine(StorePath, "left-behind"));
        File.WriteAllText(Path.Combine(StorePath, "left-behind", "old.json"), "{}");

        Assert.Equal(StoreRemovalAnswer.Removed, StoreRemoval.Remove(Folder()));
        Assert.False(Directory.Exists(StorePath));
        Assert.Empty(TreeUnderTheDataPath());
    }

    /// <summary>
    /// The removal is handed the folder rather than a path, so where the store lives stays the
    /// one answer the store folder gives and does not become a second one here.
    /// </summary>
    [Fact]
    public void TheRemovalIsHandedTheFolderAndNotAPath()
    {
        Assert.Throws<ArgumentNullException>(() => StoreRemoval.Remove(null!));
    }

    private void WriteARecord()
    {
        var record = AgreedRecords.NoneYet(_pairing, _user)
            .With(new AgreedRecord(
                TransferSubject.From(_user, _film, BaseItemKind.Movie).Value!,
                new SyncedState(true, 2, 0, new DateTime(2026, 8, 24, 22, 0, 0, DateTimeKind.Utc)),
                _evening,
                1))
            .At(Watermark.Confirmed("page-4", _evening).Mark!);

        new DocumentStore(Folder())
            .Write(AgreedRecords.DocumentName(_pairing, _user), _ => record.ToDocument());
    }

    private string[] TreeUnderTheDataPath() =>
        Directory.EnumerateFileSystemEntries(DataPath, "*", SearchOption.AllDirectories)
            .Select(Relative)
            .OrderBy(each => each, StringComparer.Ordinal)
            .ToArray();

    private string Relative(string path) => Path.GetRelativePath(DataPath, path);

    private string DataPath => Path.Join(_programData.FullPath, "data");

    private string StorePath => Folder().FullPath;

    private StoreFolder Folder()
    {
        var paths = new Mock<IApplicationPaths>();

        paths.SetupGet(each => each.DataPath).Returns(DataPath);

        return new StoreFolder(paths.Object);
    }
}
