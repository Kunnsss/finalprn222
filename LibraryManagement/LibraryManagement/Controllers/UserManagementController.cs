// Controllers/UserManagementController.cs
using LibraryManagement.Services;
using LibraryManagement.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LibraryManagement.Controllers
{
    [Authorize(Roles = "Admin")]
    public class UserManagementController : Controller
    {
        private readonly IAuthService _authService;

        public UserManagementController(IAuthService authService)
        {
            _authService = authService;
        }

        // GET: UserManagement
        public async Task<IActionResult> Index(string searchString, int? roleFilter, bool? statusFilter)
        {
            var users = await _authService.GetAllUsersAsync();

            // Lọc theo tìm kiếm
            if (!string.IsNullOrEmpty(searchString))
            {
                users = users.Where(u =>
                    u.Username.Contains(searchString, StringComparison.OrdinalIgnoreCase) ||
                    u.FullName.Contains(searchString, StringComparison.OrdinalIgnoreCase) ||
                    u.Email.Contains(searchString, StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            // Lọc theo vai trò
            if (roleFilter.HasValue)
            {
                users = users.Where(u => u.RoleId == roleFilter.Value).ToList();
            }

            // Lọc theo trạng thái
            if (statusFilter.HasValue)
            {
                users = users.Where(u => u.IsActive == statusFilter.Value).ToList();
            }

            var userViewModels = users.Select(u => new UserManagementViewModel
            {
                UserId = u.UserId,
                Username = u.Username,
                Email = u.Email,
                FullName = u.FullName,
                PhoneNumber = u.PhoneNumber,
                Address = u.Address,
                RoleId = u.RoleId,
                RoleName = u.Role?.RoleName,
                IsActive = u.IsActive,
                CreatedDate = u.CreatedDate
            }).ToList();

            // Truyền dữ liệu cho ViewBag
            ViewBag.Roles = await _authService.GetAllRolesAsync();
            ViewBag.SearchString = searchString;
            ViewBag.RoleFilter = roleFilter;
            ViewBag.StatusFilter = statusFilter;

            return View(userViewModels);
        }

        // GET: UserManagement/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var user = await _authService.GetUserByIdAsync(id);
            if (user == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy người dùng";
                return RedirectToAction(nameof(Index));
            }

            var viewModel = new UserManagementViewModel
            {
                UserId = user.UserId,
                Username = user.Username,
                Email = user.Email,
                FullName = user.FullName,
                PhoneNumber = user.PhoneNumber,
                Address = user.Address,
                RoleId = user.RoleId,
                RoleName = user.Role?.RoleName,
                IsActive = user.IsActive,
                CreatedDate = user.CreatedDate
            };

            return View(viewModel);
        }

        // GET: UserManagement/Create
        public async Task<IActionResult> Create()
        {
            ViewBag.Roles = await _authService.GetAllRolesAsync();
            return View();
        }

        // POST: UserManagement/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateUserViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Roles = await _authService.GetAllRolesAsync();
                return View(model);
            }

            var user = await _authService.CreateUserAsync(model);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Tên đăng nhập hoặc email đã tồn tại");
                ViewBag.Roles = await _authService.GetAllRolesAsync();
                return View(model);
            }

            TempData["SuccessMessage"] = "Tạo tài khoản thành công!";
            return RedirectToAction(nameof(Index));
        }

        // GET: UserManagement/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            var user = await _authService.GetUserByIdAsync(id);
            if (user == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy người dùng";
                return RedirectToAction(nameof(Index));
            }

            var viewModel = new EditUserViewModel
            {
                UserId = user.UserId,
                Username = user.Username,
                Email = user.Email,
                FullName = user.FullName,
                PhoneNumber = user.PhoneNumber,
                Address = user.Address,
                RoleId = user.RoleId,
                IsActive = user.IsActive
            };

            ViewBag.Roles = await _authService.GetAllRolesAsync();
            return View(viewModel);
        }

        // POST: UserManagement/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(EditUserViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Roles = await _authService.GetAllRolesAsync();
                return View(model);
            }

            var result = await _authService.UpdateUserAsync(model);
            if (!result)
            {
                ModelState.AddModelError(string.Empty, "Email đã tồn tại hoặc không tìm thấy người dùng");
                ViewBag.Roles = await _authService.GetAllRolesAsync();
                return View(model);
            }

            TempData["SuccessMessage"] = "Cập nhật thông tin thành công!";
            return RedirectToAction(nameof(Index));
        }

        // GET: UserManagement/ChangePassword/5
        public async Task<IActionResult> ChangePassword(int id)
        {
            var user = await _authService.GetUserByIdAsync(id);
            if (user == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy người dùng";
                return RedirectToAction(nameof(Index));
            }

            var viewModel = new ChangePasswordViewModel
            {
                UserId = user.UserId,
                Username = user.Username
            };

            return View(viewModel);
        }

        // POST: UserManagement/ChangePassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var result = await _authService.ChangePasswordAsync(model.UserId, model.NewPassword);
            if (!result)
            {
                ModelState.AddModelError(string.Empty, "Không tìm thấy người dùng");
                return View(model);
            }

            TempData["SuccessMessage"] = "Đổi mật khẩu thành công!";
            return RedirectToAction(nameof(Index));
        }

        // POST: UserManagement/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var result = await _authService.DeleteUserAsync(id);
            if (!result)
            {
                TempData["ErrorMessage"] = "Không thể xóa người dùng";
                return RedirectToAction(nameof(Index));
            }

            TempData["SuccessMessage"] = "Xóa/Vô hiệu hóa người dùng thành công!";
            return RedirectToAction(nameof(Index));
        }

        // POST: UserManagement/ToggleStatus/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStatus(int id)
        {
            var result = await _authService.ToggleUserStatusAsync(id);
            if (!result)
            {
                TempData["ErrorMessage"] = "Không thể thay đổi trạng thái người dùng";
                return RedirectToAction(nameof(Index));
            }

            TempData["SuccessMessage"] = "Thay đổi trạng thái thành công!";
            return RedirectToAction(nameof(Index));
        }
    }
}
