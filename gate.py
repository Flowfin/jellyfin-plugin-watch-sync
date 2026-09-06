#!/usr/bin/env python3
"""Run the legs the merge gate runs, here, in one command.

    python gate.py

The four contexts a merge waits for are produced by four jobs in this
repository's own workflows. This runs the steps of those jobs against the
working tree, in the order `gate.txt` declares, and stops at the first leg that
fails.

WHAT IT RUNS IS READ OUT OF THE WORKFLOW, NEVER OUT OF HERE. A leg is a job in a
workflow file, and each step's command is the `run:` block of that step as the
gate will execute it. `gate.txt` decides which steps are in and which are
deliberately out, and nothing in this file or in that one restates a command; a
step edited in the workflow is a step this run executes as edited. Both this
script and `LocalGateTests` refuse a job whose `run:` steps are not exactly the
ones the register names, in either direction, so a step added to a workflow with
no line beside it is a refusal rather than a leg that quietly stops being run.

WHAT A GREEN RUN HERE IS NOT. It is not the gate. Which contexts a merge waits
for is a repository setting that no file in this tree reads, so the four jobs
below are the required set as it was read on the day this landed and not a fact
this run re-derives. Everything else the forge runs on a pull request - the
suite on three operating systems, the coverage floors, the packaging route, the
code scanning, the pull-request hygiene checks - is outside these four jobs and
outside this command.

WHAT IT DOES NOT SKIP QUIETLY. A leg that could not be run at all, for a missing
tool or a missing runtime, is a failure and not an omission: the accounting at
the end names every leg with its verdict, every step the register keeps out with
the reason, and every step-level value that was dropped because it is a forge
expression this machine has no answer for. A run that covered less than the
whole set cannot be read here as one that covered it and found nothing.
"""

import os
import pathlib
import shutil
import subprocess
import sys

try:
    import yaml
except ImportError:  # pragma: no cover - the message is the whole behaviour
    sys.exit(
        "gate.py: PyYAML is not importable. This reads the workflow YAML rather "
        "than grepping it, so it stops here instead of running a weaker reader "
        "over a file that decides what a merge waits for. Install it with "
        "`python -m pip install pyyaml`."
    )

ROOT = pathlib.Path(__file__).resolve().parent

REGISTER = "gate.txt"

FIELDS = 5

RUN = "run"

SKIP = "skip"

EXPRESSION = "${{"


class Refusal(Exception):
    """Raised where the register and the workflows disagree, before anything runs."""


class Step:
    """One step of one leg, as the register names it and the workflow spells it."""

    def __init__(self, leg, workflow, job, disposition, name, reason):
        self.leg = leg
        self.workflow = workflow
        self.job = job
        self.disposition = disposition
        self.name = name
        self.reason = reason


def register(path):
    """Read the register into steps, in file order, refusing a line it cannot place."""
    steps = []

    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        text = line.strip()

        if not text or text.startswith("#"):
            continue

        fields = [field.strip() for field in text.split("::")]

        if len(fields) < FIELDS:
            raise Refusal(
                "{}:{}: a line carries {} fields and a step needs {}. The shape is "
                "`<leg> :: <workflow> :: <job id> :: run|skip :: <step name>`, with "
                "a sixth field giving the reason on a skipped one.".format(
                    REGISTER, number, len(fields), FIELDS
                )
            )

        leg, workflow, job, disposition, name = fields[:FIELDS]
        reason = " :: ".join(fields[FIELDS:]).strip()

        if disposition not in (RUN, SKIP):
            raise Refusal(
                "{}:{}: the disposition is {!r} and the only two are {!r} and "
                "{!r}.".format(REGISTER, number, disposition, RUN, SKIP)
            )

        if disposition == SKIP and not reason:
            raise Refusal(
                "{}:{}: {!r} is kept out of the run and says why nowhere. A step "
                "left out in silence is one nobody can tell from a step somebody "
                "forgot.".format(REGISTER, number, name)
            )

        steps.append(Step(leg, workflow, job, disposition, name, reason))

    if not steps:
        raise Refusal(
            "{} names no step, so this command would report green having run "
            "nothing.".format(REGISTER)
        )

    return steps


def legs(steps):
    """Group the steps into legs, keeping the register's order in both."""
    order = []
    grouped = {}

    for step in steps:
        if step.leg not in grouped:
            grouped[step.leg] = []
            order.append(step.leg)

        grouped[step.leg].append(step)

    return [(leg, grouped[leg]) for leg in order]


def job_of(steps):
    """Answer the one workflow and job a leg's steps name, refusing two of either."""
    workflows = {step.workflow for step in steps}
    jobs = {step.job for step in steps}

    if len(workflows) != 1 or len(jobs) != 1:
        raise Refusal(
            "leg {!r} names {} workflows and {} jobs. A leg is one job, because it "
            "is one context on the gate.".format(
                steps[0].leg, len(workflows), len(jobs)
            )
        )

    return workflows.pop(), jobs.pop()


def read_job(workflow, job):
    """Read one job out of a workflow file, refusing an absent file or job."""
    path = ROOT / workflow

    if not path.is_file():
        raise Refusal(
            "{} names {}, which is not a file in this repository.".format(
                REGISTER, workflow
            )
        )

    document = yaml.safe_load(path.read_text(encoding="utf-8")) or {}
    jobs = document.get("jobs") or {}

    if job not in jobs:
        raise Refusal(
            "{} carries no job {!r}. The register names it, so either the job was "
            "renamed and the context with it, or the register is stale.".format(
                workflow, job
            )
        )

    return jobs[job] or {}


def accounted(declared, job, workflow, job_id):
    """Refuse a job whose `run:` steps are not exactly the ones the register names."""
    running = [
        step.get("name")
        for step in (job.get("steps") or [])
        if isinstance(step, dict) and step.get("run") is not None
    ]

    unnamed = [name for name in running if not name]

    if unnamed:
        raise Refusal(
            "{}:{} carries {} step(s) with a command and no name. The register "
            "places a step by its name, so an unnamed one cannot be accounted "
            "for.".format(workflow, job_id, len(unnamed))
        )

    named = {step.name for step in declared}
    present = set(running)

    missing = sorted(present - named)
    absent = sorted(named - present)

    if missing:
        raise Refusal(
            "{}:{} runs {} that {} does not account for. Add a line running it or "
            "a line keeping it out with the reason, because a step nobody "
            "accounted for is one this command silently stops covering.".format(
                workflow, job_id, ", ".join(repr(name) for name in missing), REGISTER
            )
        )

    if absent:
        raise Refusal(
            "{} names {} in {}:{} and the job carries no such step with a "
            "command.".format(
                REGISTER, ", ".join(repr(name) for name in absent), workflow, job_id
            )
        )


def body_of(job, name):
    """Answer one named step's command and the environment it is given."""
    for step in job.get("steps") or []:
        if isinstance(step, dict) and step.get("name") == name:
            return step.get("run"), step.get("env") or {}

    raise Refusal("no step named {!r}".format(name))


def environment(job, step_env, dropped):
    """Build the environment for one step, dropping what only the forge can answer."""
    values = dict(os.environ)

    for source in (job.get("env") or {}, step_env):
        for key, value in source.items():
            text = "" if value is None else str(value)

            if EXPRESSION in text:
                dropped.append(key)
                continue

            values[key] = text

    return values


def run(command, values):
    """Run one step's command the way the gate's default shell runs it."""
    bash = shutil.which("bash")

    if not bash:
        raise Refusal(
            "there is no `bash` on this machine. Every step below is a shell "
            "script the gate runs with bash, so this command cannot stand in for "
            "the gate without one."
        )

    return subprocess.call(
        [bash, "--noprofile", "--norc", "-eo", "pipefail", "-c", command],
        cwd=str(ROOT),
        env=values,
    )


def main():
    """Run each leg in the register's order, stopping at the first that fails."""
    try:
        steps = register(ROOT / REGISTER)
        plan = []

        for leg, declared in legs(steps):
            workflow, job_id = job_of(declared)
            job = read_job(workflow, job_id)
            accounted(declared, job, workflow, job_id)
            plan.append((leg, workflow, job_id, job, declared))
    except Refusal as refusal:
        print("gate.py: {}".format(refusal))
        return 2

    verdicts = []
    kept_out = []
    dropped_values = []
    stopped = False

    for leg, workflow, job_id, job, declared in plan:
        for step in declared:
            if step.disposition == SKIP:
                kept_out.append((leg, step.name, step.reason))

        if stopped:
            verdicts.append((leg, "not run", "a leg before it failed"))
            continue

        print("\n=== {} ({}:{}) ===".format(leg, workflow, job_id), flush=True)

        failed = None

        for step in declared:
            if step.disposition != RUN:
                continue

            command, step_env = body_of(job, step.name)

            if EXPRESSION in command:
                failed = "the step {!r} carries a forge expression this machine cannot answer".format(step.name)
                print("gate.py: {}".format(failed), flush=True)
                break

            dropped = []
            values = environment(job, step_env, dropped)
            dropped_values.extend((leg, step.name, key) for key in dropped)

            print("--- {} ---".format(step.name), flush=True)

            try:
                code = run(command, values)
            except Refusal as refusal:
                failed = str(refusal)
                print("gate.py: {}".format(failed), flush=True)
                break

            if code != 0:
                failed = "the step {!r} exited {}".format(step.name, code)
                break

        if failed:
            verdicts.append((leg, "failed", failed))
            stopped = True
        else:
            verdicts.append((leg, "passed", ""))

    print("\n=== what this run covered ===", flush=True)

    for leg, verdict, detail in verdicts:
        print("{}: {}{}".format(leg, verdict, " - " + detail if detail else ""))

    for leg, name, reason in kept_out:
        print("{}: kept out of the run: {} - {}".format(leg, name, reason))

    for leg, name, key in dropped_values:
        print(
            "{}: {} was given no {}, because the workflow takes it from a forge "
            "expression".format(leg, name, key)
        )

    print(
        "These legs are the four jobs {} names. Which contexts a merge waits for "
        "is a repository setting no file here reads, and everything else the forge "
        "runs on a pull request is outside this command.".format(REGISTER)
    )

    return 0 if all(verdict == "passed" for _, verdict, _ in verdicts) else 1


if __name__ == "__main__":
    sys.exit(main())
