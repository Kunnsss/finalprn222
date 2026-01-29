// Controllers/EventsController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Hubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;

namespace LibraryManagement.Controllers
{
    [Authorize]
    public class EventsController : Controller
    {
        private readonly LibraryDbContext _context;
        private readonly IHubContext<LibraryHub> _hubContext;

        public EventsController(LibraryDbContext context, IHubContext<LibraryHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        /// <summary>
        /// Trang danh sách sự kiện cho User
        /// </summary>
        public async Task<IActionResult> Index(string filter = "all")
        {
            var query = _context.LibraryEvents
                .Include(e => e.Creator)
                .Where(e => e.IsActive);

            // Lọc theo filter
            switch (filter.ToLower())
            {
                case "upcoming":
                    query = query.Where(e => e.EventDate >= DateTime.Now);
                    break;
                case "past":
                    query = query.Where(e => e.EventDate < DateTime.Now);
                    break;
                case "available":
                    query = query.Where(e => e.EventDate >= DateTime.Now && e.CurrentParticipants < e.MaxParticipants);
                    break;
                case "full":
                    query = query.Where(e => e.EventDate >= DateTime.Now && e.CurrentParticipants >= e.MaxParticipants);
                    break;
                default: // "all"
                    break;
            }

            var events = await query
                .OrderBy(e => e.EventDate)
                .ToListAsync();

            ViewBag.Filter = filter;
            return View(events);
        }

        /// <summary>
        /// Chi tiết sự kiện
        /// </summary>
        public async Task<IActionResult> Details(int id)
        {
            var evt = await _context.LibraryEvents
                .Include(e => e.Creator)
                .FirstOrDefaultAsync(e => e.EventId == id && e.IsActive);

            if (evt == null)
            {
                TempData["Error"] = "Không tìm thấy sự kiện";
                return RedirectToAction("Index");
            }

            // Kiểm tra user đã đăng ký chưa
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userIdClaim) && int.TryParse(userIdClaim, out int userId))
            {
                var registration = await _context.EventRegistrations
                    .FirstOrDefaultAsync(r => r.EventId == id && r.UserId == userId);

                ViewBag.IsRegistered = registration != null;
                ViewBag.RegistrationStatus = registration?.Status;
                ViewBag.RegistrationId = registration?.RegistrationId;
            }
            else
            {
                ViewBag.IsRegistered = false;
            }

            // Lấy danh sách người đã đăng ký (chỉ hiển thị số lượng, không hiển thị tên)
            var registeredCount = await _context.EventRegistrations
                .CountAsync(r => r.EventId == id && r.Status == "Registered");

            ViewBag.RegisteredCount = registeredCount;

            return View(evt);
        }

        /// <summary>
        /// Đăng ký sự kiện
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(int eventId)
        {
            try
            {
                // Lấy UserId từ claims
                var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                {
                    TempData["Error"] = "Không thể xác định người dùng";
                    return RedirectToAction("Details", new { id = eventId });
                }

                // Kiểm tra sự kiện có tồn tại không
                var evt = await _context.LibraryEvents.FindAsync(eventId);
                if (evt == null || !evt.IsActive)
                {
                    TempData["Error"] = "Sự kiện không tồn tại hoặc đã bị hủy";
                    return RedirectToAction("Index");
                }

                // Kiểm tra sự kiện đã hết hạn chưa
                if (evt.EventDate < DateTime.Now)
                {
                    TempData["Error"] = "Sự kiện đã diễn ra, không thể đăng ký";
                    return RedirectToAction("Details", new { id = eventId });
                }

                // Kiểm tra đã đăng ký chưa
                var existingRegistration = await _context.EventRegistrations
                    .FirstOrDefaultAsync(r => r.EventId == eventId && r.UserId == userId);

                if (existingRegistration != null)
                {
                    if (existingRegistration.Status == "Cancelled")
                    {
                        // Cho phép đăng ký lại nếu đã hủy trước đó
                        existingRegistration.Status = "Registered";
                        existingRegistration.RegistrationDate = DateTime.Now;
                    }
                    else
                    {
                        TempData["Error"] = "Bạn đã đăng ký sự kiện này rồi";
                        return RedirectToAction("Details", new { id = eventId });
                    }
                }
                else
                {
                    // Kiểm tra còn chỗ không
                    if (evt.MaxParticipants.HasValue && evt.CurrentParticipants >= evt.MaxParticipants.Value)
                    {
                        TempData["Error"] = "Sự kiện đã đầy, không thể đăng ký";
                        return RedirectToAction("Details", new { id = eventId });
                    }

                    // Tạo đăng ký mới
                    var registration = new EventRegistration
                    {
                        EventId = eventId,
                        UserId = userId,
                        RegistrationDate = DateTime.Now,
                        Status = "Registered"
                    };

                    _context.EventRegistrations.Add(registration);
                }

                // Cập nhật số lượng người tham gia
                evt.CurrentParticipants++;
                await _context.SaveChangesAsync();

                // Thông báo qua SignalR
                await _hubContext.Clients.All.SendAsync("EventRegistrationUpdate", new
                {
                    eventId = evt.EventId,
                    currentParticipants = evt.CurrentParticipants,
                    maxParticipants = evt.MaxParticipants
                });

                TempData["Success"] = "Đăng ký sự kiện thành công!";
                return RedirectToAction("Details", new { id = eventId });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error registering event: {ex.Message}");
                TempData["Error"] = "Có lỗi xảy ra khi đăng ký: " + ex.Message;
                return RedirectToAction("Details", new { id = eventId });
            }
        }

        /// <summary>
        /// Hủy đăng ký sự kiện
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelRegistration(int registrationId)
        {
            try
            {
                var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                {
                    TempData["Error"] = "Không thể xác định người dùng";
                    return RedirectToAction("Index");
                }

                var registration = await _context.EventRegistrations
                    .Include(r => r.Event)
                    .FirstOrDefaultAsync(r => r.RegistrationId == registrationId && r.UserId == userId);

                if (registration == null)
                {
                    TempData["Error"] = "Không tìm thấy đăng ký";
                    return RedirectToAction("Index");
                }

                var evt = registration.Event;
                if (evt == null)
                {
                    TempData["Error"] = "Không tìm thấy sự kiện";
                    return RedirectToAction("Index");
                }

                // Kiểm tra sự kiện đã diễn ra chưa
                if (evt.EventDate < DateTime.Now)
                {
                    TempData["Error"] = "Không thể hủy đăng ký sự kiện đã diễn ra";
                    return RedirectToAction("Details", new { id = evt.EventId });
                }

                // Kiểm tra thời gian hủy (ví dụ: không cho hủy trước 24h)
                var hoursBefore = (evt.EventDate - DateTime.Now).TotalHours;
                if (hoursBefore < 24)
                {
                    TempData["Error"] = "Không thể hủy đăng ký trong vòng 24h trước sự kiện";
                    return RedirectToAction("Details", new { id = evt.EventId });
                }

                // Cập nhật trạng thái
                registration.Status = "Cancelled";
                evt.CurrentParticipants--;

                await _context.SaveChangesAsync();

                // Thông báo qua SignalR
                await _hubContext.Clients.All.SendAsync("EventRegistrationUpdate", new
                {
                    eventId = evt.EventId,
                    currentParticipants = evt.CurrentParticipants,
                    maxParticipants = evt.MaxParticipants
                });

                TempData["Success"] = "Đã hủy đăng ký thành công";
                return RedirectToAction("Details", new { id = evt.EventId });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error cancelling registration: {ex.Message}");
                TempData["Error"] = "Có lỗi xảy ra: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        /// <summary>
        /// Danh sách sự kiện đã đăng ký của user
        /// </summary>
        public async Task<IActionResult> MyEvents()
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                TempData["Error"] = "Không thể xác định người dùng";
                return RedirectToAction("Index");
            }

            var myRegistrations = await _context.EventRegistrations
                .Include(r => r.Event)
                .ThenInclude(e => e.Creator)
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.RegistrationDate)
                .ToListAsync();

            return View(myRegistrations);
        }

        /// <summary>
        /// API: Lấy danh sách sự kiện (cho AJAX)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetEvents(string filter = "upcoming")
        {
            try
            {
                var query = _context.LibraryEvents
                    .Where(e => e.IsActive);

                switch (filter.ToLower())
                {
                    case "upcoming":
                        query = query.Where(e => e.EventDate >= DateTime.Now);
                        break;
                    case "past":
                        query = query.Where(e => e.EventDate < DateTime.Now);
                        break;
                }

                var events = await query
                    .OrderBy(e => e.EventDate)
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
                        isFull = e.CurrentParticipants >= e.MaxParticipants
                    })
                    .ToListAsync();

                return Json(new { success = true, events });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error GetEvents: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }
    }
}