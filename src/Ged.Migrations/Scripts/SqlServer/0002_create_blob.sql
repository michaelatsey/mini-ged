-- The key is a SHA-256 digest: 64 ASCII characters. varchar, not nvarchar — nvarchar would double
-- the size of every key, every index entry and every foreign key that points at it, for a character
-- set the value can never contain.
CREATE TABLE blob (
    id           varchar(64)       NOT NULL,
    size_bytes   bigint            NOT NULL CONSTRAINT ck_blob_size CHECK (size_bytes >= 0),
    status       varchar(30)       NOT NULL,
    orphan_since datetimeoffset(7) NULL,
    created_at   datetimeoffset(7) NOT NULL,
    created_by   nvarchar(200)     NOT NULL,
    updated_at   datetimeoffset(7) NULL,
    updated_by   nvarchar(200)     NULL,
    row_version  rowversion        NOT NULL,

    CONSTRAINT pk_blob PRIMARY KEY NONCLUSTERED (id),
    CONSTRAINT ck_blob_orphan_since CHECK (
        (status = 'ORPHAN_CANDIDATE' AND orphan_since IS NOT NULL)
     OR (status <> 'ORPHAN_CANDIDATE' AND orphan_since IS NULL)
    )
);

CREATE CLUSTERED INDEX ix_blob_clustered ON blob (created_at, id);

CREATE INDEX ix_blob_orphan_candidates
    ON blob (orphan_since)
    WHERE status = 'ORPHAN_CANDIDATE';
