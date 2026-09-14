CREATE TABLE folder (
    id          uuid         PRIMARY KEY,
    parent_id   uuid         NULL REFERENCES folder (id),
    name        varchar(128) NOT NULL,
    folder_type varchar(50)  NOT NULL,
    created_at  timestamptz  NOT NULL,
    created_by  varchar(200) NOT NULL,
    updated_at  timestamptz  NULL,
    updated_by  varchar(200) NULL,
    deleted_at  timestamptz  NULL
);

-- Sibling name uniqueness cannot be enforced by the aggregate: a folder cannot see its siblings,
-- and a check against a list loaded a moment earlier looks like a guarantee while a concurrent
-- insert slips past it. The guarantee belongs here.
CREATE UNIQUE INDEX ux_folder_sibling_name
    ON folder (COALESCE(parent_id, '00000000-0000-0000-0000-000000000000'::uuid), lower(name))
    WHERE deleted_at IS NULL;

CREATE INDEX ix_folder_parent_active
    ON folder (parent_id)
    WHERE deleted_at IS NULL;
