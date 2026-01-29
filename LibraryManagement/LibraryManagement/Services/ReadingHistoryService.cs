// Services/ReadingHistoryService.cs
using LibraryManagement.Data;
using LibraryManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagement.Services
{
    public interface IReadingHistoryService
    {
        Task TrackBookView(int userId, int bookId, int? viewDuration = null, bool isCompleted = false);
        Task UpdateViewDuration(int userId, int bookId, int additionalSeconds);
        Task MarkAsCompleted(int userId, int bookId);
    }

    public class ReadingHistoryService : IReadingHistoryService
    {
        private readonly LibraryDbContext _context;

        public ReadingHistoryService(LibraryDbContext context)
        {
            _context = context;
        }

        // Lưu hoặc cập nhật lịch sử đọc
        public async Task TrackBookView(int userId, int bookId, int? viewDuration = null, bool isCompleted = false)
        {
            // Tìm record hiện tại (lần đọc gần nhất)
            var history = await _context.ReadingHistory
                .Where(h => h.UserId == userId && h.BookId == bookId)
                .OrderByDescending(h => h.ViewDate)
                .FirstOrDefaultAsync();

            if (history == null)
            {
                // Tạo mới
                history = new ReadingHistory
                {
                    UserId = userId,
                    BookId = bookId,
                    ViewDate = DateTime.Now,
                    ViewDuration = viewDuration ?? 0,
                    IsCompleted = isCompleted
                };
                _context.ReadingHistory.Add(history);
            }
            else
            {
                // Cập nhật lần đọc hiện tại
                history.ViewDate = DateTime.Now;
                if (viewDuration.HasValue)
                {
                    history.ViewDuration = (history.ViewDuration ?? 0) + viewDuration.Value;
                }
                history.IsCompleted = isCompleted || history.IsCompleted;
            }

            await _context.SaveChangesAsync();
        }

        // Cập nhật thời gian đọc
        public async Task UpdateViewDuration(int userId, int bookId, int additionalSeconds)
        {
            var history = await _context.ReadingHistory
                .Where(h => h.UserId == userId && h.BookId == bookId)
                .OrderByDescending(h => h.ViewDate)
                .FirstOrDefaultAsync();

            if (history != null)
            {
                history.ViewDuration = (history.ViewDuration ?? 0) + additionalSeconds;
                history.ViewDate = DateTime.Now; // Cập nhật lần xem cuối
                await _context.SaveChangesAsync();
            }
        }

        // Đánh dấu đã hoàn thành
        public async Task MarkAsCompleted(int userId, int bookId)
        {
            var history = await _context.ReadingHistory
                .Where(h => h.UserId == userId && h.BookId == bookId)
                .OrderByDescending(h => h.ViewDate)
                .FirstOrDefaultAsync();

            if (history != null)
            {
                history.IsCompleted = true;
                await _context.SaveChangesAsync();
            }
            else
            {
                // Nếu chưa có history thì tạo mới
                await TrackBookView(userId, bookId, null, true);
            }
        }
    }
}