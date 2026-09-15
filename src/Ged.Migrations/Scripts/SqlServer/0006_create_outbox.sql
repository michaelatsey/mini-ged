CREATE TABLE outbox_message (
    id           uniqueidentifier  NOT NULL,
    occurred_at  datetimeoffset(7) NOT NULL,
    type         varchar(300)      NOT NULL,
    payload      nvarchar(max)     NOT NULL,
    processed_at datetimeoffset(7) NULL,
    attempts     int               NOT NULL CONSTRAINT df_outbox_attempts DEFAULT 0,
    error        nvarchar(max)     NULL,

    CONSTRAINT pk_outbox_message PRIMARY KEY NONCLUSTERED (id)
);

-- The highest-insert-rate table in the system, and the one where UUIDv7's ordering mismatch with
-- SQL Server would hurt most. Clustering on occurred_at makes every insert an append.
CREATE CLUSTERED INDEX ix_outbox_clustered ON outbox_message (occurred_at, id);

-- The dispatcher only ever reads unprocessed rows; delivered ones stay until they are archived.
CREATE INDEX ix_outbox_pending
    ON outbox_message (occurred_at)
    WHERE processed_at IS NULL;
