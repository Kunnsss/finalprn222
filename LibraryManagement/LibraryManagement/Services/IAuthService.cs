// Services/IAuthService.cs
using LibraryManagement.Models;
using LibraryManagement.ViewModels;

namespace LibraryManagement.Services
{
    public interface IAuthService
    {
        // Authentication methods
        Task<User?> AuthenticateAsync(string username, string password);
        Task<User?> RegisterAsync(RegisterViewModel model);
        string HashPassword(string password);
        bool VerifyPassword(string password, string hash);
        Task<User?> GetUserByUsernameAsync(string username);

        // User Management methods
        Task<List<User>> GetAllUsersAsync();
        Task<User?> GetUserByIdAsync(int userId);
        Task<User?> CreateUserAsync(CreateUserViewModel model);
        Task<bool> UpdateUserAsync(EditUserViewModel model);
        Task<bool> DeleteUserAsync(int userId);
        Task<bool> ChangePasswordAsync(int userId, string newPassword);
        Task<bool> ToggleUserStatusAsync(int userId);

        // Role methods
        Task<List<Role>> GetAllRolesAsync();

        // Forgot Password methods - THÊM MỚI
        Task<User?> VerifyUserForPasswordResetAsync(string username, string email, string phoneNumber);
        Task<bool> ResetPasswordAsync(string username, string newPassword);
    }
}
