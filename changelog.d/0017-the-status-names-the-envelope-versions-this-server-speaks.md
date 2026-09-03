Issue: #18
Existing-Data: unchanged

The sync status names the envelope versions this server speaks, oldest first, read from
the one place they are declared. A server refusing a peer's envelope names its own set the
same way, so an operator holding two servers that refuse each other can read the status on
both and see which of them to move.

The set is this server's rather than the pairing's, and there is one version in it today.
Nothing sends or receives an envelope yet, so no refusal is produced on a server that did
not produce one before, and no watch state moves that did not move before.
