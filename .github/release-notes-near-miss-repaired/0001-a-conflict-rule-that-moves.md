Issue: #116
Existing-Data: changed
Effect: A position synced before this version could be moved back by up to the
  tolerated skew on the next exchange, and is now left where it is, so an item
  resumed on the far side lands where the newer play left it.

A position that was equal on both sides and older than the tolerated skew is now
left alone rather than rewritten from the peer.
