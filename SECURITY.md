# Security policy

## What this plugin is, and what it is today

Watch Sync is loaded into a Jellyfin server and runs inside that server's
process. It holds the server's privileges, against that server's media database
and its user accounts, so a defect here is a defect in somebody else's server.
That is why this file is specific rather than general.

It is also worth saying what the tree actually holds, because it changes which
reports can be honest. `README.md` says the plugin syncs nothing yet and the
sources agree: `Jellyfin.Plugin.WatchSync/Plugin.cs` registers a name, an
identifier and one configuration page, and there is no scheduled task, no
controller, no route and no apply path. `docs/transfer.md` describes an exchange
between two servers as a design and says in its own closing section that nothing
in the tree holds it. There are no releases and no tags.

## The outbound side

This is the part that separates this repository from the other plugins beside
it. Watch Sync is meant to have a peer: a second server it asks for what
changed. Today it has none. There is no HTTP client, no socket and no reference
to `System.Net` anywhere under `Jellyfin.Plugin.WatchSync/`, and a grep across
the 35 source files finds three calls that reach outside the process, all of
them in `Storage/StoreFolder.cs`: one `Directory.Exists` and two
`Directory.CreateDirectory`. It sends nothing, to nowhere, and there is no
remote answer for it to believe.

Two properties bound what an exchange could do, and I will hold to both, but
only one of them is something you can check today. What may leave a server is
bounded now, by `Jellyfin.Plugin.WatchSync/Model/SyncedState.cs`, which carries
four members against the ten properties the server's own per-user record holds:
a field that is not a member of that type has no route into a transfer, whatever
a later caller intends. The second is a design rule rather than a property of
any code: a peer's reply is a proposal, never an instruction, and every decision
about what to write is taken by the asking side, on its own users.
`docs/transfer.md` states that rule and says in the same document that there is
no apply path for it to bind.

This plugin holds no credential for a peer. The pairing between two servers, the
key material behind it and the mapping between user accounts all belong to a
separate pairing plugin, and this one consumes a mapping and never infers one.
A report about how Watch Sync authenticates a peer is a report about a component
that is not in this repository.

## Reporting

Private vulnerability reporting is enabled here, so the advisory form is the
channel and it answers:

    gh api repos/Flowfin/jellyfin-plugin-watch-sync/private-vulnerability-reporting
    {"enabled":true}
    exit 0

https://github.com/Flowfin/jellyfin-plugin-watch-sync/security/advisories/new

Please use that rather than a public issue for anything you think is
exploitable. A public issue is fine for everything else.

## What I do not promise

I do not promise a time to acknowledge or to fix. A deadline this project cannot
keep is worse than none at all: a reporter told to expect an answer within a
stated window, who then hears nothing, cannot tell whether the report was
received and is being worked on, or was lost on the way. Silence against no
promise is ambiguous once. Silence against a promise teaches a reporter that
what this repository writes down is not what it does.

## What is worth reporting

- A peer value that reaches a page or a log without passing
  `Jellyfin.Plugin.WatchSync/Peer/PeerText.cs`, or a hole in what that does. It
  bounds the length and removes every control, format, line separator and
  paragraph separator character, so a peer cannot forge a log line or reorder
  what a reader sees around one. It deliberately does not escape markup, because
  escaping belongs where a value is rendered. If a page lands that renders a peer
  value without escaping it, that is a finding here.
- A wrong match. `Matching/ProviderIdentifier.cs` normalises identifiers that
  scrapers wrote, and `Matching/MatchIndex.cs` answers which local item carries a
  key. Any input that makes two different works compare equal is a security bug
  in this repository and not only a correctness one, because the consequence is
  one person's watch history written onto another person's item.
- Anything that makes the store readable by an account other than the server's
  own. `Storage/StoreFolder.cs` creates `watch-sync` under the server's data
  path, owner only where the platform has POSIX modes and unset on Windows, and
  what it is meant to hold is a record of what people watched.
- A document parsed before its version was decided, or a refused document a
  caller can still write back. `Document/StoredDocument.cs` reads the `version`
  member first and `Document/DocumentReading.cs` carries no document at all when
  it refuses. A route around either is a finding.
- A work's title or a provider identifier reaching a log. `docs/logging.md`
  forbids both, `InvariantGuardTests` refuses two of its four rows over this
  plugin's own sources, and the other two are held only by a reading. A way
  through that gap is worth reporting.
- Anything under `.github/workflows/`. No workflow uses `pull_request_target`,
  most declare `permissions: {}` at the top level and `zizmor.yml` lints the
  rest, but a route from a pull request to a repository token or to a minted
  attestation is in scope.
- A published artifact that does not match its source. Releases attest build
  provenance and ship a `.sha256` beside the archive, and
  `gh attestation verify <archive>.zip --repo Flowfin/jellyfin-plugin-watch-sync`
  is how to check one. Nothing has been released, so there is nothing to check
  today.

## What is not a vulnerability here

- That the plugin syncs nothing. It is a skeleton and the README says so. An
  absent feature is not a defect in a present one.
- A defect in Jellyfin itself: its authentication, its API, its dashboard, its
  handling of media paths. Report those to the Jellyfin project. This plugin
  cannot fix them and an advisory here would only delay the people who can.
- Anything about pairing, key material or the user mapping between two servers.
  None of it is in this tree, and reporting it here sends it to the wrong
  repository.
- An item that did not sync because it carries no provider identifier.
  `docs/matching.md` refuses a file path and a file name as match rules and gives
  the reason for each, and an item carrying no identifier produces
  `NoIdentifierAtAll` in `Matching/MatchKeyRefusal.cs` rather than a guess. The
  guess is what would be the vulnerability.
- The MD5 file beside a release archive. That is the value a Jellyfin catalog
  reads as a plugin checksum, so it is a format the catalog fixes rather than an
  integrity claim I am making. The sha256 and the provenance attestation are what
  I would want checked.
- An advisory against a dependency that never reaches a running server. The
  analyzer packages are `PrivateAssets="All"` and the Jellyfin reference
  assemblies are pulled with `<ExcludeAssets>runtime</ExcludeAssets>` because the
  server carries its own copies. `.github/dependency-acceptances.txt` is the
  register for what is knowingly carried and it holds no entries today.
- That no media file ever moves. That is the permanent non-goal in
  `docs/sync-model.md`, not a missing capability. Scanner output with no path
  through this code is the same shape of report: a rule identifier and a line
  number is a starting point rather than a finding.

## What helps, and which versions I fix

The file and the function, what an attacker holds at the start, and which server
line you were on. Two are built for, 10.11 on `net9.0` and 12.0 on `net10.0`.
`docs/compatibility.md` marks both rows `supported: no` and both
`container harness: not evaluated`. What each line has had is one first load on a
stock server, recorded in `docs/first-load.md` on 2026-08-09: the plugin appeared
under its own name and its own identifier and did nothing, because there is
nothing yet for it to do. If you found it by reading rather than by running, say
so. That is still worth sending, and worth more than a claim dressed up as a
reproduction.

There is no release, so there is no supported version to name. `master` is the
only thing to fix and the only thing a fix would land on.
