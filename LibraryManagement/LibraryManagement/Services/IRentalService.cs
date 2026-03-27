// Services/IRentalService.cs
using LibraryManagement.Models;

namespace LibraryManagement.Services
{
    public interface IRentalService
    {
        Task<RentalTransaction?> RentBookAsync(int userId, int bookId, int rentalDays);
        Task<bool> ReturnBookAsync(int transactionId);
        Task<bool> ReportLostBookAsync(int transactionId, int reportingUserId);
        Task<decimal> CalculateLateFeeAsync(int transactionId);
        Task<List<RentalTransaction>> GetUserRentalsAsync(int userId);
        Task<List<RentalTransaction>> GetOverdueRentalsAsync();
        Task UpdateOverdueStatusAsync();
        Task<bool> CancelRentalAsync(int transactionId, int userId);
        Task<bool> ExtendRentalAsync(int transactionId, int additionalDays);
        Task<bool> ConfirmPaymentAsync(int transactionId);
    }
}
