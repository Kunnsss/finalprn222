// Services/RentalService.cs - Thêm logic thông báo khi trả sách
using LibraryManagement.Data;
using LibraryManagement.Hubs;
using LibraryManagement.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagement.Services
{
    public class RentalServiceEnhanced : IRentalService
    {
        private readonly LibraryDbContext _context;
        private readonly INotificationService _notificationService;
        private readonly IBookReservationService _reservationService;
        private readonly IHubContext<LibraryHub> _hubContext;

        public RentalServiceEnhanced(
            LibraryDbContext context,
            INotificationService notificationService,
            IBookReservationService reservationService,
            IHubContext<LibraryHub> hubContext)
        {
            _context = context;
            _notificationService = notificationService;
            _reservationService = reservationService;
            _hubContext = hubContext;
        }

        public async Task<bool> ReportLostBookAsync(int transactionId, int reportingUserId)
        {
            var transaction = await _context.RentalTransactions
                .Include(t => t.Book)
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

            if (transaction == null || transaction.Status == "Returned" || transaction.Status == "Lost" || transaction.Status == "Compensated")
                return false;

            if (transaction.UserId != reportingUserId)
                return false;

            transaction.ReturnDate = DateTime.Now;
            transaction.Status = "Lost";

            await CalculateLateFeeAsync(transactionId);

            var book = transaction.Book;
            if (book.Quantity > 0)
                book.Quantity -= 1;

            await _context.SaveChangesAsync();

            var renterName = transaction.User?.FullName ?? "Người thuê";
            var bookTitle = book.Title;
            var msg = $"Đã báo mất sách: \"{bookTitle}\" — người thuê: {renterName} — mã giao dịch #{transaction.TransactionId}. Xem chi tiết tại Quản lý thuê sách.";
            await _notificationService.SendNotificationToAllAdminsAsync(msg, "LostBook");

            await _notificationService.SendNotificationAsync(transaction.UserId,
                $"Bạn đã báo mất sách \"{bookTitle}\". Thư viện đã nhận thông báo. Mã giao dịch: #{transaction.TransactionId}.",
                "Warning");

            await _hubContext.Clients.All.SendAsync("BookAvailabilityUpdated", new
            {
                BookId = transaction.BookId,
                AvailableQuantity = book.AvailableQuantity
            });

            return true;
        }

        public async Task<bool> ReturnBookAsync(int transactionId)
        {
            var transaction = await _context.RentalTransactions
                .Include(t => t.Book)
                .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

            if (transaction == null || transaction.Status == "Returned" || transaction.Status == "Lost" || transaction.Status == "Compensated")
                return false;

            transaction.ReturnDate = DateTime.Now;
            transaction.Status = "Returned";

            // Calculate late fee
            await CalculateLateFeeAsync(transactionId);

            transaction.Book.AvailableQuantity++;

            await _context.SaveChangesAsync();

            // === THÔNG BÁO REALTIME ===

            // 1. Thông báo cho user trả sách
            var msg = $"Bạn đã trả sách '{transaction.Book.Title}' thành công.";
            if (transaction.LateFee > 0)
                msg += $" Phí phạt trễ: {transaction.LateFee:N0} VND. Vui lòng đến quầy thanh toán phí phạt.";
            await _notificationService.SendNotificationAsync(transaction.UserId, msg, transaction.LateFee > 0 ? "Warning" : "Success");
            
            // 2. Cập nhật số lượng sách trên trang tìm kiếm (realtime)
            await _hubContext.Clients.All.SendAsync("BookAvailabilityUpdated", new
            {
                BookId = transaction.BookId,
                AvailableQuantity = transaction.Book.AvailableQuantity
            });

            return true;
        }

        // Các method khác giữ nguyên...
        public async Task<RentalTransaction?> RentBookAsync(int userId, int bookId, int rentalDays)
        {
            // 1. Lấy sách
            var book = await _context.Books.FirstOrDefaultAsync(b => b.BookId == bookId);

            if (book == null || !book.IsPhysical || book.AvailableQuantity <= 0)
                return null;

            // 2. Tính ngày thuê & hạn trả
            var rentalDate = DateTime.Now;
            var dueDate = rentalDate.AddDays(rentalDays);

            // 3. Tính tiền
            var totalAmount = rentalDays * book.RentalPrice;

            // 4. Tạo transaction với status PendingPayment
            var transaction = new RentalTransaction
            {
                UserId = userId,
                BookId = bookId,
                RentalDate = rentalDate,
                DueDate = dueDate,
                RentalPrice = book.RentalPrice,
                TotalAmount = totalAmount,
                LateFee = 0,
                Status = "PendingPayment" // Chờ thanh toán khi mới thuê
            };

            // 5. Giảm số lượng sách
            book.AvailableQuantity -= 1;

            // 6. Lưu DB
            _context.RentalTransactions.Add(transaction);
            _context.Books.Update(book);
            await _context.SaveChangesAsync();

            // 7. SignalR: Thông báo cho admin có yêu cầu thuê sách mới
            await _hubContext.Clients.All.SendAsync("RentalUpdated");

            return transaction;
        }


        public async Task<decimal> CalculateLateFeeAsync(int transactionId)
        {
            var transaction = await _context.RentalTransactions
                .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

            if (transaction == null || transaction.ReturnDate == null)
                return 0;

            if (transaction.ReturnDate <= transaction.DueDate)
            {
                transaction.LateFee = 0;
                return 0;
            }

            // Số ngày trễ
            int lateDays = (transaction.ReturnDate.Value.Date - transaction.DueDate.Date).Days;

            // 5.000 VND / ngày, tối đa 500.000
            decimal lateFee = lateDays * 5000;
            if (lateFee > 500000)
                lateFee = 500000;

            transaction.LateFee = lateFee;
            transaction.TotalAmount += lateFee;

            await _context.SaveChangesAsync();
            return lateFee;
        }


        public async Task<List<RentalTransaction>> GetUserRentalsAsync(int userId)
        {
            return await _context.RentalTransactions
                .Include(r => r.Book)
                    .ThenInclude(b => b.Category)
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.RentalDate)
                .ToListAsync();
        }

        public async Task<List<RentalTransaction>> GetOverdueRentalsAsync()
        {
            return await _context.RentalTransactions
                .Include(r => r.User)
                .Include(r => r.Book)
                .Where(r => r.Status != "Returned" && r.Status != "Lost" && r.Status != "Compensated" && r.DueDate < DateTime.Now)
                .OrderBy(r => r.DueDate)
                .ToListAsync();
        }


        public async Task UpdateOverdueStatusAsync()
        {
            var overdueRentals = await _context.RentalTransactions
                .Where(r => r.Status == "Renting" && r.DueDate < DateTime.Now)
                .ToListAsync();

            foreach (var rental in overdueRentals)
            {
                rental.Status = "Overdue";
            }

            await _context.SaveChangesAsync();
        }

        public async Task<bool> CancelRentalAsync(int transactionId, int userId)
        {
            var transaction = await _context.RentalTransactions
                .Include(t => t.Book)
                .FirstOrDefaultAsync(t => t.TransactionId == transactionId && t.UserId == userId);

            if (transaction == null)
                return false;

            // Chỉ cho phép hủy nếu đang thuê hoặc chờ thanh toán
            if (transaction.Status != "Renting" && transaction.Status != "Overdue" && transaction.Status != "PendingPayment")
                return false;

            // Chờ thanh toán phí đền bù sách mất: không hủy qua luồng này (sách đã báo mất, không cộng lại kho)
            if (transaction.Status == "PendingPayment" && transaction.ReturnDate.HasValue)
                return false;

            // Đổi trạng thái thành Cancelled (Hủy hẳn)
            transaction.Status = "Cancelled";
            transaction.ReturnDate = DateTime.Now;

            // Trả sách về kho
            transaction.Book.AvailableQuantity++;

            await _context.SaveChangesAsync();

            // Thông báo cho user
            await _notificationService.SendNotificationAsync(userId,
                $"Bạn đã hủy thuê sách '{transaction.Book.Title}' thành công.",
                "Info");

            // SignalR: Thông báo cho tất cả client cập nhật
            await _hubContext.Clients.All.SendAsync("RentalUpdated");

            // Cập nhật số lượng sách realtime
            await _hubContext.Clients.All.SendAsync("BookAvailabilityUpdated", new
            {
                BookId = transaction.BookId,
                AvailableQuantity = transaction.Book.AvailableQuantity
            });

            // Thông báo cho người đang đặt chỗ (nếu có)
            await _reservationService.ProcessReservationWhenBookReturnedAsync(transaction.BookId);

            return true;
        }

        public async Task<bool> ExtendRentalAsync(int transactionId, int additionalDays)
        {
            var transaction = await _context.RentalTransactions
                .Include(t => t.Book)
                .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

            if (transaction == null)
                return false;

            // Chỉ cho phép gia hạn nếu đang thuê
            if (transaction.Status != "Renting")
                return false;

            // Cập nhật hạn trả mới
            transaction.DueDate = transaction.DueDate.AddDays(additionalDays);

            // Nếu đang quá hạn, tính từ hôm nay
            if (transaction.DueDate < DateTime.Now)
            {
                transaction.DueDate = DateTime.Now.AddDays(additionalDays);
            }

            // Tính thêm tiền theo số ngày gia hạn
            decimal extensionFee = additionalDays * transaction.RentalPrice;
            transaction.TotalAmount += extensionFee;

            // Đổi trạng thái thành PendingPayment (Chờ thanh toán phí gia hạn)
            transaction.Status = "PendingPayment";

            await _context.SaveChangesAsync();

            // Thông báo cho user
            await _notificationService.SendNotificationAsync(transaction.UserId,
                $"Bạn đã gia hạn sách '{transaction.Book.Title}' thêm {additionalDays} ngày. Hạn trả mới: {transaction.DueDate:dd/MM/yyyy}. Phí gia hạn: {extensionFee:N0} VND. Vui lòng đến quầy để thanh toán.",
                "Success");

            // SignalR: Thông báo cho tất cả client cập nhật
            await _hubContext.Clients.All.SendAsync("RentalUpdated");

            return true;
        }

        public async Task<bool> ProcessLostBookCompensationAsync(int transactionId, decimal compensationAmount)
        {
            if (compensationAmount <= 0)
                return false;

            var transaction = await _context.RentalTransactions
                .Include(t => t.Book)
                .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

            if (transaction == null || transaction.Status != "Lost")
                return false;

            transaction.LateFee += compensationAmount;
            transaction.TotalAmount += compensationAmount;
            transaction.Status = "PendingPayment";

            await _context.SaveChangesAsync();

            await _notificationService.SendNotificationAsync(transaction.UserId,
                $"Thư viện đã ghi nhận phí đền bù sách mất \"{transaction.Book.Title}\": {compensationAmount:N0} VND " +
                $"(tổng phí phạt: {transaction.LateFee:N0} VND). " +
                "Vui lòng đến quầy thanh toán phí phạt.",
                "Warning");

            await _hubContext.Clients.All.SendAsync("RentalUpdated");

            return true;
        }

        public async Task<bool> ConfirmPaymentAsync(int transactionId)
        {
            var transaction = await _context.RentalTransactions
                .Include(t => t.Book)
                .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

            if (transaction == null)
                return false;

            // Chỉ xác nhận nếu đang chờ thanh toán
            if (transaction.Status != "PendingPayment")
                return false;

            // Đền bù sách mất: đã có ReturnDate khi báo mất → sau thanh toán đóng hồ sơ (không trả về Renting)
            if (transaction.ReturnDate.HasValue)
            {
                transaction.Status = "Compensated";
                await _context.SaveChangesAsync();

                await _notificationService.SendNotificationAsync(transaction.UserId,
                    $"Thanh toán phí phạt sách mất '{transaction.Book.Title}' đã được xác nhận. Phí phạt: {transaction.LateFee:N0} VND.",
                    "Success");
            }
            else
            {
                transaction.Status = "Renting";
                await _context.SaveChangesAsync();

                await _notificationService.SendNotificationAsync(transaction.UserId,
                    $"Thanh toán cho sách '{transaction.Book.Title}' đã được xác nhận. Bạn có thể tiếp tục thuê sách.",
                    "Success");
            }

            await _hubContext.Clients.All.SendAsync("RentalUpdated");

            return true;
        }

    }
}