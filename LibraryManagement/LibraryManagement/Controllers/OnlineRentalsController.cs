// Controllers/OnlineRentalsController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using LibraryManagement.Services;
using LibraryManagement.ViewModels;
using LibraryManagement.Data;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagement.Controllers
{
    [Authorize]
    public class OnlineRentalsController : Controller
    {
        private readonly IOnlineRentalService _onlineRentalService;
        private readonly LibraryDbContext _context;
        private readonly IReadingHistoryService _historyService;

        public OnlineRentalsController(
            IOnlineRentalService onlineRentalService,
            LibraryDbContext context,
            IReadingHistoryService historyService)
        {
            _onlineRentalService = onlineRentalService;
            _context = context;
            _historyService = historyService;
        }

        public async Task<IActionResult> MyOnlineRentals()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var rentals = await _onlineRentalService.GetUserOnlineRentalsAsync(userId);
            return View(rentals);
        }

        [HttpGet]
        public async Task<IActionResult> RentOnline(int bookId)
        {
            var book = await _context.Books.FindAsync(bookId);
            if (book == null || book.IsPhysical || !book.OnlinePrice.HasValue)
            {
                TempData["ErrorMessage"] = "Sách không khả dụng để thuê online.";
                return RedirectToAction("Index", "Books");
            }

            var model = new OnlineRentalViewModel
            {
                BookId = book.BookId,
                BookTitle = book.Title,
                OnlinePrice = book.OnlinePrice.Value,
                AccessDays = 30 // Default 30 days
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RentOnline(OnlineRentalViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var book = await _context.Books.FindAsync(model.BookId);
                model.BookTitle = book?.Title ?? "";
                model.OnlinePrice = book?.OnlinePrice ?? 0;
                return View(model);
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var transaction = await _onlineRentalService.RentOnlineBookAsync(userId, model.BookId, model.AccessDays);

            if (transaction == null)
            {
                TempData["ErrorMessage"] = "Không thể thuê sách online. Vui lòng thử lại.";
                return RedirectToAction("Details", "Books", new { id = model.BookId });
            }

            // Lưu vào ReadingHistory
            await _historyService.TrackBookView(userId, model.BookId);

            // Redirect to payment
            return RedirectToAction(nameof(Payment), new { transactionId = transaction.OnlineTransactionId });
        }

        [HttpGet]
        public async Task<IActionResult> Payment(int transactionId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var transaction = await _context.OnlineRentalTransactions
                .Include(t => t.Book)
                .FirstOrDefaultAsync(t => t.OnlineTransactionId == transactionId && t.UserId == userId);

            if (transaction == null)
                return NotFound();

            if (transaction.PaymentStatus == "Paid")
            {
                TempData["InfoMessage"] = "Giao dịch này đã được thanh toán.";
                return RedirectToAction(nameof(MyOnlineRentals));
            }

            return View(transaction);
        }

        [HttpPost]
        public async Task<IActionResult> ProcessPayment(int transactionId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var transaction = await _context.OnlineRentalTransactions
                .FirstOrDefaultAsync(t => t.OnlineTransactionId == transactionId && t.UserId == userId);

            if (transaction == null)
                return NotFound();

            var success = await _onlineRentalService.ProcessPaymentAsync(transactionId);

            if (success)
            {
                TempData["SuccessMessage"] = "Thanh toán thành công! Bạn có thể truy cập sách ngay bây giờ.";
                return RedirectToAction(nameof(AccessBook), new { transactionId });
            }

            TempData["ErrorMessage"] = "Thanh toán thất bại. Vui lòng thử lại.";
            return RedirectToAction(nameof(Payment), new { transactionId });
        }

        [HttpGet]
        public async Task<IActionResult> AccessBook(int transactionId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var link = await _onlineRentalService.GetAccessLinkAsync(transactionId, userId);

            if (string.IsNullOrEmpty(link))
            {
                TempData["ErrorMessage"] = "Không thể truy cập sách. Vui lòng kiểm tra lại giao dịch.";
                return RedirectToAction(nameof(MyOnlineRentals));
            }

            var transaction = await _context.OnlineRentalTransactions
                .Include(t => t.Book)
                .FirstOrDefaultAsync(t => t.OnlineTransactionId == transactionId);

            if (transaction != null)
            {
                // Cập nhật lần xem cuối trong ReadingHistory
                await _historyService.TrackBookView(userId, transaction.BookId);
            }

            ViewBag.AccessLink = link;
            ViewBag.TransactionId = transactionId;

            return View(transaction);
        }
    }
}