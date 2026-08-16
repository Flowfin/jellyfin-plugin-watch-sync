# An item that did not match, and what to do about it

This is the question this plugin will be asked most. Something was watched on one
server and did not appear on the other, and nearly every time the item did not match
rather than the transfer having failed. Nearly every one of those is a metadata
problem on one of the two servers, which the operator can repair themselves.

An unmatched item is a normal outcome. It is not an error, it is never repaired by
relaxing a rule, and there is no setting here that turns a missing identifier into a
guess. `docs/matching.md` fixes the rules this document is the operator's side of,
and the refusals in it are the reason a wrong match is not offered as an option.

## What this guide cannot tell you yet

Where an operator reads these reasons is the status page, and there is no status
page. #62 holds it, and the record the page would count is #26. Neither exists, so
this document names the reasons and what to do about each one, and it does not say
which screen to look at or what a repaired item looks like there. That half arrives
with those two issues.

The reasons themselves are in the sources today, which is what makes the sections
below something other than a plan.

## The reasons, one section each

A heading that is exactly a code span names a reason, and nothing else in this
document takes that shape. `UnmatchedGuideTests` reads those headings, reads the
reason vocabulary out of the sources, and refuses the two disagreeing in either
direction, so a reason added to the code with no section here fails the suite and a
section naming a reason the code does not carry fails it as well.

The vocabulary spans two enumerations, because there are two places an item stops.
`MatchKeyRefusal` says why an item produced no key at all. `MatchAnswer` says what a
key resolved to, and two of its three answers are not a match. `None` and `Matched`
are the members that are not a reason and have no section.

### `NoIdentifierAtAll`

The item carries no provider identifier of any kind. A home video is the ordinary
case, and so is a film in a folder no metadata provider was ever pointed at.

Confirm it by opening the item in the metadata editor on the server and reading the
identifier fields. All of them are empty.

The repair is to give the item an identifier, by hand or by letting a provider scrape
it. Where the work genuinely has no identifier anywhere, there is no repair and the
item will not sync. That is the honest answer rather than a shortcoming, and this
plugin does not offer a weaker comparison in its place.

### `NoIdentifierFromAPreferredProvider`

The item was scraped and carries identifiers, and none of them is from one of the
three providers a key is derived from. The order and the reason for it are in
`docs/matching.md` under the heading about preference.

Confirm it the same way: the metadata editor shows values, and none of them sits
under IMDb, TMDb or TVDb.

The repair is to add one of the three, either by hand or by enabling a provider that
writes one and refreshing the item.

### `EveryPreferredIdentifierWasRefused`

The item carries an identifier from one of the three providers, and every value was
refused by its normal form. This is a metadata defect on the item and it is the
reason an operator can most often repair.

The two shapes that produce it are a URL written into an identifier field, and one
provider's number written under another provider's name. A five digit number under
IMDb is the second one: IMDb pads to at least seven digits, so a shorter run under
that name came from somewhere else.

Confirm it by reading the value against the normal form table in `docs/matching.md`,
which says per provider what is accepted and what is refused.

The repair is to correct the value. Removing the wrong one is a repair as well, where
another provider on the same item carries a good one.

### `NoSeasonNumber`

The episode carries no season number, so it has no position in the series' ordering
to be keyed on. An episode the server could not place is the usual cause, and a file
named in a way the server's own parser did not recognise is how it usually got there.

Confirm it in the metadata editor for the episode rather than for the series.

The repair is to give the episode its season number, or to identify the episode
against the series so the server fills both numbers in.

### `NoEpisodeNumber`

The same thing one field along. It is a separate reason because an item numbered into
a season but not inside it is a different metadata defect from one that is in no
season at all, and the two are repaired by different edits.

### `SpansSeveralEpisodes`

The item is one file covering several episodes, so it holds several positions and no
single one of them is its key. This plugin will not key it on the first number,
because that would move the whole file's watch state onto one episode of the run.

Confirm it by reading the numbering on the item: it carries a first and a last
episode number rather than one. An item whose last number is present while its first
is absent lands here too, because it covers a run whose start is unknown.

The repair is to split the file into one item per episode, which is a change to the
library rather than to the item's metadata. Where the file stays as it is, the item
will not sync, and that is a bounded loss rather than a wrong match.

### `NumberingBelowZero`

A season or an episode number below zero. Zero itself is not refused: the server
numbers a specials season zero and a scraper numbers a special inside it from zero,
so zero is a position rather than a placeholder. Below zero is neither.

The repair is to correct the number.

### `Ambiguous`

The key was derived and more than one local item claims it. The same film added twice
from two libraries is the ordinary case. Nothing moves, and both competing items are
carried in the answer rather than counted, so the repair names the items rather than
asking the operator to search for them.

Taking the first of them would land the watch state on one at random and on a
different one next time, which is why this is a refusal and not a tie break. #27
holds that rule.

The repair is to remove the duplicate, or to correct the identifier on whichever of
the two carries the wrong one. Two genuinely different works that ended up sharing an
identifier are the second case, and there the repair is on the metadata of one of
them.

### `NoMatch`

A key was derived here and the far side holds nothing under it. This is the reason
that is not about this server. The work is either absent from the peer, or present
there with one of the reasons above against it.

Confirm it by looking for the same work on the other server. Where it is there, the
reason for the failure is on that side, and this document is the guide for it as
well.

Where the peer genuinely does not hold the work, nothing is wrong and nothing needs
repairing.

## When a repair takes effect

Two effects, and the table admits no third:

- `refresh`, where the repair is an edit to the item's own metadata. It takes effect
  once the server has refreshed that item, and refreshing the library without
  refreshing the item is the step people skip.
- `sweep`, where the repair is a change to the library or to the peer rather than to
  the item. It takes effect at the next full pass rather than immediately, because
  nothing re-derives a key for an item that did not change.

Neither the record that would show the reason nor the pass that would pick a repaired
item up exists in this repository today. The record is #26 and the sweep is #55. So
the column below is the rule an implementation of them is held to, and it is not a
description of something running.

| reason | effect | what the repair is |
| --- | --- | --- |
| `NoIdentifierAtAll` | refresh | An identifier on the item, where the work has one at all. |
| `NoIdentifierFromAPreferredProvider` | refresh | One of the three preferred identifiers, added or scraped. |
| `EveryPreferredIdentifierWasRefused` | refresh | The value in the identifier field corrected. |
| `NoSeasonNumber` | refresh | The season number, or the episode identified against its series. |
| `NoEpisodeNumber` | refresh | The episode number, or the episode identified against its series. |
| `SpansSeveralEpisodes` | sweep | The file split into one item per episode, which the server picks up as new items. |
| `NumberingBelowZero` | refresh | The number corrected to zero or above. |
| `Ambiguous` | sweep | The duplicate removed, or the identifier corrected on one of the competing items. |
| `NoMatch` | sweep | Nothing here. The work is added or repaired on the peer, and the next pass finds it. |

## A series ordered differently on the two servers

This is the first of the two cases people ask about, and it is the one where the
refusal looks most like a defect.

An episode usually has no identifier of its own. It is keyed by its series'
identifier, the ordering the series is held under, the season number and the episode
number. The ordering travels inside the key, so a series held in airdate order on one
server and in DVD order on the other produces two keys for what a person would call
the same episode, and both sides record `NoMatch` rather than agreeing on the wrong
episode.

That is deliberate. Episode 1 of season 2 in airdate order and episode 1 of season 2
in DVD order are different works often enough that matching them would write one
person's history onto an episode they have not seen.

An unset ordering and airdate order are the same ordering, and this plugin folds the
first onto the second, so a server that spells the default out and one that leaves it
empty still agree. The case that does not resolve itself is two servers holding the
same series under two different named orderings.

The repair is to set both series to the same ordering, on the series rather than on
the episodes, and to refresh the series afterwards so the episodes are renumbered.
Which of the two orderings you choose does not matter to this plugin. That they are
the same one does.

What does not repair it is editing the episodes. Numbers edited by hand under one
ordering are undone the next time the series is refreshed under the other.

## A film both servers hold, scraped by different providers

The second case, and the one that most often turns out to be a one field edit.

Two independently built libraries are scraped by whichever providers were enabled at
the time. One server can hold a film with a TMDb identifier and no IMDb one, and the
other the same film with an IMDb identifier and no TMDb one. Both are correctly
scraped, and there is no identifier the two have in common, so the key on one side is
built from a provider the other side has nothing under and the answer is `NoMatch` on
both.

Confirm it by opening the same film on both servers and comparing which identifier
fields carry a value. The reason recorded is `NoMatch` and not one of the refusals,
because each item produced a perfectly good key of its own.

The repair is to give one of the two the identifier the other has, by hand or by
enabling the provider that supplies it and refreshing that item. Adding it on either
side is enough, and adding IMDb is the better choice where there is one, because it
is first in the preference order and both sides will then key on it.

What does not repair it is matching on the title and the year. Two films sharing a
title and a year are common, and a rule over titles is the shape this plugin refuses
by name. `docs/matching.md` carries that refusal and the reason for it.

## How this document is held to the reasons in the code

`UnmatchedGuideTests` reads three sets and refuses any two of them disagreeing: the
reasons the sources carry, the sections above, and the rows of the effect table. A
reason with no section, a section with no row, a row naming something the code does
not carry, and anything named twice each fail the suite.

What it does not do is judge what a section says. Whether the repair written here is
the right repair is a reading, and the review of a change to this document is where a
wrong one is caught. It also cannot see a reason an implementation records outside
those two enumerations, and #26 is where the record's own vocabulary is settled.
