Issue: #37
Existing-Data: unchanged

The first exchange between two servers is a mode of its own now, and it merges by the
conflict table like every later one. It seeds neither side and overwrites nothing, and an
item the table decides nothing about is left standing with the reason it was left standing
rather than being answered by a weaker rule.

Two of those reasons are worth knowing before a first run. Two play counts that have never
been agreed and are both above zero are not told apart, because two sides holding two and
three plays may be three watchings and may be five. And two servers that both hold a work
finished, each stopped at a different point in it, are left alone rather than having one of
the two points chosen for them.

Nothing calls it yet. There is no scheduled sweep and no apply path, so no server behaves
differently for this and no watch state moves that did not move before.
