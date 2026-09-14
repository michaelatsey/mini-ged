# What this domain does not guarantee

An honest domain documents its gaps. These invariants cannot be enforced by an aggregate, and the
model deliberately does not pretend otherwise.

## Sibling name uniqueness

An aggregate cannot see its siblings. A check against a list loaded a moment earlier would look
like a guarantee while a concurrent insert slips past it:

```
t0  load sibling names
t1  rule passes
t2  another user creates "Contrats"
t3  we write "Contrats"        → duplicate, despite the rule
```

The guarantee belongs to a unique index. The application may still pre-check to produce a friendly
message — that is ergonomics, not enforcement.

## Subtree depth after a move

`Folder.MoveTo` validates the depth of the folder being moved. It cannot see its own descendants,
so a move that pushes a deep descendant past `FolderAncestry.MaxDepth` is not caught.

Closing this is a one-parameter change — `MoveTo(targetAncestry, subtreeHeight, now, by)` — using
the same principle as the ancestry: the caller supplies the fact, the domain owns the rule. It is
not enabled by default because it burdens the common case for a secondary risk.

## Cascading deletion

`Folder.SoftDelete` refuses to delete a folder that still holds content, and cascades nothing. Child
folders and documents are separate aggregates with their own transactions. Deleting a tree is an
application-level orchestration, not an aggregate operation.

## Physical removal of content

`Document.SoftDelete` publishes every blob digest the document referenced. That is a list of
contents *worth examining*, never an instruction to remove them: another document may still
reference any of them.

Physical deletion belongs to a background collector, behind a retention window, with a
re-verification pass before it removes anything. Nothing in `Ged.Domain` can delete a byte, and
that is by design rather than by omission.

## Reference counting

`Blob.MarkOrphanCandidate` and `Blob.MarkPurged` take `hasLiveReferences` as a parameter. The
aggregate cannot answer the question itself — documents are a separate aggregate — and the model
deliberately stores no counter:

```
counter incremented on reference, decremented on release
        ↓
one incident, one failed transaction, one bad migration
        ↓
the counter no longer matches reality
        ↓
nobody trusts it, so nobody purges anything again
```

Counting at purge time is slower and always right. The caller runs the query, the rule decides.

## The window between the two collector passes

A blob marked as an orphan candidate can be referenced again before it is purged: a new upload of
identical content resolves to the same digest and reuses the very same blob.

That is why `MarkPurged` re-checks `hasLiveReferences` rather than trusting the earlier pass, and
why `Reactivate` exists. A collector that marks and purges in one pass, or that trusts a status set
days earlier, will eventually delete content out from under a live document.

## Concurrent moves

Two folders moved concurrently can produce a cycle that neither transaction could see. The rule
catches what one aggregate can observe; the rest needs optimistic concurrency or a database-level
constraint.
