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
- Raising the floor of a server line that stays declared, because a server below
  the new number is offered nothing rather than being offered a version it
  cannot run, and an operator watching updates arrive sees them stop.

The third of those four is the one a machine refuses first, and it refuses two
of its members rather than all three. `dropping-support-carries-a-changelog-entry` in
`.github/check-pull-request.py` compares two declarations at the base and at the
head of a pull request: the envelope versions in `EnvelopeVersions.Supported`,
and the server lines in the `targets` list of `build.yaml`. An entry that is
there at the base and gone at the head, with no changed path under
`changelog.d/`, is refused. The version in the manifest does not have to move
for a drop to happen, so the rule about a version bump sees none of this and
this rule is not a second reading of it.

The pairing contract version is deliberately outside it. Nothing in this tree
declares one, so there is no base and no head to compare, and a reading of a
declaration that does not exist would answer nothing on every pull request while
making the rule read as though it covered all three. It is refused by this list
and by a reader, as the first two bullets are.

One thing the rule does not reach, written down so it is not read as covered:
it declines where the repository has published no release, which is the state
today. What a drop strands is a peer or a server running a build somebody
installed, and there is none. The decline is printed as a note on the run rather
than passing in silence.

THIS PARAGRAPH NAMED A SECOND THING AND A SECOND RULE NOW REACHES IT. It said an
ABI moving under a framework that stays is a package bump rather than a line
dropped, and that the reading is over the frameworks the list names. Both halves
of that are still true of the drop rule, and the conclusion drawn from them was
not: `targetAbi` is the oldest server this plugin claims on that line, and a
server reads it to decide whether it may install at all, so a number that rises
under a framework that stays leaves every install below it holding the version
it has and never offered another. That is what a drop does, arriving without the
list losing a member and without the version moving, which is why neither rule
beside it saw it.

`raising-an-abi-floor-carries-a-changelog-entry` in
`.github/check-pull-request.py` reads the framework and the ABI as a pair at the
base and at the head, and refuses a framework declared at both ends whose number
rose with no changed path under `changelog.d/`. It declines on a repository that
has published nothing for the same reason the drop rule does, and prints the
decline.

Only upward. A floor that falls widens what this plugin installs on and strands
nobody, and a rule written over a floor that MOVED rather than one that ROSE
would ask an operator to be told about a server that has just gained the plugin.
The direction is the half a reader has to take on trust in a description and is
the half a document in `.github/` proves.

Two bounds it prints rather than hides. The pair is read in the order the
manifest writes it, so a reindented or reordered entry is unread rather than
empty and says so as a warning, because an end nothing could parse answered as
an empty one would pass the change the rule exists for. And a number that is not
a dotted number cannot be ordered against one that is: `12.0.0-rc4` against
`12.0.0.0` is the case, it is the spelling of the package this repository
actually references, and the pair is reported as unordered rather than answered
as one that did not rise.

The first two bullets are refused by nobody. Whether a rule change alters what
happens to history that already exists is a judgement about meaning, and the
review is where a missing entry for one is caught.

## What holds this

`ChangelogFragmentTests` reads every fragment `changelog.d/` holds and refuses
one whose header breaks any of the rules above. It reads the field table in this
document rather than carrying a second copy of it, so a field added here without
being added to the guard, or the reverse, is a failure rather than a drift.

The near miss the guard is proven against is a fragment marked `changed` whose
`Effect` line is missing, because that is the mistake somebody makes: the
marking is the part a writer remembers and the sentence for the operator is the
part they leave for later.

The rule over a drop is proven the same way and in the workflow rather than in
the suite, because what it reads is a pull request and not the tree. Two near
misses, one per declaration, each dropping one thing and writing no entry, and
each has to be refused for the rule's own name; one repair carrying both drops
and one entry, which has to pass; and two documents where a refusal would be
wrong, one dropping a version against a repository that has published nothing
and one that reindents the list the rule reads and takes nothing away. Deleting
either declaration from the rule leaves the other near miss refused and its own
passing, which is why there are two documents and not one.

The rule over a raised floor is proven in the same place, with five documents,
because three of the five prove something an exit code cannot say. One near miss
raises a floor and writes no entry, and has to be refused for this rule's own
name. Its repair adds one path under `changelog.d/`, and has to pass. One lowers
a floor and writes nothing, and has to pass, which is the direction proof: a rule
asking whether the number MOVED refuses it, and nothing else here would notice.
One raises a floor against a repository that has published nothing, which is the
state today, so what runs on every real pull request here is the decline rather
than the refusal, and the decline has to print. And one moves a floor to
`12.0.0-rc4`, which nothing can order against `12.0.0.0`, and has to pass while
saying it could not order the pair. Those last three all exit zero, so what the
step asserts is the line each prints and not its status, and a deleted disclosure
is caught there rather than passing as a green run.

Nothing assembles these fragments into release notes yet. The publish route asks
GitHub to generate notes from the merged pull requests and reads no fragment at
all, so an entry written today reaches this directory and stops there. #116 is
where that half is owed, and it is written here rather than left for a reader to
discover from an empty release.
