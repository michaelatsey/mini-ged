CREATE TABLE blob_location (
    id            uuid         PRIMARY KEY,
    blob_id       varchar(64)  NOT NULL REFERENCES blob (id) ON DELETE CASCADE,
    provider      varchar(50)  NOT NULL,
    bucket        varchar(100) NOT NULL,
    object_key    varchar(300) NOT NULL,
    state         varchar(20)  NOT NULL,
    registered_at timestamptz  NOT NULL,
    verified_at   timestamptz  NULL,

    CONSTRAINT ck_blob_location_state
        CHECK (state IN ('MIGRATING', 'REPLICA', 'PRIMARY', 'LEGACY')),

    -- An unverified copy must never be the location reads are served from.
    CONSTRAINT ck_blob_location_verified_before_serving
        CHECK (state = 'MIGRATING' OR verified_at IS NOT NULL OR state = 'PRIMARY')
);

-- The aggregate guarantees a single primary within one transaction. This guarantees it against
-- two. Both are needed: neither covers the other's case.
CREATE UNIQUE INDEX ux_blob_location_single_primary
    ON blob_location (blob_id)
    WHERE state = 'PRIMARY';

CREATE UNIQUE INDEX ux_blob_location_address
    ON blob_location (provider, bucket, object_key);

CREATE INDEX ix_blob_location_by_blob
    ON blob_location (blob_id, state);
