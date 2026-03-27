using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LibraryManagement.Data;
using LibraryManagement.Services;
using PayOS;
using PayOS.Models.Webhooks;

namespace LibraryManagement.Controllers
{
    [Authorize]
    [Route("PayOS")]
    public class PayOSController : Controller
    {
        private readonly LibraryDbContext _context;
        private readonly IRentalService _rentalService;
        private readonly INotificationService _notificationService;
        private readonly PayOSClient _payOSClient;

        public PayOSController(
            LibraryDbContext context,
            IRentalService rentalService,
            INotificationService notificationService,
            IConfiguration config)
        {
            _context = context;
            _rentalService = rentalService;
            _notificationService = notificationService;
            _payOSClient = new PayOSClient(new PayOSOptions
            {
                ClientId = config["PayOS:ClientId"]!,
                ApiKey = config["PayOS:ApiKey"]!,
                ChecksumKey = config["PayOS:ChecksumKey"]!
            });
        }

        /// <summary>
        /// PayOS redirects here on successful payment.
        /// URL: /PayOS/Return?type=...&transactionId=...&days=...&code=00&status=PAID&orderCode=...
        /// </summary>
        [HttpGet("Return")]
        public async Task<IActionResult> Return(string type, int transactionId, int? days,
            string? code, string? status, long? orderCode)
        {
            bool isPaid = code == "00" && status == "PAID";

            if (!isPaid)
            {
                TempData["ErrorMessage"] = "Thanh toán không thành công hoặc đã bị hủy.";
                return RedirectBasedOnType(type);
            }

            switch (type)
            {
                case "OnlineInitial":
                    return await HandleOnlineInitialReturn(transactionId, orderCode);

                case "OnlineExtend":
                    return await HandleOnlineExtendReturn(transactionId, orderCode, days ?? 0);

                case "RentalInitial":
                    return await HandleRentalInitialReturn(transactionId, orderCode);

                case "RentalExtend":
                    return await HandleRentalExtendReturn(transactionId, orderCode, days ?? 0);

                case "RentalLateFee":
                    return await HandleRentalLateFeeReturn(transactionId, orderCode);

                default:
                    return RedirectToAction("Index", "Home");
            }
        }

        /// <summary>
        /// PayOS redirects here when user cancels payment.
        /// </summary>
        [HttpGet("Cancel")]
        public async Task<IActionResult> Cancel(string type, int transactionId, long? orderCode)
        {
            switch (type)
            {
                case "OnlineInitial":
                    var onlineTx = await _context.OnlineRentalTransactions
                        .FirstOrDefaultAsync(t => t.OnlineTransactionId == transactionId);
                    if (onlineTx != null && onlineTx.PayOSOrderCode == orderCode)
                    {
                        onlineTx.PaymentStatus = "Cancelled";
                        await _context.SaveChangesAsync();
                    }
                    TempData["ErrorMessage"] = "Bạn đã hủy thanh toán.";
                    return RedirectToAction("MyOnlineRentals", "OnlineRentals");

                case "OnlineExtend":
                    TempData["ErrorMessage"] = "Bạn đã hủy gia hạn.";
                    return RedirectToAction("MyOnlineRentals", "OnlineRentals");

                case "RentalInitial":
                    var rentalTx = await _context.RentalTransactions
                        .FirstOrDefaultAsync(t => t.TransactionId == transactionId);
                    if (rentalTx != null && rentalTx.RentalPayOSOrderCode == orderCode)
                    {
                        rentalTx.RentalPaymentStatus = "Cancelled";
                        await _context.SaveChangesAsync();
                    }
                    TempData["ErrorMessage"] = "Bạn đã hủy thanh toán.";
                    return RedirectToAction("MyRentals", "Rentals");

                case "RentalExtend":
                    TempData["ErrorMessage"] = "Bạn đã hủy gia hạn.";
                    return RedirectToAction("MyRentals", "Rentals");

                case "RentalLateFee":
                    var lateFeeTx = await _context.RentalTransactions
                        .FirstOrDefaultAsync(t => t.TransactionId == transactionId);
                    if (lateFeeTx != null && lateFeeTx.LateFeePayOSOrderCode == orderCode)
                    {
                        lateFeeTx.LateFeePaymentStatus = "Cancelled";
                        await _context.SaveChangesAsync();
                    }
                    TempData["ErrorMessage"] = "Bạn đã hủy thanh toán phí phạt.";
                    return RedirectToAction("MyRentals", "Rentals");

                default:
                    return RedirectToAction("Index", "Home");
            }
        }

        /// <summary>
        /// PayOS sends payment confirmation webhook here.
        /// Must be registered via ngrok URL.
        /// </summary>
        [AllowAnonymous]
        [HttpPost("/api/payos/webhook")]
        public async Task<IActionResult> Webhook([FromBody] Webhook webhook)
        {
            try
            {
                var data = await _payOSClient.Webhooks.VerifyAsync(webhook);

                if (data == null) return Ok(new { success = true });

                long orderCode = data.OrderCode;

                // Check OnlineRentalTransactions - initial payment
                var onlineTx = await _context.OnlineRentalTransactions
                    .Include(t => t.Book)
                    .FirstOrDefaultAsync(t => t.PayOSOrderCode == orderCode);

                if (onlineTx != null)
                {
                    if (onlineTx.PaymentStatus != "Paid")
                    {
                        onlineTx.PaymentStatus = "Paid";
                        onlineTx.Status = "Active";
                        onlineTx.PayOSPaymentLinkId = data.PaymentLinkId;
                        await _context.SaveChangesAsync();
                        await _notificationService.SendNotificationAsync(onlineTx.UserId,
                            $"Thanh toán sách online '{onlineTx.Book.Title}' thành công!", "Success");
                    }
                    return Ok(new { success = true });
                }

                // Check RentalTransactions - initial/extension payment
                var rentalTx = await _context.RentalTransactions
                    .Include(t => t.Book)
                    .FirstOrDefaultAsync(t => t.RentalPayOSOrderCode == orderCode);

                if (rentalTx != null)
                {
                    if (rentalTx.RentalPaymentStatus != "Paid")
                    {
                        rentalTx.RentalPaymentStatus = "Paid";
                        rentalTx.RentalPayOSLinkId = data.PaymentLinkId;
                        await _context.SaveChangesAsync();
                        await _rentalService.ConfirmPaymentAsync(rentalTx.TransactionId);
                    }
                    return Ok(new { success = true });
                }

                // Check RentalTransactions - late fee payment
                var lateFeeTx = await _context.RentalTransactions
                    .Include(t => t.Book)
                    .FirstOrDefaultAsync(t => t.LateFeePayOSOrderCode == orderCode);

                if (lateFeeTx != null)
                {
                    if (lateFeeTx.LateFeePaymentStatus != "Paid")
                    {
                        lateFeeTx.LateFeePaymentStatus = "Paid";
                        lateFeeTx.LateFeePayOSLinkId = data.PaymentLinkId;

                        // Nếu đang quá hạn → chuyển về Renting sau khi trả phí trễ
                        if (lateFeeTx.Status == "Overdue")
                        {
                            lateFeeTx.Status = "Renting";
                        }

                        await _context.SaveChangesAsync();
                        await _notificationService.SendNotificationAsync(lateFeeTx.UserId,
                            $"Thanh toán phí phạt sách '{lateFeeTx.Book.Title}' thành công!", "Success");
                    }
                    return Ok(new { success = true });
                }

                return Ok(new { success = true });
            }
            catch
            {
                return Ok(new { success = true }); // Always return 200 to PayOS
            }
        }

        // ─── Private helpers ──────────────────────────────────────────────────

        private async Task<IActionResult> HandleOnlineInitialReturn(int transactionId, long? orderCode)
        {
            var tx = await _context.OnlineRentalTransactions
                .Include(t => t.Book)
                .FirstOrDefaultAsync(t => t.OnlineTransactionId == transactionId);

            if (tx == null || tx.PayOSOrderCode != orderCode)
            {
                TempData["ErrorMessage"] = "Không tìm thấy giao dịch.";
                return RedirectToAction("MyOnlineRentals", "OnlineRentals");
            }

            if (tx.PaymentStatus != "Paid")
            {
                tx.PaymentStatus = "Paid";
                tx.Status = "Active";
                await _context.SaveChangesAsync();
                await _notificationService.SendNotificationAsync(tx.UserId,
                    $"Thanh toán sách online '{tx.Book.Title}' thành công! Hạn đến {tx.ExpiryDate:dd/MM/yyyy}.", "Success");
            }

            TempData["SuccessMessage"] = "Thanh toán thành công! Bạn có thể đọc sách ngay bây giờ.";
            return RedirectToAction("AccessBook", "OnlineRentals", new { transactionId });
        }

        private async Task<IActionResult> HandleOnlineExtendReturn(int transactionId, long? orderCode, int days)
        {
            var tx = await _context.OnlineRentalTransactions
                .Include(t => t.Book)
                .FirstOrDefaultAsync(t => t.OnlineTransactionId == transactionId);

            if (tx == null || tx.PayOSOrderCode != orderCode || days <= 0)
            {
                TempData["ErrorMessage"] = "Không tìm thấy giao dịch gia hạn.";
                return RedirectToAction("MyOnlineRentals", "OnlineRentals");
            }

            decimal dailyRate = tx.Amount / 30m;
            decimal extensionFee = Math.Round(dailyRate * days);

            tx.ExpiryDate = tx.ExpiryDate.AddDays(days);
            tx.Amount += extensionFee;
            await _context.SaveChangesAsync();

            await _notificationService.SendNotificationAsync(tx.UserId,
                $"Gia hạn sách '{tx.Book.Title}' thêm {days} ngày thành công! Hạn mới: {tx.ExpiryDate:dd/MM/yyyy}.", "Success");

            TempData["SuccessMessage"] = $"Gia hạn thành công thêm {days} ngày! Hạn mới: {tx.ExpiryDate:dd/MM/yyyy}.";
            return RedirectToAction("MyOnlineRentals", "OnlineRentals");
        }

        private async Task<IActionResult> HandleRentalInitialReturn(int transactionId, long? orderCode)
        {
            var tx = await _context.RentalTransactions
                .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

            if (tx == null || tx.RentalPayOSOrderCode != orderCode)
            {
                TempData["ErrorMessage"] = "Không tìm thấy giao dịch.";
                return RedirectToAction("MyRentals", "Rentals");
            }

            if (tx.RentalPaymentStatus != "Paid")
            {
                tx.RentalPaymentStatus = "Paid";
                await _context.SaveChangesAsync();
                await _rentalService.ConfirmPaymentAsync(transactionId);
            }

            TempData["SuccessMessage"] = "Thanh toán thành công!";
            return RedirectToAction("MyRentals", "Rentals");
        }

        private async Task<IActionResult> HandleRentalExtendReturn(int transactionId, long? orderCode, int days)
        {
            var tx = await _context.RentalTransactions
                .Include(t => t.Book)
                .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

            if (tx == null || tx.RentalPayOSOrderCode != orderCode || days <= 0)
            {
                TempData["ErrorMessage"] = "Không tìm thấy giao dịch gia hạn.";
                return RedirectToAction("MyRentals", "Rentals");
            }

            decimal extensionFee = days * tx.RentalPrice;
            tx.DueDate = tx.DueDate < DateTime.Now
                ? DateTime.Now.AddDays(days)
                : tx.DueDate.AddDays(days);
            tx.TotalAmount += extensionFee;
            tx.Status = "Renting";
            tx.RentalPaymentStatus = "Paid";
            await _context.SaveChangesAsync();

            await _notificationService.SendNotificationAsync(tx.UserId,
                $"Gia hạn sách '{tx.Book.Title}' thêm {days} ngày thành công! Hạn trả mới: {tx.DueDate:dd/MM/yyyy}.", "Success");

            TempData["SuccessMessage"] = $"Gia hạn thành công! Hạn trả mới: {tx.DueDate:dd/MM/yyyy}.";
            return RedirectToAction("MyRentals", "Rentals");
        }

        private async Task<IActionResult> HandleRentalLateFeeReturn(int transactionId, long? orderCode)
        {
            var tx = await _context.RentalTransactions
                .Include(t => t.Book)
                .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

            if (tx == null || tx.LateFeePayOSOrderCode != orderCode)
            {
                TempData["ErrorMessage"] = "Không tìm thấy giao dịch phí phạt.";
                return RedirectToAction("MyRentals", "Rentals");
            }

            if (tx.LateFeePaymentStatus != "Paid")
            {
                tx.LateFeePaymentStatus = "Paid";

                // Nếu đang quá hạn → chuyển về Renting sau khi trả phí trễ
                if (tx.Status == "Overdue")
                {
                    tx.Status = "Renting";
                }

                await _context.SaveChangesAsync();
                await _notificationService.SendNotificationAsync(tx.UserId,
                    $"Thanh toán phí phạt sách '{tx.Book.Title}' thành công!", "Success");
            }

            TempData["SuccessMessage"] = "Thanh toán phí phạt thành công!";
            return RedirectToAction("MyRentals", "Rentals");
        }

        private IActionResult RedirectBasedOnType(string type)
        {
            if (type?.StartsWith("Online") == true)
                return RedirectToAction("MyOnlineRentals", "OnlineRentals");
            return RedirectToAction("MyRentals", "Rentals");
        }
    }
}
