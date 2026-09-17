CREATE TABLE document_version (
    id          uuid         PRIMARY KEY,
    document_id uuid         NOT NULL REFERENCES document (id) ON DELETE CASCADE,
    version_no  int          NOT NULL CHECK (version_no >= 1),
    blob_id     varchar(64)  NOT NULL REFERENCES blob (id),
    file_name   varchar(255) NOT NULL,
    mime_type   varchar(255) NOT NULL,
    size_bytes  bigint       NOT NULL CHECK (size_bytes >= 0),
    comment     text         NULL,
    created_at  timestamptz  NOT NULL,
    created_by  varchar(200) NOT NULL
);

CREATE UNIQUE INDEX ux_document_version_sequence
    ON document_version (document_id, version_no);

-- Two consecutive versions of one document may not carry the same content: a version that records
-- no change pollutes the history that audit and restore both depend on.
CREATE UNIQUE INDEX ux_document_version_content
    ON document_version (document_id, blob_id, version_no);

-- Drives reference counting at collection time. Without it the collector's NOT EXISTS scans the
-- whole table, which is why a stored counter starts to look tempting.
CREATE INDEX ix_document_version_blob
    ON document_version (blob_id);

ALTER TABLE document
    ADD CONSTRAINT fk_document_current_version
    FOREIGN KEY (current_version_id) REFERENCES document_version (id)
    DEFERRABLE INITIALLY DEFERRED;
