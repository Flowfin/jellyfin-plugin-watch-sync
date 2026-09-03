Issue: #38
Existing-Data: unchanged

The cap on what one run may change is now judged before anything is written. A decided set
reaches the apply walk through one route, and that route asks the cap first: a run within
both bounds walks exactly as it would have and pays nothing for having been asked, and a run
over either bound writes nothing and records what it would have done, item by item, with what
this server held at that moment beside what the run had decided.

A recorded plan can be approved. The approval writes exactly what the plan recorded and sets
aside every item that is not as the plan found it, so nothing that moved between the stop and
the approval is written without being noticed. The plan is a fifth kind of document in the
store, named `stopped-`, and the privacy note carries its row.

Nothing calls the cap yet. There is no exchange to hand it a decided set, no status page that
shows a stopped run and no action that takes an approval, so no server behaves differently
for this and no watch state moves that did not move before.
