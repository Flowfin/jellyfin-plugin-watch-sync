Issue: #55
Existing-Data: changed
Effect: An entry in a record of conflicts or of provenance that is older than the
  retention its setting carries is removed at the first sweep run after this version,
  where before nothing removed it. An undo of a revoked pairing then reaches back as
  far as the provenance retention and no further, which is what that setting always
  said and is now what happens.

The scheduled sweep runs as a task the server schedules, at the interval the sweep
setting holds, and an operator sees it, runs it and sees when it last ran in the
dashboard's task list. What a run does today is trim this plugin's own records of
conflicts and of provenance to their retention and record what it examined. No
exchange with a peer starts, because the pairing adapter does not exist yet, and no
watch state moves.

A configuration the rules refuse runs nothing, and the task fails naming the setting.
A change to the interval setting reaches the schedule at the next server start, and
not on a server whose operator set the task's schedule in the dashboard, which is then
the home of the interval for that server.
