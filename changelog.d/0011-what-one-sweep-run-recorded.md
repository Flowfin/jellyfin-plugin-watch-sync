Issue: #55
Existing-Data: unchanged

What one run of the scheduled sweep did is a record now: when it started, when it
ended, how many pairing and user pairs it set out over, how many of those it
examined, and how many changes it made.

Whether the run covered everything is worked out from those counts rather than
declared by whatever ended the run. A pass cancelled from the dashboard, or cut off
by a shutdown, is therefore reported as having stopped part way, with the number it
reached beside the number it was over, instead of leaving a record an operator would
read as a completed convergence.

Nothing produces one yet. The task that would is the rest of #55, so no server
sweeps today, no run is recorded anywhere, and no watch state moves that did not
move before.
