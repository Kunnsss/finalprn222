// Services/OnlineRentalService.cs
using LibraryManagement.Data;
using LibraryManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagement.Services
{
    public class OnlineRentalService : IOnlineRentalService
    {
        private readonly LibraryDbContext _context;
        private readonly INotificationService _notificationService;

        public OnlineRentalService(LibraryDbContext context, INotificationService notificationService)
        {
            _context = context;
            _notificationService = notificationService;
        }

        public async Task<OnlineRentalTransaction?> RentOnlineBookAsync(int userId, int bookId, int accessDays)
        {
            var book = await _context.Books.FindAsync(bookId);
            if (book == null || book.IsPhysical || !book.OnlinePrice.HasValue)
                return null;

            var transaction = new OnlineRentalTransaction
            {
                UserId = userId,
                BookId = bookId,
                PurchaseDate = DateTime.Now,
                ExpiryDate = DateTime.Now.AddDays(accessDays),
                Amount = book.OnlinePrice.Value,
                AccessLink = book.OnlineLink,
                Status = "Active",
                PaymentStatus = "Pending"
            };

            _context.OnlineRentalTransactions.Add(transaction);
            await _context.SaveChangesAsync();

            return transaction;
        }

        public async Task<bool> ProcessPaymentAsync(int transactionId)
        {
            var transaction = await _context.OnlineRentalTransactions
                .Include(t => t.Book)
                .FirstOrDefaultAsync(t => t.OnlineTransactionId == transactionId);

            if (transaction == null || transaction.PaymentStatus == "Paid")
                return false;

            // Simulate payment processing
            transaction.PaymentStatus = "Paid";
            await _context.SaveChangesAsync();

            await _notificationService.SendNotificationAsync(transaction.UserId,
                $"Thanh toán thành công! Bạn có thể truy cập sách '{transaction.Book.Title}' online đến {transaction.ExpiryDate:dd/MM/yyyy}",
                "Success");

            return true;
        }

        public async Task<List<OnlineRentalTransaction>> GetUserOnlineRentalsAsync(int userId)
        {
            return await _context.OnlineRentalTransactions
                .Include(t => t.Book)
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.PurchaseDate)
                .ToListAsync();
        }

        public async Task<string?> GetAccessLinkAsync(int transactionId, int userId)
        {
            var transaction = await _context.OnlineRentalTransactions
                .FirstOrDefaultAsync(t => t.OnlineTransactionId == transactionId
                    && t.UserId == userId
                    && t.PaymentStatus == "Paid"
                    && t.Status == "Active"
                    && t.ExpiryDate > DateTime.Now);

            return transaction?.AccessLink;
        }
    }
}
