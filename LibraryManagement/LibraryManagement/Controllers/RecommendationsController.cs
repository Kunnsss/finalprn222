// Controllers/RecommendationsController.cs - Gợi ý sách liên quan
using Microsoft.AspNetCore.Mvc;
using LibraryManagement.Data;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagement.Controllers
{
    public class RecommendationsController : Controller
    {
        private readonly LibraryDbContext _context;

        public RecommendationsController(LibraryDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Lấy danh sách sách liên quan (cùng category hoặc cùng tác giả)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetRelatedBooks(int bookId)
        {
            try
            {
                var currentBook = await _context.Books
                    .Include(b => b.Category)
                    .FirstOrDefaultAsync(b => b.BookId == bookId);

                if (currentBook == null)
                {
                    return Json(new List<object>());
                }

                // Lấy sách cùng category hoặc cùng tác giả (ưu tiên cùng category)
                var relatedBooks = await _context.Books
                    .Where(b => b.BookId != bookId &&
                           (b.CategoryId == currentBook.CategoryId ||
                            b.Author.ToLower() == currentBook.Author.ToLower()))
                    .OrderByDescending(b => b.CategoryId == currentBook.CategoryId) // Ưu tiên cùng category
                    .ThenByDescending(b => b.CreatedDate) // Sách mới hơn
                    .Take(4)
                    .Select(b => new
                    {
                        bookId = b.BookId,
                        title = b.Title,
                        author = b.Author,
                        coverImage = b.CoverImage,
                        categoryName = b.Category.CategoryName,
                        availableQuantity = b.AvailableQuantity
                    })
                    .ToListAsync();

                return Json(relatedBooks);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetRelatedBooks: {ex.Message}");
                return Json(new List<object>());
            }
        }

        /// <summary>
        /// Gợi ý sách cho user dựa trên lịch sử mượn
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetPersonalizedRecommendations(int userId, int count = 10)
        {
            try
            {
                // Lấy các category mà user đã từng mượn
                var userCategories = await _context.RentalTransactions
                    .Include(r => r.Book)
                    .Where(r => r.UserId == userId)
                    .Select(r => r.Book.CategoryId)
                    .Distinct()
                    .ToListAsync();

                if (!userCategories.Any())
                {
                    // Nếu chưa mượn sách nào, trả về sách mới nhất
                    var newBooks = await _context.Books
                        .OrderByDescending(b => b.CreatedDate)
                        .Take(count)
                        .Select(b => new
                        {
                            bookId = b.BookId,
                            title = b.Title,
                            author = b.Author,
                            coverImage = b.CoverImage,
                            categoryName = b.Category.CategoryName
                        })
                        .ToListAsync();

                    return Json(newBooks);
                }

                // Lấy sách từ các category mà user quan tâm
                var recommendations = await _context.Books
                    .Where(b => userCategories.Contains(b.CategoryId.Value) &&
                           b.AvailableQuantity > 0)
                    .OrderByDescending(b => b.CreatedDate)
                    .Take(count)
                    .Select(b => new
                    {
                        bookId = b.BookId,
                        title = b.Title,
                        author = b.Author,
                        coverImage = b.CoverImage,
                        categoryName = b.Category.CategoryName
                    })
                    .ToListAsync();

                return Json(recommendations);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetPersonalizedRecommendations: {ex.Message}");
                return Json(new List<object>());
            }
        }

        /// <summary>
        /// Lấy sách phổ biến (được mượn nhiều nhất)
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetPopularBooks(int count = 10)
        {
            try
            {
                var popularBooks = await _context.RentalTransactions
                    .GroupBy(r => r.BookId)
                    .Select(g => new
                    {
                        BookId = g.Key,
                        RentalCount = g.Count()
                    })
                    .OrderByDescending(x => x.RentalCount)
                    .Take(count)
                    .Join(_context.Books.Include(b => b.Category),
                          rental => rental.BookId,
                          book => book.BookId,
                          (rental, book) => new
                          {
                              bookId = book.BookId,
                              title = book.Title,
                              author = book.Author,
                              coverImage = book.CoverImage,
                              categoryName = book.Category.CategoryName,
                              rentalCount = rental.RentalCount
                          })
                    .ToListAsync();

                return Json(popularBooks);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetPopularBooks: {ex.Message}");
                return Json(new List<object>());
            }
        }
    }
}
