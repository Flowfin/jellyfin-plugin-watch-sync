# The changelog

An operator deciding whether to upgrade a plugin that writes into their users'
data needs to know what changed. That is more true here than for most plugins,
because a change to a conflict rule or a match rule changes what happens to
history that already exists on their server rather than only what happens next.

So the changelog is written for that reader and not for a contributor reading
back over the work. An entry says what changed, and where the change reaches
data that has already moved between two servers, it says what that means for
the data that is already there.

## Where an entry lives

Entries are fragment files under `changelog.d/`, one file per change, assembled
into release notes when a release is cut. A single shared file is where parallel
changes collide, and the collision lands in the one file everybody has to edit
last.

A fragment is named with a four digit ordinal, a short slug and the `.md`
suffix, so a directory listing reads in the order the entries were written:

    changelog.d/0002-refuse-a-silent-version-bump.md

The directory holds fragments and nothing else. A format document sitting inside
it would be a path under `changelog.d/` that a version bump could change instead
of writing an entry, which is the rule in `.github/check-pull-request.py`
satisfied by the file that describes it.

## What a fragment looks like

A header of `Name: value` lines at column zero, a blank line, and then the entry
itself in prose:

    Issue: #116
    Existing-Data: changed
    Effect: A position synced before this version was rounded to the second and
      is now kept to the tick, so an item resumed on the far side can land up to
      one second earlier than it did.

    A position now crosses at full precision instead of being rounded on the way
    out.

A value continues onto the next line where that line is indented, which is how a
long `Effect` is written without a line nobody can read.

## The fields

| field | required | what it carries |
| --- | --- | --- |
| `Issue` | always | The issue the change belongs to, written with the hash that links it. More than one is allowed and each is written the same way. |
| `Existing-Data` | always | Either `unchanged` or `changed`. It is `changed` when the entry alters what happens to watch state that has already been synced, including a conflict rule, a match rule, a default that decides an outcome, or the shape of a document already in the store. |
| `Effect` | sometimes | What the change means for the data that is already there. Required when `Existing-Data` is `changed`, and refused when it is `unchanged`. |

`Existing-Data` has no default, and a fragment that omits it is refused rather
than read as `unchanged`. A default here would be the permissive one in every
case, and the entry it silently mislabels is the entry an operator most needed
to see.

`Effect` is refused on an entry marked `unchanged` for the same reason in the
other direction. A field that means nothing where it sits is one the next writer
fills in with a claim.

A name this table does not carry is refused rather than ignored, and the names
are compared exactly. `Existing-data` is not `Existing-Data`, and a fragment
carrying the first one is refused twice over: once for a field nothing knows,
and once for the field that is now missing. A tolerant read would accept it and
leave the field set as whatever anybody happened to type.

## What is always an entry

- A change to a conflict rule, a match rule or a default that decides an
  outcome.
- A change to the shape of a document this plugin has already written into its
  store.
- Dropping support for an envelope version, a pairing contract version or a
  server line, because each of those strands a peer or a server that was working
  yesterday.

## What holds this

`ChangelogFragmentTests` reads every fragment `changelog.d/` holds and refuses
one whose header breaks any of the rules above. It reads the field table in this
document rather than carrying a second copy of it, so a field added here without
being added to the guard, or the reverse, is a failure rather than a drift.

The near miss the guard is proven against is a fragment marked `changed` whose
`Effect` line is missing, because that is the mistake somebody makes: the
marking is the part a writer remembers and the sentence for the operator is the
part they leave for later.

Nothing assembles these fragments into release notes yet. The publish route asks
GitHub to generate notes from the merged pull requests and reads no fragment at
all, so an entry written today reaches this directory and stops there. #116 is
where that half is owed, and it is written here rather than left for a reader to
discover from an empty release.
