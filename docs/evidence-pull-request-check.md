# Evidence only

This file exists so that a pull request can be opened that names no issue in its
title, in its body or in its commit message. It is not proposed for the mainline
and the branch carrying it is closed rather than merged.

The check being measured is the one named `Deterministic PR-hygiene checks`. Its
blocking tier is supposed to refuse a pull request in exactly this state. A
green run here would mean the tier does not bite on real input, whatever the
fixtures beside it say.
