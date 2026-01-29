// Controllers/ReviewsController.cs 
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using LibraryManagement.Data;
using LibraryManagement.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace LibraryManagement.Controllers
{
    [Authorize]
    public class ReviewsController : Controller
    {
        private readonly LibraryDbContext _context;

        public ReviewsController(LibraryDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Thêm đánh giá cho sách - trigger issue
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> AddReview(int bookId, int rating, string comment)
        {
            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

                // Kiểm tra đã review chưa
                var existingReview = await _context.BookReviews
                    .FirstOrDefaultAsync(r => r.BookId == bookId && r.UserId == userId);

                if (existingReview != null)
                {
                    return Json(new { success = false, message = "Bạn đã đánh giá sách này rồi!" });
                }

                // CÁCH 1: Sử dụng Raw SQL để tránh OUTPUT clause
                var sql = @"
                    INSERT INTO BookReviews (BookId, UserId, Rating, Comment, ReviewDate, HelpfulCount)
                    VALUES (@BookId, @UserId, @Rating, @Comment, GETDATE(), 0)";

                await _context.Database.ExecuteSqlRawAsync(sql,
                    new SqlParameter("@BookId", bookId),
                    new SqlParameter("@UserId", userId),
                    new SqlParameter("@Rating", rating),
                    new SqlParameter("@Comment", (object)comment ?? DBNull.Value));

                return Json(new { success = true, message = "Đã gửi đánh giá của bạn!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding review: {ex.Message}");
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        /// <summary>
        /// Lấy danh sách đánh giá của sách
        /// </summary>
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetBookReviews(int bookId)
        {
            try
            {
                var reviews = await _context.BookReviews
                    .Include(r => r.User)
                    .Where(r => r.BookId == bookId)
                    .OrderByDescending(r => r.ReviewDate)
                    .Select(r => new
                    {
                        reviewId = r.ReviewId,
                        userName = r.User.FullName,
                        rating = r.Rating,
                        comment = r.Comment,
                        reviewDate = r.ReviewDate.ToString("dd/MM/yyyy"),
                        helpfulCount = r.HelpfulCount
                    })
                    .ToListAsync();

                var averageRating = reviews.Any()
                    ? Math.Round(reviews.Average(r => r.rating), 1)
                    : 0;

                return Json(new
                {
                    success = true,
                    reviews = reviews,
                    averageRating = averageRating,
                    totalReviews = reviews.Count
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting reviews: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Đánh dấu review hữu ích
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> MarkHelpful([FromBody] MarkHelpfulRequest request)
        {
            try
            {
                // Sử dụng Raw SQL để tránh OUTPUT clause
                var sql = @"
                    UPDATE BookReviews 
                    SET HelpfulCount = HelpfulCount + 1 
                    WHERE ReviewId = @ReviewId";

                await _context.Database.ExecuteSqlRawAsync(sql,
                    new SqlParameter("@ReviewId", request.ReviewId));

                return Json(new { success = true, message = "Đã đánh dấu hữu ích!" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error marking helpful: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Xóa đánh giá (chỉ người tạo hoặc Admin)
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> DeleteReview(int reviewId)
        {
            try
            {
                var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var isAdmin = User.IsInRole("Admin");

                var review = await _context.BookReviews.FindAsync(reviewId);

                if (review == null)
                {
                    return Json(new { success = false, message = "Đánh giá không tồn tại" });
                }

                // Chỉ cho phép chủ review hoặc admin xóa
                if (review.UserId != userId && !isAdmin)
                {
                    return Json(new { success = false, message = "Bạn không có quyền xóa đánh giá này" });
                }

                _context.BookReviews.Remove(review);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Đã xóa đánh giá" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting review: {ex.Message}");
                return Json(new { success = false, message = ex.Message });
            }
        }
    }

    // DTOs
    public class AddReviewRequest
    {
        public int BookId { get; set; }
        public int Rating { get; set; }
        public string Comment { get; set; }
    }

    public class MarkHelpfulRequest
    {
        public int ReviewId { get; set; }
    }
}