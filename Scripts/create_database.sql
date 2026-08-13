-- =============================================================================
-- LunchOrganizer - Database Schema Creation Script
-- =============================================================================
--
-- WHAT THIS FILE IS
--   This is an idempotent SQL script that creates the LunchOrganizer database
--   schema (tables, constraints, indexes, etc.). It was generated directly
--   from the real EF Core migration '20260812075601_InitialCreate' by running:
--
--       dotnet ef migrations script --idempotent
--         --project src/LunchOrganizer.Data/LunchOrganizer.Data.csproj
--         --startup-project src/LunchOrganizer.Data/LunchOrganizer.Data.csproj
--
--   Nothing below this header has been hand-edited: it is the verbatim output
--   of that command.
--
-- IDEMPOTENCY
--   This script is safe to run multiple times, and safe to run against a
--   database that already has some or all of this schema applied. Each
--   migration's statements are wrapped in a check against EF Core's
--   "__EFMigrationsHistory" tracking table, so only migrations that have not
--   yet been recorded as applied will actually run. Running this script
--   again after it has already succeeded is a no-op.
--
-- WHAT THIS SCRIPT DOES NOT DO
--   This script does NOT create the "lunchorganizer" database itself. It only
--   creates tables/constraints/indexes/etc. inside whichever database `psql`
--   is already connected to when the script is executed. The companion
--   script `create_database.ps1` (in this same folder) creates the target
--   database first (if it does not already exist), and then runs this SQL
--   file against it.
--
-- =============================================================================

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812075601_InitialCreate') THEN
    CREATE EXTENSION IF NOT EXISTS citext;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812075601_InitialCreate') THEN
    CREATE TABLE daily_prices (
        price_date date NOT NULL,
        price numeric(10,2) NOT NULL,
        created_at_utc timestamptz NOT NULL DEFAULT (now()),
        updated_at_utc timestamptz NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_daily_prices" PRIMARY KEY (price_date),
        CONSTRAINT ck_daily_prices_price_non_negative CHECK (price >= 0)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812075601_InitialCreate') THEN
    CREATE TABLE email_log (
        summary_date date NOT NULL,
        sent_at_utc timestamptz NOT NULL,
        status text NOT NULL,
        recipients text,
        booking_count integer NOT NULL,
        error_message text,
        CONSTRAINT "PK_email_log" PRIMARY KEY (summary_date)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812075601_InitialCreate') THEN
    CREATE TABLE employees (
        id integer GENERATED ALWAYS AS IDENTITY,
        full_name citext NOT NULL,
        email text,
        is_active boolean NOT NULL DEFAULT TRUE,
        created_at_utc timestamptz NOT NULL DEFAULT (now()),
        updated_at_utc timestamptz NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_employees" PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812075601_InitialCreate') THEN
    CREATE TABLE menus (
        id integer GENERATED ALWAYS AS IDENTITY,
        menu_date date NOT NULL,
        menu_number integer NOT NULL,
        description text,
        created_at_utc timestamptz NOT NULL DEFAULT (now()),
        updated_at_utc timestamptz NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_menus" PRIMARY KEY (id),
        CONSTRAINT "AK_menus_id_menu_date" UNIQUE (id, menu_date),
        CONSTRAINT ck_menus_menu_number_positive CHECK (menu_number >= 1)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812075601_InitialCreate') THEN
    CREATE TABLE bookings (
        id integer GENERATED ALWAYS AS IDENTITY,
        employee_id integer NOT NULL,
        booking_date date NOT NULL,
        menu_id integer NOT NULL,
        price_snapshot numeric(10,2) NOT NULL,
        created_at_utc timestamptz NOT NULL DEFAULT (now()),
        updated_at_utc timestamptz NOT NULL DEFAULT (now()),
        CONSTRAINT "PK_bookings" PRIMARY KEY (id),
        CONSTRAINT ck_bookings_price_snapshot_non_negative CHECK (price_snapshot >= 0),
        CONSTRAINT "FK_bookings_employees_employee_id" FOREIGN KEY (employee_id) REFERENCES employees (id) ON DELETE RESTRICT,
        CONSTRAINT "FK_bookings_menus_menu_id_booking_date" FOREIGN KEY (menu_id, booking_date) REFERENCES menus (id, menu_date) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812075601_InitialCreate') THEN
    CREATE INDEX ix_bookings_booking_date ON bookings (booking_date);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812075601_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_bookings_employee_date ON bookings (employee_id, booking_date);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812075601_InitialCreate') THEN
    CREATE INDEX "IX_bookings_menu_id_booking_date" ON bookings (menu_id, booking_date);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812075601_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_employees_full_name ON employees (full_name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812075601_InitialCreate') THEN
    CREATE INDEX ix_menus_menu_date ON menus (menu_date);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812075601_InitialCreate') THEN
    CREATE UNIQUE INDEX ix_menus_menu_date_menu_number ON menus (menu_date, menu_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260812075601_InitialCreate') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260812075601_InitialCreate', '10.0.11');
    END IF;
END $EF$;
COMMIT;

