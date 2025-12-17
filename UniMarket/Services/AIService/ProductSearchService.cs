using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using UniMarket.DataAccess;
using UniMarket.DTO;
using UniMarket.Models;

namespace UniMarket.Services
{
    /// <summary>
    /// ProductSearchService: Chuyên trách tìm kiếm sản phẩm trên SQL Server.
    /// - Áp dụng các filter (Category, Keywords, Price, Video)
    /// - Sắp xếp sản phẩm
    /// - Map sang DTO
    /// </summary>
    public class ProductSearchService
    {
        private readonly ApplicationDbContext _context;
        private readonly TinDangDetailService _mongoService;
        private readonly ILogger<ProductSearchService> _logger;

        public ProductSearchService(ApplicationDbContext context, TinDangDetailService mongoService, ILogger<ProductSearchService> logger)
        {
            _context = context;
            _mongoService = mongoService;
            _logger = logger;
        }

        /// <summary>
        /// Tìm kiếm sản phẩm dựa trên criteria từ AiIntentResult.
        /// </summary>
        public async Task<(List<ProductSuggestionDto> Products, int TotalCount)> SearchAsync(AiIntentResult criteria)
        {
            _logger.LogInformation("[ProductSearch] Starting search with criteria: Category={catId}, Keywords={kw}, MinPrice={minP}, MaxPrice={maxP}", 
                criteria.CategoryId, string.Join(",", criteria.Keywords ?? Array.Empty<string>()), criteria.MinPrice, criteria.MaxPrice);

            var query = _context.TinDangs.AsNoTracking()
                .Include(p => p.AnhTinDangs)
                .Where(p => p.TrangThai == TrangThaiTinDang.DaDuyet);

            // 1. Category Filter
            if (criteria.CategoryId.HasValue)
            {
                var childCategoryIds = await _context.DanhMucs
                    .AsNoTracking()
                    .Where(c => c.MaDanhMucCha == criteria.CategoryId.Value)
                    .Select(c => c.MaDanhMuc)
                    .ToListAsync();
                
                var categoryConstraint = childCategoryIds.Count > 0 ? childCategoryIds : new List<int> { criteria.CategoryId.Value };
                query = query.Where(p => categoryConstraint.Contains(p.MaDanhMuc));
                _logger.LogInformation("[ProductSearch] Category filter applied: {count} categories", categoryConstraint.Count);
            }

            // 2. Keywords Filter
            if (criteria.Keywords != null && criteria.Keywords.Length > 0)
            {
                var allKeywords = criteria.Keywords
                    .SelectMany(k => k.Split(new[] { ' ', '-', '/' }, StringSplitOptions.RemoveEmptyEntries))
                    .Where(k => !string.IsNullOrWhiteSpace(k) && k.Trim().Length >= 2)
                    .Select(k => k.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                
                _logger.LogInformation("[ProductSearch] Keywords after split: {keywords}", string.Join(", ", allKeywords));
                
                if (allKeywords.Length > 0)
                {
                    // Strict: ALL keywords must match (AND logic)
                    var strictQuery = query;
                    foreach (var kw in allKeywords)
                    {
                        var param = Expression.Parameter(typeof(TinDang), "p");
                        var patternLower = Expression.Constant("%" + kw.ToLower() + "%");
                        var titleProp = Expression.PropertyOrField(param, nameof(TinDang.TieuDe));
                        var moTaProp = Expression.PropertyOrField(param, nameof(TinDang.MoTa));

                        var titleLower = Expression.Call(titleProp, typeof(string).GetMethod("ToLower", Type.EmptyTypes)!);
                        var moTaLower = Expression.Call(moTaProp, typeof(string).GetMethod("ToLower", Type.EmptyTypes)!);

                        var efFunctionsProperty = typeof(EF).GetProperty("Functions", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        var efFunctionsExpr = Expression.Property(null, efFunctionsProperty!);
                        var likeMethod = typeof(DbFunctionsExtensions).GetMethod("Like", new[] { typeof(DbFunctions), typeof(string), typeof(string) });

                        var titleLike = Expression.Call(likeMethod!, efFunctionsExpr, titleLower, patternLower);
                        var moTaLike = Expression.Call(likeMethod!, efFunctionsExpr, moTaLower, patternLower);

                        var orKeyword = Expression.OrElse(titleLike, moTaLike);
                        var lambda = Expression.Lambda<Func<TinDang, bool>>(orKeyword, param);
                        
                        strictQuery = strictQuery.Where(lambda);
                    }
                    
                    var strictCount = await strictQuery.CountAsync();
                    _logger.LogInformation("[ProductSearch] Strict query (ALL keywords) returned {count} products", strictCount);
                    
                    if (strictCount > 0)
                    {
                        query = strictQuery;
                    }
                    else if (allKeywords.Length > 1)
                    {
                        // ✅ FALLBACK 1: Nếu không tìm thấy với tất cả keywords, tìm theo bất kỳ keyword nào (OR logic)
                        _logger.LogWarning("[ProductSearch] No strict matches, falling back to flexible search (ANY keyword)");
                        
                        var flexibleParam = Expression.Parameter(typeof(TinDang), "p");
                        Expression? combinedOr = null;

                        var efFunctionsProperty = typeof(EF).GetProperty("Functions", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        var efFunctionsExpr = Expression.Property(null, efFunctionsProperty!);
                        var likeMethod = typeof(DbFunctionsExtensions).GetMethod("Like", new[] { typeof(DbFunctions), typeof(string), typeof(string) });

                        foreach (var kw in allKeywords.Where(k => k.Length >= 3))
                        {
                            var patternFlexible = Expression.Constant("%" + kw.ToLower() + "%");
                            var titleProp = Expression.PropertyOrField(flexibleParam, nameof(TinDang.TieuDe));
                            var titleLowerFlexible = Expression.Call(titleProp, typeof(string).GetMethod("ToLower", Type.EmptyTypes)!);
                            var titleLike = Expression.Call(likeMethod!, efFunctionsExpr, titleLowerFlexible, patternFlexible);

                            combinedOr = combinedOr == null ? titleLike : Expression.OrElse(combinedOr, titleLike);
                        }

                        if (combinedOr != null)
                        {
                            var flexibleLambda = Expression.Lambda<Func<TinDang, bool>>(combinedOr, flexibleParam);
                            query = query.Where(flexibleLambda);
                            _logger.LogInformation("[ProductSearch] Applied flexible search with OR logic");
                        }
                        
                        // ✅ FALLBACK 2: Nếu flexible search CŨNG không tìm thấy, nhưng có CategoryId, chỉ trả về category (bỏ qua keywords)
                        var flexibleCount = await query.CountAsync();
                        if (flexibleCount == 0 && criteria.CategoryId.HasValue)
                        {
                            _logger.LogWarning("[ProductSearch] ⚠️ Flexible search also returned 0 products. Since CategoryId is set, returning products from category only (ignoring keywords)");
                            
                            // Reset query - chỉ giữ category filter, bỏ keywords
                            query = _context.TinDangs.AsNoTracking()
                                .Include(p => p.AnhTinDangs)
                                .Where(p => p.TrangThai == TrangThaiTinDang.DaDuyet);
                            
                            var categoryChildrenIds = await _context.DanhMucs
                                .AsNoTracking()
                                .Where(c => c.MaDanhMucCha == criteria.CategoryId.Value)
                                .Select(c => c.MaDanhMuc)
                                .ToListAsync();
                            
                            var categoryConstraint = categoryChildrenIds.Count > 0 ? categoryChildrenIds : new List<int> { criteria.CategoryId.Value };
                            query = query.Where(p => categoryConstraint.Contains(p.MaDanhMuc));
                            
                            _logger.LogInformation("[ProductSearch] ✅ Category-only query applied: {count} categories", categoryConstraint.Count);
                        }
                    }
                    else if (allKeywords.Length == 1)
                    {
                        // ✅ Single keyword: tính cả strict count
                        var singleKeywordCount = await query.CountAsync();
                        if (singleKeywordCount == 0 && criteria.CategoryId.HasValue)
                        {
                            _logger.LogWarning("[ProductSearch] ⚠️ Single keyword returned 0 products. Since CategoryId is set, returning products from category only");
                            
                            query = _context.TinDangs.AsNoTracking()
                                .Include(p => p.AnhTinDangs)
                                .Where(p => p.TrangThai == TrangThaiTinDang.DaDuyet);
                            
                            var categoryChildrenIds = await _context.DanhMucs
                                .AsNoTracking()
                                .Where(c => c.MaDanhMucCha == criteria.CategoryId.Value)
                                .Select(c => c.MaDanhMuc)
                                .ToListAsync();
                            
                            var categoryConstraint = categoryChildrenIds.Count > 0 ? categoryChildrenIds : new List<int> { criteria.CategoryId.Value };
                            query = query.Where(p => categoryConstraint.Contains(p.MaDanhMuc));
                            
                            _logger.LogInformation("[ProductSearch] ✅ Category-only query applied: {count} categories", categoryConstraint.Count);
                        }
                    }
                }
            }

            // 3. Price Filter
            if (criteria.MinPrice.HasValue)
            {
                query = query.Where(p => p.Gia >= criteria.MinPrice.Value);
                _logger.LogInformation("[ProductSearch] Min price filter: {minPrice}", criteria.MinPrice.Value);
            }
            if (criteria.MaxPrice.HasValue)
            {
                query = query.Where(p => p.Gia <= criteria.MaxPrice.Value);
                _logger.LogInformation("[ProductSearch] Max price filter: {maxPrice}", criteria.MaxPrice.Value);
            }

            // 4. Video Filter
            if (criteria.RequireVideo)
            {
                query = query.Where(p => !string.IsNullOrEmpty(p.VideoUrl));
                _logger.LogInformation("[ProductSearch] Video filter applied");
            }

            // 5. Condition Filter
            if (!string.IsNullOrEmpty(criteria.Condition))
            {
                query = query.Where(p => p.TinhTrang == criteria.Condition);
                _logger.LogInformation("[ProductSearch] Condition filter: {condition}", criteria.Condition);
            }

            // 6. Hot Filter
            if (criteria.FilterByHot)
            {
                query = query.Where(p => p.TinDangYeuThichs != null && p.TinDangYeuThichs.Count >= 2);
                _logger.LogInformation("[ProductSearch] Hot filter applied (likes >= 2)");
            }

            // 7. Sort
            // ✅ AUTO-DETECT: Nếu keywords chứa "giá rẻ" hoặc "rẻ", sort theo giá từ thấp đến cao
            var keywordLower = string.Join(" ", criteria.Keywords ?? Array.Empty<string>()).ToLower();
            var shouldSortByPrice = keywordLower.Contains("giá rẻ") || 
                                   keywordLower.Contains("rẻ") || 
                                   keywordLower.Contains("cheap") ||
                                   keywordLower.Contains("giá thấp");
            
            query = (criteria.SortBy?.ToLower()) switch 
            {
                "price_asc" => query.OrderBy(p => p.Gia),
                "price_desc" => query.OrderByDescending(p => p.Gia),
                "views_desc" => query.OrderByDescending(p => p.SoLuotXem),
                _ => shouldSortByPrice 
                    ? query.OrderBy(p => p.Gia)  // ✅ Giá từ thấp đến cao nếu user tìm "giá rẻ"
                    : query.OrderByDescending(p => p.NgayDang)  // Sort by newest posts (default)
            };
            _logger.LogInformation("[ProductSearch] Sort applied: {sort} (auto-detect rẻ: {detectPrice})", 
                criteria.SortBy ?? "recent", shouldSortByPrice);

            // 8. Fetch SQL
            int limit = criteria.Limit.HasValue && criteria.Limit > 0 ? criteria.Limit.Value : 12;
            int total = await query.CountAsync();
            // Lấy nhiều hơn limit một chút để trừ hao những cái bị lọc bởi MongoDB
            var fetchedProducts = await query.Take(limit + 5).ToListAsync();
            _logger.LogInformation("[ProductSearch] Fetched {count}/{total} products from SQL", fetchedProducts.Count, total);

            // --- 9. MONGODB FILTERING (BỔ SUNG) ---
            var mongoSpecsCache = new Dictionary<int, ProductSpecDTO>();
            bool hasSpecFilters = !string.IsNullOrEmpty(criteria.Brand) || !string.IsNullOrEmpty(criteria.Color) || 
                                  !string.IsNullOrEmpty(criteria.Storage) || !string.IsNullOrEmpty(criteria.Condition) ||
                                  !string.IsNullOrEmpty(criteria.Origin) || !string.IsNullOrEmpty(criteria.Warranty);

            if (hasSpecFilters && fetchedProducts.Count > 0)
            {
                _logger.LogInformation("[ProductSearch] 🔍 Batch-fetching MongoDB specs for {count} products...", fetchedProducts.Count);
                var productIds = fetchedProducts.Select(p => p.MaTinDang).ToList();
                
                try
                {
                    // Batch fetch all product specs
                    foreach(var pid in productIds) 
                    {
                        try
                        {
                            var detail = await _mongoService.GetByMaTinDangAsync(pid);
                            if(detail?.ChiTiet != null) 
                            {
                                // Convert BsonDocument sang DTO
                                var json = detail.ChiTiet.ToJson(new MongoDB.Bson.IO.JsonWriterSettings { OutputMode = MongoDB.Bson.IO.JsonOutputMode.RelaxedExtendedJson });
                                var specs = System.Text.Json.JsonSerializer.Deserialize<ProductSpecDTO>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                                if(specs != null) mongoSpecsCache[pid] = specs;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug("[ProductSearch] Could not fetch MongoDB specs for product {id}: {msg}", pid, ex.Message);
                        }
                    }
                    _logger.LogInformation("[ProductSearch] ✅ Batch fetch complete: {count} products with specs found", mongoSpecsCache.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError("[ProductSearch] ❌ MongoDB batch fetch failed: {msg} - Continuing without MongoDB filters", ex.Message);
                    mongoSpecsCache.Clear();
                }

                // Lọc bộ nhớ (In-memory filter)
                if (mongoSpecsCache.Count > 0)
                {
                    _logger.LogInformation("[ProductSearch] 🔍 Filtering by specs: Brand={brand}, Color={color}, Storage={storage}, Warranty={warranty}, Origin={origin}, Condition={condition}", 
                        criteria.Brand ?? "null", criteria.Color ?? "null", criteria.Storage ?? "null", criteria.Warranty ?? "null", criteria.Origin ?? "null", criteria.Condition ?? "null");
                    
                    var filtered = new List<TinDang>();
                    foreach (var p in fetchedProducts)
                    {
                        // Skip nếu không có specs
                        if (!mongoSpecsCache.TryGetValue(p.MaTinDang, out var s))
                        {
                            _logger.LogDebug("[ProductSearch] ⚠️ Product {id} '{title}' has no specs - SKIPPED", p.MaTinDang, p.TieuDe);
                            continue;
                        }

                        // Check nếu match tất cả filters
                        bool matchesSpecs = true;
                        
                        if (!string.IsNullOrEmpty(criteria.Brand) && 
                            (string.IsNullOrEmpty(s.Hang) || !s.Hang.Contains(criteria.Brand, StringComparison.OrdinalIgnoreCase)))
                        {
                            matchesSpecs = false;
                            _logger.LogDebug("[ProductSearch] ❌ Product {id} filtered out: Brand '{actual}' doesn't match '{expected}'", p.MaTinDang, s.Hang ?? "null", criteria.Brand);
                        }
                        
                        if (!string.IsNullOrEmpty(criteria.Color) && 
                            (string.IsNullOrEmpty(s.MauSac) || !s.MauSac.Contains(criteria.Color, StringComparison.OrdinalIgnoreCase)))
                        {
                            matchesSpecs = false;
                            _logger.LogDebug("[ProductSearch] ❌ Product {id} filtered out: Color '{actual}' doesn't match '{expected}'", p.MaTinDang, s.MauSac ?? "null", criteria.Color);
                        }
                        
                        if (!string.IsNullOrEmpty(criteria.Storage) && 
                            (string.IsNullOrEmpty(s.DungLuong) || !s.DungLuong.Contains(criteria.Storage, StringComparison.OrdinalIgnoreCase)))
                        {
                            matchesSpecs = false;
                            _logger.LogDebug("[ProductSearch] ❌ Product {id} filtered out: Storage '{actual}' doesn't match '{expected}'", p.MaTinDang, s.DungLuong ?? "null", criteria.Storage);
                        }
                        
                        if (!string.IsNullOrEmpty(criteria.Warranty) && 
                            (string.IsNullOrEmpty(s.BaoHanh) || !s.BaoHanh.Contains(criteria.Warranty, StringComparison.OrdinalIgnoreCase)))
                        {
                            matchesSpecs = false;
                            _logger.LogDebug("[ProductSearch] ❌ Product {id} filtered out: Warranty '{actual}' doesn't match '{expected}'", p.MaTinDang, s.BaoHanh ?? "null", criteria.Warranty);
                        }
                        
                        if (!string.IsNullOrEmpty(criteria.Origin) && 
                            (string.IsNullOrEmpty(s.XuatXu) || !s.XuatXu.Contains(criteria.Origin, StringComparison.OrdinalIgnoreCase)))
                        {
                            matchesSpecs = false;
                            _logger.LogDebug("[ProductSearch] ❌ Product {id} filtered out: Origin '{actual}' doesn't match '{expected}'", p.MaTinDang, s.XuatXu ?? "null", criteria.Origin);
                        }
                        
                        if (matchesSpecs)
                        {
                            filtered.Add(p);
                            _logger.LogDebug("[ProductSearch] ✅ Product {id} '{title}' PASSED all filters", p.MaTinDang, p.TieuDe);
                        }
                    }
                    
                    fetchedProducts = filtered.Take(limit).ToList();
                    _logger.LogInformation("[ProductSearch] After MongoDB filtering: {count} products match", fetchedProducts.Count);
                }
                else
                {
                    _logger.LogWarning("[ProductSearch] ⚠️ Spec filters requested but no MongoDB specs available - returning unfiltered results");
                }
            }
            else if (hasSpecFilters)
            {
                _logger.LogWarning("[ProductSearch] ⚠️ Spec filters requested but no products to filter - returning empty");
            }
            else
            {
                _logger.LogInformation("[ProductSearch] No MongoDB filtering needed (no specs requested)");
            }
            // -----------------------------------------------

            // 10. Map to DTO
            var dtos = fetchedProducts
                .Where(p => !string.IsNullOrEmpty(p?.TieuDe))
                .Select(p => new ProductSuggestionDto
                {
                    Id = p.MaTinDang,
                    Ten = p.TieuDe ?? "Sản phẩm không tên",
                    Gia = p.Gia,
                    AnhDaiDien = p.AnhTinDangs != null && p.AnhTinDangs.Count > 0
                        ? (p.AnhTinDangs.FirstOrDefault()?.DuongDan?.StartsWith("http", StringComparison.OrdinalIgnoreCase) ?? false
                            ? p.AnhTinDangs.First().DuongDan
                            : (p.AnhTinDangs.First().DuongDan?.StartsWith("/") ?? false
                                ? p.AnhTinDangs.First().DuongDan
                                : $"/images/Posts/{p.AnhTinDangs.First().DuongDan}"))
                        : null,
                    TinhTrang = p.TinhTrang ?? "Không xác định",
                    LinkVideo = p.VideoUrl ?? "",
                    SoLuotXem = p.SoLuotXem,
                    SoLike = p.TinDangYeuThichs?.Count ?? 0,
                    MaNguoiBan = p.MaNguoiBan ?? "",
                    IsHot = (p.TinDangYeuThichs?.Count ?? 0) >= 2
                }).ToList();

            _logger.LogInformation("[ProductSearch] ✅ Mapped {count} products to DTO", dtos.Count);
            return (dtos, total);
        }
    }
}
