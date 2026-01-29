// Services/AuthService.cs
using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

namespace LibraryManagement.Services
{
    public class AuthService : IAuthService
    {
        private readonly LibraryDbContext _context;
        private readonly IPasswordHasher<User> _passwordHasher;

        public AuthService(LibraryDbContext context, IPasswordHasher<User> passwordHasher)
        {
            _context = context;
            _passwordHasher = passwordHasher;
        }

        #region Authentication Methods

        public async Task<User?> AuthenticateAsync(string username, string password)
        {
            var user = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

            if (user == null)
                return null;

            var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
            return result == PasswordVerificationResult.Success ? user : null;
        }

        public async Task<User?> RegisterAsync(RegisterViewModel model)
        {
            if (await _context.Users.AnyAsync(u => u.Username == model.Username))
                return null;

            if (await _context.Users.AnyAsync(u => u.Email == model.Email))
                return null;

            var customerRole = await _context.Roles.FirstOrDefaultAsync(r => r.RoleName == "Customer");
            if (customerRole == null)
                return null;

            var user = new User
            {
                Username = model.Username,
                FullName = model.FullName,
                Email = model.Email,
                PhoneNumber = model.PhoneNumber,
                Address = model.Address,
                RoleId = customerRole.RoleId,
                IsActive = true,
                CreatedDate = DateTime.Now
            };

            user.PasswordHash = _passwordHasher.HashPassword(user, model.Password);

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return user;
        }

        public string HashPassword(string password)
        {
            var user = new User();
            return _passwordHasher.HashPassword(user, password);
        }

        public bool VerifyPassword(string password, string hash)
        {
            var user = new User { PasswordHash = hash };
            var result = _passwordHasher.VerifyHashedPassword(user, hash, password);
            return result == PasswordVerificationResult.Success;
        }

        public async Task<User?> GetUserByUsernameAsync(string username)
        {
            return await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);
        }

        #endregion

        #region User Management Methods

        public async Task<List<User>> GetAllUsersAsync()
        {
            return await _context.Users
                .Include(u => u.Role)
                .OrderByDescending(u => u.CreatedDate)
                .ToListAsync();
        }

        public async Task<User?> GetUserByIdAsync(int userId)
        {
            return await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.UserId == userId);
        }

        public async Task<User?> CreateUserAsync(CreateUserViewModel model)
        {
            // Kiểm tra username đã tồn tại
            if (await _context.Users.AnyAsync(u => u.Username == model.Username))
                return null;

            // Kiểm tra email đã tồn tại
            if (await _context.Users.AnyAsync(u => u.Email == model.Email))
                return null;

            var user = new User
            {
                Username = model.Username,
                Email = model.Email,
                FullName = model.FullName,
                PhoneNumber = model.PhoneNumber,
                Address = model.Address,
                RoleId = model.RoleId,
                IsActive = true,
                CreatedDate = DateTime.Now
            };

            // Hash password
            user.PasswordHash = _passwordHasher.HashPassword(user, model.Password);

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return await GetUserByIdAsync(user.UserId);
        }

        public async Task<bool> UpdateUserAsync(EditUserViewModel model)
        {
            var user = await _context.Users.FindAsync(model.UserId);
            if (user == null)
                return false;

            // Kiểm tra email trùng (trừ user hiện tại)
            if (await _context.Users.AnyAsync(u => u.Email == model.Email && u.UserId != model.UserId))
                return false;

            user.Email = model.Email;
            user.FullName = model.FullName;
            user.PhoneNumber = model.PhoneNumber;
            user.Address = model.Address;
            user.RoleId = model.RoleId;
            user.IsActive = model.IsActive;

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> DeleteUserAsync(int userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return false;

            try
            {
                // Kiểm tra xem user có dữ liệu liên quan không
                var hasRentalTransactions = await _context.RentalTransactions.AnyAsync(rt => rt.UserId == userId);
                var hasOnlineRentalTransactions = await _context.OnlineRentalTransactions.AnyAsync(ort => ort.UserId == userId);

                // Kiểm tra các bảng mới
                var hasNotifications = _context.Notifications != null && await _context.Notifications.AnyAsync(n => n.UserId == userId);
                var hasSupportChats = _context.SupportChats != null && await _context.SupportChats.AnyAsync(sc => sc.UserId == userId);
                var hasBookReservations = _context.BookReservations != null && await _context.BookReservations.AnyAsync(br => br.UserId == userId);
                var hasBookReviews = _context.BookReviews != null && await _context.BookReviews.AnyAsync(br => br.UserId == userId);
                var hasWishlists = _context.Wishlists != null && await _context.Wishlists.AnyAsync(w => w.UserId == userId);
                var hasReadingHistory = _context.ReadingHistory != null && await _context.ReadingHistory.AnyAsync(rh => rh.UserId == userId);
                var hasEventRegistrations = _context.EventRegistrations != null && await _context.EventRegistrations.AnyAsync(er => er.UserId == userId);

                // Nếu có BẤT KỲ dữ liệu liên quan nào, chỉ vô hiệu hóa thay vì xóa
                if (hasRentalTransactions || hasOnlineRentalTransactions || hasNotifications ||
                    hasSupportChats || hasBookReservations || hasBookReviews ||
                    hasWishlists || hasReadingHistory || hasEventRegistrations)
                {
                    // Soft delete - chỉ vô hiệu hóa
                    user.IsActive = false;
                    await _context.SaveChangesAsync();
                    return true;
                }

                // Nếu không có dữ liệu liên quan, có thể xóa hoàn toàn
                _context.Users.Remove(user);
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception)
            {
                // Nếu có lỗi khi xóa (foreign key constraint), vô hiệu hóa thay thế
                user.IsActive = false;
                await _context.SaveChangesAsync();
                return true;
            }
        }

        public async Task<bool> ChangePasswordAsync(int userId, string newPassword)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return false;

            user.PasswordHash = _passwordHasher.HashPassword(user, newPassword);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ToggleUserStatusAsync(int userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return false;

            user.IsActive = !user.IsActive;
            await _context.SaveChangesAsync();
            return true;
        }

        #endregion

        #region Role Methods

        public async Task<List<Role>> GetAllRolesAsync()
        {
            return await _context.Roles.OrderBy(r => r.RoleName).ToListAsync();
        }

        #endregion

        // Services/AuthService.cs - THÊM các methods này vào class AuthService hiện có

        #region Forgot Password Methods

        /// <summary>
        /// Xác thực user để reset password bằng username, email và phone
        /// </summary>
        public async Task<User?> VerifyUserForPasswordResetAsync(string username, string email, string phoneNumber)
        {
            return await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u =>
                    u.Username == username &&
                    u.Email == email &&
                    u.PhoneNumber == phoneNumber &&
                    u.IsActive);
        }

        /// <summary>
        /// Reset password cho user
        /// </summary>
        public async Task<bool> ResetPasswordAsync(string username, string newPassword)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null)
                return false;

            user.PasswordHash = _passwordHasher.HashPassword(user, newPassword);
            await _context.SaveChangesAsync();
            return true;
        }

        #endregion
    }
}