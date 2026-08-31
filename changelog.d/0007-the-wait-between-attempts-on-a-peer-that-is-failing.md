Issue: #53
Existing-Data: unchanged

The wait between attempts on a peer that failed is a rule now. It doubles from
thirty seconds until it reaches thirty minutes and stays there, and the ceiling is
reached rather than approached, so a peer that stays down is asked at the interval
that was chosen rather than at whatever the last doubling under it happened to be.

The first wait is picked against what the pairing plane admits per pairing rather
than against how slow a peer is, so a server whose peer is unreachable does not
spend that pairing's arrival allowance and does not turn a peer being down into
refusals on traffic that has nothing to do with syncing.

Nothing calls it yet. There is no adapter to reach a peer through and no exchange to
retry, so no server behaves differently for this and no watch state moves that did
not move before.
