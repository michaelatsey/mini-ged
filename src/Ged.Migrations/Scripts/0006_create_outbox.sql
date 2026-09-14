CREATE TABLE outbox_message (
    id           uuid         PRIMARY KEY,   -- UUID v7: time-ordered, so the index stays compact
    occurred_at  timestamptz  NOT NULL,
    type         varchar(300) NOT NULL,
    payload      jsonb        NOT NULL,
    processed_at timestamptz  NULL,
    attempts     int          NOT NULL DEFAULT 0,
    error        text         NULL
);

-- Partial index: the dispatcher only ever reads unprocessed rows, and the processed ones grow
-- without bound until they are archived.
CREATE INDEX ix_outbox_pending
    ON outbox_message (occurred_at)
    WHERE processed_at IS NULL;
