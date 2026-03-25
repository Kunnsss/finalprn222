// Controllers/RentalsController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using LibraryManagement.Services;
using LibraryManagement.ViewModels;
using LibraryManagement.Data;
using LibraryManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagement.Controllers
{
    [Authorize]
    public class RentalsController : Controller
    {
        private readonly IRentalService _rentalService;
        private readonly LibraryDbContext _context;
        private readonly IReadingHistoryService _historyService;

        public RentalsController(
            IRentalService rentalService,
            LibraryDbContext context,
            IReadingHistoryService historyService)
        {
            _rentalService = rentalService;
            _context = context;
            _historyService = historyService;
        }

        public async Task<IActionResult> MyRentals()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var rentals = await _rentalService.GetUserRentalsAsync(userId);
            return View(rentals);
        }

        [HttpGet]
        public async Task<IActionResult> Rent(int bookId)
        {
            var book = await _context.Books.FindAsync(bookId);
            if (book == null || !book.IsPhysical || book.AvailableQuantity <= 0)
            {
                TempData["ErrorMessage"] = "Sách không khả dụng để thuê.";
                return RedirectToAction("Index", "Books");
            }

            var model = new RentalViewModel
            {
                BookId = book.BookId,
                BookTitle = book.Title,
                RentalPrice = book.RentalPrice
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Rent(RentalViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var book = await _context.Books.FindAsync(model.BookId);
                model.BookTitle = book?.Title ?? "";
                model.RentalPrice = book?.RentalPrice ?? 0;
                return View(model);
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var transaction = await _rentalService.RentBookAsync(userId, model.BookId, model.RentalDays);

            if (transaction == null)
            {
                TempData["ErrorMessage"] = "Không thể thuê sách. Vui lòng thử lại.";
                return RedirectToAction("Details", "Books", new { id = model.BookId });
            }

            // Lưu vào ReadingHistory
            await _historyService.TrackBookView(userId, model.BookId);

            TempData["SuccessMessage"] = $"Thuê sách thành công! Tổng tiền: {transaction.TotalAmount:N0} VND. Hạn trả: {transaction.DueDate:dd/MM/yyyy}";
            return RedirectToAction(nameof(MyRentals));
        }

        [HttpPost]
        public async Task<IActionResult> Return(int transactionId)
        {
            // Lấy thông tin transaction trước khi trả
            var transaction = await _context.RentalTransactions
                .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

            if (transaction == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy giao dịch.";
                return RedirectToAction(nameof(MyRentals));
            }

            var success = await _rentalService.ReturnBookAsync(transactionId);

            if (success)
            {
                // Tính thời gian thuê và cập nhật ReadingHistory
                var returnDate = DateTime.Now;
                var durationSeconds = (int)(returnDate - transaction.RentalDate).TotalSeconds;

                await _historyService.TrackBookView(
                    transaction.UserId,
                    transaction.BookId,
                    durationSeconds,
                    true // Đánh dấu đã hoàn thành
                );

                TempData["SuccessMessage"] = "Trả sách thành công!";
            }
            else
            {
                TempData["ErrorMessage"] = "Không thể trả sách. Vui lòng thử lại.";
            }

            if (User.IsInRole("Admin") || User.IsInRole("Librarian"))
            {
                return RedirectToAction(nameof(AllRentals));
            }

            return RedirectToAction(nameof(MyRentals));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReportLost(int transactionId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var ok = await _rentalService.ReportLostBookAsync(transactionId, userId);

            if (ok)
                TempData["SuccessMessage"] = "Đã ghi nhận báo mất sách. Thư viện đã nhận thông báo.";
            else
                TempData["ErrorMessage"] = "Không thể ghi nhận báo mất. Giao dịch không hợp lệ hoặc đã kết thúc.";

            return RedirectToAction(nameof(MyRentals));
        }

        [Authorize(Roles = "Admin,Librarian")]
        public async Task<IActionResult> AllRentals()
        {
            // Lấy giao dịch thuê trực tiếp (offline)
            var offlineRentals = await _context.RentalTransactions
                .Include(r => r.User)
                .Include(r => r.Book)
                .OrderByDescending(r => r.RentalDate)
                .ToListAsync();

            // Lấy giao dịch thuê online
            var onlineRentals = await _context.OnlineRentalTransactions
                .Include(r => r.User)
                .Include(r => r.Book)
                .OrderByDescending(r => r.PurchaseDate)
                .ToListAsync();

            // Tạo ViewModel để truyền cả 2 loại giao dịch
            var viewModel = new AllRentalsViewModel
            {
                OfflineRentals = offlineRentals,
                OnlineRentals = onlineRentals
            };

            return View(viewModel);
        }

        [Authorize(Roles = "Admin,Librarian")]
        public async Task<IActionResult> OverdueRentals()
        {
            // Lấy giao dịch offline quá hạn
            var overdueOffline = await _context.RentalTransactions
                .Include(r => r.User)
                .Include(r => r.Book)
                .Where(r => r.Status != "Returned" && r.Status != "Lost" && r.DueDate < DateTime.Now)
                .OrderBy(r => r.DueDate)
                .ToListAsync();

            // Lấy giao dịch online hết hạn
            var overdueOnline = await _context.OnlineRentalTransactions
                .Include(r => r.User)
                .Include(r => r.Book)
                .Where(r => r.Status == "Active" && r.ExpiryDate < DateTime.Now)
                .OrderBy(r => r.ExpiryDate)
                .ToListAsync();

            // Tạo ViewModel
            var viewModel = new AllRentalsViewModel
            {
                OfflineRentals = overdueOffline,
                OnlineRentals = overdueOnline
            };

            return View("AllRentals", viewModel); // Dùng lại view AllRentals
        }
    }
}