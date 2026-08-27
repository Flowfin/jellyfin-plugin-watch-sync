using System;

namespace Jellyfin.Plugin.WatchSync.Storage;

/// <summary>
/// One kind of document this plugin's store holds: the prefix its names begin with, and the
/// type that reads and writes it.
/// </summary>
public sealed class StoredKind
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StoredKind"/> class.
    /// </summary>
    /// <param name="namePrefix">What every document of this kind is named with.</param>
    /// <param name="declaredBy">The type that reads and writes it.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public StoredKind(string namePrefix, Type declaredBy)
    {
        ArgumentNullException.ThrowIfNull(namePrefix);
        ArgumentNullException.ThrowIfNull(declaredBy);

        NamePrefix = namePrefix;
        DeclaredBy = declaredBy;
    }

    /// <summary>
    /// Gets what every document of this kind is named with, before the identifiers that say
    /// which pairing and which person it is about.
    /// </summary>
    public string NamePrefix { get; }

    /// <summary>
    /// Gets the type that reads and writes documents of this kind.
    /// </summary>
    public Type DeclaredBy { get; }
}
