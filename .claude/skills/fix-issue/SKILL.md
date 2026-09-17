---
name: fix-issue
description: Fix one GitHub issue end to end — read it, fix only what it lists, cover each finding with a test, verify, review, commit. Stops before pushing.
disable-model-invocation: true
---

# Fix one issue

Fix the GitHub issue: $ARGUMENTS.

One issue, one branch, one commit. Anything you find that the issue does not list is a
separate issue — open it, do not fix it here.

## 1. Read the issue

```bash
gh issue view $ARGUMENTS
```

Read every finding, with its file and line references. If a finding is ambiguous, ask
before writing code. A wrong fix to the right line costs more than a question.

## 2. Fix only what the issue lists

Scope creep in a fix commit is what makes a revert impossible six months later.

Before changing anything, check `CLAUDE.md` for the decisions already rejected and the
questions deliberately left open. **Never resolve an open question as a side effect of a
fix.** If a fix appears to require one — a multi-tenancy strategy, an authorization
model — stop and say so.

## 3. Cover each finding with a test

A fix with no test is not done. The issue describes a defect that shipped, which means
nothing caught it.

- Domain behaviour → `tests/Ged.Domain.Tests`
- Upload, detection, staging → `tests/Ged.Features.Tests`

Write the test so it fails against the current code before you fix it. A test that passes
before the fix is testing something else.

Assert on the **type** of the violated rule, never on its message. Use fixed instants,
never `DateTimeOffset.UtcNow` — that is what the instant parameter exists for.

## 4. Verify, and show the evidence

```bash
dotnet build -c Release
dotnet test
```

`Release` matters: `TreatWarningsAsErrors` applies there and not in `Debug`, so the
container build is stricter than a local F5.

**Show me the output. Do not assert that it passed.** Paste the test summary and the build
result. If anything is red, fix it and run again — do not report until both are green.

If the fix touches upload, storage or the schema, also:

```bash
./scripts/check-dockerfile-copies.sh hosts/Ged.Api/Dockerfile
docker compose up --build -d && curl -fsS http://localhost:8080/health/ready
docker compose down
```

## 5. Have a subagent review the diff

Use a subagent to review the diff against the issue. Give it the issue text and the diff,
and ask it to check that every finding is addressed, that each has a test, and that nothing
outside the issue's scope changed.

**Flag only gaps that affect correctness or the stated findings.** A reviewer asked to find
gaps will usually report some even when the work is sound; chasing every one of them leads
to defensive code and tests for cases that cannot happen.

Fix what the review finds, then run step 4 again.

## 6. Commit

One commit, unless the issue genuinely covers unrelated changes.

The body explains **the decision**, not the diff. What was wrong, why this fix and not the
other one, and what it costs. Conventional commits, in English.

```
fix(scope): what changed, in the imperative

What was wrong, stated as the symptom someone would see.

Why this fix rather than the alternative that was considered.

Any behaviour that changed as a side effect, named rather than left to be discovered.

Closes #$ARGUMENTS

If the commit requires approval, show me the full message and the list of staged
files first. Do not stage anything the issue did not require.
```

## 7. Stop

**Do not push. Do not open a pull request.**

Report:

- the files you changed and why
- the tests you added, and what each one would have caught
- the output of `dotnet test`
- anything you found and deliberately did not fix, with the issue number you opened for it

Then wait. A local commit can be undone; a push to a public repository cannot.

<!-- Step 7 diverges from the documented fix-issue skill, which pushes and opens the PR.
     Deliberate: two of the four review lots touch data-loss paths, and their diff is worth
     two minutes of reading. For documentation and script lots, saying "push and open the
     PR" after the report costs one line. -->
