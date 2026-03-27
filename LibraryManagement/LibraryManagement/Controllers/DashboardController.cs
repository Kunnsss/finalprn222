// Controllers/DashboardController.cs
// Requires: dotnet add package ClosedXML

using ClosedXML.Excel;
using LibraryManagement.Data;
using LibraryManagement.Hubs;
using LibraryManagement.Models;
using LibraryManagement.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Drawing;

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

        // ========================================
        // REALTIME / SIGNALR
        // ========================================

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
                return Json(new { onlineUsers = 0, activeRentals = 0, todayRentals = 0, availableBooks = 0 });
            }
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> BroadcastMessage([FromBody] BroadcastMessageRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.Title) || string.IsNullOrEmpty(request.Message))
                    return Json(new { success = false, message = "Tiêu đề và nội dung không được để trống" });

                await _hubContext.Clients.All.SendAsync("SystemMessage", new
                {
                    title = request.Title,
                    message = request.Message,
                    type = request.Type,
                    timestamp = DateTime.Now
                });

                return Json(new { success = true, message = "Thông báo đã được gửi đến tất cả người dùng" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        [HttpGet]
        public IActionResult GetOnlineUsers()
        {
            try
            {
                return Json(new
                {
                    count = LibraryHub.GetOnlineCount(),
                    users = LibraryHub.GetOnlineUsers()
                });
            }
            catch (Exception ex)
            {
                return Json(new { count = 0, users = new List<object>() });
            }
        }

        // ========================================
        // EVENTS
        // ========================================

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
                return Json(new { success = false, events = new List<object>() });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetEventStats()
        {
            try
            {
                var topEvents = await _context.LibraryEvents
                    .Where(e => e.IsActive)
                    .OrderByDescending(e => e.CurrentParticipants)
                    .Take(5)
                    .Select(e => new { eventName = e.EventName, participants = e.CurrentParticipants })
                    .ToListAsync();

                return Json(new
                {
                    success = true,
                    totalEvents = await _context.LibraryEvents.CountAsync(e => e.IsActive),
                    upcomingEvents = await _context.LibraryEvents.CountAsync(e => e.IsActive && e.EventDate >= DateTime.Now),
                    pastEvents = await _context.LibraryEvents.CountAsync(e => e.IsActive && e.EventDate < DateTime.Now),
                    totalRegistrations = await _context.EventRegistrations.CountAsync(r => r.Status == "Registered"),
                    totalAttended = await _context.EventRegistrations.CountAsync(r => r.Status == "Attended"),
                    topEvents
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateQuickEvent([FromBody] QuickEventRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.EventName))
                    return Json(new { success = false, message = "Tên sự kiện không được để trống" });

                var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                    return Json(new { success = false, message = "Không thể xác định người tạo" });

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
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CancelEvent([FromBody] CancelEventRequest request)
        {
            try
            {
                var evt = await _context.LibraryEvents.FindAsync(request.EventId);
                if (evt == null)
                    return Json(new { success = false, message = "Không tìm thấy sự kiện" });

                evt.IsActive = false;
                await _context.SaveChangesAsync();

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
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

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
                return Json(new { success = false, registrations = new List<object>() });
            }
        }

        // ========================================
        // SYSTEM ANNOUNCEMENTS
        // ========================================

        [HttpGet]
        public async Task<IActionResult> GetAnnouncements()
        {
            try
            {
                var now = DateTime.Now;
                var validAnnouncements = await _context.SystemAnnouncements
                    .Where(a => a.IsActive && (a.ExpiryDate == null || a.ExpiryDate >= now))
                    .OrderByDescending(a => a.CreatedDate)
                    .Take(10)
                    .ToListAsync();

                var announcements = new List<object>();
                foreach (var a in validAnnouncements)
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

                return Json(new { success = true, announcements });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message, announcements = new List<object>() });
            }
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateAnnouncement([FromBody] CreateAnnouncementRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.Title) || string.IsNullOrEmpty(request.Content))
                    return Json(new { success = false, message = "Tiêu đề và nội dung không được để trống" });

                var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                    return Json(new { success = false, message = "Không thể xác định người tạo" });

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
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteAnnouncement([FromBody] DeleteAnnouncementRequest request)
        {
            try
            {
                var announcement = await _context.SystemAnnouncements.FindAsync(request.AnnouncementId);
                if (announcement == null)
                    return Json(new { success = false, message = "Không tìm thấy thông báo" });

                announcement.IsActive = false;
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Đã xóa thông báo" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // ========================================
        // EXPORT TO EXCEL
        // ========================================

        /// <summary>
        /// Đếm số bản ghi mượn trả theo filter — dùng cho live preview trên UI
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetRentalCount(
            string? search,
            string? status,
            string? fromDate,
            string? toDate)
        {
            try
            {
                var query = BuildRentalQuery(search, status, fromDate, toDate);
                return Json(new { count = await query.CountAsync() });
            }
            catch (Exception ex)
            {
                return Json(new { count = 0, error = ex.Message });
            }
        }

        /// <summary>
        /// Xuất báo cáo mượn trả ra file Excel với filter và sort
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> ExportRentals(
            string? search,
            string? sortBy,
            string? sortOrder,
            string? status,
            string? fromDate,
            string? toDate)
        {
            try
            {
                // --- Lấy & lọc dữ liệu ---
                var query = BuildRentalQuery(search, status, fromDate, toDate);

                query = (sortBy?.ToLower(), sortOrder?.ToLower()) switch
                {
                    ("username", "desc") => query.OrderByDescending(r => r.User.Username),
                    ("username", _) => query.OrderBy(r => r.User.Username),
                    ("book", "desc") => query.OrderByDescending(r => r.Book.Title),
                    ("book", _) => query.OrderBy(r => r.Book.Title),
                    ("duedate", "desc") => query.OrderByDescending(r => r.DueDate),
                    ("duedate", _) => query.OrderBy(r => r.DueDate),
                    ("status", "desc") => query.OrderByDescending(r => r.Status),
                    ("status", _) => query.OrderBy(r => r.Status),
                    _ => query.OrderByDescending(r => r.RentalDate)
                };

                var rentals = await query.ToListAsync();
                // Lấy dữ liệu sách online
                var onlineRentals = await _context.OnlineRentalTransactions
                    .Include(r => r.User)
                    .Include(r => r.Book)
                    .ToListAsync();

                // --- Tạo workbook ---
                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("Mượn Trả");

                // Tiêu đề
                ws.Cell(1, 1).Value = "BÁO CÁO MƯỢN TRẢ SÁCH";
                ws.Range(1, 1, 1, 9).Merge();
                StyleTitle(ws.Cell(1, 1), "#1a5276");

                ws.Cell(2, 1).Value =
                    $"Xuất lúc: {DateTime.Now:dd/MM/yyyy HH:mm}  |  " +
                    $"Tổng: {rentals.Count} bản ghi" +
                    (string.IsNullOrEmpty(search) ? "" : $"  |  Tìm kiếm: \"{search}\"") +
                    (string.IsNullOrEmpty(status) ? "" : $"  |  Trạng thái: {status}");
                ws.Range(2, 1, 2, 9).Merge();
                ws.Cell(2, 1).Style.Font.Italic = true;
                ws.Cell(2, 1).Style.Font.FontSize = 9;

                // Header
                var headers = new[] { "STT", "Loại sách", "Người mượn", "Tên sách", "ISBN", "Ngày mượn", "Hạn trả", "Trạng thái", "Phí phạt", "Doanh thu" };
                StyleHeaderRow(ws, row: 4, headers, "#2e86c1");
                // Sửa merge tiêu đề từ 9 lên 10 cột
                ws.Range(1, 1, 1, 10).Merge();
                ws.Range(2, 1, 2, 10).Merge();

                // Data
                for (int i = 0; i < rentals.Count; i++)
                {
                    var r = rentals[i];
                    int row = i + 5;

                    ws.Cell(row, 1).Value = i + 1;
                    ws.Cell(row, 2).Value = "Vật lý";              // ✅ Thêm cột Loại sách
                    ws.Cell(row, 3).Value = r.User?.Username ?? "";
                    ws.Cell(row, 4).Value = r.Book?.Title ?? "";
                    ws.Cell(row, 5).Value = r.Book?.ISBN ?? "";
                    ws.Cell(row, 6).Value = r.RentalDate.ToString("dd/MM/yyyy");
                    ws.Cell(row, 7).Value = r.DueDate.ToString("dd/MM/yyyy");
                    ws.Cell(row, 8).Value = r.Status ?? "";
                    ws.Cell(row, 9).Value = r.LateFee > 0 ? r.LateFee.ToString("N0") + " VND" : "-";
                    ws.Cell(row, 10).Value = r.TotalAmount > 0 ? r.TotalAmount.ToString("N0") + " VND" : "-";

                    var rowColor = r.Status switch
                    {
                        "Overdue" => XLColor.FromHtml("#fadbd8"),
                        "Returned" => XLColor.FromHtml("#d5f5e3"),
                        "Renting" => XLColor.FromHtml("#d6eaf8"),
                        _ => i % 2 == 1 ? XLColor.FromHtml("#f8f9fa") : XLColor.White
                    };
                    StyleDataRow(ws, row, 10, rowColor); // ✅ 10 cột
                }

                int offsetRow = rentals.Count + 5; // tiếp nối sau sách vật lý

                for (int i = 0; i < onlineRentals.Count; i++)
                {
                    var r = onlineRentals[i];
                    int row = offsetRow + i;

                    ws.Cell(row, 1).Value = rentals.Count + i + 1;
                    ws.Cell(row, 2).Value = "Online";              // ✅ Loại sách
                    ws.Cell(row, 3).Value = r.User?.Username ?? "";
                    ws.Cell(row, 4).Value = r.Book?.Title ?? "";
                    ws.Cell(row, 5).Value = r.Book?.ISBN ?? "";
                    ws.Cell(row, 6).Value = r.PurchaseDate.ToString("dd/MM/yyyy");
                    ws.Cell(row, 7).Value = r.ExpiryDate.ToString("dd/MM/yyyy");
                    ws.Cell(row, 8).Value = r.Status ?? "";
                    ws.Cell(row, 9).Value = "-";                   // Không có phí phạt
                    ws.Cell(row, 10).Value = r.Amount > 0
                        ? r.Amount.ToString("N0") + " VND" : "-";

                    var rowColor = r.Status switch
                    {
                        "Approved" => XLColor.FromHtml("#d5f5e3"),
                        "Pending" => XLColor.FromHtml("#fef9e7"),
                        "Rejected" => XLColor.FromHtml("#fadbd8"),
                        _ => i % 2 == 1 ? XLColor.FromHtml("#f8f9fa") : XLColor.White
                    };
                    StyleDataRow(ws, row, 10, rowColor);
                }

                // Tổng kết
                int summaryRow = rentals.Count + onlineRentals.Count + 6;

                ws.Cell(summaryRow, 1).Value = "TỔNG KẾT";
                ws.Cell(summaryRow, 2).Value = $"Vật lý: {rentals.Count}";
                ws.Cell(summaryRow, 3).Value = $"Online: {onlineRentals.Count}";
                ws.Cell(summaryRow, 4).Value = $"Quá hạn: {rentals.Count(r => r.Status == "Overdue")}";
                ws.Cell(summaryRow, 5).Value = "Tổng phí phạt:";
                ws.Cell(summaryRow, 6).Value = rentals.Sum(r => r.LateFee).ToString("N0") + " VND";
                ws.Cell(summaryRow, 7).Value = "Tổng doanh thu:";
                ws.Cell(summaryRow, 8).Value = (
                    rentals.Where(r => r.Status == "Returned").Sum(r => r.TotalAmount) +
                    onlineRentals.Where(r => r.PaymentStatus == "Paid").Sum(r => r.Amount)
                ).ToString("N0") + " VND";

                var summaryRange = ws.Range(summaryRow, 1, summaryRow, 10); // ✅ 10 cột
                summaryRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#fef9e7");
                summaryRange.Style.Font.Bold = true;
                summaryRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                FinalizeWorksheet(ws);
                await AddSummarySheet(wb);
                return ExcelFileResult(wb, $"BaoCaoMuonTra_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error ExportRentals: {ex.Message}");
                return BadRequest("Lỗi xuất Excel: " + ex.Message);
            }
        }

        /// <summary>
        /// Xuất báo cáo sự kiện ra file Excel với filter và sort
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> ExportEvents(
            string? search,
            string? sortBy,
            string? sortOrder,
            string? eventStatus)
        {
            try
            {
                var now = DateTime.Now;
                var query = _context.LibraryEvents
                    //.Include(e => e.Registrations
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var kw = search.ToLower();
                    query = query.Where(e =>
                        e.EventName.ToLower().Contains(kw) ||
                        (e.Location != null && e.Location.ToLower().Contains(kw)) ||
                        (e.Description != null && e.Description.ToLower().Contains(kw)));
                }

                if (eventStatus == "upcoming")
                    query = query.Where(e => e.IsActive && e.EventDate > now);
                else if (eventStatus == "past")
                    query = query.Where(e => e.IsActive && e.EventDate <= now);
                else if (eventStatus == "cancelled")
                    query = query.Where(e => !e.IsActive);
                else
                    query = query.Where(e => e.IsActive);

                query = (sortBy?.ToLower(), sortOrder?.ToLower()) switch
                {
                    ("name", "desc") => query.OrderByDescending(e => e.EventName),
                    ("name", _) => query.OrderBy(e => e.EventName),
                    ("participants", "desc") => query.OrderByDescending(e => e.CurrentParticipants),
                    ("participants", _) => query.OrderBy(e => e.CurrentParticipants),
                    _ => query.OrderByDescending(e => e.EventDate)
                };

                var events = await query.ToListAsync();

                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("Sự Kiện");

                ws.Cell(1, 1).Value = "BÁO CÁO SỰ KIỆN THƯ VIỆN";
                ws.Range(1, 1, 1, 7).Merge();
                StyleTitle(ws.Cell(1, 1), "#1a5276");

                ws.Cell(2, 1).Value =
                    $"Xuất lúc: {DateTime.Now:dd/MM/yyyy HH:mm}  |  Tổng: {events.Count} sự kiện" +
                    (string.IsNullOrEmpty(search) ? "" : $"  |  Tìm kiếm: \"{search}\"");
                ws.Range(2, 1, 2, 7).Merge();
                ws.Cell(2, 1).Style.Font.Italic = true;
                ws.Cell(2, 1).Style.Font.FontSize = 9;

                var headers = new[] { "STT", "Tên sự kiện", "Thời gian", "Địa điểm", "Đăng ký", "Sức chứa", "Trạng thái" };
                StyleHeaderRow(ws, row: 4, headers, "#117a65");

                for (int i = 0; i < events.Count; i++)
                {
                    var e = events[i];
                    int row = i + 5;
                    var statusText = !e.IsActive ? "Đã hủy"
                        : e.EventDate > now ? "Sắp diễn ra"
                        : "Đã kết thúc";

                    var rowColor = !e.IsActive ? XLColor.FromHtml("#fadbd8")
                        : e.EventDate > now ? XLColor.FromHtml("#d5f5e3")
                        : i % 2 == 1 ? XLColor.FromHtml("#f8f9fa") : XLColor.White;

                    ws.Cell(row, 1).Value = i + 1;
                    ws.Cell(row, 2).Value = e.EventName;
                    ws.Cell(row, 3).Value = e.EventDate.ToString("dd/MM/yyyy HH:mm");
                    ws.Cell(row, 4).Value = e.Location ?? "Chưa xác định";
                    ws.Cell(row, 5).Value = e.CurrentParticipants;
                    ws.Cell(row, 6).Value = e.MaxParticipants > 0 ? e.MaxParticipants.ToString() : "Không giới hạn";
                    ws.Cell(row, 7).Value = statusText;

                    StyleDataRow(ws, row, 7, rowColor);
                }

                FinalizeWorksheet(ws);
                await AddSummarySheet(wb);
                return ExcelFileResult(wb, $"BaoCaoSuKien_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error ExportEvents: {ex.Message}");
                return BadRequest("Lỗi xuất Excel: " + ex.Message);
            }
        }

        [HttpGet]
        public async Task<IActionResult> ExportSummary()
        {
            try
            {
                // Lấy dữ liệu tổng quan
                var totalBooks = await _context.Books.CountAsync();
                var totalUsers = await _context.Users.CountAsync();
                var activeRentals = await _context.RentalTransactions.CountAsync(r => r.Status == "Renting");
                var overdueRentals = await _context.RentalTransactions.CountAsync(r => r.Status == "Overdue");
                var todayRentals = await _context.RentalTransactions.CountAsync(r => r.RentalDate.Date == DateTime.Today);
                var totalRevenue = await _context.RentalTransactions
                    .Where(r => r.Status == "Returned")
                    .SumAsync(r => r.TotalAmount);
                var pendingReservations = await _context.BookReservations.CountAsync(r => r.Status == "Pending");
                var totalEvents = await _context.LibraryEvents.CountAsync(e => e.IsActive);
                var upcomingEvents = await _context.LibraryEvents.CountAsync(e => e.IsActive && e.EventDate >= DateTime.Now);
                var pastEvents = await _context.LibraryEvents.CountAsync(e => e.IsActive && e.EventDate < DateTime.Now);
                var totalEventRegistrations = await _context.EventRegistrations.CountAsync(r => r.Status == "Registered");

                using var wb = new XLWorkbook();
                var ws = wb.Worksheets.Add("Tổng Quan");

                // Tiêu đề
                ws.Cell(1, 1).Value = "BÁO CÁO TỔNG QUAN THƯ VIỆN";
                ws.Range(1, 1, 1, 3).Merge();
                StyleTitle(ws.Cell(1, 1), "#1a5276");

                ws.Cell(2, 1).Value = $"Xuất lúc: {DateTime.Now:dd/MM/yyyy HH:mm}";
                ws.Range(2, 1, 2, 3).Merge();
                ws.Cell(2, 1).Style.Font.Italic = true;
                ws.Cell(2, 1).Style.Font.FontSize = 9;

                // ---- Section: Sách & Thành viên ----
                ws.Cell(4, 1).Value = "SÁCH & THÀNH VIÊN";
                ws.Range(4, 1, 4, 3).Merge();
                StyleTitle(ws.Cell(4, 1), "#2e86c1");

                var bookRows = new[]
                {
            ("Tổng số sách", totalBooks.ToString(), ""),
            ("Tổng thành viên", totalUsers.ToString(), ""),
            ("Đặt chỗ chờ xử lý", pendingReservations.ToString(), ""),
        };
                FillSection(ws, 5, bookRows);

                // ---- Section: Mượn trả ----
                ws.Cell(9, 1).Value = "MƯỢN TRẢ";
                ws.Range(9, 1, 9, 3).Merge();
                StyleTitle(ws.Cell(9, 1), "#117a65");

                var rentalRows = new[]
                {
            ("Đang cho thuê", activeRentals.ToString(), "giao dịch"),
            ("Mượn hôm nay", todayRentals.ToString(), "giao dịch"),
            ("Quá hạn", overdueRentals.ToString(), "giao dịch"),
            ("Tổng doanh thu", totalRevenue.ToString("N0") + " VND", "chỉ tính giao dịch đã trả"),
        };
                FillSection(ws, 10, rentalRows);

                // ---- Section: Sự kiện ----
                ws.Cell(15, 1).Value = "SỰ KIỆN";
                ws.Range(15, 1, 15, 3).Merge();
                StyleTitle(ws.Cell(15, 1), "#6f42c1");

                var eventRows = new[]
                {
            ("Tổng sự kiện", totalEvents.ToString(), ""),
            ("Sắp diễn ra", upcomingEvents.ToString(), ""),
            ("Đã diễn ra", pastEvents.ToString(), ""),
            ("Tổng đăng ký", totalEventRegistrations.ToString(), "lượt"),
        };
                FillSection(ws, 16, eventRows);

                // ---- Section: Thông báo hệ thống ----
                var now2 = DateTime.Now;
                var announcements = await _context.SystemAnnouncements
                    .Where(a => a.IsActive && (a.ExpiryDate == null || a.ExpiryDate >= now2))
                    .OrderByDescending(a => a.CreatedDate)
                    .Take(10)
                    .ToListAsync();

                ws.Cell(21, 1).Value = "THÔNG BÁO HỆ THỐNG";
                ws.Range(21, 1, 21, 5).Merge();
                StyleTitle(ws.Cell(21, 1), "#c0392b");

                if (announcements.Count == 0)
                {
                    ws.Cell(22, 1).Value = "Không có thông báo đang hoạt động";
                    ws.Range(22, 1, 22, 3).Merge();
                    ws.Cell(22, 1).Style.Font.Italic = true;
                    ws.Cell(22, 1).Style.Font.FontColor = XLColor.Gray;
                }
                else
                {
                    // Header
                    StyleHeaderRow(ws, 22, new[] { "Tiêu đề", "Loại", "Nội dung", "Ngày tạo", "Hết hạn" }, "#c0392b");
                    ws.Range(22, 1, 22, 5).Style.Font.Bold = true;

                    for (int i = 0; i < announcements.Count; i++)
                    {
                        var a = announcements[i];
                        int row = 23 + i;
                        var creator = await _context.Users.FindAsync(a.CreatedBy);
                        var bgColor = a.Type == "Alert" ? XLColor.FromHtml("#fadbd8")
                                    : a.Type == "Warning" ? XLColor.FromHtml("#fef9e7")
                                    : XLColor.FromHtml("#d6eaf8");

                        ws.Cell(row, 1).Value = a.Title;
                        ws.Cell(row, 2).Value = a.Type;
                        ws.Cell(row, 3).Value = a.Content;
                        ws.Cell(row, 4).Value = a.CreatedDate.ToString("dd/MM/yyyy HH:mm");
                        ws.Cell(row, 5).Value = a.ExpiryDate.HasValue
                            ? a.ExpiryDate.Value.ToString("dd/MM/yyyy HH:mm")
                            : "Không giới hạn";

                        for (int col = 1; col <= 5; col++)
                        {
                            ws.Cell(row, col).Style.Fill.BackgroundColor = bgColor;
                            ws.Cell(row, col).Style.Border.OutsideBorder = XLBorderStyleValues.Hair;
                            ws.Cell(row, col).Style.Font.FontName = "Arial";
                        }
                    }
                }

                ws.Columns().AdjustToContents();
                ws.Column(1).Width = 30;
                ws.Column(2).Width = 15;
                ws.Column(3).Width = 50;  // cột nội dung rộng hơn
                ws.Column(4).Width = 20;
                ws.Column(5).Width = 20;

                return ExcelFileResult(wb, $"TongQuan_{DateTime.Now:yyyyMMdd_HHmm}.xlsx");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error ExportSummary: {ex.Message}");
                return BadRequest("Lỗi xuất Excel: " + ex.Message);
            }
        }


        // ========================================
        // PRIVATE HELPERS
        // ========================================

        /// <summary>
        /// Dùng chung cho ExportRentals và GetRentalCount để tránh lặp code filter
        /// </summary>
        private IQueryable<RentalTransaction> BuildRentalQuery(
            string? search, string? status, string? fromDate, string? toDate)
        {
            var query = _context.RentalTransactions
                .Include(r => r.User)
                .Include(r => r.Book)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var kw = search.ToLower();
                query = query.Where(r =>
                    r.User.Username.ToLower().Contains(kw) ||
                    r.Book.Title.ToLower().Contains(kw) ||
                    (r.Book.ISBN != null && r.Book.ISBN.ToLower().Contains(kw)));
            }

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(r => r.Status == status);

            if (DateTime.TryParse(fromDate, out var from))
                query = query.Where(r => r.RentalDate >= from);

            if (DateTime.TryParse(toDate, out var to))
                query = query.Where(r => r.RentalDate <= to.AddDays(1));

            return query;
        }

        private static void StyleTitle(IXLCell cell, string hexColor)
        {
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontSize = 14;
            cell.Style.Font.FontName = "Arial";
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml(hexColor);
            cell.Style.Font.FontColor = XLColor.White;
        }

        private static void StyleHeaderRow(IXLWorksheet ws, int row, string[] headers, string hexColor)
        {
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontName = "Arial";
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml(hexColor);
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }
        }

        private static void StyleDataRow(IXLWorksheet ws, int row, int colCount, XLColor bgColor)
        {
            for (int col = 1; col <= colCount; col++)
            {
                ws.Cell(row, col).Style.Fill.BackgroundColor = bgColor;
                ws.Cell(row, col).Style.Border.OutsideBorder = XLBorderStyleValues.Hair;
                ws.Cell(row, col).Style.Font.FontName = "Arial";
            }
        }

        private static void FinalizeWorksheet(IXLWorksheet ws)
        {
            ws.Columns().AdjustToContents();
            ws.Column(1).Width = 6;         // STT column nhỏ hơn
            ws.SheetView.FreezeRows(4);     // Freeze header
            ws.RangeUsed()?.SetAutoFilter(); // Bật auto-filter
        }

        private static void FillSection(IXLWorksheet ws, int startRow, (string label, string value, string note)[] rows)
        {
            for (int i = 0; i < rows.Length; i++)
            {
                int row = startRow + i;
                ws.Cell(row, 1).Value = rows[i].label;
                ws.Cell(row, 2).Value = rows[i].value;
                ws.Cell(row, 3).Value = rows[i].note;

                var bgColor = i % 2 == 0 ? XLColor.FromHtml("#f8f9fa") : XLColor.White;
                for (int col = 1; col <= 3; col++)
                {
                    ws.Cell(row, col).Style.Fill.BackgroundColor = bgColor;
                    ws.Cell(row, col).Style.Border.OutsideBorder = XLBorderStyleValues.Hair;
                    ws.Cell(row, col).Style.Font.FontName = "Arial";
                }
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
            }
        }

        private async Task AddSummarySheet(XLWorkbook wb)
        {
            var ws = wb.Worksheets.Add("Tổng Quan");
            var now = DateTime.Now;

            var totalBooks = await _context.Books.CountAsync();
            var totalUsers = await _context.Users.CountAsync();
            var activeRentals = await _context.RentalTransactions.CountAsync(r => r.Status == "Renting");
            var overdueRentals = await _context.RentalTransactions.CountAsync(r => r.Status == "Overdue");
            var todayRentals = await _context.RentalTransactions.CountAsync(r => r.RentalDate.Date == DateTime.Today);
            var totalRevenue = await _context.RentalTransactions
                .Where(r => r.Status == "Returned")
                .SumAsync(r => r.TotalAmount);
            var pendingReservations = await _context.BookReservations.CountAsync(r => r.Status == "Pending");
            var totalEvents = await _context.LibraryEvents.CountAsync(e => e.IsActive);
            var upcomingEvents = await _context.LibraryEvents.CountAsync(e => e.IsActive && e.EventDate >= now);
            var pastEvents = await _context.LibraryEvents.CountAsync(e => e.IsActive && e.EventDate < now);
            var totalEventRegistrations = await _context.EventRegistrations.CountAsync(r => r.Status == "Registered");

            ws.Cell(1, 1).Value = "BÁO CÁO TỔNG QUAN THƯ VIỆN";
            ws.Range(1, 1, 1, 3).Merge();
            StyleTitle(ws.Cell(1, 1), "#1a5276");

            ws.Cell(2, 1).Value = $"Xuất lúc: {now:dd/MM/yyyy HH:mm}";
            ws.Range(2, 1, 2, 3).Merge();
            ws.Cell(2, 1).Style.Font.Italic = true;
            ws.Cell(2, 1).Style.Font.FontSize = 9;

            ws.Cell(4, 1).Value = "SÁCH & THÀNH VIÊN";
            ws.Range(4, 1, 4, 3).Merge();
            StyleTitle(ws.Cell(4, 1), "#2e86c1");
            FillSection(ws, 5, new[]
            {
        ("Tổng số sách",           totalBooks.ToString(),           ""),
        ("Tổng thành viên",        totalUsers.ToString(),           ""),
        ("Đặt chỗ chờ xử lý",     pendingReservations.ToString(),  ""),
    });

            ws.Cell(9, 1).Value = "MƯỢN TRẢ";
            ws.Range(9, 1, 9, 3).Merge();
            StyleTitle(ws.Cell(9, 1), "#117a65");
            FillSection(ws, 10, new[]
            {
        ("Đang cho thuê",   activeRentals.ToString(),                  "giao dịch"),
        ("Mượn hôm nay",    todayRentals.ToString(),                   "giao dịch"),
        ("Quá hạn",         overdueRentals.ToString(),                 "giao dịch"),
        ("Tổng doanh thu",  totalRevenue.ToString("N0") + " VND",      "chỉ tính giao dịch đã trả"),
    });

            ws.Cell(15, 1).Value = "SỰ KIỆN";
            ws.Range(15, 1, 15, 3).Merge();
            StyleTitle(ws.Cell(15, 1), "#6f42c1");
            FillSection(ws, 16, new[]
            {
        ("Tổng sự kiện",    totalEvents.ToString(),             ""),
        ("Sắp diễn ra",     upcomingEvents.ToString(),          ""),
        ("Đã diễn ra",      pastEvents.ToString(),              ""),
        ("Tổng đăng ký",    totalEventRegistrations.ToString(), "lượt"),
    });

            // Thêm section thông báo hệ thống
            var announcements = await _context.SystemAnnouncements
                .Where(a => a.IsActive && (a.ExpiryDate == null || a.ExpiryDate >= now))
                .OrderByDescending(a => a.CreatedDate)
                .Take(10)
                .ToListAsync();

            ws.Cell(21, 1).Value = "THÔNG BÁO HỆ THỐNG";
            ws.Range(21, 1, 21, 5).Merge();
            StyleTitle(ws.Cell(21, 1), "#c0392b");

            if (announcements.Count == 0)
            {
                ws.Cell(22, 1).Value = "Không có thông báo đang hoạt động";
                ws.Range(22, 1, 22, 5).Merge();
                ws.Cell(22, 1).Style.Font.Italic = true;
                ws.Cell(22, 1).Style.Font.FontColor = XLColor.Gray;
            }
            else
            {
                StyleHeaderRow(ws, 22,
                    new[] { "Tiêu đề", "Loại", "Nội dung", "Ngày tạo", "Hết hạn" },
                    "#c0392b");

                for (int i = 0; i < announcements.Count; i++)
                {
                    var a = announcements[i];
                    int row = 23 + i;
                    var bgColor = a.Type == "Alert" ? XLColor.FromHtml("#fadbd8")
                                : a.Type == "Warning" ? XLColor.FromHtml("#fef9e7")
                                : XLColor.FromHtml("#d6eaf8");

                    ws.Cell(row, 1).Value = a.Title;
                    ws.Cell(row, 2).Value = a.Type;
                    ws.Cell(row, 3).Value = a.Content;
                    ws.Cell(row, 4).Value = a.CreatedDate.ToString("dd/MM/yyyy HH:mm");
                    ws.Cell(row, 5).Value = a.ExpiryDate.HasValue
                        ? a.ExpiryDate.Value.ToString("dd/MM/yyyy HH:mm")
                        : "Không giới hạn";

                    for (int col = 1; col <= 5; col++)
                    {
                        ws.Cell(row, col).Style.Fill.BackgroundColor = bgColor;
                        ws.Cell(row, col).Style.Border.OutsideBorder = XLBorderStyleValues.Hair;
                        ws.Cell(row, col).Style.Font.FontName = "Arial";
                    }
                }
            }

            // Column width đủ 5 cột
            ws.Columns().AdjustToContents();
            ws.Column(1).Width = 30;
            ws.Column(2).Width = 15;
            ws.Column(3).Width = 50;
            ws.Column(4).Width = 20;
            ws.Column(5).Width = 20;
        }


        private FileStreamResult ExcelFileResult(XLWorkbook wb, string fileName)
        {
            var stream = new MemoryStream();
            wb.SaveAs(stream);
            stream.Position = 0;
            return File(stream,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }
    }


    // ========================================
    // REQUEST MODELS
    // ========================================

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
        public string Type { get; set; } = "Info";
        public DateTime? ExpiryDate { get; set; }
    }

    public class DeleteAnnouncementRequest
    {
        public int AnnouncementId { get; set; }
    }
}
