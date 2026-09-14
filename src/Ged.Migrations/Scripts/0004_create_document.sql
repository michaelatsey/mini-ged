CREATE TABLE document (
    id                 uuid         PRIMARY KEY,
    folder_id          uuid         NOT NULL REFERENCES folder (id),
    name               varchar(255) NOT NULL,
    doc_type           varchar(50)  NOT NULL,
    current_version_id uuid         NOT NULL,
    created_at         timestamptz  NOT NULL,
    created_by         varchar(200) NOT NULL,
    updated_at         timestamptz  NULL,
    updated_by         varchar(200) NULL,
    deleted_at         timestamptz  NULL
);

CREATE INDEX ix_document_folder_active
    ON document (folder_id)
    WHERE deleted_at IS NULL;
