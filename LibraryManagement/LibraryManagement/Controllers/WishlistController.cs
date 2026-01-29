// Controllers/WishlistController.cs - Quản lý danh sách yêu thích (ĐÃ SỬA)
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using LibraryManagement.Data;
using LibraryManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagement.Controllers
{
    [Authorize]
    public class WishlistController : Controller
    {
        private readonly LibraryDbContext _context;

        public WishlistController(LibraryDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Thêm sách vào danh sách yêu thích
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Add([FromBody] WishlistRequest request)
        {
            try
            {
                // Debug logging
                Console.WriteLine($"=== WISHLIST ADD DEBUG ===");
                Console.WriteLine($"BookId received: {request?.BookId}");
                Console.WriteLine($"User authenticated: {User.Identity.IsAuthenticated}");

                if (!User.Identity.IsAuthenticated)
                {
                    return Json(new { success = false, message = "Bạn cần đăng nhập để thêm vào yêu thích" });
                }

                var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
                Console.WriteLine($"UserId claim: {userIdClaim}");

                if (string.IsNullOrEmpty(userIdClaim))
                {
                    return Json(new { success = false, message = "Không tìm thấy thông tin người dùng" });
                }

                var userId = int.Parse(userIdClaim);
                Console.WriteLine($"Parsed UserId: {userId}");

                // Kiểm tra sách có tồn tại không
                var book = await _context.Books.FindAsync(request.BookId);
                if (book == null)
                {
                    return Json(new { success = false, message = "Sách không tồn tại" });
                }

                // Kiểm tra đã có trong wishlist chưa
                var exists = await _context.Wishlists
                    .AnyAsync(w => w.UserId == userId && w.BookId == request.BookId);

                if (exists)
                {
                    return Json(new { success = false, message = "Sách đã có trong danh sách yêu thích" });
                }

                // Thêm vào wishlist
                var wishlist = new Wishlist
                {
                    UserId = userId,
                    BookId = request.BookId,
                    AddedDate = DateTime.Now
                };

                _context.Wishlists.Add(wishlist);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Đã thêm vào danh sách yêu thích!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        /// <summary>
        /// Xóa sách khỏi danh sách yêu thích
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> Remove([FromBody] WishlistRequest request)
        {
            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

                var wishlist = await _context.Wishlists
                    .FirstOrDefaultAsync(w => w.UserId == userId && w.BookId == request.BookId);

                if (wishlist == null)
                {
                    return Json(new { success = false, message = "Sách không có trong danh sách yêu thích" });
                }

                _context.Wishlists.Remove(wishlist);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Đã xóa khỏi danh sách yêu thích!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        /// <summary>
        /// Xem danh sách yêu thích
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            var wishlist = await _context.Wishlists
                .Include(w => w.Book)
                    .ThenInclude(b => b.Category)
                .Where(w => w.UserId == userId)
                .OrderByDescending(w => w.AddedDate)
                .ToListAsync();

            return View(wishlist);
        }

        /// <summary>
        /// Lấy danh sách yêu thích dạng JSON
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetWishlist()
        {
            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

                var wishlist = await _context.Wishlists
                    .Include(w => w.Book)
                    .Where(w => w.UserId == userId)
                    .Select(w => new
                    {
                        wishlistId = w.WishlistId,
                        bookId = w.BookId,
                        title = w.Book.Title,
                        author = w.Book.Author,
                        coverImage = w.Book.CoverImage,
                        addedDate = w.AddedDate.ToString("dd/MM/yyyy")
                    })
                    .ToListAsync();

                return Json(new { success = true, data = wishlist });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }
    }

    // DTO cho Wishlist request
    public class WishlistRequest
    {
        public int BookId { get; set; }
    }
}