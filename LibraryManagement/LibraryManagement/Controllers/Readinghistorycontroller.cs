// Controllers/ReadingHistoryController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LibraryManagement.Data;
using LibraryManagement.Models;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace LibraryManagement.Controllers
{
    [Authorize]
    public class ReadingHistoryController : Controller
    {
        private readonly LibraryDbContext _context;

        public ReadingHistoryController(LibraryDbContext context)
        {
            _context = context;
        }

        // GET: ReadingHistory
        public async Task<IActionResult> Index()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                return RedirectToAction("Login", "Account");
            }

            // Cập nhật trạng thái Online Rentals hết hạn
            await UpdateExpiredOnlineRentals(userId);

            // Lấy từ ReadingHistory - nguồn chính
            var readingHistoryList = await _context.ReadingHistory
                .Include(r => r.Book)
                    .ThenInclude(b => b.Category)
                .Where(r => r.UserId == userId)
                .Select(r => new ReadingHistoryViewModel
                {
                    BookId = r.BookId,
                    Book = r.Book,
                    ViewDate = r.ViewDate,
                    ViewDuration = r.ViewDuration,
                    IsCompleted = r.IsCompleted,
                    Source = "ReadingHistory"
                })
                .OrderByDescending(r => r.ViewDate)
                .ToListAsync();

            // Lấy các bookId đã có trong ReadingHistory
            var existingBookIds = readingHistoryList.Select(r => r.BookId).ToHashSet();

            // Lấy từ OnlineRentalTransactions - chỉ những sách chưa có trong ReadingHistory
            // Kiểm tra hết hạn realtime
            var onlineRentals = await _context.OnlineRentalTransactions
                .Include(o => o.Book)
                    .ThenInclude(b => b.Category)
                .Where(o => o.UserId == userId && !existingBookIds.Contains(o.BookId))
                .ToListAsync();

            var onlineRentalsViewModel = onlineRentals.Select(o => new ReadingHistoryViewModel
            {
                BookId = o.BookId,
                Book = o.Book,
                ViewDate = o.PurchaseDate,
                ViewDuration = null,
                // FIXED: Bỏ HasValue vì ExpiryDate là DateTime, không phải DateTime?
                IsCompleted = o.Status == "Expired" || o.ExpiryDate < DateTime.Now,
                Source = "Online",
                ExpiryDate = o.ExpiryDate,
                // FIXED: Bỏ HasValue
                Status = (o.Status == "Active" && o.ExpiryDate < DateTime.Now)
                    ? "Expired"
                    : o.Status
            }).ToList();

            // Lấy từ RentalTransactions - chỉ những sách chưa có
            var physicalRentals = await _context.RentalTransactions
                .Include(r => r.Book)
                    .ThenInclude(b => b.Category)
                .Where(r => r.UserId == userId && !existingBookIds.Contains(r.BookId))
                .Select(r => new ReadingHistoryViewModel
                {
                    BookId = r.BookId,
                    Book = r.Book,
                    ViewDate = r.RentalDate,
                    ViewDuration = r.ReturnDate.HasValue ?
                        (int)(r.ReturnDate.Value - r.RentalDate).TotalSeconds : null,
                    IsCompleted = r.Status == "Returned",
                    Source = "Physical",
                    DueDate = r.DueDate,
                    ReturnDate = r.ReturnDate,
                    Status = r.Status
                })
                .ToListAsync();

            // Gộp tất cả
            var allHistory = readingHistoryList
                .Concat(onlineRentalsViewModel)
                .Concat(physicalRentals)
                .OrderByDescending(h => h.ViewDate)
                .ToList();

            return View(allHistory);
        }

        // GET: ReadingHistory/CompletedBooks
        public async Task<IActionResult> CompletedBooks()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                return RedirectToAction("Login", "Account");
            }

            // Cập nhật trạng thái Online Rentals hết hạn
            await UpdateExpiredOnlineRentals(userId);

            // Lấy từ ReadingHistory - đã hoàn thành
            var readingHistoryList = await _context.ReadingHistory
                .Include(r => r.Book)
                    .ThenInclude(b => b.Category)
                .Where(r => r.UserId == userId && r.IsCompleted)
                .Select(r => new ReadingHistoryViewModel
                {
                    BookId = r.BookId,
                    Book = r.Book,
                    ViewDate = r.ViewDate,
                    ViewDuration = r.ViewDuration,
                    IsCompleted = true,
                    Source = "ReadingHistory"
                })
                .ToListAsync();

            // Lấy các bookId đã có trong ReadingHistory
            var existingBookIds = readingHistoryList.Select(r => r.BookId).ToHashSet();

            // Lấy từ OnlineRentalTransactions - đã hết hạn
            var onlineRentals = await _context.OnlineRentalTransactions
                .Include(o => o.Book)
                    .ThenInclude(b => b.Category)
                .Where(o => o.UserId == userId && !existingBookIds.Contains(o.BookId))
                .ToListAsync();

            // FIXED: Bỏ HasValue
            var completedOnlineRentals = onlineRentals
                .Where(o => o.Status == "Expired" || o.ExpiryDate < DateTime.Now)
                .Select(o => new ReadingHistoryViewModel
                {
                    BookId = o.BookId,
                    Book = o.Book,
                    ViewDate = o.PurchaseDate,
                    ViewDuration = null,
                    IsCompleted = true,
                    Source = "Online",
                    ExpiryDate = o.ExpiryDate,
                    Status = "Expired"
                })
                .ToList();

            // Lấy từ RentalTransactions - đã trả và chưa có trong ReadingHistory
            var physicalRentals = await _context.RentalTransactions
                .Include(r => r.Book)
                    .ThenInclude(b => b.Category)
                .Where(r => r.UserId == userId && r.Status == "Returned" && !existingBookIds.Contains(r.BookId))
                .Select(r => new ReadingHistoryViewModel
                {
                    BookId = r.BookId,
                    Book = r.Book,
                    ViewDate = r.RentalDate,
                    ViewDuration = r.ReturnDate.HasValue ?
                        (int)(r.ReturnDate.Value - r.RentalDate).TotalSeconds : null,
                    IsCompleted = true,
                    Source = "Physical",
                    ReturnDate = r.ReturnDate
                })
                .ToListAsync();

            // Gộp tất cả lại
            var completedBooks = readingHistoryList
                .Concat(completedOnlineRentals)
                .Concat(physicalRentals)
                .OrderByDescending(h => h.ViewDate)
                .ToList();

            return View(completedBooks);
        }

        // GET: ReadingHistory/AllHistory - Xem tất cả lịch sử không lọc trùng
        public async Task<IActionResult> AllHistory()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                return RedirectToAction("Login", "Account");
            }

            // Cập nhật trạng thái Online Rentals hết hạn
            await UpdateExpiredOnlineRentals(userId);

            // Lấy từ ReadingHistory
            var readingHistoryList = await _context.ReadingHistory
                .Include(r => r.Book)
                    .ThenInclude(b => b.Category)
                .Where(r => r.UserId == userId)
                .Select(r => new ReadingHistoryViewModel
                {
                    BookId = r.BookId,
                    Book = r.Book,
                    ViewDate = r.ViewDate,
                    ViewDuration = r.ViewDuration,
                    IsCompleted = r.IsCompleted,
                    Source = "ReadingHistory"
                })
                .ToListAsync();

            // Lấy từ OnlineRentalTransactions với kiểm tra hết hạn realtime
            var onlineRentals = await _context.OnlineRentalTransactions
                .Include(o => o.Book)
                    .ThenInclude(b => b.Category)
                .Where(o => o.UserId == userId)
                .ToListAsync();

            // FIXED: Bỏ HasValue
            var onlineRentalsViewModel = onlineRentals.Select(o => new ReadingHistoryViewModel
            {
                BookId = o.BookId,
                Book = o.Book,
                ViewDate = o.PurchaseDate,
                ViewDuration = null,
                IsCompleted = o.Status == "Expired" || o.ExpiryDate < DateTime.Now,
                Source = "Online",
                ExpiryDate = o.ExpiryDate,
                Status = (o.Status == "Active" && o.ExpiryDate < DateTime.Now)
                    ? "Expired"
                    : o.Status
            }).ToList();

            // Lấy từ RentalTransactions
            var physicalRentals = await _context.RentalTransactions
                .Include(r => r.Book)
                    .ThenInclude(b => b.Category)
                .Where(r => r.UserId == userId)
                .Select(r => new ReadingHistoryViewModel
                {
                    BookId = r.BookId,
                    Book = r.Book,
                    ViewDate = r.RentalDate,
                    ViewDuration = r.ReturnDate.HasValue ?
                        (int)(r.ReturnDate.Value - r.RentalDate).TotalSeconds : null,
                    IsCompleted = r.Status == "Returned",
                    Source = "Physical",
                    DueDate = r.DueDate,
                    ReturnDate = r.ReturnDate,
                    Status = r.Status
                })
                .ToListAsync();

            // Gộp tất cả KHÔNG lọc trùng
            var allHistory = readingHistoryList
                .Concat(onlineRentalsViewModel)
                .Concat(physicalRentals)
                .OrderByDescending(h => h.ViewDate)
                .ToList();

            return View(allHistory);
        }

        // GET: ReadingHistory/Statistics
        public async Task<IActionResult> Statistics()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                return RedirectToAction("Login", "Account");
            }

            // Cập nhật trạng thái Online Rentals hết hạn
            await UpdateExpiredOnlineRentals(userId);

            // Tổng số sách (loại bỏ trùng lặp) - ưu tiên ReadingHistory
            var allBookIds = await _context.ReadingHistory
                .Where(r => r.UserId == userId)
                .Select(r => r.BookId)
                .Union(_context.OnlineRentalTransactions
                    .Where(o => o.UserId == userId)
                    .Select(o => o.BookId))
                .Union(_context.RentalTransactions
                    .Where(r => r.UserId == userId)
                    .Select(r => r.BookId))
                .Distinct()
                .CountAsync();

            // Đếm sách hoàn thành
            var completedFromHistory = await _context.ReadingHistory
                .Where(r => r.UserId == userId && r.IsCompleted)
                .Select(r => r.BookId)
                .Distinct()
                .ToListAsync();

            // Đếm sách hoàn thành từ OnlineRentalTransactions
            var onlineRentals = await _context.OnlineRentalTransactions
                .Where(o => o.UserId == userId)
                .Select(o => new { o.BookId, o.Status, o.ExpiryDate })
                .ToListAsync();

            // FIXED: Bỏ HasValue
            var completedFromOnline = onlineRentals
                .Where(o => o.Status == "Expired" || o.ExpiryDate < DateTime.Now)
                .Select(o => o.BookId)
                .Distinct()
                .ToList();

            var completedFromPhysical = await _context.RentalTransactions
                .Where(r => r.UserId == userId && r.Status == "Returned")
                .Select(r => r.BookId)
                .Distinct()
                .ToListAsync();

            // Gộp và loại bỏ trùng lặp
            var completedBooks = completedFromHistory
                .Union(completedFromOnline)
                .Union(completedFromPhysical)
                .Distinct()
                .Count();

            // Tính tổng thời gian đọc từ ReadingHistory
            var totalReadingTimeFromHistory = await _context.ReadingHistory
                .Where(r => r.UserId == userId)
                .SumAsync(r => r.ViewDuration ?? 0);

            // Tính thời gian từ RentalTransactions (nếu đã trả)
            var physicalRentals = await _context.RentalTransactions
                .Where(r => r.UserId == userId && r.ReturnDate.HasValue)
                .Select(r => new { r.RentalDate, r.ReturnDate })
                .ToListAsync();

            var totalReadingTimeFromPhysical = physicalRentals
                .Sum(r => (int)(r.ReturnDate!.Value - r.RentalDate).TotalSeconds);

            var totalReadingTime = totalReadingTimeFromHistory + totalReadingTimeFromPhysical;

            ViewBag.TotalBooks = allBookIds;
            ViewBag.CompletedBooks = completedBooks;
            ViewBag.TotalReadingTime = totalReadingTime;
            ViewBag.TotalReadingHours = Math.Round((double)totalReadingTime / 3600, 2);

            return View();
        }

        // GET: ReadingHistory/MigrateData - Đồng bộ dữ liệu cũ (chạy 1 lần)
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> MigrateData()
        {
            try
            {
                // Migrate từ OnlineRentalTransactions
                var onlineRentals = await _context.OnlineRentalTransactions
                    .Where(o => !_context.ReadingHistory
                        .Any(h => h.UserId == o.UserId && h.BookId == o.BookId))
                    .ToListAsync();

                foreach (var rental in onlineRentals)
                {
                    // FIXED: Bỏ HasValue khi kiểm tra hết hạn
                    bool isExpired = rental.Status == "Expired" || rental.ExpiryDate < DateTime.Now;

                    _context.ReadingHistory.Add(new ReadingHistory
                    {
                        UserId = rental.UserId,
                        BookId = rental.BookId,
                        ViewDate = rental.PurchaseDate,
                        ViewDuration = null,
                        IsCompleted = isExpired
                    });
                }

                // Migrate từ RentalTransactions
                var physicalRentals = await _context.RentalTransactions
                    .Where(r => !_context.ReadingHistory
                        .Any(h => h.UserId == r.UserId && h.BookId == r.BookId))
                    .ToListAsync();

                foreach (var rental in physicalRentals)
                {
                    _context.ReadingHistory.Add(new ReadingHistory
                    {
                        UserId = rental.UserId,
                        BookId = rental.BookId,
                        ViewDate = rental.RentalDate,
                        ViewDuration = rental.ReturnDate.HasValue ?
                            (int)(rental.ReturnDate.Value - rental.RentalDate).TotalSeconds : null,
                        IsCompleted = rental.Status == "Returned"
                    });
                }

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Đã đồng bộ {onlineRentals.Count + physicalRentals.Count} bản ghi vào ReadingHistory!";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Lỗi khi đồng bộ dữ liệu: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }
        }

        // Private method: Tự động cập nhật status của Online Rentals đã hết hạn
        private async Task UpdateExpiredOnlineRentals(int? userId = null)
        {
            try
            {
                // FIXED: Bỏ HasValue trong query vì ExpiryDate là DateTime
                var query = _context.OnlineRentalTransactions
                    .Where(o => o.Status == "Active" && o.ExpiryDate < DateTime.Now);

                if (userId.HasValue)
                {
                    query = query.Where(o => o.UserId == userId.Value);
                }

                var expiredRentals = await query.ToListAsync();

                if (expiredRentals.Any())
                {
                    foreach (var rental in expiredRentals)
                    {
                        rental.Status = "Expired";
                    }

                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception)
            {
                // Log error nhưng không throw để không ảnh hưởng UI
            }
        }

        // GET: ReadingHistory/UpdateAllExpiredRentals - Cập nhật tất cả (Admin only)
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateAllExpiredRentals()
        {
            try
            {
                await UpdateExpiredOnlineRentals(); // Không truyền userId để update tất cả

                var count = await _context.OnlineRentalTransactions
                    .Where(o => o.Status == "Expired")
                    .CountAsync();

                TempData["SuccessMessage"] = $"Đã cập nhật trạng thái cho {count} giao dịch online hết hạn!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Lỗi khi cập nhật: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}