// Hubs/LibraryHub.cs - SignalR Hub với Online User Tracking
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Collections.Concurrent;

namespace LibraryManagement.Hubs
{
    [Authorize]
    public class LibraryHub : Hub
    {
        // Class để lưu thông tin user online
        public class OnlineUserInfo
        {
            public string UserId { get; set; }
            public string UserName { get; set; }
            public DateTime ConnectedAt { get; set; }
            public string ConnectionId { get; set; }
        }

        // Static dictionary để track online users
        private static readonly ConcurrentDictionary<string, OnlineUserInfo> _onlineUsers = new();

        /// <summary>
        /// Lấy số lượng user đang online
        /// </summary>
        public static int GetOnlineCount()
        {
            return _onlineUsers.Count;
        }

        /// <summary>
        /// Lấy danh sách user đang online
        /// </summary>
        public static IEnumerable<object> GetOnlineUsers()
        {
            return _onlineUsers.Values.Select(u => new
            {
                userId = u.UserId,
                userName = u.UserName,
                connectedAt = u.ConnectedAt,
                connectionId = u.ConnectionId
            });
        }

        /// <summary>
        /// Người dùng gửi tin nhắn đến thủ thư
        /// </summary>
        public async Task SendMessageToLibrarian(string message)
        {
            var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            var userName = Context.User?.FindFirstValue(ClaimTypes.Name);

            // Gửi tin nhắn đến tất cả Librarian (group "Librarians")
            await Clients.Group("Librarians").SendAsync("ReceiveUserMessage", new
            {
                userId = userId,
                userName = userName,
                message = message,
                timestamp = DateTime.Now
            });

            // NOTE: ReceiveNotification cho Support message được gửi qua
            // NotificationService -> NotificationHub thay vì tại đây để tránh trùng lặp.
        }

        /// <summary>
        /// Thủ thư gửi phản hồi cho người dùng
        /// </summary>
        [Authorize(Roles = "Admin,Librarian")]
        public async Task SendLibrarianReply(string targetUserId, string message)
        {
            var librarianName = Context.User?.FindFirstValue(ClaimTypes.Name);

            // Gửi phản hồi đến user cụ thể
            await Clients.User(targetUserId).SendAsync("ReceiveLibrarianReply", new
            {
                message = message,
                librarianName = librarianName,
                timestamp = DateTime.Now
            });
        }

        /// <summary>
        /// Broadcast thông báo hệ thống đến tất cả users
        /// </summary>
        [Authorize(Roles = "Admin")]
        public async Task BroadcastSystemMessage(string title, string message, string type)
        {
            await Clients.All.SendAsync("SystemMessage", new
            {
                title = title,
                message = message,
                type = type,
                timestamp = DateTime.Now
            });
        }

        /// <summary>
        /// Thông báo sách đã có sẵn (cho đặt chỗ)
        /// </summary>
        public async Task NotifyBookAvailable(string userId, string bookTitle)
        {
            await Clients.User(userId).SendAsync("BookNowAvailable", new
            {
                message = $"Sách '{bookTitle}' đã có sẵn! Hãy đến thư viện để mượn.",
                bookTitle = bookTitle,
                timestamp = DateTime.Now
            });
        }

        /// <summary>
        /// Force logout một user (Admin only)
        /// </summary>
        [Authorize(Roles = "Admin")]
        public async Task ForceLogoutUser(string userId, string reason)
        {
            // Tìm connection ID của user
            var userInfo = _onlineUsers.Values.FirstOrDefault(u => u.UserId == userId);
            if (userInfo != null)
            {
                await Clients.Client(userInfo.ConnectionId).SendAsync("ForceLogout", new
                {
                    reason = reason,
                    timestamp = DateTime.Now
                });
            }
        }

        /// <summary>
        /// Khi user kết nối (Override)
        /// </summary>
        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            var userName = Context.User?.FindFirstValue(ClaimTypes.Name) ?? "Unknown";
            var userRole = Context.User?.FindFirstValue(ClaimTypes.Role);

            // Thêm user vào danh sách online
            if (!string.IsNullOrEmpty(userId))
            {
                _onlineUsers.TryAdd(Context.ConnectionId, new OnlineUserInfo
                {
                    UserId = userId,
                    UserName = userName,
                    ConnectedAt = DateTime.Now,
                    ConnectionId = Context.ConnectionId
                });
            }

            // Nếu là Librarian/Admin, thêm vào group "Librarians"
            if (userRole == "Admin" || userRole == "Librarian")
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, "Librarians");
            }

            // Broadcast số lượng user online đến tất cả clients
            await Clients.All.SendAsync("UpdateOnlineCount", _onlineUsers.Count);

            await base.OnConnectedAsync();
        }

        /// <summary>
        /// Khi user ngắt kết nối (Override)
        /// </summary>
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userRole = Context.User?.FindFirstValue(ClaimTypes.Role);

            // Xóa user khỏi danh sách online
            _onlineUsers.TryRemove(Context.ConnectionId, out _);

            // Xóa khỏi group nếu là Librarian/Admin
            if (userRole == "Admin" || userRole == "Librarian")
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Librarians");
            }

            // Broadcast số lượng user online đến tất cả clients
            await Clients.All.SendAsync("UpdateOnlineCount", _onlineUsers.Count);

            await base.OnDisconnectedAsync(exception);
        }
    }
}
