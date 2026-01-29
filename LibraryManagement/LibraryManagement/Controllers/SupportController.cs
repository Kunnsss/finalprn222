// Controllers/SupportController.cs 
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using LibraryManagement.Data;
using LibraryManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagement.Controllers
{
    [Authorize]
    public class SupportController : Controller
    {
        private readonly LibraryDbContext _context;

        public SupportController(LibraryDbContext context)
        {
            _context = context;
        }

        // Trang chat của người dùng
        public async Task<IActionResult> Chat()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var messages = await _context.SupportChats
                .Where(c => c.UserId == userId)
                .OrderBy(c => c.SentDate)
                .ToListAsync();

            return View(messages);
        }

        // Trang quản lý chat của Librarian
        [Authorize(Roles = "Admin,Librarian")]
        public async Task<IActionResult> ManageChats()
        {
            var recentChats = await _context.SupportChats
                .Include(c => c.User)
                .Where(c => !c.IsRead && c.IsFromUser)
                .OrderByDescending(c => c.SentDate)
                .Take(50)
                .ToListAsync();

            return View(recentChats);
        }

        /// <summary>
        /// Gửi tin nhắn - Nhận JSON 
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
        {
            Console.WriteLine($"=== SEND MESSAGE DEBUG ===");
            Console.WriteLine($"Request: {System.Text.Json.JsonSerializer.Serialize(request)}");

            // Validate input
            if (string.IsNullOrWhiteSpace(request?.Message))
            {
                Console.WriteLine("Message is empty");
                return Json(new { success = false, message = "Tin nhắn không được để trống" });
            }

            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                Console.WriteLine($"UserId: {userId}");

                var chat = new SupportChat
                {
                    UserId = userId,
                    Message = request.Message,
                    IsFromUser = true,
                    SentDate = DateTime.Now,
                    IsRead = false
                };

                _context.SupportChats.Add(chat);
                await _context.SaveChangesAsync();

                Console.WriteLine($"Message saved with ChatId: {chat.ChatId}");

                return Json(new { success = true, chatId = chat.ChatId });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        /// <summary>
        /// Lấy chat của một user cụ thể (cho Admin/Librarian)
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Admin,Librarian")]
        public async Task<IActionResult> GetUserChats(int userId)
        {
            try
            {
                Console.WriteLine($"=== GET USER CHATS DEBUG ===");
                Console.WriteLine($"UserId: {userId}");

                var chats = await _context.SupportChats
                    .Include(c => c.User)
                    .Where(c => c.UserId == userId)
                    .OrderBy(c => c.SentDate)
                    .Select(c => new ChatDto
                    {
                        ChatId = c.ChatId,
                        UserId = c.UserId,
                        Message = c.Message,
                        SentDate = c.SentDate,
                        IsFromUser = c.IsFromUser,
                        IsRead = c.IsRead,
                        UserName = c.User.FullName
                    })
                    .ToListAsync();

                Console.WriteLine($"Found {chats.Count} messages");

                return Json(new { success = true, chats });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Đánh dấu chat của user là đã đọc
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin,Librarian")]
        public async Task<IActionResult> MarkChatAsRead(int userId)
        {
            try
            {
                Console.WriteLine($"=== MARK CHAT AS READ ===");
                Console.WriteLine($"UserId: {userId}");

                var chats = await _context.SupportChats
                    .Where(c => c.UserId == userId && !c.IsRead && c.IsFromUser)
                    .ToListAsync();

                Console.WriteLine($"Marking {chats.Count} messages as read");

                foreach (var chat in chats)
                {
                    chat.IsRead = true;
                }

                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Đếm số tin nhắn chưa đọc (cho Admin/Librarian)
        /// </summary>
        [HttpGet]
        [Authorize(Roles = "Admin,Librarian")]
        public async Task<IActionResult> GetUnreadChatCount()
        {
            try
            {
                var count = await _context.SupportChats
                    .CountAsync(c => !c.IsRead && c.IsFromUser);

                Console.WriteLine($"Unread count: {count}");
                return Json(count);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting unread count: {ex.Message}");
                return Json(0);
            }
        }

        /// <summary>
        /// Thủ thư gửi phản hồi (lưu vào database) 
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin,Librarian")]
        public async Task<IActionResult> SendLibrarianReply([FromBody] SendLibrarianReplyRequest request)
        {
            try
            {
                Console.WriteLine($"=== LIBRARIAN REPLY DEBUG ===");
                Console.WriteLine($"UserId: {request.UserId}, Message: {request.Message}");

                if (string.IsNullOrWhiteSpace(request?.Message))
                {
                    return Json(new { success = false, message = "Tin nhắn không được để trống" });
                }

                if (request.UserId <= 0)
                {
                    return Json(new { success = false, message = "UserId không hợp lệ" });
                }

                var librarianId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

                var chat = new SupportChat
                {
                    UserId = request.UserId,
                    LibrarianId = librarianId,
                    Message = request.Message,
                    IsFromUser = false,
                    SentDate = DateTime.Now,
                    IsRead = true
                };

                _context.SupportChats.Add(chat);
                await _context.SaveChangesAsync();

                Console.WriteLine($"Librarian reply saved with ChatId: {chat.ChatId}");

                return Json(new { success = true, chatId = chat.ChatId });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                return Json(new { success = false, message = ex.Message });
            }
        }

      

        [HttpGet]
        public async Task<IActionResult> GetAllChats()
        {
            try
            {
                var chats = await _context.SupportChats
                    .Include(c => c.User)
                    .OrderByDescending(c => c.SentDate)
                    .ToListAsync();

                var chatDtos = chats.Select(c => new
                {
                    userId = c.UserId,
                    userName = c.User?.FullName,
                    message = c.Message,
                    sentDate = c.SentDate,
                    isRead = c.IsRead,
                    isFromUser = c.IsFromUser
                }).ToList();

                return Json(new { success = true, chats = chatDtos });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }

    // DTO cho SendMessage request
    public class SendMessageRequest
    {
        public string Message { get; set; }
    }

    // DTO cho SendLibrarianReply request
    public class SendLibrarianReplyRequest
    {
        public int UserId { get; set; }
        public string Message { get; set; }
    }

    // DTO cho chat response - MỚI THÊM
    public class ChatDto
    {
        public int ChatId { get; set; }
        public int UserId { get; set; }
        public string Message { get; set; }
        public DateTime SentDate { get; set; }
        public bool IsFromUser { get; set; }
        public bool IsRead { get; set; }
        public string UserName { get; set; }
    }
}