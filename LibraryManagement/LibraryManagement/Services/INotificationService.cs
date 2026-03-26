// Services/INotificationService.cs
using LibraryManagement.Models;

namespace LibraryManagement.Services
{
    public interface INotificationService
    {
        Task SendNotificationAsync(int userId, string message, string type);
        Task SendNotificationToAllAdminsAsync(string message, string type);
        Task<List<Notification>> GetUserNotificationsAsync(int userId);
        Task MarkAsReadAsync(int notificationId);
    }
}