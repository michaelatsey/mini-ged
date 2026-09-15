-- SQL Server orders `uniqueidentifier` from the LAST bytes first (10-15, then 8-9, 6-7, 4-5, 0-3).
-- UUIDv7 puts its timestamp in the LEADING bytes, so values that are perfectly sequential everywhere
-- else land at random positions here and split pages on every insert.
--
-- The fix used throughout this schema: keep the GUID as a NONCLUSTERED primary key and cluster on an
-- ever-increasing column instead. Inserts then append, and lookups by id still use the PK index.
CREATE TABLE folder (
    id          uniqueidentifier  NOT NULL,
    parent_id   uniqueidentifier  NULL,
    name        nvarchar(128)     NOT NULL,
    folder_type varchar(50)       NOT NULL,
    created_at  datetimeoffset(7) NOT NULL,
    created_by  nvarchar(200)     NOT NULL,
    updated_at  datetimeoffset(7) NULL,
    updated_by  nvarchar(200)     NULL,
    deleted_at  datetimeoffset(7) NULL,
    row_version rowversion        NOT NULL,

    CONSTRAINT pk_folder PRIMARY KEY NONCLUSTERED (id),
    CONSTRAINT fk_folder_parent FOREIGN KEY (parent_id) REFERENCES folder (id)
);

CREATE CLUSTERED INDEX ix_folder_clustered ON folder (created_at, id);

-- Sibling name uniqueness cannot be enforced by the aggregate: a folder cannot see its siblings, and
-- a check against a list loaded a moment earlier looks like a guarantee while a concurrent insert
-- slips past it.
--
-- PostgreSQL can index an expression directly. SQL Server cannot, so the normalised key becomes a
-- persisted computed column and the index is built on that.
ALTER TABLE folder ADD sibling_key AS (
    CONVERT(varchar(36), ISNULL(parent_id, '00000000-0000-0000-0000-000000000000'))
    + '|' + LOWER(name)
) PERSISTED;

CREATE UNIQUE INDEX ux_folder_sibling_name
    ON folder (sibling_key)
    WHERE deleted_at IS NULL;

CREATE INDEX ix_folder_parent_active
    ON folder (parent_id)
    WHERE deleted_at IS NULL;
