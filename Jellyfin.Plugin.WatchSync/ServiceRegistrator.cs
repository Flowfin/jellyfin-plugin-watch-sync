using System;
using Jellyfin.Plugin.WatchSync.Configuration;
using Jellyfin.Plugin.WatchSync.Matching;
using Jellyfin.Plugin.WatchSync.Storage;
using Jellyfin.Plugin.WatchSync.Transfer;
using Jellyfin.Plugin.WatchSync.UserData;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jellyfin.Plugin.WatchSync;

/// <summary>
/// Where this plugin's services are registered, which is #8.
///
/// The template holds its instance in a static and reaches for it from anywhere, and that is
/// the shape that makes a matcher, a store and a resolver untestable in isolation. The refusal
/// half of that is already in the tree: `static-instance-not-read` refuses a read of
/// the plugin's own static instance anywhere in this plugin's sources. That literal is not
/// spelled here, because the scan reads these files too and a comment about the rule would be
/// refused by it. What was missing is the other half, a way for a caller to be handed what it
/// needs instead.
///
/// Registration is per service and arrives with the service, which is the answer taken on #8
/// after the single-pass shape was refused: an empty registrator written before anything
/// existed would have satisfied a condition by reading and removed the pressure that makes each
/// later service arrive registered.
/// </summary>
public class ServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);

        // The folder is the server's to give and this plugin's to fill, so it is constructed
        // from IApplicationPaths the server registers rather than from a path composed here.
        serviceCollection.AddSingleton<StoreFolder>();

        // One store over that folder. It is a singleton because its write path is what
        // serialises two callers against one document, and two stores over one folder would
        // each be serialising against themselves.
        serviceCollection.AddSingleton<DocumentStore>();

        // The one place in this plugin that names a real clock, which is the answer taken on
        // #32. Every rule downstream judges at an instant handed in, so a test moves time by
        // handing in a different one, and the declared departure in the invariant register
        // covers this file and no other; ClockEntryTests refuses a second one. TryAdd rather
        // than Add, because the host may already have registered a clock, and two clocks in
        // one container are two answers to what time it is.
        serviceCollection.TryAddSingleton(TimeProvider.System);

        // The settings as the server holds them, read at the moment a rule needs them. It takes
        // the plugin manager rather than the plugin's static instance, which the invariant
        // register refuses, and it reads the configuration afresh rather than copying it at
        // start, which would be the setting an operator changes to no effect.
        serviceCollection.AddSingleton<StoredSettings>();

        // Where the last run of the scheduled sweep is kept. The server constructs a task for
        // its own worker and hands it to nobody, so the run is written here for the status page
        // to read; one instance, or the task and the page would be looking at two records.
        serviceCollection.AddSingleton<SweepRuns>();

        // Where the index reads the library from. It is a singleton because it holds the
        // snapshot of identifiers one walk pages through, and two of them over one library
        // would each be taking a snapshot of their own for the same walk.
        serviceCollection.AddSingleton<IMatchIndexSource, ServerLibrary>();

        // The index over that source, which is #29. One instance, because the index is the
        // thing a rebuild swaps a new map into and the events keep current, and two of them
        // would each be rebuilt by the sweep and fed by nothing. It is the first service here
        // that takes an interface of this plugin's own rather than one of the server's, and
        // the registration is what makes the sweep's constructor satisfiable.
        serviceCollection.AddSingleton<MatchIndex>();

        // One adapter per supported line, and only the line's own implementation is compiled
        // into the target it is built for, so this is a registration rather than a choice made
        // at run time. A branch asking which line this is would be a branch one of the two
        // builds could never take.
#if NET10_0_OR_GREATER
        serviceCollection.AddSingleton<IUserDataGateway, NewerLineUserData>();
#else
        serviceCollection.AddSingleton<IUserDataGateway, OlderLineUserData>();
#endif
    }
}
