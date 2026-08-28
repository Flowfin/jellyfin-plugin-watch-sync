# The endpoints this plugin serves

A document that drifts from the routes is worse than no document, because it is
trusted. So this one is held against the routes by a comparison that fails in both
directions rather than by somebody remembering to update it, and what that comparison
cannot see is written down here beside what it can.

**This plugin serves no endpoint today and the table below has no row.** That is a
reading rather than a claim:

    git grep -l 'ControllerBase\|\[HttpGet\|\[HttpPost\|ApiController' origin/master -- Jellyfin.Plugin.WatchSync/ ; echo "exit=$?"
    exit=1

The surface arrives with the administrator page: the status in #62, the manual actions
in #64, and what one person may ask about their own record in #74. Their authorisation
is #66.

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

The table is empty because the surface is. It is not a placeholder to be filled in
later by whoever notices: the comparison below fails on the day a route exists without
a row here, so the first controller cannot land without one.

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

The case this plugin will meet first: a request naming a pairing that does not exist,
and a request naming a pairing the caller may not see. These answer identically, and
they must, because an answer that separates them tells a caller which pairings exist on
a server they were not authorised to ask about. The same shape applies to a user
identifier and to an item.

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
