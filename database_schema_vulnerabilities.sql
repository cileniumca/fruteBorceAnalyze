-- WordPress Plugin Vulnerability Analysis Database Schema
-- Tables for storing vulnerability detection and exploit testing results

-- Table for storing detected plugin vulnerabilities
CREATE TABLE IF NOT EXISTS public.site_plugin_vulnerabilities (
    id SERIAL PRIMARY KEY,
    site_id INTEGER NOT NULL,
    plugin_name VARCHAR(255) NOT NULL,
    vulnerability_type VARCHAR(255) NOT NULL,
    description TEXT,
    target_url TEXT,
    severity VARCHAR(50) NOT NULL,
    confidence VARCHAR(50) NOT NULL,
    detection_method VARCHAR(255),
    exploit_successful BOOLEAN DEFAULT FALSE,
    exploit_details TEXT,
    metadata JSONB,
    discovered_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    -- Add foreign key constraint if sites table exists
    CONSTRAINT fk_site_vulnerabilities_site_id 
        FOREIGN KEY (site_id) REFERENCES public.sites(id) ON DELETE CASCADE,
    
    -- Add indexes for better query performance
    INDEX idx_site_plugin_vulnerabilities_site_id (site_id),
    INDEX idx_site_plugin_vulnerabilities_plugin_name (plugin_name),
    INDEX idx_site_plugin_vulnerabilities_severity (severity),
    INDEX idx_site_plugin_vulnerabilities_discovered_at (discovered_at)
);

-- Table for storing vulnerability exploit test results
CREATE TABLE IF NOT EXISTS public.site_vulnerability_exploit_results (
    id SERIAL PRIMARY KEY,
    site_id INTEGER NOT NULL,
    target_url TEXT NOT NULL,
    vulnerability_type VARCHAR(255) NOT NULL,
    success BOOLEAN DEFAULT FALSE,
    details TEXT,
    method VARCHAR(255),
    response_data JSONB,
    executed_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    
    -- Add foreign key constraint if sites table exists
    CONSTRAINT fk_site_exploit_results_site_id 
        FOREIGN KEY (site_id) REFERENCES public.sites(id) ON DELETE CASCADE,
    
    -- Add indexes for better query performance
    INDEX idx_site_exploit_results_site_id (site_id),
    INDEX idx_site_exploit_results_vulnerability_type (vulnerability_type),
    INDEX idx_site_exploit_results_success (success),
    INDEX idx_site_exploit_results_executed_at (executed_at)
);

-- Add comments for documentation
COMMENT ON TABLE public.site_plugin_vulnerabilities IS 'Stores detected WordPress plugin vulnerabilities for educational security research';
COMMENT ON TABLE public.site_vulnerability_exploit_results IS 'Stores results of educational exploit testing for vulnerability research';

COMMENT ON COLUMN public.site_plugin_vulnerabilities.metadata IS 'JSON metadata containing detection details, patterns, and additional context';
COMMENT ON COLUMN public.site_vulnerability_exploit_results.response_data IS 'JSON response data from exploit testing attempts';

-- Grant appropriate permissions (adjust schema and user as needed)
-- GRANT SELECT, INSERT, UPDATE, DELETE ON public.site_plugin_vulnerabilities TO analyzer_user;
-- GRANT SELECT, INSERT, UPDATE, DELETE ON public.site_vulnerability_exploit_results TO analyzer_user;
-- GRANT USAGE, SELECT ON SEQUENCE site_plugin_vulnerabilities_id_seq TO analyzer_user;
-- GRANT USAGE, SELECT ON SEQUENCE site_vulnerability_exploit_results_id_seq TO analyzer_user;
