# Readings taken on a running server

A reading is somebody installing a packaged artifact on a server that was not
built here and writing down what happened, with the server version, the artifact
checksum and the date beside it. The suite re-runs none of it and no check
asserts any of it, so each entry carries the commands that produced it.

A reading that failed is recorded the same way as one that passed. An entry is
never removed and never edited once it names a date; a later reading is a later
entry.

## 2026-08-09, first load, both declared lines

The plugin loaded on a stock server of each declared line and appeared under its
own name and its own identifier. Nothing else about the plugin was exercised,
because there is nothing else to exercise yet: it registers no service, reads no
user data and writes nothing.

### What was installed

Two packages, both built from `build.yaml` at `7d548a8` by the packaging tool the
publish route uses, one per declared framework:

    jprm --version
    Jellyfin Plugin Repository Manager, version 1.1.0

    jprm plugin build . --output <dir> --dotnet-framework net9.0  --dotnet-configuration Release
    jprm plugin build . --output <dir> --dotnet-framework net10.0 --dotnet-configuration Release

    sha256sum pkg-net9/watch-sync_1.0.0.0.zip pkg-net10/watch-sync_1.0.0.0.zip
    031fc0a28279c8909780379cc8d8f8bca33eb0b49d0a30c03b84a0466234efb8 *pkg-net9/watch-sync_1.0.0.0.zip
    b2ddddbe2f28388cc0f9396753ea50fb1c4656c703ed46b6daa70755c6370338 *pkg-net10/watch-sync_1.0.0.0.zip

Each archive holds two entries and no third:

    python -c "import zipfile,sys;[print(n) for n in zipfile.ZipFile(sys.argv[1]).namelist()]" pkg-net9/watch-sync_1.0.0.0.zip
    Jellyfin.Plugin.WatchSync.dll
    meta.json

That is the answer to the failure this reading was opened against. A package
reference that was not excluded from the output arrives as a second assembly in
the archive, and there is none in either.

The `net9.0` package is what the publish route builds today, because the route
passes the single top-level pair `build.yaml` declares. The `net10.0` package is
not something that route produces; it was built here by asking the same tool for
the other declared framework. Packaging one artifact per declared line is #101
and #117.

### The servers

Containers of the official images, one per line, with no library, no media and
nothing mounted from the host. The package was copied in with `docker cp` and the
server restarted.

    curl -s http://127.0.0.1:18096/System/Info/Public
    {"LocalAddress":"http://172.17.0.2:8096","ServerName":"aec8c13b44ff","Version":"10.11.11","ProductName":"Jellyfin Server","OperatingSystem":"","Id":"a05c5c6cf1d643439e0477c26a70d5de","StartupWizardCompleted":false}

    curl -s http://127.0.0.1:18097/System/Info/Public
    {"LocalAddress":"http://172.17.0.3:8096","ServerName":"e31987660cb3","Version":"12.0.0","ProductName":"Jellyfin Server","OperatingSystem":"","Id":"1d092870fa4f4437b9db40862ef947c8","StartupWizardCompleted":false}

The image tagged `12.0-rc4` reports its version as `12.0.0`. The plugin's
`net10.0` target compiles against the `12.0.0-rc4` packages, so the row this
reading fills is that line's.

### 10.11.11, from `jellyfin/jellyfin:10.11.11`

    docker logs wsync-1011 | grep WatchSync
    [16:42:06] [INF] [9] Emby.Server.Implementations.Plugins.PluginManager: Loaded assembly Jellyfin.Plugin.WatchSync, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null from /config/plugins/Watch Sync_1.0.0.0/Jellyfin.Plugin.WatchSync.dll
    [16:42:06] [INF] [9] Emby.Server.Implementations.Plugins.PluginManager: Loaded plugin: Watch Sync 1.0.0.0

    curl -s http://127.0.0.1:18096/Plugins -H "Authorization: ..." | sed 's/},{/}\n{/g' | grep 'Watch Sync'
    {"Name":"Watch Sync","Version":"1.0.0.0","ConfigurationFileName":"Jellyfin.Plugin.WatchSync.xml","Description":"","Id":"aa15847e24174fa7889c4c1960d2efec","CanUninstall":true,"HasImage":false,"Status":"Active"}

    curl -s "http://127.0.0.1:18096/web/ConfigurationPage?name=Watch%20Sync" | head -5
    <!DOCTYPE html>
    <html lang="en">
    <head>
        <meta charset="utf-8">
        <title>Watch Sync</title>

No line of that server's log mentions the plugin at warning level or above:

    docker logs wsync-1011 2>&1 | grep -E '\[ERR\]|\[WRN\]' | grep -ci watchsync
    0

### 12.0.0, from `jellyfin/jellyfin:12.0-rc4`

    docker logs wsync-12rc4 | grep WatchSync
    [16:28:51.104] [INF] [10] Emby.Server.Implementations.Plugins.PluginManager: Loaded assembly Jellyfin.Plugin.WatchSync, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null from /config/plugins/Watch Sync_1.0.0.0/Jellyfin.Plugin.WatchSync.dll
    [16:28:52.045] [INF] [10] Emby.Server.Implementations.Plugins.PluginManager: Loaded plugin: Watch Sync 1.0.0.0

    curl -s http://127.0.0.1:18097/Plugins -H "Authorization: ..." | sed 's/},{/}\n{/g' | grep 'Watch Sync'
    {"Name":"Watch Sync","Version":"1.0.0.0","ConfigurationFileName":"Jellyfin.Plugin.WatchSync.xml","Description":"","Id":"aa15847e24174fa7889c4c1960d2efec","CanUninstall":true,"HasImage":false,"Status":"Active"}

    curl -s "http://127.0.0.1:18097/web/ConfigurationPage?name=Watch%20Sync" | head -5
    <!DOCTYPE html>
    <html lang="en">
    <head>
        <meta charset="utf-8">
        <title>Watch Sync</title>

    docker logs wsync-12rc4 2>&1 | grep -E '\[ERR\]|\[WRN\]' | grep -ci watchsync
    0

### The identifier the two servers report

Both report `aa15847e24174fa7889c4c1960d2efec`, which is the manifest's own guid
with its separators removed:

    grep '^guid:' build.yaml
    guid: "aa15847e-2417-4fa7-889c-4c1960d2efec"

So the plugin carries its own identity on both lines rather than the template's,
which is the half of this reading a build cannot answer.

### Two things this reading found on the way

The packaged metadata for the `net10.0` archive declares the 10.11 ABI:

    grep -o '"targetAbi": "[^"]*"' pkg-net10/watch-sync_1.0.0.0.zip.meta.json
    "targetAbi": "10.11.11.0"

The packaging tool takes that value from the single top-level pair in
`build.yaml` and does not know about the per-line list beside it, so an archive
built at the other framework still carries the first line's ABI. The 12.0 server
loaded it anyway. What that server does with the value was not measured here, and
the archive is wrong about itself either way. It belongs with #101 and #117,
where packaging one artifact per line is decided.

Packaging in a working tree rewrites a tracked file:

    git diff --stat Directory.Build.props
     Directory.Build.props | 6 +++---

The tool replaces the contents of `Version`, `AssemblyVersion` and `FileVersion`
with a literal, removing the derivation from the manifest that
`Directory.Build.props` otherwise carries. The publish route runs it on a
throwaway checkout, so nothing there notices. Anybody repeating this reading has
to restore the file afterwards, and nothing refuses committing it.

### What was not checked

The user data event, any write through the server's own user data manager, the
configuration page rendered in a browser, an upgrade over an earlier install, and
every behaviour this plugin is for. None of them exists yet.

No client played anything. No browser was opened: the name and the identifier
come from the plugins endpoint that the dashboard's plugin list is built from,
and the configuration page from the address the dashboard fetches it at, rather
than from a rendered screen.

Both servers ran their startup wizard through the API with a single local
account, because the plugins endpoint refuses an unauthenticated caller. Neither
server was given a library.

The reading was taken on one machine, on Linux containers under one container
runtime. A different host, a different runtime or an install that is not a
container is not covered by it.
