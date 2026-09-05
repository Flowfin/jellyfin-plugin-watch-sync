Issue: #19
Existing-Data: unchanged

The body of an envelope arriving from a paired server is now held to the quarter of a
mebibyte this plugin allows before it is in memory, rather than after. Where the transport
says how long the body is and says more than that, nothing is read off it at all; where it
says nothing, or says a length its body then exceeds, the read itself stops one byte past
the bound and refuses. So a peer cannot make this server hold what it sent by sending more
than it declared, and it cannot make it hold anything by declaring nothing.

Bytes that are not text are refused rather than repaired into text with replacement
characters, because a character this plugin invented and then read is a refusal nobody sees.

Nothing about what is synced changes, and no setting moves. Nothing calls this yet: there
is no transfer between two servers in this plugin today, so what landed is the rule and the
facts that hold it rather than a change an operator can observe on a running server.
