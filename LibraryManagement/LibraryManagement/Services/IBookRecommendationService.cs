// Services/IBookRecommendationService.cs
using LibraryManagement.Models;

namespace LibraryManagement.Services
{
    public interface IBookRecommendationService
    {
        Task<List<Book>> GetPersonalizedRecommendationsAsync(int userId);
        Task<List<Book>> GetTrendingBooksAsync();
        Task<List<Book>> GetRelatedBooksAsync(int bookId);
        Task<List<Book>> GetNewArrivalsAsync();
    }
}