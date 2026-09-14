-- A single well-known root so the first upload has somewhere to go. Idempotent: DbUp runs a script
-- once, but a restored dump may already contain the row.
INSERT INTO folder (id, parent_id, name, folder_type, created_at, created_by)
VALUES (
    '00000000-0000-7000-8000-000000000001',
    NULL,
    'Racine',
    'CATEGORY',
    now(),
    'migration'
)
ON CONFLICT (id) DO NOTHING;
