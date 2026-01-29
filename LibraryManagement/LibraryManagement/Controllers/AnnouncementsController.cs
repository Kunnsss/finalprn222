// Controllers/AnnouncementsController.cs - Xem thông báo hệ thống
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using LibraryManagement.Data;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagement.Controllers
{
    [Authorize]
    public class AnnouncementsController : Controller
    {
        private readonly LibraryDbContext _context;

        public AnnouncementsController(LibraryDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Trang danh sách thông báo hệ thống cho User
        /// </summary>
        public async Task<IActionResult> Index()
        {
            var announcements = await _context.SystemAnnouncements
                .Include(a => a.Creator)
                .Where(a => a.IsActive && (a.ExpiryDate == null || a.ExpiryDate >= DateTime.Now))
                .OrderByDescending(a => a.CreatedDate)
                .ToListAsync();

            return View(announcements);
        }

        /// <summary>
        /// Chi tiết thông báo
        /// </summary>
        public async Task<IActionResult> Details(int id)
        {
            var announcement = await _context.SystemAnnouncements
                .Include(a => a.Creator)
                .FirstOrDefaultAsync(a => a.AnnouncementId == id && a.IsActive);

            if (announcement == null)
            {
                TempData["Error"] = "Không tìm thấy thông báo";
                return RedirectToAction("Index");
            }

            return View(announcement);
        }

        /// <summary>
        /// API: Lấy thông báo mới nhất cho widget
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetLatestAnnouncements(int count = 5)
        {
            try
            {
                var announcements = await _context.SystemAnnouncements
                    .Include(a => a.Creator)
                    .Where(a => a.IsActive && (a.ExpiryDate == null || a.ExpiryDate >= DateTime.Now))
                    .OrderByDescending(a => a.CreatedDate)
                    .Take(count)
                    .Select(a => new
                    {
                        announcementId = a.AnnouncementId,
                        title = a.Title,
                        content = a.Content,
                        type = a.Type,
                        createdBy = a.Creator != null ? a.Creator.Username : "Admin",
                        createdDate = a.CreatedDate,
                        expiryDate = a.ExpiryDate
                    })
                    .ToListAsync();

                return Json(new { success = true, announcements });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error GetLatestAnnouncements: {ex.Message}");
                return Json(new { success = false, announcements = new List<object>() });
            }
        }
    }
}