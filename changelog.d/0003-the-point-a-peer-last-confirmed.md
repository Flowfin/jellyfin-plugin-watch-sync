Issue: #51
Existing-Data: changed
Effect: An agreed record written before this version carries no point, so the first
  exchange for that pairing and that mapped user asks the peer for everything rather
  than for what changed. Nothing already agreed is re-decided by that: the agreements
  are read as they stand and the conflict rules answer them the same way.

Watch Sync remembers the point up to which it and a peer have agreed, per pairing and
per mapped user, so a server that was off for a week can ask for what changed instead
of for the whole library. The point is a value the peer produced and is compared only
against itself, because two clocks that disagree turn a point read as a time into
either a gap or a permanent re-send.

It moves when the peer confirms it and at no other ending. A send that was made, a
peer that did not answer, an envelope that was refused and a run that stopped part way
all leave it where it was, so nothing between the old point and the new one is skipped.
A point the peer no longer recognises, which is what a peer restored from a backup
looks like, is not an error: the next exchange asks for everything.

The point is written in the same document as the agreements it belongs to, so a store
restored from a backup never carries a point later than the agreements beside it.
