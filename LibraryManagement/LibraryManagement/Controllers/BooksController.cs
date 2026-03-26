// Controllers/BooksController.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Services;
using System.Security.Claims;
using System.IO;

namespace LibraryManagement.Controllers
{
    [Authorize]
    public class BooksController : Controller
    {
        private readonly LibraryDbContext _context;
        private readonly IReadingHistoryService _historyService;
        private readonly IWebHostEnvironment _environment;

        public BooksController(LibraryDbContext context, IReadingHistoryService historyService, IWebHostEnvironment environment)
        {
            _context = context;
            _historyService = historyService;
            _environment = environment;
        }

        [AllowAnonymous]
        public async Task<IActionResult> Index(
            string? search,
            int? categoryId,
            string? bookType,
            int page = 1,
            int pageSize = 12)
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

            // Lọc theo loại sách: vật lý hoặc online
            if (!string.IsNullOrEmpty(bookType))
            {
                if (bookType == "physical")
                    books = books.Where(b => b.IsPhysical);
                else if (bookType == "online")
                    books = books.Where(b => !b.IsPhysical);
            }

            int totalBooks = await books.CountAsync();
            int totalPages = (int)Math.Ceiling(totalBooks / (double)pageSize);
            if (page < 1) page = 1;
            if (page > totalPages && totalPages > 0) page = totalPages;

            var pagedBooks = await books
                .OrderByDescending(b => b.CreatedDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.Categories = await _context.Categories.ToListAsync();
            ViewBag.Search = search;
            ViewBag.CategoryId = categoryId;
            ViewBag.BookType = bookType;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalBooks = totalBooks;
            ViewBag.TotalPages = totalPages;

            return View(pagedBooks);
        }

        [AllowAnonymous]
        public async Task<IActionResult> Details(int id)
        {
            var book = await _context.Books
                .Include(b => b.Category)
                .FirstOrDefaultAsync(b => b.BookId == id);

            if (book == null)
                return View("BookNotFound");

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
        public async Task<IActionResult> Create(Book book, string CoverImageOption, IFormFile? CoverImageFile)
        {
            // Xử lý ảnh bìa (upload file hoặc URL)
            if (CoverImageOption == "upload" && CoverImageFile != null && CoverImageFile.Length > 0)
            {
                book.CoverImage = await SaveBookCoverImageAsync(CoverImageFile);
            }

            // Xử lý theo loại sách
            if (!book.IsPhysical)
            {
                // Sách online - không cần số lượng vật lý
                book.Quantity = 0;
                book.AvailableQuantity = 0;
                book.RentalPrice = 0;
            }
            else
            {
                // Sách vật lý - đảm bảo có số lượng hợp lệ
                if (book.Quantity <= 0)
                    book.Quantity = 1;
                book.AvailableQuantity = book.Quantity;
            }

            if (ModelState.IsValid)
            {
                _context.Books.Add(book);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Thêm sách thành công!";
                return RedirectToAction(nameof(Index));
            }

            // Lấy danh sách lỗi validation
            var errors = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .ToList();

            if (errors.Any())
            {
                TempData["ErrorMessage"] = "Không thể thêm sách. Vui lòng kiểm tra lại: " + string.Join("; ", errors);
            }
            else
            {
                TempData["ErrorMessage"] = "Không thể thêm sách. Vui lòng kiểm tra lại dữ liệu nhập vào.";
            }

            ViewBag.Categories = await _context.Categories.ToListAsync();
            return View(book);
        }

        private async Task<string> SaveBookCoverImageAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return null;

            var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
            var extension = Path.GetExtension(file.FileName).ToLower();

            if (!allowedExtensions.Contains(extension))
                return null;

            var fileName = $"{Guid.NewGuid()}{extension}";
            var uploadsFolder = Path.Combine(_environment.WebRootPath, "uploads", "covers");

            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            var filePath = Path.Combine(uploadsFolder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return $"/uploads/covers/{fileName}";
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
        public async Task<IActionResult> Edit(int id, Book book, string CoverImageOption, IFormFile? CoverImageFile)
        {
            if (id != book.BookId)
            {
                TempData["ErrorMessage"] = $"Không thể cập nhật: id route ({id}) khác BookId form ({book.BookId}).";
                return View("BookNotFound");
            }

            // Xử lý ảnh bìa (upload file hoặc URL)
            if (CoverImageOption == "upload" && CoverImageFile != null && CoverImageFile.Length > 0)
            {
                book.CoverImage = await SaveBookCoverImageAsync(CoverImageFile);
            }

            // Xử lý theo loại sách
            if (!book.IsPhysical)
            {
                // Sách online - không cần số lượng vật lý
                book.Quantity = 0;
                book.AvailableQuantity = 0;
                book.RentalPrice = 0;
            }
            else
            {
                // Sách vật lý - đảm bảo có số lượng hợp lệ
                if (book.Quantity <= 0)
                    book.Quantity = 1;
                if (book.AvailableQuantity > book.Quantity)
                    book.AvailableQuantity = book.Quantity;
            }

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
                        return View("BookNotFound");
                    throw;
                }
            }

            var firstError = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .FirstOrDefault();
            TempData["ErrorMessage"] = !string.IsNullOrWhiteSpace(firstError)
                ? $"Không thể cập nhật sách. Lỗi: {firstError}"
                : "Không thể cập nhật sách. Vui lòng kiểm tra lại dữ liệu nhập vào.";

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