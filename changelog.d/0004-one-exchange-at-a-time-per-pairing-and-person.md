Issue: #55
Existing-Data: unchanged

Watch Sync will run one exchange at a time for each pairing and each mapped person.
A scheduled sweep that meets an exchange already running for that pair is refused
rather than held, and so is a run an operator starts by hand: the exchange in progress
reaches the state the refused one would have, so the refusal costs one interval and
nothing else.

The exclusion is over the pairing and the person together and never over the pairing
alone. Two people of one household exchange at the same time, because they share no
agreed record and no watermark and nothing they write can collide.
