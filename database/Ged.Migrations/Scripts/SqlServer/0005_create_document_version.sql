CREATE TABLE document_version (
    id          uniqueidentifier  NOT NULL,
    document_id uniqueidentifier  NOT NULL,
    version_no  int               NOT NULL CONSTRAINT ck_document_version_no CHECK (version_no >= 1),
    blob_id     varchar(64)       NOT NULL,
    file_name   nvarchar(255)     NOT NULL,
    mime_type   varchar(255)      NOT NULL,
    size_bytes  bigint            NOT NULL CONSTRAINT ck_document_version_size CHECK (size_bytes >= 0),
    comment     nvarchar(max)     NULL,
    created_at  datetimeoffset(7) NOT NULL,
    created_by  nvarchar(200)     NOT NULL,

    CONSTRAINT pk_document_version PRIMARY KEY NONCLUSTERED (id),
    CONSTRAINT fk_document_version_document FOREIGN KEY (document_id)
        REFERENCES document (id) ON DELETE CASCADE,
    CONSTRAINT fk_document_version_blob FOREIGN KEY (blob_id) REFERENCES blob (id)
);

-- Clustered on (document_id, version_no): the aggregate always loads a document's whole history,
-- and this makes that one sequential range read in version order.
CREATE UNIQUE CLUSTERED INDEX ux_document_version_sequence
    ON document_version (document_id, version_no);

-- Drives reference counting at collection time. Without it the collector's NOT EXISTS scans the
-- whole table, which is exactly when a stored counter starts to look tempting.
CREATE INDEX ix_document_version_blob ON document_version (blob_id);

-- SQL Server has no deferrable constraints, so the document -> current_version foreign key cannot be
-- added: the document row and its first version are inserted in the same transaction and neither can
-- legally be written first. The relationship is enforced by the aggregate, which never exposes a way
-- to point CurrentVersionId at a version it does not own.
