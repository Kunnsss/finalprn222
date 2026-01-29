// Services/BookRecommendationService.cs
using LibraryManagement.Data;
using LibraryManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagement.Services
{
    public class BookRecommendationService : IBookRecommendationService
    {
        private readonly LibraryDbContext _context;

        public BookRecommendationService(LibraryDbContext context)
        {
            _context = context;
        }

        // Gợi ý cá nhân hóa dựa trên lịch sử mượn
        public async Task<List<Book>> GetPersonalizedRecommendationsAsync(int userId)
        {
            // Lấy các thể loại sách user đã mượn
            var userCategories = await _context.RentalTransactions
                .Where(r => r.UserId == userId)
                .Select(r => r.Book.CategoryId)
                .Distinct()
                .ToListAsync();

            if (!userCategories.Any())
                return await GetTrendingBooksAsync();

            // Gợi ý sách cùng thể loại nhưng chưa mượn
            var borrowedBookIds = await _context.RentalTransactions
                .Where(r => r.UserId == userId)
                .Select(r => r.BookId)
                .Distinct()
                .ToListAsync();

            return await _context.Books
                .Include(b => b.Category)
                .Where(b => userCategories.Contains(b.CategoryId)
                    && !borrowedBookIds.Contains(b.BookId)
                    && (b.IsPhysical && b.AvailableQuantity > 0 || !b.IsPhysical))
                .OrderByDescending(b => b.CreatedDate)
                .Take(10)
                .ToListAsync();
        }

        // Sách đọc nhiều (Trending)
        public async Task<List<Book>> GetTrendingBooksAsync()
        {
            var thirtyDaysAgo = DateTime.Now.AddDays(-30);

            return await _context.RentalTransactions
                .Where(r => r.RentalDate >= thirtyDaysAgo)
                .GroupBy(r => r.BookId)
                .Select(g => new { BookId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(10)
                .Join(_context.Books.Include(b => b.Category),
                    x => x.BookId,
                    b => b.BookId,
                    (x, b) => b)
                .ToListAsync();
        }

        // Sách liên quan (cùng tác giả hoặc thể loại)
        public async Task<List<Book>> GetRelatedBooksAsync(int bookId)
        {
            var book = await _context.Books.FindAsync(bookId);
            if (book == null) return new List<Book>();

            return await _context.Books
                .Include(b => b.Category)
                .Where(b => b.BookId != bookId
                    && (b.Author == book.Author || b.CategoryId == book.CategoryId)
                    && (b.IsPhysical && b.AvailableQuantity > 0 || !b.IsPhysical))
                .Take(5)
                .ToListAsync();
        }

        // Sách mới nhập
        public async Task<List<Book>> GetNewArrivalsAsync()
        {
            var oneMonthAgo = DateTime.Now.AddMonths(-1);

            return await _context.Books
                .Include(b => b.Category)
                .Where(b => b.CreatedDate >= oneMonthAgo)
                .OrderByDescending(b => b.CreatedDate)
                .Take(10)
                .ToListAsync();
        }
    }
}