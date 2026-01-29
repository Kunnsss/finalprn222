// Services/IBookReservationService.cs - Interface đầy đủ
using LibraryManagement.Models;

namespace LibraryManagement.Services
{
    public interface IBookReservationService
    {
        // User functions
        Task<BookReservation?> ReserveBookAsync(int userId, int bookId);
        Task<bool> CancelReservationAsync(int reservationId);
        Task<List<BookReservation>> GetUserReservationsAsync(int userId);
        Task<bool> ProcessReservationWhenBookReturnedAsync(int bookId);

        // Admin functions
        Task<List<BookReservation>> GetAllReservationsAsync();
        Task<BookReservation?> GetReservationByIdAsync(int reservationId);
        Task<bool> MarkAsReadyAsync(int reservationId);
        Task<bool> MarkAsCompletedAsync(int reservationId);
        Task<bool> AdminCancelReservationAsync(int reservationId, string reason);
        Task<bool> SendReminderAsync(int reservationId);
    }
}