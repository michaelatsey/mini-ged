CREATE TABLE document (
    id                 uniqueidentifier  NOT NULL,
    folder_id          uniqueidentifier  NOT NULL,
    name               nvarchar(255)     NOT NULL,
    doc_type           varchar(50)       NOT NULL,
    current_version_id uniqueidentifier  NOT NULL,
    created_at         datetimeoffset(7) NOT NULL,
    created_by         nvarchar(200)     NOT NULL,
    updated_at         datetimeoffset(7) NULL,
    updated_by         nvarchar(200)     NULL,
    deleted_at         datetimeoffset(7) NULL,
    row_version        rowversion        NOT NULL,

    CONSTRAINT pk_document PRIMARY KEY NONCLUSTERED (id),
    CONSTRAINT fk_document_folder FOREIGN KEY (folder_id) REFERENCES folder (id)
);

CREATE CLUSTERED INDEX ix_document_clustered ON document (created_at, id);

CREATE INDEX ix_document_folder_active
    ON document (folder_id)
    WHERE deleted_at IS NULL;
