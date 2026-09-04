Issue: #29
Existing-Data: unchanged

The match index, which answers which local item carries a given identifier, is built
when the server starts and rebuilt by every run of the scheduled sweep, one page of the
library at a time. Before this it was built by the first lookup that needed it and never
rebuilt, so an item the library gained that no event carried to the index stayed absent
from it until the server restarted. The sweep now runs once at server start as well as
at its interval, so the records it trims and the index it rebuilds are both reached
before the first interval elapses.

No watch state moves that did not move before. The index is a cache: losing it costs a
walk of the library and never a wrong answer, and a lookup during a rebuild is answered
from the map before rather than from an empty one. An operator who removes the startup
run from the task's schedule in the dashboard has removed it for that server, and the
index is then built by the first lookup that needs it, as it was before.
