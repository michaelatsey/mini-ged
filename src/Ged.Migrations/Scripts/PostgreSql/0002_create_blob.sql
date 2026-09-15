CREATE TABLE blob (
    id           varchar(64)  PRIMARY KEY,   -- lowercase hex SHA-256 of the content itself
    size_bytes   bigint       NOT NULL CHECK (size_bytes >= 0),
    status       varchar(30)  NOT NULL,
    orphan_since timestamptz  NULL,
    created_at   timestamptz  NOT NULL,
    created_by   varchar(200) NOT NULL,
    updated_at   timestamptz  NULL,
    updated_by   varchar(200) NULL,

    CONSTRAINT ck_blob_orphan_since CHECK (
        (status = 'ORPHAN_CANDIDATE' AND orphan_since IS NOT NULL)
     OR (status <> 'ORPHAN_CANDIDATE' AND orphan_since IS NULL)
    )
);

-- Drives the collector's first pass. Without it, marking orphan candidates scans the whole table.
CREATE INDEX ix_blob_orphan_candidates
    ON blob (orphan_since)
    WHERE status = 'ORPHAN_CANDIDATE';
