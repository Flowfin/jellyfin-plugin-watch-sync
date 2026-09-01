Issue: #74
Existing-Data: unchanged

An administrator can now show everything this plugin holds about one person, and
remove it, from the plugin's configuration page. Both are endpoints of this plugin's
own, and both need an administrator account: the server's own elevation policy is
what decides that rather than anything this plugin invents.

Removing these records does not remove what that person watched. Their watch history
belongs to the server, it stays exactly where it is, and the page says so beside the
control rather than only in a document. What goes is this plugin's own record of what
it agreed with a peer, what it wrote, what it could not match and which conflicts it
resolved.

A person this plugin holds nothing about and a person this server has never had are
answered the same way, with an empty report and a removal of nothing. That is
deliberate: an answer that separated them would say which accounts exist on a server
to somebody who was authorised to ask one question of it.

These are the first two endpoints this plugin serves. Nothing else on the page
changed and no watch state moves that did not move before.
