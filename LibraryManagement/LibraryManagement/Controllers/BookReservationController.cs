// Controllers/BookReservationController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using LibraryManagement.Services;
using LibraryManagement.Models;
using LibraryManagement.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace LibraryManagement.Controllers
{
    [Authorize]
    public class BookReservationController : Controller
    {
        private readonly IBookReservationService _reservationService;
        private readonly LibraryDbContext _context;

        public BookReservationController(
            IBookReservationService reservationService,
            LibraryDbContext context)
        {
            _reservationService = reservationService;
            _context = context;
        }

        #region User Actions

        // Xem danh sách đặt chỗ của user
        public async Task<IActionResult> MyReservations(int page = 1, int pageSize = 8)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
            var reservations = await _reservationService.GetUserReservationsAsync(userId);
            var list = reservations.OrderByDescending(r => r.ReservationDate).ToList();

            int total = list.Count;
            int totalPages = (int)Math.Ceiling(total / (double)pageSize);
            if (page < 1) page = 1;
            if (page > totalPages && totalPages > 0) page = totalPages;

            var paged = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalItems = total;
            ViewBag.TotalPages = totalPages;
            return View(paged);
        }

        // Đặt chỗ sách
        [HttpPost]
        public async Task<IActionResult> Reserve(int bookId)
        {
            var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

            // Kiểm tra chi tiết trước khi gọi service
            var book = await _context.Books.FindAsync(bookId);

            if (book == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy sách!";
                return RedirectToAction("Details", "Books", new { id = bookId });
            }

            if (!book.IsPhysical)
            {
                TempData["ErrorMessage"] = "Chỉ có thể đặt chỗ sách vật lý!";
                return RedirectToAction("Details", "Books", new { id = bookId });
            }

            if (book.AvailableQuantity > 0)
            {
                TempData["ErrorMessage"] = "Sách vẫn còn! Bạn có thể thuê trực tiếp.";
                return RedirectToAction("Details", "Books", new { id = bookId });
            }

            // Kiểm tra đã đặt chỗ chưa
            var existingReservation = await _context.BookReservations
                .FirstOrDefaultAsync(r => r.BookId == bookId
                                       && r.UserId == userId
                                       && r.Status == "Pending");

            if (existingReservation != null)
            {
                TempData["ErrorMessage"] = "Bạn đã đặt chỗ sách này rồi!";
                return RedirectToAction("MyReservations");
            }

            // Gọi service để đặt chỗ
            var reservation = await _reservationService.ReserveBookAsync(userId, bookId);

            if (reservation == null)
            {
                TempData["ErrorMessage"] = "Có lỗi xảy ra khi đặt chỗ!";
            }
            else
            {
                TempData["SuccessMessage"] = "Đặt chỗ sách thành công! Bạn sẽ được thông báo khi sách có sẵn.";
                return RedirectToAction("MyReservations");
            }

            return RedirectToAction("Details", "Books", new { id = bookId });
        }

        // Hủy đặt chỗ
        [HttpPost]
        public async Task<IActionResult> Cancel(int reservationId)
        {
            var success = await _reservationService.CancelReservationAsync(reservationId);

            if (success)
            {
                TempData["SuccessMessage"] = "Đã hủy đặt chỗ!";
            }
            else
            {
                TempData["ErrorMessage"] = "Không thể hủy đặt chỗ!";
            }

            return RedirectToAction("MyReservations");
        }

        #endregion

        #region Admin Actions

        // Quản lý đặt chỗ (Admin/Librarian)
        [Authorize(Roles = "Admin,Librarian")]
        public async Task<IActionResult> ManageReservations()
        {
            try
            {
                var reservations = await _reservationService.GetAllReservationsAsync();
                return View(reservations ?? new List<BookReservation>());
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Không thể tải danh sách đặt chỗ.";
                return View(new List<BookReservation>());
            }
        }

        // POST: Đánh dấu sách sẵn sàng
        [HttpPost]
        [Authorize(Roles = "Admin,Librarian")]
        public async Task<IActionResult> MarkAsReady(int id)
        {
            try
            {
                var success = await _reservationService.MarkAsReadyAsync(id);

                if (success)
                {
                    return Ok(new { message = "Đã đánh dấu sẵn sàng" });
                }

                var reservation = await _context.BookReservations
                    .FirstOrDefaultAsync(r => r.ReservationId == id);

                if (reservation == null)
                {
                    return NotFound(new { message = "Không tìm thấy đặt chỗ" });
                }

                return BadRequest(new
                {
                    message = $"Không thể đánh dấu sẵn sàng. ReservationId={id} hiện tại đang ở trạng thái '{reservation.Status}'."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // POST: Đánh dấu đã hoàn thành
        [HttpPost]
        [Authorize(Roles = "Admin,Librarian")]
        public async Task<IActionResult> MarkAsCompleted(int id)
        {
            try
            {
                var success = await _reservationService.MarkAsCompletedAsync(id);

                if (success)
                {
                    return Ok(new { message = "Đã đánh dấu hoàn thành" });
                }

                var reservation = await _context.BookReservations
                    .Include(r => r.Book)
                    .FirstOrDefaultAsync(r => r.ReservationId == id);

                if (reservation == null)
                {
                    return NotFound(new { message = "Không tìm thấy đặt chỗ" });
                }

                var availableQuantity = reservation.Book?.AvailableQuantity ?? 0;
                return BadRequest(new
                {
                    message = $"Không thể đánh dấu hoàn thành. ReservationId={id} hiện tại '{reservation.Status}', AvailableQuantity của sách={availableQuantity}."
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // POST: Hủy đặt chỗ (bởi admin)
        [HttpPost]
        [Authorize(Roles = "Admin,Librarian")]
        public async Task<IActionResult> AdminCancel(int id, [FromBody] CancelRequest request)
        {
            try
            {
                var success = await _reservationService.AdminCancelReservationAsync(
                    id,
                    request?.Reason ?? "Hủy bởi quản trị viên");

                if (success)
                {
                    return Ok(new { message = "Đã hủy đặt chỗ" });
                }

                return BadRequest(new { message = "Không thể hủy đặt chỗ" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // POST: Gửi thông báo nhắc nhở
        [HttpPost]
        [Authorize(Roles = "Admin,Librarian")]
        public async Task<IActionResult> SendReminder(int id)
        {
            try
            {
                var success = await _reservationService.SendReminderAsync(id);

                if (success)
                {
                    return Ok(new { message = "Đã gửi thông báo nhắc nhở" });
                }

                return BadRequest(new { message = "Không thể gửi thông báo" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // GET: Lấy chi tiết đặt chỗ (AJAX)
        [HttpGet]
        [Authorize(Roles = "Admin,Librarian")]
        public async Task<IActionResult> GetReservationDetails(int id)
        {
            try
            {
                var reservation = await _reservationService.GetReservationByIdAsync(id);

                if (reservation == null)
                {
                    return NotFound(new { message = "Không tìm thấy đặt chỗ" });
                }

                var result = new
                {
                    userName = reservation.User?.FullName ?? "N/A",
                    userEmail = reservation.User?.Email ?? "N/A",
                    userPhone = reservation.User?.PhoneNumber ?? "N/A",
                    bookTitle = reservation.Book?.Title ?? "N/A",
                    bookAuthor = reservation.Book?.Author ?? "N/A",
                    availableQuantity = reservation.Book?.AvailableQuantity ?? 0,
                    reservationDate = reservation.ReservationDate,
                    expiryDate = reservation.ExpiryDate,
                    status = reservation.Status,
                    notifiedDate = reservation.NotifiedDate
                };

                return Json(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        #endregion
    }

    // Model cho request hủy đặt chỗ
    public class CancelRequest
    {
        public string? Reason { get; set; }
    }
}