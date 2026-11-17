-- Run as a PostgreSQL administrator. Replace the password before execution.
CREATE ROLE kartoteka_app LOGIN PASSWORD 'REPLACE_WITH_UNIQUE_LONG_PASSWORD';
CREATE DATABASE kartoteka OWNER kartoteka_app ENCODING 'UTF8';
