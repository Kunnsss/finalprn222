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

        public async Task<bool> ReturnBookAsync(int transactionId)
        {
            var transaction = await _context.RentalTransactions
                .Include(t => t.Book)
                .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

            if (transaction == null || transaction.Status == "Returned")
                return false;

            transaction.ReturnDate = DateTime.Now;
            transaction.Status = "Returned";

            // Calculate late fee
            await CalculateLateFeeAsync(transactionId);

            transaction.Book.AvailableQuantity++;

            await _context.SaveChangesAsync();

            // === THÔNG BÁO REALTIME ===

            // 1. Thông báo cho user trả sách
           
                await _notificationService.SendNotificationAsync(transaction.UserId,
                $"Bạn đã trả sách '{transaction.Book.Title}' thành công. " +
                $"Tổng tiền: {transaction.TotalAmount:N0} VND" +
                (transaction.LateFee > 0 ? $" (Phí trễ: {transaction.LateFee:N0} VND)" : ""),
                "Success");
            
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

            // 4. Tạo transaction
            var transaction = new RentalTransaction
            {
                UserId = userId,
                BookId = bookId,
                RentalDate = rentalDate,
                DueDate = dueDate,
                RentalPrice = book.RentalPrice,
                TotalAmount = totalAmount,
                LateFee = 0,
                Status = "Renting"
            };

            // 5. Giảm số lượng sách
            book.AvailableQuantity -= 1;

            // 6. Lưu DB
            _context.RentalTransactions.Add(transaction);
            _context.Books.Update(book);
            await _context.SaveChangesAsync();

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
                .Where(r => r.Status != "Returned" && r.DueDate < DateTime.Now)
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

    }
}