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
        private readonly IPayOSService _payOSService;

        public RentalsController(
            IRentalService rentalService,
            LibraryDbContext context,
            IReadingHistoryService historyService,
            IPayOSService payOSService)
        {
            _rentalService = rentalService;
            _context = context;
            _historyService = historyService;
            _payOSService = payOSService;
        }

        public async Task<IActionResult> MyRentals(int page = 1, int pageSize = 8)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var query = await _rentalService.GetUserRentalsAsync(userId);
            var list = query.OrderByDescending(r => r.RentalDate).ToList();

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

            TempData["SuccessMessage"] = $"Đăng ký thuê sách thành công! Tổng tiền: {transaction.TotalAmount:N0} VND. Hạn trả: {transaction.DueDate:dd/MM/yyyy}. Vui lòng đến quầy để thanh toán.";
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
            var offlineRentals = await _context.RentalTransactions
                .Include(r => r.User)
                .Include(r => r.Book)
                .OrderByDescending(r => r.RentalDate)
                .ToListAsync();

            var onlineRentals = await _context.OnlineRentalTransactions
                .Include(r => r.User)
                .Include(r => r.Book)
                .OrderByDescending(r => r.PurchaseDate)
                .ToListAsync();

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
                .Where(r => r.Status != "Returned" && r.Status != "Lost" && r.Status != "Compensated" && r.DueDate < DateTime.Now)
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelRental(int transactionId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var success = await _rentalService.CancelRentalAsync(transactionId, userId);

            if (success)
            {
                TempData["SuccessMessage"] = "Hủy thuê sách thành công!";
            }
            else
            {
                TempData["ErrorMessage"] = "Không thể hủy thuê sách. Vui lòng thử lại.";
            }

            return RedirectToAction(nameof(MyRentals));
        }

        /// <summary>Modal submit → trang trung gian xác nhận gia hạn.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExtendRental(int transactionId, int additionalDays)
        {
            if (additionalDays <= 0)
            {
                TempData["ErrorMessage"] = "Số ngày gia hạn không hợp lệ.";
                return RedirectToAction(nameof(MyRentals));
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var transaction = await _context.RentalTransactions
                .FirstOrDefaultAsync(t => t.TransactionId == transactionId && t.UserId == userId);

            if (transaction == null || (transaction.Status != "Renting" && transaction.Status != "Overdue"))
            {
                TempData["ErrorMessage"] = "Không thể gia hạn sách. Vui lòng thử lại.";
                return RedirectToAction(nameof(MyRentals));
            }

            // Redirect to intermediate confirmation page
            return RedirectToAction(nameof(ExtendRentalDetail), new { transactionId, additionalDays });
        }

        /// <summary>Trang trung gian: hiển thị chi tiết gia hạn trước khi thanh toán PayOS.</summary>
        [HttpGet]
        public async Task<IActionResult> ExtendRentalDetail(int transactionId, int additionalDays)
        {
            if (additionalDays <= 0)
                return RedirectToAction(nameof(MyRentals));

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var transaction = await _context.RentalTransactions
                .Include(t => t.Book)
                .FirstOrDefaultAsync(t => t.TransactionId == transactionId && t.UserId == userId);

            if (transaction == null || (transaction.Status != "Renting" && transaction.Status != "Overdue"))
            {
                TempData["ErrorMessage"] = "Giao dịch không hợp lệ.";
                return RedirectToAction(nameof(MyRentals));
            }

            ViewBag.AdditionalDays = additionalDays;
            ViewBag.ExtensionFee = additionalDays * transaction.RentalPrice;
            ViewBag.NewDueDate = (transaction.DueDate < DateTime.Now
                ? DateTime.Now
                : transaction.DueDate).AddDays(additionalDays);

            return View(transaction);
        }

        /// <summary>Tạo link PayOS và chuyển hướng sang cổng thanh toán (gia hạn sách).</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> InitiateExtendPayment(int transactionId, int additionalDays)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var transaction = await _context.RentalTransactions
                .Include(t => t.Book)
                .FirstOrDefaultAsync(t => t.TransactionId == transactionId && t.UserId == userId);

            if (transaction == null || (transaction.Status != "Renting" && transaction.Status != "Overdue"))
            {
                TempData["ErrorMessage"] = "Giao dịch không hợp lệ.";
                return RedirectToAction(nameof(MyRentals));
            }

            decimal extensionFee = additionalDays * transaction.RentalPrice;
            var amount = (int)Math.Max(2000, Math.Round(extensionFee));
            var orderCode = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var description = $"Gia han sach #{transactionId}";
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var returnUrl = $"{baseUrl}/PayOS/Return?type=RentalExtend&transactionId={transactionId}&days={additionalDays}";
            var cancelUrl = $"{baseUrl}/PayOS/Cancel?type=RentalExtend&transactionId={transactionId}&orderCode={orderCode}";

            try
            {
                var (checkoutUrl, linkId) = await _payOSService.CreatePaymentLinkAsync(
                    orderCode, amount, description, returnUrl, cancelUrl);

                transaction.RentalPayOSOrderCode = orderCode;
                transaction.RentalPayOSLinkId = linkId;
                await _context.SaveChangesAsync();

                return Redirect(checkoutUrl);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Không thể tạo link thanh toán: {ex.Message}";
                return RedirectToAction(nameof(ExtendRentalDetail), new { transactionId, additionalDays });
            }
        }

        /// <summary>
        /// Trang chi tiết thanh toán trước khi chuyển sang PayOS.
        /// Dùng cho: PendingPayment (thuê sách, đền bù mất sách) và phí phạt trễ hạn.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> PaymentDetail(int transactionId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var transaction = await _context.RentalTransactions
                .Include(t => t.Book)
                .FirstOrDefaultAsync(t => t.TransactionId == transactionId && t.UserId == userId);

            if (transaction == null)
                return NotFound();

            // Determine payment type for the view
            if (transaction.Status == "PendingPayment")
            {
                ViewBag.PaymentType = transaction.ReturnDate.HasValue ? "Compensation" : "Initial";
                ViewBag.PaymentAmount = transaction.TotalAmount;
            }
            else if (transaction.Status == "Returned" && transaction.LateFee > 0 && transaction.LateFeePaymentStatus != "Paid")
            {
                ViewBag.PaymentType = "LateFee";
                ViewBag.PaymentAmount = transaction.LateFee;
            }
            else
            {
                TempData["ErrorMessage"] = "Giao dịch không hợp lệ.";
                return RedirectToAction(nameof(MyRentals));
            }

            return View(transaction);
        }

        /// <summary>
        /// Thanh toán online cho thuê sách vật lý đang chờ thanh toán (PendingPayment).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PayOnline(int transactionId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var transaction = await _context.RentalTransactions
                .Include(t => t.Book)
                .FirstOrDefaultAsync(t => t.TransactionId == transactionId && t.UserId == userId);

            if (transaction == null || transaction.Status != "PendingPayment")
            {
                TempData["ErrorMessage"] = "Giao dịch không hợp lệ.";
                return RedirectToAction(nameof(MyRentals));
            }

            // Amount = TotalAmount (covers initial rental OR compensation)
            var amount = (int)Math.Max(2000, Math.Round(transaction.TotalAmount));
            var orderCode = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var description = $"Thanh toan thue #{transactionId}";
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var returnUrl = $"{baseUrl}/PayOS/Return?type=RentalInitial&transactionId={transactionId}";
            var cancelUrl = $"{baseUrl}/PayOS/Cancel?type=RentalInitial&transactionId={transactionId}&orderCode={orderCode}";

            try
            {
                var (checkoutUrl, linkId) = await _payOSService.CreatePaymentLinkAsync(
                    orderCode, amount, description, returnUrl, cancelUrl);

                transaction.RentalPayOSOrderCode = orderCode;
                transaction.RentalPayOSLinkId = linkId;
                transaction.RentalPaymentStatus = "Pending";
                await _context.SaveChangesAsync();

                return Redirect(checkoutUrl);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Không thể tạo link thanh toán: {ex.Message}";
                return RedirectToAction(nameof(MyRentals));
            }
        }

        /// <summary>
        /// Thanh toán phí phạt trễ hạn online (sau khi trả sách).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PayLateFeeOnline(int transactionId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var transaction = await _context.RentalTransactions
                .Include(t => t.Book)
                .FirstOrDefaultAsync(t => t.TransactionId == transactionId && t.UserId == userId);

            if (transaction == null || transaction.LateFee <= 0 || transaction.LateFeePaymentStatus == "Paid")
            {
                TempData["ErrorMessage"] = "Không có phí phạt cần thanh toán.";
                return RedirectToAction(nameof(MyRentals));
            }

            var amount = (int)Math.Max(2000, Math.Round(transaction.LateFee));
            var orderCode = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var description = $"Phi phat tre #{transactionId}";
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var returnUrl = $"{baseUrl}/PayOS/Return?type=RentalLateFee&transactionId={transactionId}";
            var cancelUrl = $"{baseUrl}/PayOS/Cancel?type=RentalLateFee&transactionId={transactionId}&orderCode={orderCode}";

            try
            {
                var (checkoutUrl, linkId) = await _payOSService.CreatePaymentLinkAsync(
                    orderCode, amount, description, returnUrl, cancelUrl);

                transaction.LateFeePayOSOrderCode = orderCode;
                transaction.LateFeePayOSLinkId = linkId;
                transaction.LateFeePaymentStatus = "Pending";
                await _context.SaveChangesAsync();

                return Redirect(checkoutUrl);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Không thể tạo link thanh toán: {ex.Message}";
                return RedirectToAction(nameof(MyRentals));
            }
        }

        [HttpPost]
        [Authorize(Roles = "Admin,Librarian")]
        public async Task<IActionResult> ConfirmPayment(int transactionId)
        {
            var success = await _rentalService.ConfirmPaymentAsync(transactionId);

            if (success)
            {
                TempData["SuccessMessage"] = "Xác nhận thanh toán thành công!";
            }
            else
            {
                TempData["ErrorMessage"] = "Không thể xác nhận thanh toán. Vui lòng thử lại.";
            }

            return RedirectToAction(nameof(AllRentals));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Librarian")]
        public async Task<IActionResult> ProcessLostCompensation(int transactionId, decimal compensationAmount)
        {
            if (compensationAmount <= 0)
            {
                TempData["ErrorMessage"] = "Số tiền đền bù phải lớn hơn 0.";
                return RedirectToAction(nameof(AllRentals));
            }

            var ok = await _rentalService.ProcessLostBookCompensationAsync(transactionId, compensationAmount);
            if (ok)
                TempData["SuccessMessage"] = "Đã ghi nhận phí đền bù. Người thuê chuyển sang chờ thanh toán.";
            else
                TempData["ErrorMessage"] = "Không thể xử lý. Giao dịch không ở trạng thái \"Đã báo mất\".";

            return RedirectToAction(nameof(AllRentals));
        }
    }
}