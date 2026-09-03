# The endpoints this plugin serves

A document that drifts from the routes is worse than no document, because it is
trusted. So this one is held against the routes by a comparison that fails in both
directions rather than by somebody remembering to update it, and what that comparison
cannot see is written down here beside what it can.

**This plugin serves four endpoints and the table below has a row for each.** That is a
reading rather than a claim:

    git grep -l 'ControllerBase' -- Jellyfin.Plugin.WatchSync/
    Jellyfin.Plugin.WatchSync/Api/HeldAboutOnePersonController.cs
    Jellyfin.Plugin.WatchSync/Api/SyncStatusController.cs

Two are #74's and two are #62's, and they are the whole of the surface. The manual actions
in #64 are still ahead of it, and each adds its own rows here in the change that adds it,
because the comparison below fails on the day a route exists without one. Their
authorisation is #66.

## What counts as an endpoint

A public method of a public type deriving from `ControllerBase` that carries an
attribute implementing `IActionHttpMethodProvider`, which is what every `HttpGet`,
`HttpPost`, `HttpPut` and `HttpDelete` is. The interface is named rather than those
four so that a verb nobody has used here yet is inside the population rather than
outside it.

The definition is not restated in either guard that reads it.
`Jellyfin.Plugin.WatchSync.Tests/Endpoints/EndpointReflection.cs` holds it, this
document and #66's policy table both call it, and that is deliberate: two reflections
written separately disagree about the population, and while there are no endpoints the
disagreement is invisible. The one holding less than it claims would surface on the day
the first controller lands, which is the day both are believed.

## Every endpoint, its route, its authorisation, what it takes and what it answers

| Endpoint | Method | Route | Authorisation | Inputs | Outputs | Refusals |
| --- | --- | --- | --- | --- | --- | --- |
| `HeldAboutOnePersonController.Report` | GET | Plugins/WatchSync/Persons/{mappedUserId}/Records | RequiresElevation | The person, in the route. No body. | `200` with the person, a count, and one entry per document: its name in the store, the prefix of the kind that wrote it, the pairing it is about, the version it was written at, and the document itself as the store holds it. | `400` where the identifier names nobody, which is the all-zero one. `401` where the caller has not signed in and `403` where they are signed in and not elevated, both from the server's own policy rather than from anything here. |
| `HeldAboutOnePersonController.Remove` | DELETE | Plugins/WatchSync/Persons/{mappedUserId}/Records | RequiresElevation | The person, in the route. No body. | `200` with the person and how many documents were removed, which is the count of documents that went rather than of documents that were found. | `400` where the identifier names nobody, which is the all-zero one. `401` and `403` as above. |
| `SyncStatusController.Status` | GET | Plugins/WatchSync/Pairings/{pairingId}/Persons/{mappedUserId}/Status | RequiresElevation | The pairing and the person, in the route. No body. | `200` with the status: first whether anything needs attention, then the stopped run, the last exchange, the last sweep, the unmatched count with its top three reasons, and the conflicts, each section read from a document saying whether its record was read, is absent, or could not be read. The last sweep is the server's run rather than the pairing's, read from the record the task keeps in memory, and it says whether any run has ended since the server started and whether that run covered its set or stopped short. Every number is the record's own. No title, no path, and no text that came from a peer. | `400` where either identifier is the all-zero one. `401` and `403` as above. A store the filesystem refuses to read answers the server's own `500`, and nothing here turns that into an empty status. |
| `SyncStatusController.Unmatched` | GET | Plugins/WatchSync/Pairings/{pairingId}/Persons/{mappedUserId}/Unmatched | RequiresElevation | The pairing and the person, in the route. No body. | `200` with every unmatched item of that pairing and person, in the record's order: the item's identifier, its kind, the refusal or the lookup's answer, and when it was last attempted, and whether the record was read. This is the export, and it is the record read out entry by entry. | `400` where either identifier is the all-zero one. `401` and `403` as above. `500` as above. |

The four rows are the whole surface rather than the start of a list somebody fills in
when they notice: the comparison below fails on the day a route exists without a row
here, so the next controller cannot land without one either.

Neither endpoint answers a body on a refusal, and the cells say a status and nothing
more, because a status and nothing more is what a caller receives.

## Refusals as the caller sees them

Written from the outside, and this section is the rule the first endpoint is held to
rather than a description of anything running.

**A refusal is documented as the caller receives it, not as the server decided it.**
What a document owes somebody automating an installation is the status and the body
they will actually get, because that is what their script branches on. An internal
distinction that never reaches the wire does not belong in the cell.

**Where two different causes deliberately answer the same, both are named and the
sameness is stated as deliberate.** This is the row that gets tidied. A reader who
finds two rows with identical answers assumes a copying mistake and merges them, and
what they remove is a decision.

The case this plugin meets today, on both rows above: a person this plugin holds nothing
about, and a person this server has never had. Both answer `200` with an empty report,
or with a removal of nothing, and neither says which of the two it was. That is
deliberate. This plugin holds no list of users, so it could not separate them without
asking the server, and an answer that did separate them would tell a caller which
accounts exist on a server they were authorised to ask one question of.

The two status rows meet the same case with a pairing: a request naming a pairing this
plugin has never exchanged on, and a request naming a pairing that does not exist, both
answer `200` with every record absent, and neither says which of the two it was. That is
deliberate for the same reason: this plugin holds no pairing and could not separate them
without asking the pairing plugin, which is #40, and an answer that did would tell a
caller which pairings exist. The same applies to an item.

**A refusal never carries a peer's own text.** What arrived from another machine is
bounded and stripped before anything is built out of it, which is #63, and a refusal
body is one of the two surfaces that rule is about.

## No example carries an identifier from a real server

Every identifier written in this document is the all-zero placeholder,
`00000000-0000-0000-0000-000000000000`. The rule is not a convention: an example is
copied out of whatever was to hand while somebody was testing, and what is to hand on a
working server is a real user's identifier and a real item's. Once it is in a document
it is in the repository's history, and the person it belongs to was never asked.

A fact refuses any other identifier in this file, so the rule is not carried by whoever
writes the next example.

## How this document is held true

`EndpointDocumentTests` compares the table against the routes in both directions: a
route with no row fails, and a row naming no route fails. It also refuses a row that
describes a different method or route from the one the attribute declares, because a
document held to the names alone lets the rest drift and a reader consults the cell
precisely to find out what to call.

What it cannot do is read a sentence. Whether the refusals column says what a caller
actually receives, and whether the two deliberately identical rows still are, is a
judgement about meaning that no reading of this tree makes. The comparison holds the
names, the verbs and the routes; the review is where a drifted description is caught.
The section above is held only by a fact that refuses its deletion, which is how a
wording gets shortened rather than how it gets rewritten, and that bound is written
here rather than left for a reader to discover by trusting the guard.
