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
        private readonly IPayOSService _payOSService;

        public OnlineRentalsController(
            IOnlineRentalService onlineRentalService,
            LibraryDbContext context,
            IReadingHistoryService historyService,
            IPayOSService payOSService)
        {
            _onlineRentalService = onlineRentalService;
            _context = context;
            _historyService = historyService;
            _payOSService = payOSService;
        }

        public async Task<IActionResult> MyOnlineRentals(int page = 1, int pageSize = 8)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var query = await _onlineRentalService.GetUserOnlineRentalsAsync(userId);
            var list = query.OrderByDescending(r => r.PurchaseDate).ToList();

            int total = list.Count;
            int totalPages = (int)Math.Ceiling(total / (double)pageSize);
            if (page < 1) page = 1;
            if (page > totalPages && totalPages > 0) page = totalPages;

            var paged = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalItems = total;
            ViewBag.TotalPages = totalPages;
            return View(paged);
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

        /// <summary>Trang trung gian: hiển thị thông tin giao dịch trước khi thanh toán PayOS.</summary>
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

        /// <summary>Tạo link PayOS và chuyển hướng sang cổng thanh toán.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> InitiatePayment(int transactionId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var transaction = await _context.OnlineRentalTransactions
                .Include(t => t.Book)
                .FirstOrDefaultAsync(t => t.OnlineTransactionId == transactionId && t.UserId == userId);

            if (transaction == null || transaction.PaymentStatus == "Paid")
            {
                TempData["ErrorMessage"] = "Giao dịch không hợp lệ.";
                return RedirectToAction(nameof(MyOnlineRentals));
            }

            var orderCode = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var amount = (int)Math.Max(2000, Math.Round(transaction.Amount));
            var description = $"Thue sach online #{transactionId}";
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var returnUrl = $"{baseUrl}/PayOS/Return?type=OnlineInitial&transactionId={transactionId}";
            var cancelUrl = $"{baseUrl}/PayOS/Cancel?type=OnlineInitial&transactionId={transactionId}&orderCode={orderCode}";

            try
            {
                var (checkoutUrl, linkId) = await _payOSService.CreatePaymentLinkAsync(
                    orderCode, amount, description, returnUrl, cancelUrl);

                transaction.PayOSOrderCode = orderCode;
                transaction.PayOSPaymentLinkId = linkId;
                await _context.SaveChangesAsync();

                return Redirect(checkoutUrl);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Không thể tạo link thanh toán: {ex.Message}";
                return RedirectToAction(nameof(Payment), new { transactionId });
            }
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelOnlineRental(int transactionId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var success = await _onlineRentalService.CancelOnlineRentalAsync(transactionId, userId);

            if (success)
            {
                TempData["SuccessMessage"] = "Hủy thuê sách online thành công!";
            }
            else
            {
                TempData["ErrorMessage"] = "Không thể hủy thuê sách online. Vui lòng thử lại.";
            }

            return RedirectToAction(nameof(MyOnlineRentals));
        }

        /// <summary>
        /// Trang chi tiết gia hạn: chọn số ngày, xem phí, rồi bấm "Thanh toán bằng PayOS".
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> ExtendDetail(int transactionId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var transaction = await _context.OnlineRentalTransactions
                .Include(t => t.Book)
                .FirstOrDefaultAsync(t => t.OnlineTransactionId == transactionId && t.UserId == userId);

            if (transaction == null || transaction.PaymentStatus != "Paid" || transaction.Status != "Active")
            {
                TempData["ErrorMessage"] = "Không thể gia hạn giao dịch này.";
                return RedirectToAction(nameof(MyOnlineRentals));
            }

            return View(transaction);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExtendOnlineRental(int transactionId, int additionalDays)
        {
            if (additionalDays <= 0)
            {
                TempData["ErrorMessage"] = "Số ngày gia hạn không hợp lệ.";
                return RedirectToAction(nameof(MyOnlineRentals));
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var transaction = await _context.OnlineRentalTransactions
                .Include(t => t.Book)
                .FirstOrDefaultAsync(t => t.OnlineTransactionId == transactionId && t.UserId == userId);

            if (transaction == null || transaction.PaymentStatus != "Paid" || transaction.Status != "Active")
            {
                TempData["ErrorMessage"] = "Không thể gia hạn sách online. Vui lòng thử lại.";
                return RedirectToAction(nameof(MyOnlineRentals));
            }

            // Calculate extension fee (same logic as service)
            decimal dailyRate = transaction.Amount / 30m;
            decimal extensionFee = Math.Round(dailyRate * additionalDays);
            var amount = (int)Math.Max(2000, extensionFee);

            var orderCode = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var description = $"Gia han sach #{transactionId}";
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var returnUrl = $"{baseUrl}/PayOS/Return?type=OnlineExtend&transactionId={transactionId}&days={additionalDays}";
            var cancelUrl = $"{baseUrl}/PayOS/Cancel?type=OnlineExtend&transactionId={transactionId}&orderCode={orderCode}";

            try
            {
                var (checkoutUrl, linkId) = await _payOSService.CreatePaymentLinkAsync(
                    orderCode, amount, description, returnUrl, cancelUrl);

                transaction.PayOSOrderCode = orderCode;
                transaction.PayOSPaymentLinkId = linkId;
                await _context.SaveChangesAsync();

                return Redirect(checkoutUrl);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Không thể tạo link thanh toán: {ex.Message}";
                return RedirectToAction(nameof(MyOnlineRentals));
            }
        }
    }
}