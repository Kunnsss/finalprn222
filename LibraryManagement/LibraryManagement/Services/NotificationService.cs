// Services/NotificationService.cs
using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagement.Services
{
    public class NotificationService : INotificationService
    {
        private readonly LibraryDbContext _context;
        private readonly IHubContext<NotificationHub> _hubContext;

        public NotificationService(LibraryDbContext context, IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        public async Task SendNotificationAsync(int userId, string message, string type)
        {
            var user = await _context.Users
         .Include(u => u.Role)
         .FirstOrDefaultAsync(u => u.UserId == userId);

            if (user == null || user.Role?.RoleName == "Admin")
                return;

            var notification = new Notification
            {
                UserId = userId,
                Message = message,
                Type = type,
                IsRead = false,
                CreatedDate = DateTime.Now
            };

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();

            // Send real-time notification via SignalR
            await _hubContext.Clients.User(userId.ToString())
                .SendAsync("ReceiveNotification", notification);
            
        }

        public async Task<List<Notification>> GetUserNotificationsAsync(int userId)
        {
            return await _context.Notifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedDate)
                .Take(50)
                .ToListAsync();
        }

        public async Task MarkAsReadAsync(int notificationId)
        {
            var notification = await _context.Notifications.FindAsync(notificationId);
            if (notification != null)
            {
                notification.IsRead = true;
                await _context.SaveChangesAsync();
            }
        }
    }
}