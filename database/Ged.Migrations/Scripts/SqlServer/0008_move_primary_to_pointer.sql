-- Which location serves reads was a state spread over blob_location rows, guarded by a filtered
-- unique index. A cardinality of one cannot be enforced that way without also fixing the order of
-- the two writes that move it: PromoteToPrimary demotes one row and promotes another, and the
-- direction that promotes the older row -- a rollback -- violates the index. SQL Server has no
-- deferrable constraints, so the escape PostgreSQL might have had was never available here at all.
--
-- So the value moves into the type of a column. blob.primary_location_id holds one id, the index
-- goes, and the order EF picks stops being part of the correctness argument. document already
-- resolves the same question the same way, with current_version_id.

ALTER TABLE blob ADD primary_location_id uniqueidentifier NULL;

-- These scripts carry no GO, so the whole file is one batch and SQL Server parses every statement
-- before running any of them: a DML statement naming the column added above would fail to compile
-- with "Invalid column name". EXEC defers that parse until the column exists. DDL is exempt, which
-- is why the ALTER statements below need no such wrapper.
EXEC('
UPDATE b
   SET b.primary_location_id = l.id
  FROM blob b
  JOIN blob_location l ON l.blob_id = b.id
 WHERE l.state = ''PRIMARY'';');

-- PostgreSQL exempted PRIMARY from its verified-before-serving check, so the location created by
-- Register could serve reads having never been confirmed against the digest. That exemption has
-- nowhere to go now that PRIMARY is not a state; registered_at is when those bytes were written.
-- Applied here too so a row means the same thing on both engines, even though this engine never
-- carried the check itself -- and kept in the same order, so the two scripts stay comparable.
--
-- The constraint itself is deliberately NOT added here. PostgreSQL has enforced it since 0003 and
-- this engine never has, so adding it now would be a new guarantee rather than the migration of an
-- existing one. The consequence is worth stating rather than leaving to be discovered: a writer bug
-- that leaves a REPLICA row with a null verified_at is caught on one engine and not the other, in a
-- slice that runs on both. Closing that gap is its own change.
UPDATE blob_location
   SET verified_at = registered_at
 WHERE verified_at IS NULL
   AND state <> 'MIGRATING';

UPDATE blob_location SET state = 'REPLICA' WHERE state = 'PRIMARY';

DROP INDEX ux_blob_location_single_primary ON blob_location;

ALTER TABLE blob_location DROP CONSTRAINT ck_blob_location_state;
ALTER TABLE blob_location
    ADD CONSTRAINT ck_blob_location_state
    CHECK (state IN ('MIGRATING', 'REPLICA', 'LEGACY'));

-- SQL Server has no deferrable constraints, so the blob -> primary_location foreign key cannot be
-- added: the blob row and its first location are inserted in the same transaction, a purge deletes
-- the locations and nulls the pointer in another, and neither side can legally be written first.
-- The relationship is enforced by the aggregate, which never exposes a way to point
-- PrimaryLocationId at a location it does not own.
