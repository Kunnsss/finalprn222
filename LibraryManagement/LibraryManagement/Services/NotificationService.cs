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
        private readonly IHubContext<LibraryHub> _hubContext;

        public NotificationService(LibraryDbContext context, IHubContext<LibraryHub> hubContext)
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

        /// <summary>Gửi thông báo lưu DB + SignalR tới mọi tài khoản Admin và Librarian (cùng quản lý giao dịch).</summary>
        public async Task SendNotificationToAllAdminsAsync(string message, string type)
        {
            var adminIds = await _context.Users
                .Include(u => u.Role)
                .Where(u => u.IsActive && u.Role != null &&
                    (u.Role.RoleName == "Admin" || u.Role.RoleName == "Librarian"))
                .Select(u => u.UserId)
                .ToListAsync();

            if (adminIds.Count == 0)
                return;

            var notifications = adminIds.Select(adminId => new Notification
            {
                UserId = adminId,
                Message = message,
                Type = type,
                IsRead = false,
                CreatedDate = DateTime.Now
            }).ToList();

            _context.Notifications.AddRange(notifications);
            await _context.SaveChangesAsync();

            // Gửi realtime qua nhóm Librarians (Admin + Thủ thư đều vào group này khi mở trang)
            // — đáng tin cậy hơn Clients.User (IUserIdProvider / mapping user)
            var first = notifications[0];
            await _hubContext.Clients.Group("Librarians").SendAsync("ReceiveNotification", new
            {
                notificationId = first.NotificationId,
                message = message,
                type = type,
                isRead = false,
                createdDate = first.CreatedDate
            });
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