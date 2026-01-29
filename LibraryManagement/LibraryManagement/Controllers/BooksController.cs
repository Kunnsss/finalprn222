// Controllers/BooksController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Services;
using System.Security.Claims;

namespace LibraryManagement.Controllers
{
    [Authorize]
    public class BooksController : Controller
    {
        private readonly LibraryDbContext _context;
        private readonly IReadingHistoryService _historyService;

        public BooksController(LibraryDbContext context, IReadingHistoryService historyService)
        {
            _context = context;
            _historyService = historyService;
        }

        [AllowAnonymous]
        public async Task<IActionResult> Index(string? search, int? categoryId)
        {
            var books = _context.Books.Include(b => b.Category).AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                books = books.Where(b => b.Title.Contains(search) || b.Author.Contains(search));
            }

            if (categoryId.HasValue)
            {
                books = books.Where(b => b.CategoryId == categoryId);
            }

            ViewBag.Categories = await _context.Categories.ToListAsync();
            ViewBag.Search = search;
            ViewBag.CategoryId = categoryId;

            return View(await books.ToListAsync());
        }

        [AllowAnonymous]
        public async Task<IActionResult> Details(int id)
        {
            var book = await _context.Books
                .Include(b => b.Category)
                .FirstOrDefaultAsync(b => b.BookId == id);

            if (book == null)
                return NotFound();

            // Lưu lịch sử xem sách (nếu user đã đăng nhập)
            if (User.Identity?.IsAuthenticated == true)
            {
                var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!string.IsNullOrEmpty(userIdClaim) && int.TryParse(userIdClaim, out int userId))
                {
                    await _historyService.TrackBookView(userId, id);
                }
            }

            return View(book);
        }

        [Authorize(Roles = "Admin,Librarian")]
        public IActionResult Create()
        {
            ViewBag.Categories = _context.Categories.ToList();
            return View();
        }

        [HttpPost]
        [Authorize(Roles = "Admin,Librarian")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Book book)
        {
            if (ModelState.IsValid)
            {
                if (!book.IsPhysical)
                {
                    book.Quantity = 0;
                    book.AvailableQuantity = 0;
                }
                else
                {
                    book.AvailableQuantity = book.Quantity;
                }

                _context.Books.Add(book);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Thêm sách thành công!";
                return RedirectToAction(nameof(Index));
            }

            ViewBag.Categories = await _context.Categories.ToListAsync();
            return View(book);
        }

        [Authorize(Roles = "Admin,Librarian")]
        public async Task<IActionResult> Edit(int id)
        {
            var book = await _context.Books.FindAsync(id);
            if (book == null)
                return NotFound();

            ViewBag.Categories = await _context.Categories.ToListAsync();
            return View(book);
        }

        [HttpPost]
        [Authorize(Roles = "Admin,Librarian")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Book book)
        {
            if (id != book.BookId)
                return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(book);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Cập nhật sách thành công!";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Books.Any(b => b.BookId == id))
                        return NotFound();
                    throw;
                }
            }

            ViewBag.Categories = await _context.Categories.ToListAsync();
            return View(book);
        }

        [Authorize(Roles = "Admin,Librarian")]
        public async Task<IActionResult> Delete(int id)
        {
            var book = await _context.Books
                .Include(b => b.Category)
                .FirstOrDefaultAsync(b => b.BookId == id);

            if (book == null)
                return NotFound();

            return View(book);
        }

        [HttpPost, ActionName("Delete")]
        [Authorize(Roles = "Admin,Librarian")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var book = await _context.Books.FindAsync(id);
            if (book != null)
            {
                _context.Books.Remove(book);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Xóa sách thành công!";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}