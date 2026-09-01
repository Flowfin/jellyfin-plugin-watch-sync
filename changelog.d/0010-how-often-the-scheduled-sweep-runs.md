Issue: #55
Existing-Data: unchanged

How often the scheduled sweep runs is a setting now. It defaults to fifteen minutes
and an operator may choose anything from one minute up to six hours, on the
configuration page beside the other server-wide settings.

The interval is what decides how long a change nobody's server raised an event for
goes unseen, so raising it makes two paired servers agree later rather than agree
less often. The upper bound is where the sweep would start asking a pairing that is
working less often than the longest wait one that is failing is ever made to serve,
which is the point at which a broken pairing is reached faster than a healthy one.

Nothing runs on this schedule yet. The task that reads it is the rest of #55, so no
server sweeps today, nothing is scheduled, and no watch state moves that did not
move before.
