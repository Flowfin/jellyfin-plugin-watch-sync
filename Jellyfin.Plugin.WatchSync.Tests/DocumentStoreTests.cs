using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.WatchSync.Document;
using Jellyfin.Plugin.WatchSync.Storage;
using MediaBrowser.Common.Configuration;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// What writing a document into this plugin's store does, which is #70 for everything except the
/// killed process. That one needs a process and is in <see cref="DocumentWriteKillTests"/>.
///
/// The property the whole set is about is one sentence: a reader finds the document that was
/// there or the document that arrived, never a mixture and never half of either. Every case here
/// is one way of arriving at a mixture, and the two that are not about a mixture are about the
/// other half of the sentence, which is that a document one caller wrote is not quietly dropped
/// by another caller who read the store a moment earlier.
/// </summary>
public sealed class DocumentStoreTests : IDisposable
{
    private const string Name = "agreed";

    private readonly TemporaryDirectory _programData;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentStoreTests"/> class, with a directory
    /// of its own standing in for what a server would hand over.
    /// </summary>
    public DocumentStoreTests()
    {
        _programData = TemporaryDirectory.Create("documents");
        Directory.CreateDirectory(DataPath);
    }

    /// <inheritdoc />
    public void Dispose() => _programData.Dispose();

    /// <summary>
    /// The ordinary case, and the one every other case here is a departure from.
    /// </summary>
    [Fact]
    public void AWrittenDocumentIsWhatTheNextReaderFinds()
    {
        var store = Store();

        var answer = store.Write(Name, _ => Document(("who", "first")));

        Assert.Equal(DocumentWriteOutcome.Written, answer.Outcome);
        Assert.False(answer.IsRefused);

        var reading = store.Read(Name);

        Assert.NotNull(reading);
        Assert.Equal(DocumentAnswer.Current, reading!.Answer);
        Assert.Equal("first", Member(reading.Document!, "who"));
    }

    /// <summary>
    /// Absence is not a reading, and the store says so by answering with nothing rather than with
    /// a document at some version nobody wrote.
    ///
    /// The alternative is what the fifth condition of #14 is about from the other end: an item
    /// with no agreed record is a first exchange, and a store that handed back an empty document
    /// instead of nothing would turn that into an exchange over a record somebody had agreed.
    /// </summary>
    [Fact]
    public void ADocumentThatHasNeverBeenWrittenIsNotAReading()
    {
        Assert.Null(Store().Read(Name));
    }

    /// <summary>
    /// The write path refuses a document from the future, which is #69's rule arriving on the
    /// side that would destroy the thing it protects.
    ///
    /// A reader that refuses such a document and a writer that overwrites one are the same defect
    /// seen from two sides, and the writer is the side where the fields the newer version needed
    /// actually go. So the bytes on disk are compared before and after, rather than only the
    /// answer being read.
    /// </summary>
    [Fact]
    public void ADocumentFromTheFutureIsNotWrittenOver()
    {
        var store = Store();
        var path = Path.Combine(StorePath, Name + ".json");

        Directory.CreateDirectory(StorePath);
        File.WriteAllText(
            path,
            "{\"version\":" + (DocumentVersions.Current + 1).ToString(CultureInfo.InvariantCulture) + ",\"who\":\"a version this code does not know\"}");

        var before = File.ReadAllText(path);

        var answer = store.Write(Name, _ => Document(("who", "this code")));

        Assert.Equal(DocumentWriteOutcome.RefusedByADocumentFromTheFuture, answer.Outcome);
        Assert.Null(answer.Document);
        Assert.Equal(before, File.ReadAllText(path));
    }

    /// <summary>
    /// The third condition of #70. A filesystem that runs out of room leaves the document that
    /// was there rather than a truncated one in its place.
    ///
    /// No filesystem was filled to read this. A run that filled one would need a filesystem it is
    /// allowed to fill, which no machine running this suite is obliged to have, and could take
    /// that machine down with it. What is raised here is the exception the runtime raises when a
    /// write runs out of room, on the stream the bytes go into, so what is measured is what this
    /// store does with it and not what the platform does.
    /// </summary>
    [Fact]
    public void AWriteThatRunsOutOfRoomLeavesTheDocumentThatWasThere()
    {
        var store = Store();

        store.Write(Name, _ => Document(("who", "first"), ("payload", new string('x', 4096))));

        var whole = File.ReadAllText(Path.Combine(StorePath, Name + ".json"));

        var refused = new DocumentStore(Folder(), path => new RunsOutOfRoom(path, room: 64));
        var answer = refused.Write(Name, _ => Document(("who", "second"), ("payload", new string('y', 4096))));

        Assert.Equal(DocumentWriteOutcome.RefusedByTheFilesystem, answer.Outcome);
        Assert.Null(answer.Document);
        Assert.Equal(whole, File.ReadAllText(Path.Combine(StorePath, Name + ".json")));

        var reading = store.Read(Name);
        Assert.Equal(DocumentAnswer.Current, reading!.Answer);
        Assert.Equal("first", Member(reading.Document!, "who"));
    }

    /// <summary>
    /// The same refusal where there was nothing there before, which is the case that would leave
    /// a truncated first document if the bytes went straight at the document's own name.
    ///
    /// The half of the sentence that is easy to leave out is the file the attempt was writing.
    /// A refusal that left it behind would leave the store growing a file per failed write, so
    /// what is asserted is that the folder holds nothing at all.
    /// </summary>
    [Fact]
    public void AFirstWriteThatRunsOutOfRoomLeavesNothingBehind()
    {
        var store = new DocumentStore(Folder(), path => new RunsOutOfRoom(path, room: 8));

        var answer = store.Write(Name, _ => Document(("who", "first"), ("payload", new string('x', 4096))));

        Assert.Equal(DocumentWriteOutcome.RefusedByTheFilesystem, answer.Outcome);
        Assert.Empty(Directory.GetFiles(StorePath));
        Assert.Null(Store().Read(Name));
    }

    /// <summary>
    /// A filesystem that refuses the read is not a store with nothing in it.
    ///
    /// A directory where the document should be is the shape this reads: something else put it
    /// there, and every read and every write against that name fails from then on. The two
    /// answers are kept apart because a write that read the refusal as absence would hand the
    /// change a null and go on to write a first document over whatever the situation actually is,
    /// and a caller reading it as absence would open a first exchange against a peer it has
    /// already agreed with.
    /// </summary>
    [Fact]
    public void AFilesystemThatRefusesTheReadIsNotAnAbsentDocument()
    {
        var store = Store();

        Directory.CreateDirectory(Path.Combine(StorePath, Name + ".json"));

        var answer = store.Write(Name, _ => Document(("who", "first")));

        Assert.Equal(DocumentWriteOutcome.RefusedByTheFilesystem, answer.Outcome);
        Assert.True(Directory.Exists(Path.Combine(StorePath, Name + ".json")));
        Assert.ThrowsAny<Exception>(() => store.Read(Name));
    }

    /// <summary>
    /// The second condition of #70. Many writers of one document lose nothing.
    ///
    /// Each writer adds a member of its own and carries over everything it was handed, so the end
    /// state is decided rather than raced: the document holds one member per writer. A store that
    /// let the last writer win would answer with as few as one, and it would do so intermittently,
    /// which is the shape that gets re-run until it passes.
    /// </summary>
    [Fact]
    public void ManyWritersOfOneDocumentLoseNothing()
    {
        var store = Store();
        var writers = 24;

        var running = Enumerable.Range(0, writers).Select(index => Task.Run(() =>
            store.Write(Name, reading => Adding(reading, "writer-" + index.ToString(CultureInfo.InvariantCulture), "here")))).ToArray();

        Task.WaitAll(running);

        Assert.All(running, each => Assert.Equal(DocumentWriteOutcome.Written, each.Result.Outcome));

        var document = store.Read(Name)!.Document!;

        Assert.Equal(
            Enumerable.Range(0, writers).Select(index => "writer-" + index.ToString(CultureInfo.InvariantCulture)).OrderBy(each => each, StringComparer.Ordinal).ToList(),
            document.Fields.Select(pair => pair.Key).OrderBy(each => each, StringComparer.Ordinal).ToList());
    }

    /// <summary>
    /// The fourth condition of #70, on the document where a lock would be easiest to justify.
    ///
    /// One writer is held inside the stream its bytes are going into, which is the input and
    /// output a slow filesystem makes slow. A second writer of the same document runs its own
    /// bytes down to disk while the first is still held, which it could not do if the write path
    /// took a lock before it started writing. Both are then let go and both changes are in the
    /// document, so what the arrangement bought is not a lost write.
    /// </summary>
    [Fact]
    public void TheBytesOfOneWriteAreNotPutDownUnderALockAnotherWriteWaitsOn()
    {
        var held = new ManualResetEventSlim(false);
        var reached = new ManualResetEventSlim(false);
        var first = 1;

        var store = new DocumentStore(Folder(), path =>
        {
            if (Interlocked.Exchange(ref first, 0) == 0)
            {
                return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            }

            return new HeldOpen(path, reached, held);
        });

        var slow = Task.Run(() => store.Write(Name, reading => Adding(reading, "slow", "here")));

        Assert.True(reached.Wait(TimeSpan.FromSeconds(30)), "The first writer never reached the stream its bytes go into.");

        var quick = Task.Run(() => store.Write(Name, reading => Adding(reading, "quick", "here")));

        Assert.True(quick.Wait(TimeSpan.FromSeconds(30)), "A second write of the same document could not finish while the first was held inside its stream, so the write path holds a lock across the bytes.");
        Assert.Equal(DocumentWriteOutcome.Written, quick.Result.Outcome);

        held.Set();

        Assert.True(slow.Wait(TimeSpan.FromSeconds(30)), "The held writer never finished once it was let go.");
        Assert.Equal(DocumentWriteOutcome.Written, slow.Result.Outcome);

        var document = store.Read(Name)!.Document!;

        Assert.Equal("here", Member(document, "slow"));
        Assert.Equal("here", Member(document, "quick"));
    }

    /// <summary>
    /// The other half of the fourth condition, and the reason #70 asks for the serialisation to
    /// be per document rather than one lock over the store.
    ///
    /// A sweep writing one document and an event writing another have to run at once. With a lock
    /// over the store the second waits for as long as the first one's bytes take, which on a slow
    /// filesystem and a large document is the stall an operator reports as the plugin hanging.
    /// </summary>
    [Fact]
    public void ADocumentBeingWrittenDoesNotHoldAnotherOne()
    {
        var held = new ManualResetEventSlim(false);
        var reached = new ManualResetEventSlim(false);
        var first = 1;

        var store = new DocumentStore(Folder(), path =>
        {
            if (Interlocked.Exchange(ref first, 0) == 0)
            {
                return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            }

            return new HeldOpen(path, reached, held);
        });

        var slow = Task.Run(() => store.Write("queue", _ => Document(("who", "the sweep"))));

        Assert.True(reached.Wait(TimeSpan.FromSeconds(30)), "The first writer never reached the stream its bytes go into.");

        var other = Task.Run(() => store.Write(Name, _ => Document(("who", "the event"))));

        Assert.True(other.Wait(TimeSpan.FromSeconds(30)), "A write of one document waited on a write of another, so there is a lock over the store.");

        held.Set();

        Assert.True(slow.Wait(TimeSpan.FromSeconds(30)), "The held writer never finished once it was let go.");
        Assert.Equal("the sweep", Member(store.Read("queue")!.Document!, "who"));
        Assert.Equal("the event", Member(store.Read(Name)!.Document!, "who"));
    }

    /// <summary>
    /// The file an attempt writes is gone once the attempt has landed.
    ///
    /// Left behind, it would be a file per write in a folder that grows with the library. It is
    /// gone here because the landing is a rename rather than a copy, which is the same fact the
    /// atomicity rests on, seen from the side an operator would notice.
    /// </summary>
    [Fact]
    public void NothingIsLeftBesideADocumentThatLanded()
    {
        var store = Store();

        store.Write(Name, _ => Document(("who", "first")));
        store.Write(Name, reading => Adding(reading, "again", "here"));

        Assert.Equal(
            new[] { Name + ".json" },
            Directory.GetFiles(StorePath).Select(Path.GetFileName).ToArray());
    }

    /// <summary>
    /// A name this store may not compose a path out of is refused before anything is opened.
    ///
    /// The names will come from a pairing, a user and an item, so part of every one of them is
    /// chosen outside this server. What is refused is everything outside a closed set, rather
    /// than the separators of whichever platform the run is on, because a rule written as no
    /// slash misses the other slash, the drive letter and the two dots.
    /// </summary>
    /// <param name="name">The name a caller asked for.</param>
    [Theory]
    [InlineData("../outside")]
    [InlineData("..\\outside")]
    [InlineData("agreed/../../outside")]
    [InlineData("c:outside")]
    [InlineData("Agreed")]
    [InlineData("agreed.json")]
    [InlineData("")]
    public void ANameThisStoreMayNotComposeIsRefused(string name)
    {
        var store = Store();

        Assert.ThrowsAny<ArgumentException>(() => store.Read(name));
        Assert.ThrowsAny<ArgumentException>(() => store.Write(name, _ => Document(("who", "nobody"))));
        Assert.Empty(Directory.Exists(StorePath) ? Directory.GetFiles(StorePath) : Array.Empty<string>());
    }

    private static string Member(StoredDocument document, string name) =>
        document.Fields[name]!.GetValue<string>();

    private static StoredDocument Document(params (string Name, string Value)[] members)
    {
        var fields = new JsonObject
        {
            ["version"] = JsonValue.Create(DocumentVersions.Current),
        };

        foreach (var member in members)
        {
            fields[member.Name] = JsonValue.Create(member.Value);
        }

        return StoredDocument.Read(fields.ToJsonString(), DocumentVersions.Current).Document!;
    }

    private static StoredDocument Adding(DocumentReading? reading, string name, string value)
    {
        var members = new List<(string Name, string Value)>();

        if (reading?.Document is not null)
        {
            foreach (var pair in reading.Document.Fields)
            {
                members.Add((pair.Key, pair.Value!.GetValue<string>()));
            }
        }

        members.Add((name, value));

        return Document(members.ToArray());
    }

    private string DataPath => Path.Combine(_programData.FullPath, "data");

    private string StorePath => Path.Combine(DataPath, "watch-sync");

    private StoreFolder Folder()
    {
        var paths = new Mock<IApplicationPaths>(MockBehavior.Loose);
        paths.SetupGet(each => each.DataPath).Returns(DataPath);

        return new StoreFolder(paths.Object);
    }

    private DocumentStore Store() => new DocumentStore(Folder());
}

/// <summary>
/// A stream that runs out of room, which is what a full disk looks like from inside a write.
/// </summary>
internal sealed class RunsOutOfRoom : Stream
{
    private readonly FileStream _underneath;
    private readonly int _room;

    private int _written;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunsOutOfRoom"/> class.
    /// </summary>
    /// <param name="path">The file the bytes were going into.</param>
    /// <param name="room">How many bytes fit before there is no more room.</param>
    internal RunsOutOfRoom(string path, int room)
    {
        _underneath = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        _room = room;
    }

    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override long Length => _underneath.Length;

    /// <inheritdoc />
    public override long Position
    {
        get => _underneath.Position;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Flush() => _underneath.Flush();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        var fits = Math.Max(0, Math.Min(count, _room - _written));

        _underneath.Write(buffer, offset, fits);
        _written += fits;

        if (fits < count)
        {
            throw new IOException("There is no space left on the device.");
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _underneath.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// A stream that stops inside the write, which is what a slow filesystem looks like from inside
/// one. It says when it has been reached and goes on when it is let go.
/// </summary>
internal sealed class HeldOpen : Stream
{
    private readonly FileStream _underneath;
    private readonly ManualResetEventSlim _reached;
    private readonly ManualResetEventSlim _held;

    /// <summary>
    /// Initializes a new instance of the <see cref="HeldOpen"/> class.
    /// </summary>
    /// <param name="path">The file the bytes are going into.</param>
    /// <param name="reached">Set once the write has reached this stream.</param>
    /// <param name="held">Waited on before any byte is written.</param>
    internal HeldOpen(string path, ManualResetEventSlim reached, ManualResetEventSlim held)
    {
        _underneath = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        _reached = reached;
        _held = held;
    }

    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override long Length => _underneath.Length;

    /// <inheritdoc />
    public override long Position
    {
        get => _underneath.Position;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override void Flush() => _underneath.Flush();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        _reached.Set();
        _held.Wait(TimeSpan.FromSeconds(60));
        _underneath.Write(buffer, offset, count);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _underneath.Dispose();
        }

        base.Dispose(disposing);
    }
}
