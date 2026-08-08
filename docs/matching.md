# Matching an item across two servers

Matching is where this class of tool fails, so the rule is written here before it
is built and every refusal is named.

The premise the whole document rests on: the two servers are not required to hold
the same files, the same libraries or the same folder layout. One may hold a remux
where the other holds a web release, one may keep films in three libraries where
the other keeps them in one, and neither is wrong. That is the difference between
this plugin and the prior art, and it is why the key is metadata identity and never
storage identity.

## The three answers, and there is no fourth

An item on this server, compared against what the peer holds, gets exactly one of
three answers. An implementation that invents a fourth is wrong.

A **match** is one local item and one peer item that carry the same key. Watch state
may move between them.

An **ambiguity** is one key that resolves to more than one item on either side. The
same film added twice from two libraries produces one. Nothing moves, and both
competing items are recorded, because taking the first would land the state on one
of them at random and on a different one next run. #27 holds the rule.

**No match** is one key that resolves to nothing on the far side, or an item that
produces no key at all. Nothing moves, and the item is recorded with the reason so
an operator can act on it. #26 holds the record. No match is a terminal answer for
that item in that run. There is no second pass at a weaker comparison.

## The key, per item kind

The server's own vocabulary for what an item is, is `BaseItemKind`. Every member of
it appears in the table below exactly once, and `MatchingDocumentTests` refuses the
table and the enumeration disagreeing in either direction, so a kind added upstream
reddens the suite rather than being quietly left out.

The vocabulary is the same on both server lines this plugin intends to run on, so
the table is one decision and not two:

    git -C jellyfin show v10.9.11:Jellyfin.Data/Enums/BaseItemKind.cs | grep -cE '^\s+[A-Z][A-Za-z0-9]*,?\s*$'
    37
    git -C jellyfin show v10.11.11:Jellyfin.Data/Enums/BaseItemKind.cs | grep -cE '^\s+[A-Z][A-Za-z0-9]*,?\s*$'
    37

The trailing comma is optional in that pattern, and it has to be. A pattern requiring
one counts every member but the last, which is how the first draft of this table came
to be missing `Year`. The guard refused it, which is the whole reason the count above
is derived from the assembly rather than trusted from a reading of the source.

The two files differ in one place and it is not a member:

    diff <(git -C jellyfin show v10.9.11:Jellyfin.Data/Enums/BaseItemKind.cs) \
         <(git -C jellyfin show v10.11.11:Jellyfin.Data/Enums/BaseItemKind.cs) | wc -l
    4

The four lines are one changed line, `1c1`, and the only difference on it is a byte
order mark at the start of the older file. The literal is not pasted here: this
repository refuses invisible characters in its own text, and a document quoting one
would be refused by the guard that exists to catch it. The command above
reconstructs it for a reader who wants to see it.

Six dispositions, and the table admits no seventh:

- `synced`, which carries a key rule;
- `aggregate`, whose played state the server derives from the leaf items under it
  rather than storing it, so there is nothing to carry and carrying it would mass
  mark what the peer holds and the sender does not. #13 refuses aggregates as
  transfer subjects by construction;
- `container`, a folder or a view, which holds no watch state of its own;
- `facet`, a way of grouping items rather than something a person watches;
- `ephemeral`, live television and channel content, where the two servers do not
  hold the same instances and a key over them would match the wrong thing;
- `deferred`, not synced in this version. Widening the set of media classes is
  decision 2 in #1, and until it is answered these are not synced.

Everything that is not `synced` is not synced. That is the whole of what the
disposition means; the reason column says why.

| kind | disposition | key rule, or the reason it is not synced |
| --- | --- | --- |
| `AggregateFolder` | container | The root that gathers the libraries. It holds no watch state. |
| `Audio` | deferred | A track has its own identifiers and played means something different for it than for a film. Decision 2 in #1. |
| `AudioBook` | deferred | Position on an audiobook needs a rule of its own, which no issue on this board writes yet. Decision 2 in #1. |
| `BasePluginFolder` | container | A folder another plugin owns. Nothing here is watched. |
| `Book` | deferred | Read state is not watch state and the identifiers differ. Decision 2 in #1. |
| `BoxSet` | aggregate | A collection is played when its members are. Carrying it would mark films the peer holds and this server does not. |
| `Channel` | ephemeral | A channel is a source rather than a work, and two servers do not hold the same one. |
| `ChannelFolderItem` | container | A folder inside a channel. It holds no watch state. |
| `CollectionFolder` | container | A library as the operator arranged it. Two servers are not required to arrange them alike. |
| `Episode` | synced | The series key, the ordering the series was matched under, the season number and the episode number, or the episode's own provider identifier where it carries one. #23 derives it. |
| `Folder` | container | A folder. It holds no watch state. |
| `Genre` | facet | A grouping rather than something a person watches. |
| `ManualPlaylistsFolder` | container | The folder playlists live in. It holds no watch state. |
| `Movie` | synced | The provider identifiers, in the order fixed below. #22 derives it. |
| `LiveTvChannel` | ephemeral | A tuner's channel on this machine. The peer's channel of the same name is not the same thing. |
| `LiveTvProgram` | ephemeral | A broadcast at a time, on this server's guide. It does not exist as the same item on the peer. |
| `MusicAlbum` | aggregate | An album is played when its tracks are, and its tracks are `Audio`, which is deferred. |
| `MusicArtist` | facet | A grouping of albums rather than something a person watches. |
| `MusicGenre` | facet | A grouping rather than something a person watches. |
| `MusicVideo` | deferred | It carries the music identifiers rather than the film ones, so it needs its own key rule. Decision 2 in #1. |
| `Person` | facet | A grouping rather than something a person watches. |
| `Photo` | deferred | A photograph usually carries no external identifier at all, so nothing can match it and nothing should try. Decision 2 in #1. |
| `PhotoAlbum` | container | A folder of photographs. It holds no watch state. |
| `Playlist` | aggregate | Membership is the operator's arrangement on this server, and the peer's list of the same name is a different list. |
| `PlaylistsFolder` | container | The view that lists playlists. It holds no watch state. |
| `Program` | ephemeral | A broadcast at a time on this server's guide. |
| `Recording` | ephemeral | A recording this server made from its own tuner. The peer has no counterpart to key against. |
| `Season` | aggregate | A season is played when its episodes are. Carrying it would mark episodes the peer holds and this server does not. |
| `Series` | aggregate | A series is played when its episodes are. This is the mass marking the prior art keeps producing. |
| `Studio` | facet | A grouping rather than something a person watches. |
| `Trailer` | deferred | A trailer is attached to a work rather than being one, and two servers rarely hold the same trailer for it. Decision 2 in #1. |
| `TvChannel` | ephemeral | A channel rather than a work. |
| `TvProgram` | ephemeral | A broadcast at a time on this server's guide. |
| `UserRootFolder` | container | The root a user sees. It holds no watch state. |
| `UserView` | container | A view over a library. It holds no watch state. |
| `Video` | deferred | A video with no more specific kind is usually a home recording with no external identifier. Decision 2 in #1. |
| `Year` | facet | A grouping rather than something a person watches. |

## The order identifiers are preferred in

An item can carry several provider identifiers, and two servers can have scraped
different ones for the same work. The key uses the first identifier in this order
that the item carries:

1. `Imdb`
2. `Tmdb`
3. `Tvdb`

The reason for that order and not another. An IMDb identifier names one work, is
allocated once and is not reused, and it is the identifier the widest set of
scrapers writes, so two independently scraped libraries agree on it more often than
on anything else. A TMDb identifier names one work as well and is second only
because fewer sources carry it. A TVDb identifier is last because it is organised
around series and its film side is the thinnest of the three.

The order is fixed rather than left to whichever identifier is present, because an
item carrying two of them would otherwise produce a different key depending on which
was read first, and the two servers would then disagree about a work they both
identified correctly.

The three above are named rather than the whole provider enumeration, because that
enumeration is not the same on both lines and a document naming all of it would be
wrong on one of them:

    diff <(git -C jellyfin show v10.9.11:MediaBrowser.Model/Entities/MetadataProvider.cs) \
         <(git -C jellyfin show v10.11.11:MediaBrowser.Model/Entities/MetadataProvider.cs)
    87c87,92
    <         TvMaze = 19
    ---
    >         TvMaze = 19,
    >
    >         /// <summary>
    >         /// The MusicBrainz recording provider.
    >         /// </summary>
    >         MusicBrainzRecording = 20,

The music identifiers that difference is about belong to the kinds this document
marks `deferred`, so nothing in the order above moves when that decision is taken.

## The normal form of an identifier

Comparing identifiers is not comparing strings as they were stored. The same
identifier is written several ways by the scrapers that produce it, and two servers
that scraped at different times with different scrapers will differ in exactly that
way. So every value is brought to one spelling before anything is compared, and the
spelling is fixed here rather than in a comment.

Two mistakes are possible and they are not symmetrical. Normalising too little
leaves two servers unable to see that they hold the same work. Normalising too much
makes two different works compare equal, and that writes one person's watch state
onto the wrong film. The second is worse and it is silent, so the rule is that a
value which is not the provider's shape after normalisation is refused as unusable
rather than stretched until it compares to something.

Every value has whitespace at either end removed before anything else. What is left
is judged per provider.

| provider | normal form | also accepted, and normalised to it | refused as unusable |
| --- | --- | --- | --- |
| `Imdb` | `tt` in lower case, then the digits with leading zeros removed and then padded back out to seven | the prefix in any case or absent altogether, and any amount of zero padding | a digit run shorter than seven, a value carrying anything that is not a digit once the prefix is off, and a number that is zero |
| `Tmdb` | the digits with leading zeros removed | any amount of leading zero padding | a value carrying anything that is not a digit, and a number that is zero |
| `Tvdb` | the digits with leading zeros removed | any amount of leading zero padding | a value carrying anything that is not a digit, and a number that is zero |

A URL is refused by all three, because a URL carries characters that are not digits.
That is deliberate rather than an oversight of the table. Pulling an identifier out
of a URL means deciding which part of somebody else's path layout is the identifier,
which is a guess, and this document refuses guesses everywhere else. An item whose
provider field holds a URL is an item with a metadata defect, and it goes into the
unmatched record with the reason, which is what an operator can act on.

The seven digit floor on IMDb carries the weight of a second rule and it is worth
naming why. IMDb pads its numbers to at least seven digits, so a shorter run under
the IMDb name is a number that came from somewhere else, usually a TMDb or TVDb
identifier written into the wrong field. Without the floor that number would
normalise into a perfectly well formed IMDb identifier for a film nobody meant. The
floor is what makes the shape test discriminate in both directions: an IMDb value
fails the TMDb and TVDb tests because it carries letters, and a TMDb or TVDb value
fails the IMDb test because it is too short.

The normal forms are not restated in the source. `ProviderIdentifierTests` reads the
provider column of the table above and refuses it and the providers the code carries
disagreeing in either direction, so a provider added to one and not the other fails
the suite.

What this holds and what it does not. A value that reached a comparison is a value
that was normalised, because the type that carries an identifier has no public
constructor and the only way to obtain one is the normalising call. That is a
property of the type rather than a rule anybody has to remember. It does not stop a
future source comparing two raw strings without ever making an identifier at all;
nothing refuses that today, and #148 is where a scan over the sources would land.

## What this plugin refuses to match on

Each refusal names the failure it avoids. None of them is a setting.

**A file path.** Refused. A rule over paths only works where the two servers hold
their files the same way, which this plugin does not require and exists to serve the
absence of. The prior art shows the cost: one plugin requires the media identifiers
on both servers to be identical, which in practice means identical paths, and states
it as a precondition rather than a feature
(https://github.com/GermanCoding/jellyfin-server-sync). Another matches strictly by
path and can therefore only support single folder libraries
(https://github.com/JPKribs/jellyfin-plugin-serversync).

**A file name.** Refused, for the reason above and one more. A file name is chosen
by whoever produced the file, so it is input this server did not derive and did not
check, and matching on it lets the naming of a download decide whose watch history
moves where. A third plugin matches on file names together with provider
identifiers and carries a standing warning that it is not perfect
(https://github.com/luigi311/JellyPlex-Watched).

**A container, a size or a hash of the file.** Refused. All three identify one
encoding of a work rather than the work. Two servers holding the same film at
different qualities would never match, which is the ordinary case rather than the
edge one.

**A title, with or without a year.** Refused. Titles differ by region, by release
and by scraper, and two different works share a title often enough that a match on
it writes one person's history onto the wrong film.

**A local item identifier.** Refused. It is this server's own addressing and it
changes when a library is rebuilt. Nothing this plugin stores may be keyed on one
alone.

**Anything the operator was asked to make identical by hand.** Refused as a class,
because a rule that works only while two libraries are kept in step fails silently
on the day one of them is not.

## What an operator sees for an item that did not match

An item that produced no key, or a key the peer had nothing for, appears in the
unmatched record with its kind and the reason. The reason distinguishes the cases an
operator can act on from the ones they cannot, and the record points at the metadata
fix rather than at making the two libraries hold the same files. #26 holds the
record and its reasons, and #62 shows the count.

The wording matters and is fixed here so that no later change softens it. An
unmatched item is a normal outcome. It is not an error, and it is never repaired by
relaxing a rule in this document.

## How this document is held to the server's vocabulary

`MatchingDocumentTests` reads the table above and the `BaseItemKind` enumeration out
of the referenced assembly and refuses them disagreeing in either direction, so a
kind added upstream fails the suite, a row naming a kind the server does not have
fails it, and a kind named twice fails it. It also refuses a row whose disposition
is not one of the six above.

What it does not do: judge whether the disposition on a row is the right one. That
is a reading, and the review of a change to this table is where a wrong one is
caught.
