-- Create extension for full-text search
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- Create vector extension for embedding search
CREATE EXTENSION IF NOT EXISTS vector;

-- Create extension for chamkho_parser to support Thai language search
CREATE EXTENSION IF NOT EXISTS chamkho_parser;
CREATE TEXT SEARCH CONFIGURATION chamkho (PARSER = chamkho_parser);
ALTER TEXT SEARCH CONFIGURATION chamkho ADD MAPPING FOR word WITH simple;

-- Create documents table
CREATE TABLE IF NOT EXISTS documents (
    id SERIAL PRIMARY KEY,
    doc_name TEXT NOT NULL,
    doc_desc TEXT,
    doc_link TEXT,
    embedding VECTOR(1024) -- Assuming 1024 dimensions for the embedding vector
);

-- Generated column for full-text search
ALTER TABLE documents ADD COLUMN search_vector TSVECTOR 
    GENERATED ALWAYS AS (
        setweight(to_tsvector('english', COALESCE(doc_name, '')), 'A') ||
        setweight(to_tsvector('chamkho', COALESCE(doc_name, '')), 'B') ||
        setweight(to_tsvector('english', COALESCE(doc_desc, '')), 'C') ||
        setweight(to_tsvector('chamkho', COALESCE(doc_desc, '')), 'D')
    ) STORED;

-- Create search_text colume to merge doc_name and doc_desc for ngram search
ALTER TABLE documents ADD COLUMN search_text TEXT GENERATED ALWAYS AS (doc_name || ' ' || doc_desc) STORED;

-- Create index for full-text search from search_text column
CREATE INDEX idx_documents_search_text_fts ON documents USING GIN (to_tsvector('chamkho', search_text));

-- Create index for trigram search for doc_name
CREATE INDEX idx_documents_doc_name_trgm ON documents USING GIN (doc_name gin_trgm_ops);


