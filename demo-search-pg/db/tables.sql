-- Create extension for full-text search
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- Create vector extension for embedding search
CREATE EXTENSION IF NOT EXISTS vector;

-- Create documents table
CREATE TABLE IF NOT EXISTS documents (
    id SERIAL PRIMARY KEY,
    doc_name TEXT NOT NULL,
    doc_desc TEXT,
    doc_link TEXT,
    embedding VECTOR(1536) -- Assuming 1536 dimensions for the embedding vector
);


-- Generated column for full-text search
ALTER TABLE documents ADD COLUMN search_vector TSVECTOR 
    GENERATED ALWAYS AS (
        setweight(to_tsvector('english', COALESCE(doc_name, '')), 'A') ||
        setweight(to_tsvector('english', COALESCE(doc_desc, '')), 'B')
    ) STORED;