# What this plugin has been checked against

This plugin declares two server lines. A claim about a version nobody ran it on is
how a bug report arrives about a version the author never intended to support, so
this file separates what was built from what was run, and names what was neither.

Read the table first and the prose second. Every cell says what proved it, or says
`not evaluated`, and there is no third kind of cell and no blank one.
`CompatibilityMatrixTests` refuses a blank, refuses a softer word in place of
`not evaluated`, and refuses the rows and `build.yaml` disagreeing about which
lines exist.

## The matrix

| plugin version | framework | declared ABI | built | unit suite | container harness | read by a person on a running server | supported | not checked |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1.0.0.0 | net9.0 | 10.11.11.0 | `call / build` | `call / test` | not evaluated | not evaluated | no | loading on a server, the user data event, any write through the server's own manager, the configuration page in a browser, an upgrade over an earlier install |
| 1.0.0.0 | net10.0 | 12.0.0.0 | `call / build` | `call / test` | not evaluated | not evaluated | no | loading on a server, the user data event, any write through the server's own manager, the configuration page in a browser, an upgrade over an earlier install |

`supported` is `no` on both rows and it is a derived value rather than an opinion.
A line nobody has run this on is unsupported, and the test refuses a row claiming
otherwise while its person column still says `not evaluated`.

## What each cell means, and the command behind it

**built** is a compile against that line's server assemblies. It is the weakest of
the four and it is the only one either line has. The check name in the cell is a
check run on the mainline commit this file was written against:

    gh api repos/iderex/jellyfin-plugin-watch-sync/commits/2253b101ec690b085f616d4a9a7a8c502cbba3a7/check-runs \
      --jq '[.check_runs[] | select(.name=="call / build" or .name=="call / test") | "\(.name)=\(.conclusion)"]'
    ["call / test=success","call / build=success"]

**unit suite** is this repository's own tests, which run once per target framework
because the facts they check differ per target. They exercise this plugin's own
code and its agreement with the build manifest. They do not start a server and
they never will; that is the point of the rule they are held to, which is in
`Jellyfin.Plugin.WatchSync.Tests/headless-rule.md`.

**container harness** is two real servers with this plugin installed from the
packaged artifact, which is #88 and does not exist. Until it does, no cell in this
column can say anything else.

**read by a person on a running server** is somebody installing the artifact on a
stock server of that line and writing down the server version and the date. #10 is
where that lands for the first load. Nothing here has been read that way, so the
column says `not evaluated` rather than describing an expectation.

**not checked** is the column that is usually missing. It names, per row, the
behaviour nobody has evidence about. It is not a list of everything that could go
wrong; it is the set of things a person would have found out by running the plugin
once, and which nothing in this repository can find out for them.

## The declared ABI, and where it comes from

The ABI in each row is not written here by hand in the sense that matters: the
value is held against `build.yaml`, which is itself held against the assemblies
each target compiled against by `BuildTargetsTests`. So the chain from this table
to the bytes is closed at both ends and no link in it is a memory.

    grep -A4 '^targets:' build.yaml
    targets:
    - framework: "net9.0"
      targetAbi: "10.11.11.0"
    - framework: "net10.0"
      targetAbi: "12.0.0.0"

The 10.11 line builds against released server assemblies. The 12.0 line builds
against a release candidate of them, which is what is published today:

    grep -A2 "'\$(TargetFramework)' == 'net10.0'" Jellyfin.Plugin.WatchSync/Jellyfin.Plugin.WatchSync.csproj
      <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
        <PackageReference Include="Jellyfin.Controller" Version="12.0.0-rc4">
          <ExcludeAssets>runtime</ExcludeAssets>

A release candidate can move before it is released. When it does, the reference
moves, `BuildTargetsTests` refuses the manifest that did not follow, and this row
moves with it.

## How a row is added

A row per plugin version and server line, added when that version is built rather
than when somebody remembers. The forcing function today is the test rather than a
release step: a target added to `build.yaml` with no row here fails the suite, and
a row naming a target the manifest does not carry fails it too, so the matrix
cannot be left behind by a change to the lines.

What that does not do is fill in the two right-hand columns. A container run and a
reading by a person are things that happen outside this repository, and the row
they belong to says `not evaluated` until somebody does them and edits the cell.
The release process writing those cells is #122 and #10 rather than this file.
