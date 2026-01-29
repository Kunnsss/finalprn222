// Controllers/DashboardController.cs - VỚI SYSTEM ANNOUNCEMENTS
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using LibraryManagement.Data;
using LibraryManagement.Hubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using LibraryManagement.Models.ViewModels;
using LibraryManagement.Models;

namespace LibraryManagement.Controllers
{
    [Authorize(Roles = "Admin,Librarian")]
    public class DashboardController : Controller
    {
        private readonly LibraryDbContext _context;
        private readonly IHubContext<LibraryHub> _hubContext;

        public DashboardController(LibraryDbContext context, IHubContext<LibraryHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        public async Task<IActionResult> Index()
        {
            var viewModel = new DashboardViewModel
            {
                TotalBooks = await _context.Books.CountAsync(),
                TotalUsers = await _context.Users.CountAsync(),
                ActiveRentals = await _context.RentalTransactions
                    .CountAsync(r => r.Status == "Renting"),
                OverdueRentals = await _context.RentalTransactions
                    .CountAsync(r => r.Status == "Overdue"),
                TodayRentals = await _context.RentalTransactions
                    .CountAsync(r => r.RentalDate.Date == DateTime.Today),
                TotalRevenue = await _context.RentalTransactions
                    .Where(r => r.Status == "Returned")
                    .SumAsync(r => r.TotalAmount),
                PendingReservations = await _context.BookReservations
                    .CountAsync(r => r.Status == "Pending"),

                // Thống kê sự kiện
                TotalEvents = await _context.LibraryEvents.CountAsync(e => e.IsActive),
                UpcomingEvents = await _context.LibraryEvents
                    .CountAsync(e => e.IsActive && e.EventDate >= DateTime.Now),
                PastEvents = await _context.LibraryEvents
                    .CountAsync(e => e.IsActive && e.EventDate < DateTime.Now),
                TotalEventRegistrations = await _context.EventRegistrations
                    .CountAsync(r => r.Status == "Registered")
            };

            return View(viewModel);
        }

        /// <summary>
        /// API cho biểu đồ realtime
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetRealtimeStats()
        {
            try
            {
                var stats = new
                {
                    onlineUsers = LibraryHub.GetOnlineCount(),
                    activeRentals = await _context.RentalTransactions
                        .CountAsync(r => r.Status == "Renting"),
                    todayRentals = await _context.RentalTransactions
                        .CountAsync(r => r.RentalDate.Date == DateTime.Today),
                    availableBooks = await _context.Books
                        .Where(b => b.IsPhysical)
                        .SumAsync(b => b.AvailableQuantity)
                };

                return Json(stats);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error GetRealtimeStats: {ex.Message}");
                return Json(new
                {
                    onlineUsers = 0,
                    activeRentals = 0,
                    todayRentals = 0,
                    availableBooks = 0
                });
            }
        }

        /// <summary>
        /// Broadcast thông báo toàn hệ thống - Nhận từ JSON body
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> BroadcastMessage([FromBody] BroadcastMessageRequest request)
        {
            try
            {
                Console.WriteLine($"=== BROADCAST MESSAGE ===");
                Console.WriteLine($"Title: {request.Title}");
                Console.WriteLine($"Message: {request.Message}");
                Console.WriteLine($"Type: {request.Type}");

                if (string.IsNullOrEmpty(request.Title) || string.IsNullOrEmpty(request.Message))
                {
                    return Json(new { success = false, message = "Tiêu đề và nội dung không được để trống" });
                }

                // Gửi thông báo qua SignalR
                await _hubContext.Clients.All.SendAsync("SystemMessage", new
                {
                    title = request.Title,
                    message = request.Message,
                    type = request.Type,
                    timestamp = DateTime.Now
                });

                Console.WriteLine("Broadcast message sent successfully");

                return Json(new { success = true, message = "Thông báo đã được gửi đến tất cả người dùng" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error broadcasting: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        /// <summary>
        /// Lấy danh sách người dùng đang online
        /// </summary>
        [HttpGet]
        public IActionResult GetOnlineUsers()
        {
            try
            {
                var onlineUsers = LibraryHub.GetOnlineUsers();
                var count = LibraryHub.GetOnlineCount();

                Console.WriteLine($"Online users count: {count}");

                return Json(new
                {
                    count = count,
                    users = onlineUsers
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error GetOnlineUsers: {ex.Message}");
                return Json(new
                {
                    count = 0,
                    users = new List<object>()
                });
            }
        }

        /// <summary>
        /// Lấy danh sách sự kiện sắp diễn ra
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetUpcomingEvents()
        {
            try
            {
                var upcomingEvents = await _context.LibraryEvents
                    .Where(e => e.IsActive && e.EventDate >= DateTime.Now)
                    .OrderBy(e => e.EventDate)
                    .Take(5)
                    .Select(e => new
                    {
                        eventId = e.EventId,
                        eventName = e.EventName,
                        description = e.Description,
                        eventDate = e.EventDate,
                        location = e.Location,
                        maxParticipants = e.MaxParticipants,
                        currentParticipants = e.CurrentParticipants,
                        availableSlots = e.MaxParticipants - e.CurrentParticipants,
                        creatorName = e.Creator != null ? e.Creator.Username : "Unknown"
                    })
                    .ToListAsync();

                return Json(new { success = true, events = upcomingEvents });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error GetUpcomingEvents: {ex.Message}");
                return Json(new { success = false, events = new List<object>() });
            }
        }

        /// <summary>
        /// Lấy thống kê sự kiện
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetEventStats()
        {
            try
            {
                var totalEvents = await _context.LibraryEvents.CountAsync(e => e.IsActive);
                var upcomingEvents = await _context.LibraryEvents
                    .CountAsync(e => e.IsActive && e.EventDate >= DateTime.Now);
                var pastEvents = await _context.LibraryEvents
                    .CountAsync(e => e.IsActive && e.EventDate < DateTime.Now);
                var totalRegistrations = await _context.EventRegistrations
                    .CountAsync(r => r.Status == "Registered");
                var totalAttended = await _context.EventRegistrations
                    .CountAsync(r => r.Status == "Attended");

                // Top 5 sự kiện có nhiều người đăng ký nhất
                var topEvents = await _context.LibraryEvents
                    .Where(e => e.IsActive)
                    .OrderByDescending(e => e.CurrentParticipants)
                    .Take(5)
                    .Select(e => new
                    {
                        eventName = e.EventName,
                        participants = e.CurrentParticipants
                    })
                    .ToListAsync();

                return Json(new
                {
                    success = true,
                    totalEvents,
                    upcomingEvents,
                    pastEvents,
                    totalRegistrations,
                    totalAttended,
                    topEvents
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error GetEventStats: {ex.Message}");
                return Json(new { success = false });
            }
        }

        /// <summary>
        /// Tạo sự kiện mới nhanh từ dashboard
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin,Librarian")]
        public async Task<IActionResult> CreateQuickEvent([FromBody] QuickEventRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.EventName))
                {
                    return Json(new { success = false, message = "Tên sự kiện không được để trống" });
                }

                var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                {
                    return Json(new { success = false, message = "Không thể xác định người tạo" });
                }

                var newEvent = new LibraryEvent
                {
                    EventName = request.EventName,
                    Description = request.Description,
                    EventDate = request.EventDate,
                    Location = request.Location,
                    MaxParticipants = request.MaxParticipants,
                    CurrentParticipants = 0,
                    CreatedBy = userId,
                    CreatedDate = DateTime.Now,
                    IsActive = true
                };

                _context.LibraryEvents.Add(newEvent);
                await _context.SaveChangesAsync();

                // Broadcast thông báo sự kiện mới
                await _hubContext.Clients.All.SendAsync("NewEventCreated", new
                {
                    eventId = newEvent.EventId,
                    eventName = newEvent.EventName,
                    eventDate = newEvent.EventDate,
                    message = $"Sự kiện mới: {newEvent.EventName} - {newEvent.EventDate:dd/MM/yyyy HH:mm}"
                });

                return Json(new { success = true, message = "Đã tạo sự kiện thành công", eventId = newEvent.EventId });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error CreateQuickEvent: {ex.Message}");
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        /// <summary>
        /// Hủy sự kiện
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin,Librarian")]
        public async Task<IActionResult> CancelEvent([FromBody] CancelEventRequest request)
        {
            try
            {
                var evt = await _context.LibraryEvents.FindAsync(request.EventId);
                if (evt == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy sự kiện" });
                }

                evt.IsActive = false;
                await _context.SaveChangesAsync();

                // Thông báo hủy sự kiện
                await _hubContext.Clients.All.SendAsync("EventCancelled", new
                {
                    eventId = evt.EventId,
                    eventName = evt.EventName,
                    reason = request.Reason
                });

                return Json(new { success = true, message = "Đã hủy sự kiện" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error CancelEvent: {ex.Message}");
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        /// <summary>
        /// Lấy danh sách người đăng ký sự kiện
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetEventRegistrations(int eventId)
        {
            try
            {
                var registrations = await _context.EventRegistrations
                    .Where(r => r.EventId == eventId)
                    .Include(r => r.User)
                    .Select(r => new
                    {
                        registrationId = r.RegistrationId,
                        userId = r.UserId,
                        userName = r.User != null ? r.User.Username : "Unknown",
                        email = r.User != null ? r.User.Email : "",
                        registrationDate = r.RegistrationDate,
                        status = r.Status
                    })
                    .ToListAsync();

                return Json(new { success = true, registrations });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error GetEventRegistrations: {ex.Message}");
                return Json(new { success = false, registrations = new List<object>() });
            }
        }

        // ========================================
        // SYSTEM ANNOUNCEMENTS MANAGEMENT
        // ========================================
        /// <summary>
        /// Lấy danh sách thông báo hệ thống - FIX
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAnnouncements()
        {
            try
            {
                Console.WriteLine("=== GetAnnouncements Debug ===");

                // Query tất cả announcements để debug
                var allAnnouncements = await _context.SystemAnnouncements.ToListAsync();
                Console.WriteLine($"Total announcements in DB: {allAnnouncements.Count}");

                // Query chỉ active
                var activeAnnouncements = allAnnouncements.Where(a => a.IsActive).ToList();
                Console.WriteLine($"Active announcements: {activeAnnouncements.Count}");

                // Query với expiry check
                var now = DateTime.Now;
                var validAnnouncements = activeAnnouncements
                    .Where(a => a.ExpiryDate == null || a.ExpiryDate >= now)
                    .ToList();
                Console.WriteLine($"Valid announcements: {validAnnouncements.Count}");

                // Load Creator separately để tránh lỗi
                var announcements = new List<object>();
                foreach (var a in validAnnouncements.OrderByDescending(x => x.CreatedDate).Take(10))
                {
                    var creator = await _context.Users.FindAsync(a.CreatedBy);
                    announcements.Add(new
                    {
                        announcementId = a.AnnouncementId,
                        title = a.Title,
                        content = a.Content,
                        type = a.Type,
                        createdBy = creator?.Username ?? "Unknown",
                        createdDate = a.CreatedDate,
                        expiryDate = a.ExpiryDate,
                        isActive = a.IsActive
                    });
                }

                Console.WriteLine($"Returning {announcements.Count} announcements");
                return Json(new { success = true, announcements });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error GetAnnouncements: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                return Json(new { success = false, message = ex.Message, announcements = new List<object>() });
            }
        }

        /// <summary>
        /// Tạo thông báo hệ thống mới
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateAnnouncement([FromBody] CreateAnnouncementRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.Title) || string.IsNullOrEmpty(request.Content))
                {
                    return Json(new { success = false, message = "Tiêu đề và nội dung không được để trống" });
                }

                var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                {
                    return Json(new { success = false, message = "Không thể xác định người tạo" });
                }

                var announcement = new SystemAnnouncement
                {
                    Title = request.Title,
                    Content = request.Content,
                    Type = request.Type,
                    CreatedBy = userId,
                    CreatedDate = DateTime.Now,
                    ExpiryDate = request.ExpiryDate,
                    IsActive = true
                };

                _context.SystemAnnouncements.Add(announcement);
                await _context.SaveChangesAsync();

                // Broadcast thông báo ngay lập tức
                await _hubContext.Clients.All.SendAsync("SystemMessage", new
                {
                    title = request.Title,
                    message = request.Content,
                    type = request.Type,
                    timestamp = DateTime.Now
                });

                return Json(new { success = true, message = "Đã tạo thông báo hệ thống", announcementId = announcement.AnnouncementId });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error CreateAnnouncement: {ex.Message}");
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        /// <summary>
        /// Xóa/Vô hiệu hóa thông báo hệ thống
        /// </summary>
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteAnnouncement([FromBody] DeleteAnnouncementRequest request)
        {
            try
            {
                var announcement = await _context.SystemAnnouncements.FindAsync(request.AnnouncementId);
                if (announcement == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy thông báo" });
                }

                announcement.IsActive = false;
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Đã xóa thông báo" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error DeleteAnnouncement: {ex.Message}");
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }
    }

    // Request Models
    public class BroadcastMessageRequest
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }

    public class QuickEventRequest
    {
        public string EventName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime EventDate { get; set; }
        public string? Location { get; set; }
        public int MaxParticipants { get; set; } = 50;
    }

    public class CancelEventRequest
    {
        public int EventId { get; set; }
        public string Reason { get; set; } = string.Empty;
    }

    public class CreateAnnouncementRequest
    {
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string Type { get; set; } = "Info"; // Info, Warning, Alert
        public DateTime? ExpiryDate { get; set; }
    }

    public class DeleteAnnouncementRequest
    {
        public int AnnouncementId { get; set; }
    }
}