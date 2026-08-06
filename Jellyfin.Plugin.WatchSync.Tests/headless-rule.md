# The headless test rule

Every test this plugin ships runs without a display, without elevation, without a
machine-wide trust store, without the network, without a real wait and without the
machine clock.

This is a birth requirement rather than a cleanup. A suite that needs any of those
runs on one machine and quietly stops running everywhere else, and the first anybody
hears of it is when a regression ships.

The rule is enforced by `HeadlessGuardTests`, which scans the sources of this project
against `Headless/vocabulary.txt`. The table at the end of this file is the same set
of identifiers, and a test refuses the two lists disagreeing, so a rule added to the
guard without a line here fails and a line here naming a rule the guard does not
carry fails as well.

## What is refused, and what replaces it

A test that drives the configuration page in a browser. Refused, it needs a display.
The replacement is to parse the page's markup in process, compare its controls
against the configuration type, and call the endpoints behind it directly.

A test that installs a certificate into a machine trust store so two test servers can
speak to each other over TLS. Refused. The replacement is the two-server harness with
no transport at all, and where a transport is genuinely needed, loopback inside a
container in the opt-in harness.

A test that needs administrator rights: a machine-wide path, a privileged port, a
symbolic link on a system that gates them. Refused. The replacement is a temporary
directory the test owns, an ephemeral high port, and a file copy where a link was
wanted.

A test that reads the machine clock or the local time zone. Refused, it fails at a
date boundary on somebody else's machine, and much of what this plugin does is about
time. The replacement is the injected clock, advanced on command.

A test that reaches the network, including to a peer, a metadata provider or a
package feed. Refused. The replacement is a fake for the one interface that would
have made the call.

A test that sleeps to wait for a backoff, a timeout or a schedule. Refused, it is the
test that gets deleted the first time it is flaky. The replacement is the injected
clock and a scheduler that can be driven.

A test that requires a real Jellyfin server installed on the machine running the
suite. Refused. The replacement is the container harness, which is an opt-in job and
never a unit test.

A test that requires the pairing plugin to be present. Refused. The replacement is
the fake behind the adapter, and later the test double that board ships.

## What the guard does not refuse

The last two above have no entry in the vocabulary. Neither has a call to grep for:
a test needing a real server is a test reading a path or a port that a running server
happens to hold, and a test needing the pairing plugin is a test referencing a type
that is not in this tree and would fail to compile before any scan ran. Both are
refused by this document and by the review, and neither is refused by a machine.

The two entries covering the network and the machine clock are written as outright
refusals. The rule asks for them to be refused outside the designated fake and
outside the injected clock, and neither of those exists yet. Once they do, those
entries name the place they permit rather than refusing everywhere.

## The rules the guard carries

| id | what a test matching it would need |
| --- | --- |
| `browser-selenium` | a display and a browser |
| `browser-playwright` | a display and a browser |
| `browser-puppeteer` | a display and a browser |
| `browser-webdriver` | a display and a browser |
| `elevation-windows-identity` | administrator rights to be asked about or held |
| `elevation-windows-principal` | administrator rights to be asked about or held |
| `elevation-builtin-role` | administrator rights to be asked about or held |
| `elevation-runas` | an elevation prompt on the machine running the suite |
| `elevation-sudo` | elevation on the machine running the suite |
| `trust-store-x509store` | a machine-wide certificate store |
| `trust-store-store-name` | a machine-wide certificate store |
| `trust-store-store-location` | a machine-wide certificate store |
| `machine-path-special-folder` | a path outside the test's own output |
| `machine-path-etc` | a path outside the test's own output |
| `machine-path-usr` | a path outside the test's own output |
| `machine-path-var` | a path outside the test's own output |
| `machine-path-library` | a path outside the test's own output |
| `machine-path-program-files` | a path outside the test's own output |
| `network-http-client` | a network the machine running the suite may not have |
| `network-socket` | a network the machine running the suite may not have |
| `network-tcp-client` | a network the machine running the suite may not have |
| `network-tcp-listener` | a network the machine running the suite may not have |
| `network-udp-client` | a network the machine running the suite may not have |
| `network-web-request` | a network the machine running the suite may not have |
| `network-dns` | a resolver and a network |
| `sleep-thread` | real elapsed time |
| `sleep-task-delay` | real elapsed time |
| `sleep-spin-wait` | real elapsed time |
| `clock-datetime-now` | the machine clock |
| `clock-datetime-utcnow` | the machine clock |
| `clock-datetimeoffset-now` | the machine clock |
| `clock-datetimeoffset-utcnow` | the machine clock |
| `clock-tick-count` | the machine clock |
| `clock-stopwatch` | the machine clock |
| `clock-time-provider-system` | the machine clock |
| `clock-local-time-zone` | the local time zone of the machine running the suite |

## Declaring a departure

`Headless/exceptions.txt` holds one entry per departure, with the path, the rule and
the reason. An entry whose file no longer carries the call it was written for is
refused as dangling, so an exception is a debt with the thing that retires it written
next to it rather than a permanent hole.
