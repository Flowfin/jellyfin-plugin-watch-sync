Issue: #62
Existing-Data: unchanged

The sync status shows what the last run of the scheduled sweep did: when it started and
ended, how many records it set out over, how many it examined, how many entries it
removed, and whether it covered its set or stopped short. Every number is the run's own,
read from the record the sweep keeps, and a run that stopped short is shown above the
rest of the status like a run the cap stopped, because its counts look like a finished
run and what it did not reach was not trimmed.

The run is the server's rather than the pairing's, because the sweep walks this plugin's
records rather than pairs today, so every status shows the same run. It is held in memory:
after a restart the status says no sweep has ended since the server started, and the
server's own task list is where the last run and its failure survive one.

Whether the peer is unreachable and the last refusal still have no record and are not
shown. No watch state moves that did not move before.
