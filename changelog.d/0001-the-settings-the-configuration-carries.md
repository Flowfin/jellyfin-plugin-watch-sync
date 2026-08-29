Issue: #58
Existing-Data: unchanged

Watch Sync has settings. The configuration page now offers six of them: how far a
position moves before it is carried, how close to the end counts as finished, the
shortest item that carries a position at all, how long this plugin's own write
suppresses the event it caused, and how long a conflict and the provenance of a
written value are kept. Each keeps the value it had before, so a server that is
upgraded and left alone behaves exactly as it did.

A value outside what its rule accepts is refused rather than quietly corrected, and
the refusal names the setting, what was found and what it had to be.
