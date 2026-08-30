Issue: #73
Existing-Data: unchanged

Watch Sync can remove everything it holds, as an action an operator takes before
uninstalling rather than as something an uninstall does for them. What it removes is
this plugin's own store: the agreed records, the point each pairing has reached, and
whatever else it wrote about people. What it never touches is the watch state in the
server's own records, because that is the server's data and removing a plugin is not an
instruction to erase a household's history.

Taking the removal and not taking it are two different reinstalls. Without it, the next
install resumes from the store that is there, which is what an operator upgrading wants.
After it, there is no agreement to resume from and each pairing starts over, which is
what an operator who no longer wants history moving between servers asked for.

Asking twice is not an error. The second answer says there was nothing to remove rather
than reporting a deletion that did not happen.
