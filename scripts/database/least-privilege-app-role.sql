-- HudhudNest: OPTIONAL. A database role for the running API that can read and write rows but
-- cannot change the schema, drop tables or touch other databases. The Migrator keeps using the
-- owner account. Run once as the database owner. Replace CHANGE_ME with a long random password.
-- Tried locally 2026-10-29: the API starts, reads, writes and passes /health/ready under this role,
-- and DROP TABLE / ALTER TABLE are refused. Not yet tried on Render: if CREATE ROLE is refused there, Render's plan does not allow extra
-- roles and this step is skipped.
CREATE ROLE hudhudnest_app LOGIN PASSWORD 'CHANGE_ME';
DO $$ BEGIN EXECUTE format('GRANT CONNECT ON DATABASE %I TO hudhudnest_app', current_database()); END $$;
GRANT USAGE ON SCHEMA public TO hudhudnest_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO hudhudnest_app;
GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO hudhudnest_app;
-- Tables and sequences that future migrations create (run by the owner) get the same rights.
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO hudhudnest_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO hudhudnest_app;
-- Then set the API's (not the Migrator's) ConnectionStrings__DefaultConnection to this user.
