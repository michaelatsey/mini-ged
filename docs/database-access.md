# Connecting to the databases

Both engines publish their port, so any local client works. Nothing needs installing: each image
ships its own client.

```
postgres     localhost:5432    ged / ged            database: ged
sqlserver    localhost:1433    sa / Ged!Passw0rd    database: ged
```

Credentials come from `.env`, or from the defaults in `compose.yaml`. They are local development
values and nothing more — production reads them from the orchestrator's secret store.

## From inside the container

```bash
docker compose exec postgres psql -U ged -d ged

docker compose --profile sqlserver exec sqlserver \
  /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Ged!Passw0rd' -C -d ged
```

`-C` is not optional. The ODBC 18 tools refuse a self-signed certificate without it, and the failure
reads as a timeout rather than as a certificate problem — the same trap as the healthcheck.

## From the host

```bash
psql "postgresql://ged:ged@localhost:5432/ged"
sqlcmd -S localhost,1433 -U sa -P 'Ged!Passw0rd' -C -d ged
```

One query, no session:

```bash
docker compose exec -T postgres psql -U ged -d ged -c "\dt"
```

`-T` skips the pseudo-terminal, which a script cannot allocate.

## From a GUI

DBeaver and DataGrip handle both engines; Azure Data Studio handles SQL Server. For SQL Server, tick
**Trust server certificate** — the graphical equivalent of `-C`.

---

## Verifying the schema

Connecting proves the server answers. These five queries prove the schema is the one this
application expects.

### The DbUp journal

```sql
-- PostgreSQL
SELECT scriptname, applied FROM schemaversions ORDER BY schemaversionsid;

-- SQL Server
SELECT ScriptName, Applied FROM SchemaVersions ORDER BY Id;
```

Eight rows, `0001` through `0008`. This is the table DbUp reads to decide what is left to apply, so
a missing row means a script never ran — not that it failed.

Grepping the repository for `schemaversions` finds nothing, and that is correct: DbUp creates the
table itself on first run. It is the only object in the schema that no script owns.

### The tables

```sql
\dt                                            -- PostgreSQL
SELECT name FROM sys.tables ORDER BY name;     -- SQL Server
```

```
blob   blob_location   document   document_version   folder   outbox_message
```

### The seeded root

```sql
SELECT id, name, folder_type FROM folder;
```

One row, `00000000-0000-7000-8000-000000000001`. This is the `folderId` every first call uses.

### The column that carries a domain invariant

The most interesting check, because "one location serves reads" is a cardinality and this is where
it is kept — in a column that holds one value, not in a state several rows could claim at once.

```sql
-- PostgreSQL
SELECT column_name, data_type, is_nullable
FROM   information_schema.columns
WHERE  table_name = 'blob' AND column_name = 'primary_location_id';
--   expected: uuid, YES

SELECT indexname FROM pg_indexes WHERE tablename = 'blob_location';
--   expected: NO ux_blob_location_single_primary — 0008 dropped it

-- SQL Server
SELECT name, is_nullable FROM sys.columns
WHERE  object_id = OBJECT_ID('blob') AND name = 'primary_location_id';
```

It used to be a partial unique index over `blob_location.state = 'PRIMARY'`. That form needed the
two writes that move it — one demotion, one promotion — to reach the engine in the right order, and
a rollback sends them in the other one. Nullable because a purged blob has no locations left.

### The computed column — SQL Server only

```sql
SELECT name, definition, is_persisted
FROM   sys.computed_columns
WHERE  object_id = OBJECT_ID('folder');
```

`is_persisted = 1` on `sibling_key`. SQL Server cannot index an expression, so uniqueness of a name
among siblings needs a persisted computed column. Its presence is already implied by the script
having applied — an expression that is not deterministic cannot be persisted, and the script would
have failed.

### The deferrable constraint — PostgreSQL only

```sql
SELECT conname, condeferrable, condeferred
FROM   pg_constraint
WHERE  conname IN ('fk_document_current_version', 'fk_blob_primary_location');
```

`condeferrable = t` on both. This is what allows a document and its first version — or a blob and its
first location — to be inserted in one transaction, when neither side can legally be written before
the other. SQL Server has no deferrable constraints, so neither foreign key exists there; the
aggregates enforce both, and `0005` and `0008` each say so where the constraint would have gone.

---

## After an upload

```sql
SELECT d.name, v.version_no, b.id AS digest, b.status, l.provider, l.state
FROM   document d
JOIN   document_version v ON v.id = d.current_version_id
JOIN   blob b             ON b.id = v.blob_id
JOIN   blob_location l    ON l.id = b.primary_location_id;
```

Both joins follow a pointer: `current_version_id` to reach the version, `primary_location_id` to
reach the copy reads are served from.

```
version_no  1
status      ACTIVE
provider    filesystem
state       REPLICA
```

Upload the same bytes into another folder and the query returns **two documents sharing one blob**.
That is the content-addressed model, visible in one result set.

The events written in the same transaction:

```sql
SELECT type, occurred_at, processed_at FROM outbox_message ORDER BY occurred_at;
```

`processed_at` is null on every row. No dispatcher is wired yet; a non-null value would mean
something is reading the outbox that should not be.

If a row exists here without its document, or the reverse, the interceptor is not enrolled in the
transaction — which is the one thing the outbox pattern exists to guarantee.

## And on disk

```bash
docker run --rm -v mini-ged_ged-storage:/d alpine find /d -type f
```

A separate container, because the API image has no shell. Expect `ged/a3/f9/a3f9c1…` — two levels of
sharding, and a file name that **is** the SHA-256. No user-supplied name ever reaches the filesystem;
the original lives in `document_version.file_name` and nowhere else.

---

## One thing to change

```yaml
ports:
  - "5432:5432"        # binds 0.0.0.0
```

As written, the database answers anything on the local network, with `ged/ged`. On a shared or public
network that is an open database.

```yaml
ports:
  - "127.0.0.1:5432:5432"
  - "127.0.0.1:1433:1433"
```

`psql` and DBeaver keep working; nothing else can reach it.
