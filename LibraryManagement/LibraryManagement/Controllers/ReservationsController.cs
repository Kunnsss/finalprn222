// Controllers/ReservationsController.cs - Đặt chỗ sách
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using LibraryManagement.Services;

namespace LibraryManagement.Controllers
{
    [Authorize]
    public class ReservationsController : Controller
    {
        private readonly IBookReservationService _reservationService;

        public ReservationsController(IBookReservationService reservationService)
        {
            _reservationService = reservationService;
        }

        public async Task<IActionResult> MyReservations()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var reservations = await _reservationService.GetUserReservationsAsync(userId);
            return View(reservations);
        }

        [HttpPost]
        public async Task<IActionResult> Reserve(int bookId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var reservation = await _reservationService.ReserveBookAsync(userId, bookId);

            if (reservation == null)
            {
                TempData["ErrorMessage"] = "Không thể đặt chỗ sách này.";
                return RedirectToAction("Details", "Books", new { id = bookId });
            }

            TempData["SuccessMessage"] = "Đặt chỗ thành công! Chúng tôi sẽ thông báo khi sách có sẵn.";
            return RedirectToAction(nameof(MyReservations));
        }

        [HttpPost]
        public async Task<IActionResult> Cancel(int reservationId)
        {
            var success = await _reservationService.CancelReservationAsync(reservationId);

            if (success)
                TempData["SuccessMessage"] = "Hủy đặt chỗ thành công!";
            else
                TempData["ErrorMessage"] = "Không thể hủy đặt chỗ.";

            return RedirectToAction(nameof(MyReservations));
        }
    }
}