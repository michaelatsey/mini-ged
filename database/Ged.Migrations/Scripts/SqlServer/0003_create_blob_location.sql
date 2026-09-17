CREATE TABLE blob_location (
    id            uniqueidentifier  NOT NULL,
    blob_id       varchar(64)       NOT NULL,
    provider      varchar(50)       NOT NULL,
    bucket        nvarchar(100)     NOT NULL,
    object_key    nvarchar(300)     NOT NULL,
    state         varchar(20)       NOT NULL,
    registered_at datetimeoffset(7) NOT NULL,
    verified_at   datetimeoffset(7) NULL,

    CONSTRAINT pk_blob_location PRIMARY KEY NONCLUSTERED (id),
    CONSTRAINT fk_blob_location_blob FOREIGN KEY (blob_id)
        REFERENCES blob (id) ON DELETE CASCADE,
    CONSTRAINT ck_blob_location_state
        CHECK (state IN ('MIGRATING', 'REPLICA', 'PRIMARY', 'LEGACY'))
);

-- Clustered on the blob so every location of one blob sits on the same page: the aggregate always
-- loads them together, and the read-order resolution walks them together.
CREATE CLUSTERED INDEX ix_blob_location_clustered ON blob_location (blob_id, id);

-- The aggregate guarantees a single primary within one transaction. This guarantees it against two.
-- Both are needed: neither covers the other's case.
CREATE UNIQUE INDEX ux_blob_location_single_primary
    ON blob_location (blob_id)
    WHERE state = 'PRIMARY';

CREATE UNIQUE INDEX ux_blob_location_address
    ON blob_location (provider, bucket, object_key);
