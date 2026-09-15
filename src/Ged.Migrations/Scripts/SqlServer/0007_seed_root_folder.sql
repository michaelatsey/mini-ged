-- A single well-known root so the first upload has somewhere to go. Idempotent: DbUp runs a script
-- once, but a restored backup may already contain the row.
IF NOT EXISTS (SELECT 1 FROM folder WHERE id = '00000000-0000-7000-8000-000000000001')
BEGIN
    INSERT INTO folder (id, parent_id, name, folder_type, created_at, created_by)
    VALUES (
        '00000000-0000-7000-8000-000000000001',
        NULL,
        N'Racine',
        'CATEGORY',
        SYSDATETIMEOFFSET(),
        N'migration'
    );
END;
