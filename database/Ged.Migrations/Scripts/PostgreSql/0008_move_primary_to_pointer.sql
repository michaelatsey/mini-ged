-- Which location serves reads was a state spread over blob_location rows, guarded by a partial
-- unique index. A cardinality of one cannot be enforced that way without also fixing the order of
-- the two writes that move it: PromoteToPrimary demotes one row and promotes another, and the
-- direction that promotes the older row -- a rollback -- violates the index. The documented escape,
-- DEFERRABLE on the index, is not valid SQL: PostgreSQL defers constraints, and a UNIQUE constraint
-- cannot carry the WHERE filter this index needs.
--
-- So the value moves into the type of a column. blob.primary_location_id holds one id, the index
-- goes, and the order EF picks stops being part of the correctness argument. document already
-- resolves the same question the same way, with current_version_id.

ALTER TABLE blob ADD COLUMN primary_location_id uuid NULL;

UPDATE blob b
   SET primary_location_id = l.id
  FROM blob_location l
 WHERE l.blob_id = b.id
   AND l.state = 'PRIMARY';

-- Before the state moves, not after: the old check exempts PRIMARY and this one is still in force,
-- so a PRIMARY row with no verified_at becomes a REPLICA row that violates it the moment it is
-- rewritten. The exemption existed because Register created its location unverified; it has nowhere
-- to go now that PRIMARY is not a state, and registered_at is when those bytes were written.
UPDATE blob_location
   SET verified_at = registered_at
 WHERE verified_at IS NULL
   AND state <> 'MIGRATING';

UPDATE blob_location SET state = 'REPLICA' WHERE state = 'PRIMARY';

DROP INDEX ux_blob_location_single_primary;

ALTER TABLE blob_location DROP CONSTRAINT ck_blob_location_state;
ALTER TABLE blob_location
    ADD CONSTRAINT ck_blob_location_state
    CHECK (state IN ('MIGRATING', 'REPLICA', 'LEGACY'));

-- Stronger than what it replaces, and now sayable in one row: every readable copy has been checked.
-- That the checked copy is also the one reads go to is a claim about two tables, so it stays where
-- it can be made -- LocationMustBeVerifiedBeforePromotionRule, in the aggregate.
ALTER TABLE blob_location DROP CONSTRAINT ck_blob_location_verified_before_serving;
ALTER TABLE blob_location
    ADD CONSTRAINT ck_blob_location_verified_before_serving
    CHECK (state = 'MIGRATING' OR verified_at IS NOT NULL);

-- Deferred because this closes a cycle with blob_location.blob_id: a blob and its first location are
-- written together, and a purge clears the locations and nulls the pointer in the same SaveChanges.
-- Neither side can legally be written first, which is the same reason fk_document_current_version
-- carries this in 0005.
ALTER TABLE blob
    ADD CONSTRAINT fk_blob_primary_location
    FOREIGN KEY (primary_location_id) REFERENCES blob_location (id)
    DEFERRABLE INITIALLY DEFERRED;
