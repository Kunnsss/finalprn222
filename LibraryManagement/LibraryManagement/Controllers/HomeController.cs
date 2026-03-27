// Controllers/HomeController.cs
using Microsoft.AspNetCore.Mvc;
using LibraryManagement.Data;
using Microsoft.EntityFrameworkCore;

namespace LibraryManagement.Controllers
{
    public class HomeController : Controller
    {
        private readonly LibraryDbContext _context;

        public HomeController(LibraryDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            ViewBag.TotalBooks = await _context.Books.CountAsync();
            ViewBag.AvailableBooks = await _context.Books.Where(b => b.AvailableQuantity > 0).CountAsync();
            ViewBag.ActiveRentals = await _context.RentalTransactions.Where(r => r.Status == "Renting").CountAsync();

            var featuredBooks = await _context.Books
                .Include(b => b.Category)
                .Where(b => b.AvailableQuantity > 0 || !b.IsPhysical)
                .OrderByDescending(b => b.CreatedDate)
                .Take(8)
                .ToListAsync();

            return View(featuredBooks);
        }

        public IActionResult About()
        {
            return View();
        }

        public IActionResult Contact()
        {
            return View();
        }
    }
}