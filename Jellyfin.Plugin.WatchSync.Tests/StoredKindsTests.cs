using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.WatchSync.Document;
using Jellyfin.Plugin.WatchSync.Storage;
using Xunit;

namespace Jellyfin.Plugin.WatchSync.Tests;

/// <summary>
/// Holds the store's declaration of what it holds to the types that write into it, which is the
/// property #74's second and third conditions rest on.
///
/// Those conditions ask that a report of everything held about one person be driven by the
/// store's own type list, and that a removal be checked by a scan that finds nothing naming that
/// person in any document. Both are worth exactly what the list they walk is worth. A list
/// maintained by hand and checked by nobody is one that looks complete on the day it is written
/// and silently omits the kind somebody adds afterwards, which is the kind a person asking what
/// is held about them never hears about.
///
/// So the closure runs in both directions and neither direction is the cheap one. A type that
/// writes a document and is not declared is a kind no report reaches. An entry naming a type that
/// writes none is a kind a walk over the store would look for and never find, which reads as an
/// empty answer rather than as a mistake.
/// </summary>
public class StoredKindsTests
{
    /// <summary>
    /// A prefix as the store will accept it in a name: lower case letters, digits and hyphens,
    /// ending on the hyphen that separates it from the identifiers.
    /// </summary>
    private static readonly Regex _shape =
        new Regex("^[a-z0-9]+(-[a-z0-9]+)*-$", RegexOptions.None, TimeSpan.FromSeconds(1));

    /// <summary>
    /// Every type that writes a document into the store is declared as a kind.
    ///
    /// This is the direction #74's second condition is about. A kind that writes documents and is
    /// in no list is one a report driven by the list cannot mention, and the person reading that
    /// report is told less is held about them than is.
    /// </summary>
    [Fact]
    public void EveryTypeThatWritesADocumentIsDeclaredAsAKind()
    {
        var declared = StoredKinds.All.Select(kind => kind.DeclaredBy).ToList();

        Assert.NotEmpty(Writers());

        Assert.Empty(Writers()
            .Where(writer => !declared.Contains(writer))
            .Select(writer =>
                $"{writer.Name} writes a document into the store and {nameof(StoredKinds)} does not declare it, so a report driven by that declaration would not mention what it holds about anybody."));
    }

    /// <summary>
    /// The other direction. An entry naming a type that writes no document is a kind a walk over
    /// the store looks for and never finds, and an empty answer from such a walk is
    /// indistinguishable from a store that holds nothing of that kind.
    /// </summary>
    [Fact]
    public void EveryDeclaredKindNamesATypeThatWritesADocument()
    {
        var writers = Writers();

        Assert.Empty(StoredKinds.All
            .Where(kind => !writers.Contains(kind.DeclaredBy))
            .Select(kind =>
                $"{nameof(StoredKinds)} declares {kind.DeclaredBy.Name} and it writes no document, so a walk over the store would look for a kind that is never there."));
    }

    /// <summary>
    /// Every declared kind can read what it writes, so a walk that meets one of its documents can
    /// open it. A kind that only writes is one a report can count and cannot report.
    /// </summary>
    [Fact]
    public void EveryDeclaredKindCanReadWhatItWrites()
    {
        Assert.Empty(StoredKinds.All
            .Where(kind => kind.DeclaredBy
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .All(method => !IsReader(method)))
            .Select(kind =>
                $"{kind.DeclaredBy.Name} writes a document and declares no static reader taking a {nameof(StoredDocument)}, so a walk over the store could count its documents and not say what is in them."));
    }

    /// <summary>
    /// Two kinds never share a prefix, and no prefix is the beginning of another.
    ///
    /// This is what makes a name readable back into a kind without opening the document, which is
    /// what both of #74's operations need: a report walks the store and says which kind each
    /// document is, and a removal deletes by that reading. Two kinds whose names begin alike make
    /// one kind's documents readable as the other's, and a removal driven by that reading either
    /// misses documents or deletes somebody else's.
    /// </summary>
    [Fact]
    public void NoPrefixIsThePrefixOfAnother()
    {
        var prefixes = StoredKinds.All.Select(kind => kind.NamePrefix).ToList();

        Assert.Empty(prefixes
            .SelectMany(one => prefixes
                .Where(other => !ReferenceEquals(one, other)
                    && other.StartsWith(one, StringComparison.Ordinal))
                .Select(other =>
                    $"{other} begins with {one}, so a document of one kind reads as a document of the other and a removal by that reading deletes or misses the wrong ones."))
            .ToList());
    }

    /// <summary>
    /// Every prefix is a name the store will take, ending on the hyphen that separates it from
    /// the identifiers that follow. A prefix the store refuses is a kind that cannot be written
    /// at all, and it would be met by an operator rather than here.
    /// </summary>
    [Fact]
    public void EveryPrefixIsANameTheStoreWillTake()
    {
        Assert.NotEmpty(StoredKinds.All);

        Assert.Empty(StoredKinds.All
            .Where(kind => !_shape.IsMatch(kind.NamePrefix))
            .Select(kind =>
                $"{kind.DeclaredBy.Name} is named with '{kind.NamePrefix}', which is not a prefix the store composes a path out of."));
    }

    /// <summary>
    /// A kind refuses to be made out of nothing, because a declaration holding a null is one
    /// every walk over the store falls over on rather than one anybody notices here.
    /// </summary>
    [Fact]
    public void AKindOfNoPrefixOrNoTypeIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => new StoredKind(null!, typeof(StoredKinds)));
        Assert.Throws<ArgumentNullException>(() => new StoredKind("kind-", null!));
    }

    /// <summary>
    /// Whether a method is the reader a kind offers: static, taking one document, and answering
    /// something other than the document it was handed.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <returns>Whether it reads a document.</returns>
    private static bool IsReader(MethodInfo method)
    {
        var parameters = method.GetParameters();

        return parameters.Length == 1
            && parameters[0].ParameterType == typeof(StoredDocument)
            && method.ReturnType != typeof(void);
    }

    /// <summary>
    /// Every type in the plugin that writes a document, found by reflection rather than listed
    /// here.
    ///
    /// A list written in this file would be the drift the closure exists to refuse, one level in:
    /// somebody adding the third record kind would add it here as readily as to the declaration,
    /// and the two would agree about a tree neither of them had read.
    ///
    /// A property getter is not a writer, which is what the special-name test is for rather than
    /// tidiness. Three types beside the store carry a document they were handed and offer it
    /// back, and a reading that counted those would declare an answer type as a kind of document
    /// the store holds, which is a walk looking for files nothing has ever written.
    /// </summary>
    /// <returns>The types.</returns>
    private static IReadOnlyList<Type> Writers() =>
        typeof(StoredKinds).Assembly
            .GetTypes()
            .Where(type => type.IsPublic
                && type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Any(method => !method.IsSpecialName
                        && method.ReturnType == typeof(StoredDocument)
                        && method.GetParameters().Length == 0))
            .ToList();
}
