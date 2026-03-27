// Services/BookReservationService.cs - Full Implementation
using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagement.Services
{
    public class BookReservationService : IBookReservationService
    {
        private readonly LibraryDbContext _context;
        private readonly INotificationService _notificationService;
        private readonly IHubContext<LibraryHub> _hubContext;

        public BookReservationService(
            LibraryDbContext context,
            INotificationService notificationService,
            IHubContext<LibraryHub> hubContext)
        {
            _context = context;
            _notificationService = notificationService;
            _hubContext = hubContext;
        }

        #region User Functions

        public async Task<BookReservation?> ReserveBookAsync(int userId, int bookId)
        {
            var book = await _context.Books.FindAsync(bookId);
            if (book == null || !book.IsPhysical) return null;

            // Kiểm tra đã đặt chỗ chưa
            var existing = await _context.BookReservations
                .FirstOrDefaultAsync(r => r.UserId == userId && r.BookId == bookId && r.Status == "Pending");

            if (existing != null) return null;

            var reservation = new BookReservation
            {
                UserId = userId,
                BookId = bookId,
                ReservationDate = DateTime.Now,
                ExpiryDate = DateTime.Now.AddDays(3),
                Status = "Pending"
            };

            _context.BookReservations.Add(reservation);
            await _context.SaveChangesAsync();

            await _notificationService.SendNotificationAsync(userId,
                $"Bạn đã đặt chỗ sách '{book.Title}' thành công. Chúng tôi sẽ thông báo khi sách có sẵn.",
                "Success");

            return reservation;
        }

        public async Task<bool> CancelReservationAsync(int reservationId)
        {
            var reservation = await _context.BookReservations.FindAsync(reservationId);
            if (reservation == null || reservation.Status != "Pending") return false;

            reservation.Status = "Cancelled";
            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<List<BookReservation>> GetUserReservationsAsync(int userId)
        {
            return await _context.BookReservations
                .Include(r => r.Book)
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.ReservationDate)
                .ToListAsync();
        }

        public async Task<bool> ProcessReservationWhenBookReturnedAsync(int bookId)
        {
            var book = await _context.Books.FindAsync(bookId);
            if (book == null) return false;

            var firstReservation = await _context.BookReservations
                .Include(r => r.User)
                .Where(r => r.BookId == bookId && r.Status == "Pending")
                .OrderBy(r => r.ReservationDate)
                .FirstOrDefaultAsync();

            if (firstReservation != null)
            {
                firstReservation.Status = "Ready";
                firstReservation.NotifiedDate = DateTime.Now;
                await _context.SaveChangesAsync();

                await _hubContext.Clients.Group(firstReservation.UserId.ToString())
                    .SendAsync("BookNowAvailable", new
                    {
                        BookId = bookId,
                        BookTitle = book.Title,
                        Message = $"Sách '{book.Title}' bạn đặt chỗ đã có sẵn! Vui lòng đến lấy trong 3 ngày."
                    });

                await _notificationService.SendNotificationAsync(
                    firstReservation.UserId,
                    $"Sách '{book.Title}' bạn đặt chỗ đã có sẵn! Vui lòng đến lấy trong 3 ngày.",
                    "BookAvailable");

                return true;
            }

            await _hubContext.Clients.Group($"WaitingFor_{bookId}")
                .SendAsync("BookNowAvailable", new
                {
                    BookId = bookId,
                    BookTitle = book.Title,
                    Message = $"Sách '{book.Title}' đã có sẵn!"
                });

            return false;
        }

        #endregion

        #region Admin Functions

        public async Task<List<BookReservation>> GetAllReservationsAsync()
        {
            return await _context.BookReservations
                .Include(r => r.User)
                .Include(r => r.Book)
                .OrderByDescending(r => r.ReservationDate)
                .ToListAsync();
        }

        public async Task<BookReservation?> GetReservationByIdAsync(int reservationId)
        {
            return await _context.BookReservations
                .Include(r => r.User)
                .Include(r => r.Book)
                .FirstOrDefaultAsync(r => r.ReservationId == reservationId);
        }

        public async Task<bool> MarkAsReadyAsync(int reservationId)
        {
            var reservation = await _context.BookReservations
                .Include(r => r.User)
                .Include(r => r.Book)
                .FirstOrDefaultAsync(r => r.ReservationId == reservationId);

            if (reservation == null || reservation.Status != "Pending")
                return false;

            reservation.Status = "Ready";
            reservation.NotifiedDate = DateTime.Now;

            await _context.SaveChangesAsync();

            // Gửi thông báo realtime
            await _hubContext.Clients.Group(reservation.UserId.ToString())
                .SendAsync("BookNowAvailable", new
                {
                    BookId = reservation.BookId,
                    BookTitle = reservation.Book?.Title,
                    Message = $"Sách '{reservation.Book?.Title}' đã sẵn sàng! Vui lòng đến lấy trong 3 ngày."
                });

            // Gửi notification
            await _notificationService.SendNotificationAsync(
                reservation.UserId,
                $"Sách '{reservation.Book?.Title}' đã sẵn sàng! Vui lòng đến lấy trong 3 ngày.",
                "BookReady");

            return true;
        }

        public async Task<bool> MarkAsCompletedAsync(int reservationId)
        {
            var reservation = await _context.BookReservations
                .Include(r => r.Book)
                .FirstOrDefaultAsync(r => r.ReservationId == reservationId);

            if (reservation == null || reservation.Status != "Ready")
                return false;

            var book = reservation.Book;
            if (book == null || !book.IsPhysical || book.AvailableQuantity <= 0)
                return false;

            // Khi admin xác nhận user đã đến lấy sách:
            // - Tạo RentalTransaction với RentalDate là thời điểm Completed
            // - DueDate mặc định = +7 ngày
            // - Trừ 1 bản copy sách và set Status Rental = Renting
            var rentalDate = DateTime.Now;
            const int rentalDays = 7;

            var transaction = new RentalTransaction
            {
                UserId = reservation.UserId,
                BookId = reservation.BookId,
                RentalDate = rentalDate,
                DueDate = rentalDate.AddDays(rentalDays),
                RentalPrice = book.RentalPrice,
                TotalAmount = rentalDays * book.RentalPrice,
                LateFee = 0,
                Status = "Renting"
            };

            // Giảm số lượng sách khả dụng khi đã cho user thuê
            book.AvailableQuantity -= 1;

            reservation.Status = "Completed";

            _context.RentalTransactions.Add(transaction);
            await _context.SaveChangesAsync();

            // Realtime update số lượng khả dụng (để UI Books/Details cập nhật ngay)
            await _hubContext.Clients.All.SendAsync("BookAvailabilityUpdated", new
            {
                BookId = reservation.BookId,
                AvailableQuantity = book.AvailableQuantity
            });

            return true;
        }

        public async Task<bool> AdminCancelReservationAsync(int reservationId, string reason)
        {
            var reservation = await _context.BookReservations
                .Include(r => r.User)
                .Include(r => r.Book)
                .FirstOrDefaultAsync(r => r.ReservationId == reservationId);

            if (reservation == null)
                return false;

            reservation.Status = "Cancelled";
            await _context.SaveChangesAsync();

            // Gửi thông báo cho user
            await _notificationService.SendNotificationAsync(
                reservation.UserId,
                $"Đặt chỗ sách '{reservation.Book?.Title}' đã bị hủy. Lý do: {reason}",
                "ReservationCancelled");

            return true;
        }

        public async Task<bool> SendReminderAsync(int reservationId)
        {
            var reservation = await _context.BookReservations
                .Include(r => r.User)
                .Include(r => r.Book)
                .FirstOrDefaultAsync(r => r.ReservationId == reservationId);

            if (reservation == null || reservation.Status != "Ready")
                return false;

            // Gửi thông báo nhắc nhở
            await _notificationService.SendNotificationAsync(
                reservation.UserId,
                $"Nhắc nhở: Sách '{reservation.Book?.Title}' đang chờ bạn đến lấy. Vui lòng đến trước khi hết hạn!",
                "Reminder");

            // Gửi realtime notification
            await _hubContext.Clients.Group(reservation.UserId.ToString())
                .SendAsync("ReminderNotification", new
                {
                    BookTitle = reservation.Book?.Title,
                    ExpiryDate = reservation.ExpiryDate,
                    Message = $"Nhắc nhở: Vui lòng đến lấy sách '{reservation.Book?.Title}' trước {reservation.ExpiryDate:dd/MM/yyyy HH:mm}"
                });

            return true;
        }

        #endregion
    }
}