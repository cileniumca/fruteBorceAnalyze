using AnalyzeDomains.Domain.Enums;
using AnalyzeDomains.Domain.Interfaces.Analyzers;
using AnalyzeDomains.Domain.Interfaces.Services;
using AnalyzeDomains.Domain.Models.AnalyzeModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace AnalyzeDomains.Infrastructure.Analyzers
{
    public class ThemeDetector : IThemeDetector
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ThemeDetector> _logger;
        private readonly ISocksService _socksService;


        private readonly List<string> _popularThemes = new()
{
    // Default WordPress Themes
    "twentytwentyfour", "twentytwentythree", "twentytwentytwo", "twentytwentyone",
    "twentytwenty", "twentynineteen", "twentyeighteen", "twentyseventeen",
    "twentysixteen", "twentyfifteen", "twentyfourteen", "twentythirteen",
    "twentytwelve", "twentyeleven", "twentyten",

    // Most Popular Free Themes
    "astra", "oceanwp", "generatepress", "neve", "hello-elementor",
    "storefront", "kadence", "blocksy", "customify", "hestia",
    "zakra", "colormag", "newsup", "asgaros", "bento",
    "sydney", "spacious", "flash", "llorix-one-lite", "zerif-lite",
    "onepress", "mesmerize", "shapely", "virtue", "interface",
    "accelerate", "enigma", "futurio", "cenote", "bizberg",

    // Business & Corporate Themes
    "divi", "avada", "the7", "x", "betheme", "enfold", "jupiter",
    "salient", "bridge", "total", "uncode", "porto", "flatsome",
    "newspaper", "soledad", "kalium", "woodmart", "rey", "sahifa",
    "vantage", "hueman", "businessx", "creativo", "corporate-plus",
    "enigma", "accelerate", "futurio", "cenote", "bizberg",
    "business-consulting-wp", "corp-biz", "business-way", "spacious",
    "interface", "ample", "flash", "llorix-one-lite", "zerif-lite",

    // E-commerce Themes
    "storefront", "flatsome", "porto", "woodmart", "astra-child",
    "oceanwp-child", "generatepress-child", "neve-child", "kadence-child",
    "shopisle", "shop-isle", "mystile", "botiga", "woostify",
    "ecommerce-gem", "shopay", "shopping-cart", "online-shop",
    "mega-shop", "shop-town", "big-store", "shoptimizer", "retailer",
    "electro", "mercado", "digital-download", "accessories-shop",
    "pet-store", "book-store", "furniture-store", "jewelry-store",

    // Blog & Magazine Themes
    "newspaper", "soledad", "sahifa", "jnews", "publisher", "schema",
    "gridlove", "voice", "colormag", "newsup", "newscard", "newspaper-x",
    "editorial", "mag", "startblog", "blog-way", "flash-blog",
    "blog-diary", "clean-blog", "simple-blog", "minimal-blog",
    "magazine-blog", "news-portal", "news-magazine", "daily-news",
    "blogging", "writer", "author", "journalist", "press",
    "herald", "tribune", "gazette", "chronicle", "bulletin",

    // Portfolio & Creative Themes
    "uncode", "salient", "kalium", "portfolio", "oshine", "h-code",
    "creativex", "jevelin", "impreza", "stockholm", "authentic",
    "photography", "photographer", "photo-gallery", "creative-portfolio",
    "artist", "designer", "agency", "studio", "freelancer",
    "minimal-portfolio", "clean-portfolio", "modern-portfolio",
    "dark-portfolio", "light-portfolio", "responsive-portfolio",
    "bootstrap-portfolio", "jquery-portfolio", "css3-portfolio",

    // Restaurant & Food Themes
    "restaurantpress", "food-restaurant", "restaurant", "resto",
    "foodie", "chef", "bistro", "cafe", "diner", "eatery",
    "cuisine", "culinary", "bakery", "pizzeria", "barbecue",
    "wine-bar", "pub", "brewery", "cocktail", "catering",
    "food-delivery", "recipe", "cookbook", "food-blog", "cooking",
    "restaurant-menu", "dining", "gastronomy", "gourmet", "tasty",

    // Health & Medical Themes
    "medica", "health-center", "medical", "clinic", "hospital",
    "doctor", "dentist", "pharmacy", "healthcare", "wellness",
    "fitness", "gym", "yoga", "spa", "beauty", "salon",
    "massage", "therapy", "physiotherapy", "veterinary", "vet",
    "mental-health", "nutrition", "diet", "organic", "natural",
    "holistic", "alternative-medicine", "chiropractic", "acupuncture",

    // Education & Learning Themes
    "education", "university", "college", "school", "academy",
    "learning", "online-course", "elearning", "tutor", "teacher",
    "student", "campus", "kindergarten", "preschool", "elementary",
    "high-school", "training", "coaching", "workshop", "seminar",
    "conference", "research", "library", "bookstore", "knowledge",
    "skill", "certification", "diploma", "degree", "graduation",

    // Real Estate Themes
    "houzez", "real-estate", "property", "realtor", "realty",
    "homes", "apartments", "condos", "villas", "commercial",
    "residential", "rent", "buy", "sell", "lease",
    "mortgage", "investment", "development", "construction", "architect",
    "interior-design", "home-decor", "furniture", "renovation", "remodeling",

    // Travel & Tourism Themes
    "travelify", "travel", "tourism", "vacation", "holiday",
    "destination", "tour", "booking", "hotel", "resort",
    "hostel", "bed-breakfast", "adventure", "expedition", "journey",
    "explore", "discover", "wanderlust", "backpacker", "cruise",
    "flight", "airline", "transportation", "guide", "itinerary",

    // Non-Profit & Charity Themes
    "charity", "non-profit", "donation", "fundraising", "cause",
    "volunteer", "community", "social", "humanitarian", "foundation",
    "organization", "campaign", "awareness", "support", "help",
    "care", "rescue", "shelter", "environment", "conservation",
    "wildlife", "green", "eco", "sustainable", "renewable",

    // Event & Wedding Themes
    "wedding", "event", "celebration", "party", "ceremony",
    "reception", "bridal", "groom", "engagement", "anniversary",
    "birthday", "conference", "seminar", "workshop", "meeting",
    "convention", "exhibition", "festival", "concert", "show",
    "performance", "theater", "music", "entertainment", "venue",

    // Fashion & Lifestyle Themes
    "fashion", "style", "boutique", "clothing", "apparel",
    "accessories", "jewelry", "shoes", "bags", "cosmetics",
    "beauty", "makeup", "skincare", "lifestyle", "luxury",
    "trendy", "chic", "elegant", "modern", "vintage",
    "retro", "classic", "contemporary", "designer", "brand",

    // Technology & Software Themes
    "technology", "software", "app", "startup", "saas",
    "tech", "digital", "innovation", "development", "programming",
    "coding", "web-design", "mobile", "responsive", "bootstrap",
    "material-design", "flat-design", "minimal", "clean", "modern",
    "futuristic", "cyber", "data", "analytics", "ai",

    // Music & Entertainment Themes
    "music", "band", "musician", "artist", "singer",
    "songwriter", "composer", "producer", "studio", "record",
    "album", "concert", "tour", "festival", "venue",
    "entertainment", "media", "radio", "podcast", "streaming",
    "video", "film", "movie", "cinema", "theater",

    // Sports & Fitness Themes
    "sports", "fitness", "gym", "workout", "training",
    "athlete", "coach", "team", "club", "league",
    "tournament", "championship", "competition", "game", "match",
    "yoga", "pilates", "crossfit", "bodybuilding", "cardio",
    "martial-arts", "boxing", "swimming", "running", "cycling",

    // Multipurpose & Business Themes
    "divi", "avada", "the7", "x", "betheme", "enfold",
    "jupiter", "salient", "bridge", "total", "uncode", "porto",
    "kalium", "h-code", "oshine", "jevelin", "impreza", "stockholm",
    "authentic", "creativex", "brooklyn", "manhattan", "california",
    "florida", "texas", "nevada", "arizona", "colorado"
};

        public ThemeDetector(IHttpClientFactory httpClientFactory,
                            ILogger<ThemeDetector> logger,
                            ISocksService socksService
                            )
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _socksService = socksService;
        }

        public async Task<List<Theme>> DetectThemesAsync(string url, DetectionMode mode = DetectionMode.Mixed, CancellationToken cancellationToken = default)
        {
            var themes = new List<Theme>();
            var client = _httpClientFactory.CreateClient();

            try
            {
                // Always try to detect active theme first
                var activeTheme = await DetectActiveThemeAsync(url, cancellationToken);
                if (activeTheme != null)
                {
                    themes.Add(activeTheme);
                }

                // Passive detection - analyze homepage content
                if (mode == DetectionMode.Passive || mode == DetectionMode.Mixed)
                {
                    var passiveThemes = await DetectThemesPassiveAsync(client, url, cancellationToken);
                    themes.AddRange(passiveThemes);
                }

                // Aggressive detection - check for theme directories
                if (mode == DetectionMode.Aggressive || mode == DetectionMode.Mixed)
                {
                    var aggressiveThemes = await DetectThemesAggressiveAsync(client, url, cancellationToken);
                    themes.AddRange(aggressiveThemes);
                }

                // Remove duplicates
                return themes.GroupBy(t => t.Name).Select(g => g.First()).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detecting themes for {Url}", url);
                return new List<Theme>();
            }
        }

        public async Task<Theme?> DetectActiveThemeAsync(string url, CancellationToken cancellationToken = default)
        {
            var client = await _socksService.GetHttpWithBalancedSocksConnection();

            try
            {
                var response = await client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode) return null;

                var content = await response.Content.ReadAsStringAsync(cancellationToken);

                // Look for theme stylesheet references
                var stylesheetPattern = @"wp-content/themes/([^/'""\s]+)/style\.css";
                var match = Regex.Match(content, stylesheetPattern, RegexOptions.IgnoreCase);

                if (match.Success)
                {
                    var themeName = match.Groups[1].Value;
                    return new Theme
                    {
                        Name = themeName,
                        IsActive = true,
                        DetectionMethod = "Active Theme - Stylesheet Reference",
                        Confidence = ConfidenceLevel.High,
                        Path = $"/wp-content/themes/{themeName}/"
                    };
                }

                // Look for theme template references
                var templatePattern = @"wp-content/themes/([^/'""\s]+)/";
                var templateMatch = Regex.Match(content, templatePattern, RegexOptions.IgnoreCase);

                if (templateMatch.Success)
                {
                    var themeName = templateMatch.Groups[1].Value;
                    return new Theme
                    {
                        Name = themeName,
                        IsActive = true,
                        DetectionMethod = "Active Theme - Template Reference",
                        Confidence = ConfidenceLevel.Medium,
                        Path = $"/wp-content/themes/{themeName}/"
                    };
                }

                // Check HTML comments for theme information
                var commentPattern = @"<!--.*?theme.*?([a-zA-Z0-9\-_]+).*?-->|<!--.*?([a-zA-Z0-9\-_]+).*?theme.*?-->";
                var commentMatches = Regex.Matches(content, commentPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);

                foreach (Match commentMatch in commentMatches)
                {
                    var themeName = commentMatch.Groups[1].Success ? commentMatch.Groups[1].Value : commentMatch.Groups[2].Value;
                    if (!string.IsNullOrEmpty(themeName) && themeName.Length > 3)
                    {
                        return new Theme
                        {
                            Name = themeName.ToLower(),
                            IsActive = true,
                            DetectionMethod = "Active Theme - HTML Comment",
                            Confidence = ConfidenceLevel.Low,
                            Path = $"/wp-content/themes/{themeName.ToLower()}/"
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detecting active theme for {Url}", url);
            }

            return null;
        }

        private async Task<List<Theme>> DetectThemesPassiveAsync(HttpClient client, string url, CancellationToken cancellationToken)
        {
            var themes = new List<Theme>();

            try
            {
                var response = await client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode) return themes;

                var content = await response.Content.ReadAsStringAsync(cancellationToken);

                // Look for theme references in HTML
                var themePatterns = new[]
                {
                @"wp-content/themes/([^/'""\s]+)",
                @"themes/([^/'""\s]+)/",
                @"template-([^/'""\s]+)/"
            };

                foreach (var pattern in themePatterns)
                {
                    var matches = Regex.Matches(content, pattern, RegexOptions.IgnoreCase);
                    foreach (Match match in matches)
                    {
                        if (match.Groups.Count > 1)
                        {
                            var themeName = match.Groups[1].Value;
                            if (!string.IsNullOrEmpty(themeName) && !themes.Any(t => t.Name == themeName))
                            {
                                themes.Add(new Theme
                                {
                                    Name = themeName,
                                    DetectionMethod = "Passive - Homepage Analysis",
                                    Confidence = ConfidenceLevel.Medium,
                                    IsActive = false,
                                    Path = $"/wp-content/themes/{themeName}/"
                                });
                            }
                        }
                    }
                }

                // Check for theme-specific CSS classes or IDs
                var themeIdentifiers = new Dictionary<string, string>
                {
                    ["twenty"] = "twentytwenty",
                    ["astra"] = "astra",
                    ["oceanwp"] = "oceanwp",
                    ["generatepress"] = "generatepress",
                    ["neve"] = "neve",
                    ["hello-elementor"] = "hello-elementor"
                };

                foreach (var identifier in themeIdentifiers)
                {
                    if (content.Contains(identifier.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!themes.Any(t => t.Name == identifier.Value))
                        {
                            themes.Add(new Theme
                            {
                                Name = identifier.Value,
                                DetectionMethod = "Passive - CSS Class/ID Analysis",
                                Confidence = ConfidenceLevel.Low,
                                IsActive = false,
                                Path = $"/wp-content/themes/{identifier.Value}/"
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in passive theme detection for {Url}", url);
            }

            return themes;
        }

        private async Task<List<Theme>> DetectThemesAggressiveAsync(HttpClient client, string url, CancellationToken cancellationToken)
        {
            var themes = new List<Theme>();

            try
            {
                // Check for popular themes
                var tasks = _popularThemes.Select(async theme =>
                {
                    var themeUrl = $"{url.TrimEnd('/')}/wp-content/themes/{theme}/";
                    try
                    {
                        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, themeUrl), cancellationToken);
                        if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                        {
                            return new Theme
                            {
                                Name = theme,
                                DetectionMethod = "Aggressive - Directory Access",
                                Confidence = response.IsSuccessStatusCode ? ConfidenceLevel.High : ConfidenceLevel.Medium,
                                IsActive = false,
                                Path = $"/wp-content/themes/{theme}/"
                            };
                        }
                    }
                    catch
                    {
                        // Ignore individual theme check failures
                    }
                    return null;
                });

                var results = await Task.WhenAll(tasks);
                themes.AddRange(results.Where(t => t != null)!);

                // Check for common theme files
                var commonFiles = new[]
                {
                "wp-content/themes/twentytwentythree/style.css",
                "wp-content/themes/twentytwentytwo/style.css",
                "wp-content/themes/twentytwentyone/style.css"
            };

                foreach (var file in commonFiles)
                {
                    try
                    {
                        var fileUrl = $"{url.TrimEnd('/')}/{file}";
                        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, fileUrl), cancellationToken);

                        if (response.IsSuccessStatusCode)
                        {
                            var themeName = ExtractThemeNameFromPath(file);
                            if (!string.IsNullOrEmpty(themeName) && !themes.Any(t => t.Name == themeName))
                            {
                                themes.Add(new Theme
                                {
                                    Name = themeName,
                                    DetectionMethod = "Aggressive - File Access",
                                    Confidence = ConfidenceLevel.High,
                                    IsActive = false,
                                    Path = $"/wp-content/themes/{themeName}/"
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
                _logger.LogError(ex, "Error in aggressive theme detection for {Url}", url);
            }

            return themes;
        }

        private string ExtractThemeNameFromPath(string path)
        {
            // Extract theme name from file path
            var match = Regex.Match(path, @"themes/([^/]+)/", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }
    }

}
