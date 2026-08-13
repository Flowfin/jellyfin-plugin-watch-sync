# What this plugin logs, and what it may never log

This plugin handles a record of what people watched. A log is a file that gets copied
into a support thread, shipped to a collector, and read by whoever administers the
server, so what goes into it is a decision rather than a habit. The decision is here,
and the part of it a machine refuses is marked as such, so a reader can tell a rule that
is held from a rule that is written down.

The rules were argued in #67. This document is where they are stated, and
[invariants.md](invariants.md) is where the scan carrying two of them is argued.

## What may be logged at the ordinary level

Counts, durations, pairing identities, refusal reasons, item identifiers and the rule
that decided a conflict.

An item identifier here is this plugin's own match key. It names an item to an operator
looking at two servers, and it resolves to nothing outside this plugin, which is what
makes it usable in a log where a title and a provider identifier are not.

## What may never be logged, at any level

| what may never be logged | held by |
| --- | --- |
| the title of a work next to a user | `log-item-title` |
| a provider identifier for a work next to a user | `log-provider-identifier` |
| anything from the key material the pairing plugin holds | this document and a reading, #40 |
| any part of a peer's payload, verbatim | this document and a reading, #63 |

The right-hand column is the whole of the disclosure. Two of the four are refused by a
pattern that runs on every suite run; the other two are refused by nobody, and the
section below says why and what would change that.

## What may be logged at the diagnostic level

The per-item decisions of one run, still without titles, only when an operator turns it
on, and only with the plugin saying that it is on.

The level is a setting and the page has to show it is on. Neither exists: the
configuration type carries no setting, which is #58, and there is no page of this
plugin's own, which is #57. So this level is a rule about a switch nobody can flip yet.

## What a machine refuses

Two rules carry the first two rows above. They are held as data in the invariant
vocabulary beside the test project, and `InvariantGuardTests` runs them over this
plugin's own sources.

| rule | what a matching call does | what to reach for instead |
| --- | --- | --- |
| `log-item-title` | writes the title of a work somebody watched into a log | the match key, which names the item to an operator without naming the work |
| `log-provider-identifier` | writes an identifier that resolves to the work in one search | the match key, which is this plugin's own addressing and resolves to nothing outside it |

**Both are stronger than the rows they carry, on purpose.** The rows forbid a title and a
provider identifier next to a user. A pattern reads one line, so it cannot decide whether
a user is named in the same statement, and a log call split across lines would defeat one
that tried. The rules therefore refuse a title or a provider identifier reaching a log
call at all. That refuses nothing this document permits, because the ordinary level
allows the match key and allows a title at no level.

Each rule is proven by a near miss rather than by an obviously broken file, in
`EachInvariantIsRefusedOnItsNearMissAndPassesItsRepair`. The fixture is an apply-path log
line that already carries the match key one argument to its left and reaches for the
title anyway, and its repair is that one argument.

`LoggingDocumentTests` holds this document and that vocabulary to the same set of rules,
in both directions, so a rule added to the guard with no row here fails, and a row here
claiming a rule the guard does not carry fails as well.

## What nothing scans

**The pairing plugin's key material.** There is nothing to scan for. This plugin has no
pairing adapter, so there is no call that could carry key material into a log. #40 is the
adapter, and the rule becomes scannable at the moment it exists, because the material
will then have a name to pattern on.

**A peer's payload.** Same shape and a different reason. There is no transfer, so no
value has ever come from a peer. #63 is where a peer value is bounded and stripped before
it reaches a page or a log, and it is one guard shared with the page rather than a second
implementation of the same stripping.

**The subject is small.** The two rules that do run read this plugin's own sources, and
those sources carry no log call at all today. A run that finds nothing over code that
could not have violated the rule is not evidence that the rule holds. The near-miss
fixtures are what make each rule a guard rather than a green tick.

## What is owed

The privacy note in M11, which is #107, has to reference this document, and it does not
exist yet. That reference is owed rather than made.
