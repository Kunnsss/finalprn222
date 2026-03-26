// Services/IEmailService.cs
namespace LibraryManagement.Services
{
    public interface IEmailService
    {
        Task SendEmailAsync(string toEmail, string subject, string htmlBody);
    }
}
