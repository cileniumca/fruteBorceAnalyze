using AnalyzeDomains.Domain.Enums;
using AnalyzeDomains.Domain.Interfaces.Analyzers;
using AnalyzeDomains.Domain.Interfaces.Services;
using AnalyzeDomains.Domain.Models.AnalyzeModels;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace AnalyzeDomains.Infrastructure.Analyzers
{
    public class PluginDetector : IPluginDetector
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<PluginDetector> _logger; private readonly List<string> _popularPlugins = new()
    {
        // Security Plugins
        "akismet", "wordfence", "wp-security-audit-log", "really-simple-ssl", "ithemes-security",
        "sucuri-scanner", "all-in-one-wp-security-and-firewall", "bulletproof-security", "hide-my-wp",
        "wp-hide-security-enhancer", "anti-malware", "wp-fail2ban", "limit-login-attempts-reloaded",
        "loginizer", "wp-security-scan", "mainwp-child", "defender-security", "wp-cerber",
        "wp-simple-firewall", "acunetix-wp-security", "blackhole-bad-bots", "disable-wp-rest-api",
        
        // SEO Plugins
        "yoast", "all-in-one-seo-pack", "rankmath", "the-seo-framework", "wp-seo",
        "premium-seo-pack", "seo-by-rank-math", "wordpress-seo", "seo-ultimate", "platinum-seo",
        "smartcrawl-seo", "squirrly-seo", "seopressor", "wpmu-dev-seo", "seo-optimized-images",
        "breadcrumb-navxt", "google-sitemap-generator", "xml-sitemap-feed", "better-wp-security",
        "wp-meta-seo", "schema-and-structured-data-for-wp", "local-seo", "seo-friendly-images",
        
        // Performance & Caching
        "wp-super-cache", "w3-total-cache", "wp-optimize", "wp-fastest-cache", "litespeed-cache",
        "wp-rocket", "autoptimize", "wp-smushit", "shortpixel-image-optimiser", "ewww-image-optimizer",
        "perfmatters", "asset-cleanup", "flying-press", "nitropack", "sg-cachepress",
        "comet-cache", "hyper-cache", "cache-enabler", "wp-super-minify", "fast-velocity-minify",
        "hummingbird-performance", "a3-lazy-load", "wp-cloudflare-page-cache", "breeze",
        "swift-performance", "redis-cache", "memcached", "opcache-dashboard", "query-monitor",
        
        // Page Builders
        "elementor", "beaver-builder-lite-version", "visual-composer", "divi-builder", "oxygen",
        "gutenberg", "page-builder-by-site-origin", "live-composer-page-builder", "themify-builder",
        "cornerstone", "fusion-builder", "brizy", "wp-page-builder", "motopress-content-editor",
        "yellow-pencil-visual-css-style-editor", "microthemer", "css-hero", "beaver-themer",
        "elementor-pro", "visual-composer-premium", "divi-theme-builder", "oxygen-vsb",
        
        // E-commerce
        "woocommerce", "easy-digital-downloads", "wp-ecommerce", "ecwid-ecommerce-shopping-cart",
        "woocommerce-gateway-stripe", "woocommerce-services", "woocommerce-admin", "jetpack-boost",
        "woocommerce-gateway-paypal-express-checkout", "woocommerce-pdf-invoices-packing-slips",
        "woocommerce-subscriptions", "woocommerce-bookings", "woocommerce-memberships",
        "yith-woocommerce-wishlist", "woocommerce-checkout-field-editor", "currency-switcher",
        "woocommerce-multilingual", "woocommerce-product-addon", "woocommerce-brands",
        "woocommerce-google-analytics-integration", "mailchimp-for-woocommerce", "facebook-for-woocommerce",
        
        // Contact Forms
        "contact-form-7", "wpforms-lite", "ninja-forms", "caldera-forms", "gravity-forms",
        "formidable", "contact-form-by-supsystic", "jetpack-contact-form", "wp-forms",
        "contact-bank", "quform", "contact-form-maker", "form-maker", "visual-form-builder",
        "cforms2", "si-contact-form", "fast-secure-contact-form", "clean-and-simple-contact-form",
        "kontakt-io", "contact-form-clean-and-simple", "pirate-forms", "happyforms",
        
        // Social Media
        "jetpack", "social-media-share-buttons", "simple-share-buttons-adder", "addthis",
        "shareaholic", "social-warfare", "ultimate-social-media-icons", "monarch",
        "social-media-builder", "instagram-feed", "custom-facebook-feed", "youtube-embed-plus",
        "twitter-widget-pro", "linkedin-auto-publish", "buffer", "hootsuite", "social-auto-poster",
        "revive-social", "social-share-wordpress", "wp-social-login", "nextend-social-login",
        "social-media-auto-publish", "wp-to-twitter", "tweet-old-post", "social-polls-by-opinionstage",
        
        // Analytics
        "google-analytics-for-wordpress", "monster-insights", "google-analytics-dashboard-for-wp",
        "ga-google-analytics", "google-tag-manager", "facebook-pixel-master", "hotjar",
        "crazy-egg", "google-optimize", "kissmetrics", "mixpanel", "segment", "amplitude",
        "google-site-kit", "exactmetrics", "analytics-insights", "wp-statistics", "statcounter",
        "clicky", "piwik-analytics", "simple-analytics", "open-web-analytics", "woopra",
        
        // Email Marketing
        "mailchimp-for-wp", "constant-contact-forms", "convertkit", "mailerlite", "aweber-web-form-plugin",
        "campaign-monitor-dual-registration", "emma-emarketing-automated", "getresponse-integration",
        "icontact-sign-up-forms", "benchmark-email-lite", "sendinblue-mailin", "mailjet-for-wordpress",
        "newsletter", "mailpoet", "sumo", "optinmonster", "thrive-leads", "bloom", "icegram",
        "popup-maker", "ninja-popups", "convertplug", "layered-popups", "exit-intent-popup",
        
        // Backup & Migration
        "updraftplus", "backwpup", "duplicator", "all-in-one-wp-migration", "backup-migration",
        "wp-migrate-db", "velvet-blues-update-urls", "backupbuddy", "wp-clone-by-wp-academy",
        "wp-staging", "migrate-guru", "blogvault-real-time-backup", "wp-time-capsule",
        "xcloner-backup-and-restore", "backup-wp", "wp-db-backup", "myrepono-wordpress-backup",
        "dropbox-backup", "google-drive-wp-backup", "amazon-s3-and-cloudfront", "wp-offload-media",
        
        // Membership & User Management
        "ultimate-member", "memberpress", "restrict-content-pro", "s2member", "paid-memberships-pro",
        "user-registration", "profile-builder", "wp-user-manager", "users-wp", "wp-members",
        "theme-my-login", "user-role-editor", "members", "capability-manager-enhanced",
        "admin-columns", "user-switching", "wp-user-frontend", "frontend-dashboard", "user-meta",
        "custom-registration-form-builder-with-submission-manager", "wp-user-profiles", "reign-theme",
        
        // LMS & Education
        "learnpress", "lifter-lms", "tutor", "learnpress-course-review", "wp-courseware",
        "sensei-lms", "masteriyo", "learndash", "thrive-apprentice", "good-lms",
        "wp-learning-management-system", "namaste-lms", "coursepress-pro", "wplms", "academy-lms",
        "elearning-evolution", "wp-quiz", "quiz-master-next", "wp-pro-quiz", "viral-quiz-maker",
        
        // Multilingual
        "wpml-multilingual-cms", "polylang", "weglot", "translatepress-multilingual", "gtranslate",
        "google-language-translator", "loco-translate", "wpml-string-translation", "qsot-qtranslate-x",
        "multilingual-press", "linguise", "conveythis-translate", "bablic", "localizer",
        "wp-native-articles", "falang", "xili-language", "sublanguage", "multilingual-wp",
        
        // Media & Images
        "wp-smushit", "shortpixel-image-optimiser", "ewww-image-optimizer", "optimole-wp",
        "imagify", "tinypng", "kraken-image-optimizer", "recompressor", "wp-retina-2x",
        "regenerate-thumbnails", "force-regenerate-thumbnails", "ajax-thumbnail-rebuild",
        "media-library-assistant", "wp-media-folder", "real-media-library", "enhanced-media-library",
        "file-manager-advanced", "wp-file-manager", "media-cleaner", "enable-media-replace",
        "custom-upload-dir", "media-from-ftp", "photo-gallery", "nextgen-gallery", "modula-best-grid-gallery",
        
        // Slider Plugins
        "smart-slider-3", "slider-revolution", "meta-slider", "master-slider", "layerslider",
        "slider-by-supsystic", "easing-slider", "smooth-slider", "cyclone-slider-2", "nivo-slider",
        "royal-slider", "slider-wd", "huge-it-slider", "image-slider-widget", "responsive-slider",
        "flex-slider", "accordion-slider", "touch-slider", "wp-slider-image", "slideshow-gallery",
        
        // Event Management
        "the-events-calendar", "event-organiser", "events-manager", "wp-event-manager", "event-espresso-decaf",
        "modern-events-calendar-lite", "my-calendar", "all-in-one-event-calendar", "eventbrite-api",
        "wp-events-plugin", "events-calendar-pro", "event-tickets", "TicketTailor", "eventbrite-services",
        "sugar-calendar-lite", "simple-calendar", "booking-calendar", "events-made-easy", "church-admin",
        
        // Forum & Community
        "bbpress", "buddypress", "wpforo", "asgaros-forum", "simple-forum", "cm-answers",
        "sabai-discuss", "wp-symposium", "user-pro", "peepso-core", "ultimate-member-forumwp",
        "wpmu-dev-forums", "vanilla-forums", "discourse-wp", "mingleforum", "simple-press",
        "dw-question-answer", "wp-polls", "yop-poll", "poll-maker", "crowdsignal-forms",
        
        // Real Estate
        "easy-property-listings", "wp-property", "realtyna-wpl", "essential-real-estate", "houzez-theme-functionality",
        "real-estate-manager", "wp-residence-add-on", "estatik", "real-homes-theme", "propertypress",
        "agentpress-listings", "dsidxpress", "placester", "real-estate-listing-realtyna-wpl",
        "real-estate-7", "wp-pro-real-estate-7", "easy-real-estate", "real-estate-manager-premium",
        
        // Job Board
        "wp-job-manager", "simple-job-board", "job-manager", "careerfy-job-manager", "jobmonster",
        "wp-jobsearch", "jobhunt", "workscout", "jobster", "superio", "nokri", "jobcareer",
        "wp-resume-manager", "resume-manager", "job-board-manager", "job-portal", "indeed-apply",
        "ziprecruiter-job-search", "careerbuilder-for-employers", "monster-job-search", "glassdoor-job-search",
        
        // Restaurant & Food
        "restaurant-reservations", "five-star-restaurant-reservations", "quick-restaurant-reservations",
        "wp-restaurant-listings", "food-and-drink-menu", "restaurant-menu", "food-store", "gloria-food",
        "wp-pizza", "restaurant-menu-by-pricelisto", "menu-ordering-reservations", "food-online",
        "takeaway-and-delivery", "restaurant-cafe-addon-for-elementor", "menuz", "wp-food-manager",
        
        // Directory & Listings
        "business-directory-plugin", "geodirectory", "sabai-directory", "advanced-classifieds-and-directory-pro",
        "another-wordpress-classifieds-plugin", "wp-business-directory", "connections", "listify",
        "directory-theme", "listingpro", "classified-listing", "wp-business-reviews", "review-wp",
        "site-reviews", "customer-reviews-woocommerce", "wp-product-review", "rich-reviews",
        
        // Booking & Appointments
        "booking", "bookly-responsive-appointment-booking-tool", "easy-appointments", "salon-booking-system",
        "wp-booking-system", "booked", "team-booking", "appointment-booking-calendar", "amelia",
        "simply-schedule-appointments", "bookingpress-appointment-booking", "time-slots-booking-calendar",
        "wp-simple-booking-calendar", "start-booking", "latepoint", "birchpress-scheduler", "booking-ultra-pro",
        
        // Custom Post Types & Fields
        "advanced-custom-fields", "custom-post-type-ui", "pods", "meta-box", "acf-extended",
        "toolset-types", "cmb2", "custom-fields-suite", "ultimate-fields", "carbon-fields",
        "redux-framework", "kirki", "customizer-framework", "wp-customizer-framework", "options-framework",
        "codestar-framework", "vafpress-framework", "option-tree", "easy-theme-and-plugin-upgrades", "custom-post-type-maker",
        
        // Maintenance & Development
        "wp-maintenance-mode", "coming-soon", "under-construction-page", "seedprod-coming-soon-pro",
        "maintenance", "wp-coming-soon-and-maintenance-mode", "ultimate-coming-soon-page", "mm3-coming-soon-maintenance",
        "construction-coming-soon-mode", "wp-under-construction", "maintenace-mode", "simple-maintenance-mode",
        "wp-clone-by-wp-academy", "duplicator-pro", "search-replace-db", "better-search-replace",
        "velvet-blues-update-urls", "wp-migrate-db-pro", "wp-staging-pro", "wp-sync-db",
        
        // Admin & Dashboard
        "admin-menu-editor", "adminimize", "white-label-cms", "wp-admin-ui-customize", "admin-columns-pro",
        "custom-admin-interface", "wp-admin-bar-removal", "remove-wp-meta-tags", "hide-admin-bar",
        "admin-bar-disabler", "wp-hide-post", "post-status-notifier", "admin-post-navigation",
        "bulk-delete", "wp-bulk-delete", "user-role-editor-pro", "capability-manager", "members-directory",
        "wp-show-posts", "custom-post-limits", "simply-show-hooks", "query-monitor-extend",
        
        // Database & Optimization
        "wp-optimize", "wp-sweep", "wp-clean-up", "optimize-database-after-deleting-revisions",
        "wp-db-manager", "adminer", "wp-dbmanager", "advanced-database-cleaner", "wp-reset",
        "clean-options", "wp-security-hardening", "wp-file-permissions", "file-permissions-and-user-roles",
        "database-backup", "wp-database-backup", "myrepono-wordpress-backup", "wp-db-backup-pro",
        "wp-database-reset", "better-delete-revision", "delete-expired-transients", "transients-manager",
        
        // Legacy & Classic Editor
        "classic-editor", "classic-widgets", "disable-gutenberg", "classic-commerce", "gutenberg-ramp",
        "disable-block-editor", "classic-editor-addon", "gutenberg-migration-guide", "block-options",
        "editor-blocks", "reusable-blocks-extended", "block-lab", "lazy-blocks", "custom-post-type-ui",
        "genesis-custom-blocks", "kadence-blocks", "ultimate-addons-for-gutenberg", "stackable-ultimate-gutenberg-blocks",
        
        // WordPress Multisite
        "wordpress-mu-domain-mapping", "multisite-language-switcher", "wp-multi-network", "multisite-clone-duplicator",
        "ns-cloner-site-copier", "multisite-toolbar-additions", "wp-ultimo", "domain-mapping-system",
        "wp-multi-store-locator", "network-shared-media", "multisite-enhancements", "unconfirmed",
        "wp-multisite-sso", "blog-copier", "wp-multisite-feed", "more-privacy-options",
        
        // Popup & Modal
        "popup-maker", "ninja-popups", "popup-by-supsystic", "layered-popups", "convertplug",
        "thrive-leads", "bloom", "icegram", "exit-intent-popup", "popup-anything-on-click",
        "modal-window", "wp-modal-login", "easy-modal", "popup-press", "responsive-lightbox",
        "fancybox-for-wordpress", "wp-colorbox", "simple-lightbox", "nextgen-gallery-plus",
        
        // Cookie & GDPR
        "cookie-notice", "gdpr-cookie-compliance", "cookiebot", "wp-gdpr-compliance", "cookie-law-info",
        "eu-cookie-law", "uk-cookie-consent", "cookiepro", "complianz-gdpr", "wp-consent-api",
        "cookie-banner", "privacy-policy-generator", "wp-privacy-policy", "gdpr-framework",
        "wp-gdpr-core", "termly", "iubenda-cookie-law-solution", "osano-cookie-consent",
        
        // WP-CLI & Developer Tools
        "wp-cli", "debug-bar", "debug-bar-console", "debug-bar-cron", "log-deprecated-notices",
        "developer", "theme-check", "plugin-check", "rtl-tester", "regenerate-thumbnails-advanced",
        "what-the-file", "show-current-template", "simply-show-ids", "reveal-ids-for-wp-admin-25",
        "wp-crontrol", "cron-view", "wp-super-edit", "advanced-wp-columns", "genesis-simple-hooks",
        
        // Custom CSS & JavaScript
        "simple-custom-css", "easy-theme-and-plugin-upgrades", "wp-add-custom-css", "simple-custom-css-and-js",
        "custom-css-js", "wp-custom-css-js", "css-javascript-toolbox", "head-footer-code",
        "insert-headers-and-footers", "scripts-n-styles", "custom-fonts", "easy-google-fonts",
        "wp-google-fonts", "typekit-fonts-for-wordpress", "custom-typekit-fonts", "adobe-fonts",
        
        // Import/Export
        "wordpress-importer", "all-in-one-wp-migration", "duplicator", "wp-all-export", "wp-all-import",
        "ultimate-csv-importer", "import-users-from-csv-with-meta", "user-import-export", "woocommerce-product-csv-import-suite",
        "advanced-export", "export-user-data", "wp-csv-exporter", "simple-import", "cms2cms-automated-migration",
        "fg-drupal-to-wp", "fg-joomla-to-wp", "blogger-to-wordpress-redirection", "tumblr-importer",
        
        // Redirects & URL Management
        "redirection", "simple-301-redirects", "safe-redirect-manager", "quick-page-post-redirect",
        "301-redirects", "eps-redirects", "automatic-domain-changer", "search-replace-db",
        "better-search-replace", "link-checker", "broken-link-checker", "wp-external-links",
        "pretty-link", "short-url", "yourls-wordpress-plugin", "wp-bitly", "simple-urls",
        
        // Schema & Structured Data
        "schema-and-structured-data-for-wp", "wp-seo-structured-data-schema", "schema-app-structured-data",
        "all-in-one-schemaorg-rich-snippets", "wp-product-review", "wp-review", "site-reviews",
        "rich-snippets-wordpress-plugin", "markup-json-ld-structured-data", "schema-premium",
        "wp-structuring-markup", "structured-content", "local-business-seo", "wp-business-reviews",
        
        // Weather & Location
        "awesome-weather", "weather-atlas", "wp-weather", "simple-weather", "weather-station",
        "openweather", "clima", "weather-shortcode", "wp-forecast", "live-weather-station",
        "wp-maps", "wp-google-maps", "mappress-easy-google-maps", "leaflet-maps-marker",
        "wp-store-locator", "store-locator-le", "agile-store-locator", "super-store-finder",
        
        // Testimonials & Reviews
        "strong-testimonials", "testimonial-rotator", "easy-testimonials", "testimonials-widget",
        "customer-testimonials", "testimonials-by-woothemes", "rich-reviews", "site-reviews",
        "wp-customer-reviews", "testimonial-slider", "responsive-testimonials", "testimonials-showcase",
        "ultimate-testimonials", "testimonial-basics", "testimonials-carousel", "review-builder",
        
        // Table Plugins
        "tablepress", "wp-table-builder", "data-tables-generator-by-supsystic", "ninja-tables",
        "wpforms-surveys-polls", "league-table", "posts-table-pro", "wpdatatables", "ultimate-tables",
        "table-maker", "go-pricing", "responsive-pricing-table", "pricing-deals-for-woocommerce",
        "ari-fancy-lightbox", "wp-responsive-table", "easy-pricing-tables", "css3-responsive-web-pricing-tables-grids",
        
        // Audio & Video
        "audio-player", "compact-wp-audio-player", "html5-jquery-audio-player", "mp3-jplayer",
        "podcast-player", "powerpress", "seriously-simple-podcasting", "video-embed-thumbnail-generator",
        "wp-video-lightbox", "video-gallery", "youtube-embed-plus", "vimeo-master", "flowplayer5",
        "wp-responsive-video", "automatic-youtube-video-posts", "embed-plus-for-youtube", "ultimate-video-player",
        
        // Coming Soon & Maintenance
        "coming-soon", "seedprod-coming-soon-pro", "under-construction-page", "wp-maintenance-mode",
        "maintenance", "ultimate-coming-soon-page", "minimal-coming-soon-maintenance-mode", "construction-coming-soon-mode",
        "wp-coming-soon-and-maintenance-mode", "coming-soon-by-supsystic", "coming-soon-master", "launch-countdown",
        "cmp-coming-soon-maintenance", "ez-launch", "ignition-deck-coming-soon", "wp-coming-soon-page",
        
        // Related Posts
        "yet-another-related-posts-plugin", "contextual-related-posts", "related-posts-for-wp", "wp-related-posts",
        "inline-related-posts", "similar-posts", "nrelate-related-content", "jetpack-related-posts",
        "advanced-post-slider", "post-slider-carousel", "content-views-query-and-display-post-page",
        "ultimate-posts-widget", "recent-posts-widget-extended", "custom-post-widget", "display-posts-shortcode"
    };
        private readonly ISocksService _socksService;

        public PluginDetector(
            IHttpClientFactory httpClientFactory,
            ILogger<PluginDetector> logger,
            ISocksService socksService
                            )
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _socksService = socksService;

        }

        public async Task<List<Plugin>> DetectPluginsAsync(string url, DetectionMode mode = DetectionMode.Mixed, CancellationToken cancellationToken = default)
        {
            var plugins = new List<Plugin>();
            var client = await _socksService.GetHttpWithBalancedSocksConnection();

            try
            {
                // Passive detection - analyze homepage content
                if (mode == DetectionMode.Passive || mode == DetectionMode.Mixed)
                {
                    var passivePlugins = await DetectPluginsPassiveAsync(client, url, cancellationToken);
                    plugins.AddRange(passivePlugins);
                }

                // Aggressive detection - check for common plugin files
                if (mode == DetectionMode.Aggressive || mode == DetectionMode.Mixed)
                {
                    var aggressivePlugins = await DetectPluginsAggressiveAsync(client, url, cancellationToken);
                    plugins.AddRange(aggressivePlugins);
                }

                // Remove duplicates
                return plugins.GroupBy(p => p.Name).Select(g => g.First()).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detecting plugins for {Url}", url);
                return new List<Plugin>();
            }
        }

        private async Task<List<Plugin>> DetectPluginsPassiveAsync(HttpClient client, string url, CancellationToken cancellationToken)
        {
            var plugins = new List<Plugin>();

            try
            {
                var response = await client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode) return plugins;

                var content = await response.Content.ReadAsStringAsync(cancellationToken);

                // Look for plugin references in HTML
                var pluginPatterns = new[]
                {
                @"wp-content/plugins/([^/'""\s]+)",
                @"plugins/([^/'""\s]+)/",
                @"plugin-([^/'""\s]+)/"
            };

                foreach (var pattern in pluginPatterns)
                {
                    var matches = Regex.Matches(content, pattern, RegexOptions.IgnoreCase);
                    foreach (Match match in matches)
                    {
                        if (match.Groups.Count > 1)
                        {
                            var pluginName = match.Groups[1].Value;
                            if (!string.IsNullOrEmpty(pluginName) && !plugins.Any(p => p.Name == pluginName))
                            {
                                plugins.Add(new Plugin
                                {
                                    Name = pluginName,
                                    DetectionMethod = "Passive - Homepage Analysis",
                                    Confidence = ConfidenceLevel.Medium,
                                    IsActive = true
                                });
                            }
                        }
                    }
                }

                // Check for generator meta tags
                var generatorMatches = Regex.Matches(content, @"<meta\s+name=['""]generator['""].*?content=['""]([^'""]*)['""]", RegexOptions.IgnoreCase);
                foreach (Match match in generatorMatches)
                {
                    var generator = match.Groups[1].Value;
                    if (generator.Contains("plugin", StringComparison.OrdinalIgnoreCase))
                    {
                        var pluginName = ExtractPluginNameFromGenerator(generator);
                        if (!string.IsNullOrEmpty(pluginName) && !plugins.Any(p => p.Name == pluginName))
                        {
                            plugins.Add(new Plugin
                            {
                                Name = pluginName,
                                DetectionMethod = "Passive - Generator Meta Tag",
                                Confidence = ConfidenceLevel.High,
                                IsActive = true
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in passive plugin detection for {Url}", url);
            }

            return plugins;
        }

        private async Task<List<Plugin>> DetectPluginsAggressiveAsync(HttpClient client, string url, CancellationToken cancellationToken)
        {
            var plugins = new List<Plugin>();

            try
            {
                // Check for popular plugins
                var tasks = _popularPlugins.Select(async plugin =>
                {
                    var pluginUrl = $"{url.TrimEnd('/')}/wp-content/plugins/{plugin}/";
                    try
                    {
                        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, pluginUrl), cancellationToken);
                        if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                        {
                            return new Plugin
                            {
                                Name = plugin,
                                DetectionMethod = "Aggressive - Directory Access",
                                Confidence = response.IsSuccessStatusCode ? ConfidenceLevel.High : ConfidenceLevel.Medium,
                                IsActive = true,
                                Path = $"/wp-content/plugins/{plugin}/"
                            };
                        }
                    }
                    catch
                    {
                        // Ignore individual plugin check failures
                    }
                    return null;
                });

                var results = await Task.WhenAll(tasks);
                plugins.AddRange(results.Where(p => p != null)!);

                // Check for common plugin files
                var commonFiles = new[]
                {
                "wp-content/plugins/akismet/akismet.php",
                "wp-content/plugins/hello.php",
                "wp-admin/admin-ajax.php"
            };

                foreach (var file in commonFiles)
                {
                    try
                    {
                        var fileUrl = $"{url.TrimEnd('/')}/{file}";
                        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, fileUrl), cancellationToken);

                        if (response.IsSuccessStatusCode)
                        {
                            var pluginName = ExtractPluginNameFromPath(file);
                            if (!string.IsNullOrEmpty(pluginName) && !plugins.Any(p => p.Name == pluginName))
                            {
                                plugins.Add(new Plugin
                                {
                                    Name = pluginName,
                                    DetectionMethod = "Aggressive - File Access",
                                    Confidence = ConfidenceLevel.High,
                                    IsActive = true,
                                    Path = file
                                });
                            }
                        }
                    }
                    catch
                    {
                        // Ignore individual file check failures
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in aggressive plugin detection for {Url}", url);
            }

            return plugins;
        }

        private string ExtractPluginNameFromGenerator(string generator)
        {
            // Extract plugin name from generator string
            var match = Regex.Match(generator, @"(\w+)\s+plugin", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value.ToLower() : string.Empty;
        }

        private string ExtractPluginNameFromPath(string path)
        {
            // Extract plugin name from file path
            var match = Regex.Match(path, @"plugins/([^/]+)/", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }
    }

}
