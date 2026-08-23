using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Jellyfin.Plugin.WatchSync.Document;

namespace Jellyfin.Plugin.WatchSync.Storage;

/// <summary>
/// Reads and writes the documents in this plugin's store, and answers #70: a reader sees the
/// document that was there or the document that arrived, and never a mixture of the two.
///
/// Servers are killed. A container is stopped, a power supply is pulled, a disk fills. What
/// makes that survivable is not care at the call site but the shape of the write: the bytes go
/// into a file beside the document, and the document is replaced by that file only once every
/// byte is down. A kill before the replace leaves the previous document untouched, a kill after
/// it leaves the new one, and there is no moment in between where a reader could find half of
/// either.
///
/// <para>What is serialised, and what deliberately is not. Two writes of one document may not
/// interleave, so the replace of one document is taken under a gate of that document's own.
/// There is no gate over the store, because the sweep and the event path have to run at once and
/// a gate over the store is what stops them: a sweep writing a large document would hold every
/// other document still for as long as its bytes took to land. The bytes are therefore written
/// outside every gate, and the gate covers the replace and the generation check and nothing
/// else.</para>
///
/// <para>What that costs, said rather than left to be found. A caller's change is computed from
/// the document that was on disk when the attempt began, so where another caller replaced the
/// document in between, the attempt is dropped and made again against what is there now. The
/// change therefore runs more than once, and it has to be a function of what it was handed
/// rather than of anything it counted on the way. A change that adds to what it was given
/// converges; one that increments a number it read somewhere else does not.</para>
///
/// <para>The bound on the atomicity is the platform's replace. On a POSIX filesystem that is
/// the rename call, which replaces the name in one step. On Windows it is the move-with-replace
/// the runtime issues, which is the same guarantee for a source and a destination on one volume,
/// and both of ours are in the store folder for exactly that reason. Nothing here would survive
/// a store spread over two volumes, and nothing puts it there.</para>
/// </summary>
public sealed class DocumentStore
{
    /// <summary>
    /// What a document is called on disk, under the name the caller asks for.
    /// </summary>
    internal const string DocumentSuffix = ".json";

    /// <summary>
    /// What the file holding the bytes of a write in flight is called.
    ///
    /// It is a suffix rather than a hidden name so that an operator who finds one after a kill
    /// can tell what it is and that removing it loses nothing. Nothing reads a file with this
    /// suffix: a reader asks for a document by name and gets the document or nothing.
    /// </summary>
    internal const string InFlightSuffix = ".writing";

    private static readonly UTF8Encoding _bytesOfADocument = new UTF8Encoding(false);

    private readonly StoreFolder _folder;
    private readonly Func<string, Stream> _openForWriting;
    private readonly ConcurrentDictionary<string, Gate> _gates =
        new ConcurrentDictionary<string, Gate>(StringComparer.Ordinal);

    private long _attempts;

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentStore"/> class over the folder the
    /// server's data path holds.
    /// </summary>
    /// <param name="folder">Where this plugin's documents sit.</param>
    public DocumentStore(StoreFolder folder)
        : this(folder, OpenForWriting)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DocumentStore"/> class over a way of opening
    /// the file a write in flight goes into.
    ///
    /// The second parameter is here because a full disk is a state this write path answers for
    /// and a suite cannot produce one. Filling a real filesystem needs a filesystem the run is
    /// allowed to fill, which no machine running this suite is obliged to have, and a run that
    /// filled one could take that machine down with it. So the failure is raised where the
    /// platform raises it, on the stream the bytes go into, and the assertion is about what this
    /// type does with it rather than about what the platform does.
    /// </summary>
    /// <param name="folder">Where this plugin's documents sit.</param>
    /// <param name="openForWriting">How the file a write in flight goes into is opened.</param>
    public DocumentStore(StoreFolder folder, Func<string, Stream> openForWriting)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(openForWriting);

        _folder = folder;
        _openForWriting = openForWriting;
    }

    /// <summary>
    /// What one replace of one document came back with.
    /// </summary>
    private enum ReplaceOutcome
    {
        /// <summary>
        /// The document is the one this attempt wrote.
        /// </summary>
        Replaced,

        /// <summary>
        /// Somebody else replaced the document while this attempt was writing its bytes, so this
        /// attempt is about a document that is no longer there and is made again.
        /// </summary>
        Stale,

        /// <summary>
        /// The filesystem refused the replace, and the document that was there is still there.
        /// </summary>
        Refused,
    }

    /// <summary>
    /// Reads one document out of the store.
    /// </summary>
    /// <param name="name">The document's name, without a suffix.</param>
    /// <returns>
    /// What the document turned out to be, or null where there is no such document. Absence is
    /// not a reading: an item nothing has been agreed about yet and a document that could not be
    /// read are different situations with different repairs, and <see cref="DocumentAnswer"/>
    /// carries no value for the first because there is nothing there to answer about.
    /// </returns>
    /// <exception cref="IOException">
    /// The filesystem refused the read, which is a different thing from there being nothing
    /// there. It leaves here as it arrived rather than as a null, because a caller that read the
    /// two as one answer would treat an unreadable store as a store nothing has been agreed in,
    /// and the exchange after that would be a first one.
    /// </exception>
    public DocumentReading? Read(string name) => ReadWhereItIsThere(PathFor(name));

    /// <summary>
    /// Replaces one document with what a change makes of the document that is there.
    /// </summary>
    /// <param name="name">The document's name, without a suffix.</param>
    /// <param name="change">
    /// What the document becomes, given what was read. It is handed null where there is no
    /// document yet, and it may be called more than once where another caller replaced the
    /// document while this one was writing its bytes.
    /// </param>
    /// <returns>What the attempt came back with.</returns>
    public DocumentWriteAnswer Write(string name, Func<DocumentReading?, StoredDocument> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        var path = PathFor(name);
        var gate = _gates.GetOrAdd(name, _ => new Gate());

        while (true)
        {
            var seen = gate.Generation;

            if (!ReadAt(path, out var reading))
            {
                return DocumentWriteAnswer.RefusedByTheFilesystem();
            }

            if (reading is not null && reading.Answer == DocumentAnswer.FromTheFuture)
            {
                return DocumentWriteAnswer.RefusedByADocumentFromTheFuture();
            }

            var document = change(reading);

            ArgumentNullException.ThrowIfNull(document);

            var inFlight = InFlightPathFor(name);

            if (!Land(inFlight, document))
            {
                return DocumentWriteAnswer.RefusedByTheFilesystem();
            }

            var replaced = gate.Under(seen, () => Replace(inFlight, path));

            if (replaced == ReplaceOutcome.Stale)
            {
                Discard(inFlight);
                continue;
            }

            if (replaced == ReplaceOutcome.Refused)
            {
                Discard(inFlight);
                return DocumentWriteAnswer.RefusedByTheFilesystem();
            }

            return DocumentWriteAnswer.Written(document);
        }
    }

    private static Stream OpenForWriting(string path) =>
        new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);

    /// <summary>
    /// Puts the file a write in flight wrote in the document's place, in one step.
    /// </summary>
    /// <param name="inFlight">The file holding every byte of the new document.</param>
    /// <param name="path">The document being replaced.</param>
    /// <returns>Whether the replace happened.</returns>
    private static bool Replace(string inFlight, string path)
    {
        try
        {
            File.Move(inFlight, path, true);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void Discard(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Refuses a name this store may not compose a path out of.
    ///
    /// A document's name will reach this type from a pairing, a user or an item, so part of it
    /// is a name something outside this server chose. The refusal is a closed set of characters
    /// rather than a search for the separators of whichever platform the code happens to run on:
    /// a rule written as no slash is a rule that misses the other slash, the drive letter and
    /// the two dots, and each of those has been somebody's directory traversal.
    /// </summary>
    /// <param name="name">The name the caller asked for.</param>
    private static void RefuseANameThisStoreMayNotCompose(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        foreach (var character in name)
        {
            var allowed = character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-';

            if (!allowed)
            {
                throw new ArgumentException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "A document name is lower case letters, digits and hyphens. {0} is not, so this store composes no path for it.",
                        name),
                    nameof(name));
            }
        }
    }

    /// <summary>
    /// Reads the document at a path, telling absence apart from a filesystem that refused.
    /// </summary>
    /// <param name="path">The document's path.</param>
    /// <param name="reading">What it turned out to be, or null where there is nothing there.</param>
    /// <returns>Whether the filesystem answered at all.</returns>
    private static bool ReadAt(string path, out DocumentReading? reading)
    {
        reading = null;

        try
        {
            reading = ReadWhereItIsThere(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// The document at a path, or nothing where there is no such file.
    /// </summary>
    /// <param name="path">The document's path.</param>
    /// <returns>What it turned out to be, or null where there is nothing there.</returns>
    private static DocumentReading? ReadWhereItIsThere(string path)
    {
        string json;

        try
        {
            json = File.ReadAllText(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }

        return StoredDocument.Read(json, DocumentVersions.Current);
    }

    /// <summary>
    /// Puts every byte of a document into the file a write in flight uses.
    /// </summary>
    /// <param name="path">The file the bytes go into.</param>
    /// <param name="document">The document being written.</param>
    /// <returns>Whether every byte is down.</returns>
    private bool Land(string path, StoredDocument document)
    {
        var bytes = _bytesOfADocument.GetBytes(document.ToJson());

        try
        {
            using (var stream = _openForWriting(path))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();

                if (stream is FileStream file)
                {
                    // The kill this write survives is a killed process, and a killed process
                    // loses nothing the operating system already holds. A pulled power supply is
                    // the other half of the sentence #70 opens with, and it is the one that
                    // loses a write the operating system had not put on the disk yet.
                    file.Flush(true);
                }
            }

            return true;
        }
        catch (IOException)
        {
            Discard(path);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            Discard(path);
            return false;
        }
    }

    private string PathFor(string name)
    {
        RefuseANameThisStoreMayNotCompose(name);

        return Path.Combine(_folder.CreateIfAbsent(), name + DocumentSuffix);
    }

    /// <summary>
    /// The file the bytes of one attempt go into, beside the document they will become.
    ///
    /// Beside it rather than anywhere else, because the replace is a rename and a rename across
    /// two filesystems is a copy. The number makes two attempts on one document two files, so
    /// neither is writing over the other's bytes while both are outside the gate. It restarts at
    /// one in a new process, so a file left behind by a killed one is written over rather than
    /// accumulated beside.
    /// </summary>
    /// <param name="name">The document's name.</param>
    /// <returns>The path of the file this attempt writes.</returns>
    private string InFlightPathFor(string name)
    {
        var attempt = Interlocked.Increment(ref _attempts);

        return Path.Combine(
            _folder.CreateIfAbsent(),
            name + "." + attempt.ToString(CultureInfo.InvariantCulture) + InFlightSuffix);
    }

    /// <summary>
    /// What serialises the replaces of one document, and of one document only.
    /// </summary>
    private sealed class Gate
    {
        private readonly object _replace = new object();

        private long _generation;

        /// <summary>
        /// Gets how many times this document has been replaced through this store.
        ///
        /// It is what an attempt compares against to find out whether the document it read is
        /// still the document it is about to replace. One process owns a store, which is why a
        /// number in memory is enough; a second process writing into the same folder is outside
        /// what this type answers for and is not a state a server produces.
        /// </summary>
        internal long Generation => Interlocked.Read(ref _generation);

        /// <summary>
        /// Replaces the document, where it is still the one the attempt read.
        ///
        /// This is the whole of what is serialised. The bytes are already down by the time it is
        /// called, so what is held here is a comparison and a rename rather than a write, and a
        /// document being written elsewhere in the store waits on none of it.
        /// </summary>
        /// <param name="seen">What the generation was when the attempt began.</param>
        /// <param name="replace">The replace itself, which answers whether it happened.</param>
        /// <returns>What the attempt came back with.</returns>
        internal ReplaceOutcome Under(long seen, Func<bool> replace)
        {
            lock (_replace)
            {
                if (Interlocked.Read(ref _generation) != seen)
                {
                    return ReplaceOutcome.Stale;
                }

                if (!replace())
                {
                    return ReplaceOutcome.Refused;
                }

                Interlocked.Increment(ref _generation);
                return ReplaceOutcome.Replaced;
            }
        }
    }
}
