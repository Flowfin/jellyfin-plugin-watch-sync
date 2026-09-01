Issue: #313
Existing-Data: changed
Effect: A record of what this server and one peer agreed for one person now holds
  at most twenty thousand items. A record already at or past that number goes on
  agreeing every item it already holds and takes no item it has never agreed;
  nothing is removed and nothing already agreed stops being agreed.

The wire was bounded and the store was not. An envelope carrying more changes,
more bytes or longer strings than this plugin accepts was already refused, but
nothing said how much a peer could make this side hold by sending legal envelopes
one after another. Every one of them added agreements about items this server had
never agreed, and the only thing bounding the record was how many items the peer
could name.

There is a number now, and reaching it is a refusal rather than room being made.
Dropping an older entry would unagree an item two servers had settled, and an item
with no agreement is a first exchange, which is the run allowed to change the most:
making room would quietly turn the far end of a library back into first exchanges,
one item at a time.

The number is deliberately reachable, so `docs/configuration.md` says why it is
where it is and what an operator does when a record arrives at it.
