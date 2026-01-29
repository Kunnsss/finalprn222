// Controllers/AccountController.cs
using LibraryManagement.Services;
using LibraryManagement.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace LibraryManagement.Controllers
{
    public class AccountController : Controller
    {
        private readonly IAuthService _authService;

        public AccountController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (!ModelState.IsValid)
                return View(model);

            var user = await _authService.AuthenticateAsync(model.Username, model.Password);

            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Tên đăng nhập hoặc mật khẩu không đúng");
                return View(model);
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim("FullName", user.FullName),
                new Claim(ClaimTypes.Role, user.Role.RoleName)
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = model.RememberMe,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(model.RememberMe ? 30 : 1)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var user = await _authService.RegisterAsync(model);

            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Tên đăng nhập hoặc email đã tồn tại");
                return View(model);
            }

            TempData["SuccessMessage"] = "Đăng ký thành công! Vui lòng đăng nhập.";
            return RedirectToAction(nameof(Login));
        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Index", "Home");
        }



        public IActionResult AccessDenied()
        {
            return View();
        }

       [HttpGet]
        public async Task<IActionResult> Profile()
        {
            // Lấy username từ cookie đăng nhập
            string username = User.Identity?.Name;

            if (string.IsNullOrEmpty(username))
                return RedirectToAction("Login", "Account");

            // Lấy user từ DB
            var user = await _authService.GetUserByUsernameAsync(username);

            if (user == null)
                return NotFound("Không tìm thấy người dùng");

            return View(user); // ✅ Model không null
        }


        // Controllers/AccountController.cs 

        #region Forgot Password

        /// <summary>
        /// GET: Trang nhập thông tin để xác thực tài khoản
        /// </summary>
        [HttpGet]
        public IActionResult ForgotPassword()
        {
            return View();
        }

        /// <summary>
        /// POST: Xác thực thông tin user và chuyển đến trang reset password
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // Xác thực user bằng username, email và phone
            var user = await _authService.VerifyUserForPasswordResetAsync(
                model.Username,
                model.Email,
                model.PhoneNumber);

            if (user == null)
            {
                ModelState.AddModelError(string.Empty,
                    "Thông tin không chính xác. Vui lòng kiểm tra lại tên đăng nhập, email và số điện thoại.");
                return View(model);
            }

            // Lưu username vào TempData để dùng ở trang ResetPassword
            TempData["ResetUsername"] = user.Username;
            TempData["SuccessMessage"] = "Xác thực thành công! Vui lòng nhập mật khẩu mới.";

            return RedirectToAction(nameof(ResetPassword));
        }
        
        /// <summary>
        /// GET: Trang nhập mật khẩu mới
        /// </summary>
        [HttpGet]
        public IActionResult ResetPassword()
        {
            var username = TempData["ResetUsername"] as string;

            if (string.IsNullOrEmpty(username))
            {
                TempData["ErrorMessage"] = "Phiên xác thực đã hết hạn. Vui lòng thử lại.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            // Giữ lại username cho POST
            TempData.Keep("ResetUsername");

            var model = new ResetPasswordViewModel
            {
                Username = username
            };

            return View(model);
        }

        /// <summary>
        /// POST: Đổi mật khẩu mới
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var result = await _authService.ResetPasswordAsync(model.Username, model.NewPassword);

            if (!result)
            {
                ModelState.AddModelError(string.Empty, "Có lỗi xảy ra. Vui lòng thử lại.");
                return View(model);
            }

            TempData["SuccessMessage"] = "Đổi mật khẩu thành công! Vui lòng đăng nhập.";
            return RedirectToAction(nameof(Login));
        }

        #endregion



    }
}