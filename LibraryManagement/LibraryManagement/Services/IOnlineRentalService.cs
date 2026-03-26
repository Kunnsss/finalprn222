// Services/IOnlineRentalService.cs
using LibraryManagement.Models;

namespace LibraryManagement.Services
{
    public interface IOnlineRentalService
    {
        Task<OnlineRentalTransaction?> RentOnlineBookAsync(int userId, int bookId, int accessDays);
        Task<bool> ProcessPaymentAsync(int transactionId);
        Task<List<OnlineRentalTransaction>> GetUserOnlineRentalsAsync(int userId);
        Task<string?> GetAccessLinkAsync(int transactionId, int userId);
        Task<bool> CancelOnlineRentalAsync(int transactionId, int userId);
        Task<bool> ExtendOnlineRentalAsync(int transactionId, int additionalDays);
    }
}