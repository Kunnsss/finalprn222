// Models/ViewModels/DashboardViewModel.cs
namespace LibraryManagement.Models.ViewModels
{
    public class DashboardViewModel
    {
        // Thống kê tổng quan
        public int TotalBooks { get; set; }
        public int TotalUsers { get; set; }
        public int ActiveRentals { get; set; }
        public int OverdueRentals { get; set; }
        public int TodayRentals { get; set; }
        public decimal TotalRevenue { get; set; }
        public int PendingReservations { get; set; }
        public int AvailableBooks { get; set; }

        // User online
        public int OnlineUsersCount { get; set; }
        public List<OnlineUserInfo> OnlineUsers { get; set; } = new List<OnlineUserInfo>();

        // Top sách
        public List<TopBookInfo> TopBooks { get; set; } = new List<TopBookInfo>();

        // Thống kê theo thể loại
        public List<CategoryStats> CategoryStats { get; set; } = new List<CategoryStats>();

        // Thống kê sự kiện
        public int TotalEvents { get; set; }
        public int UpcomingEvents { get; set; }
        public int PastEvents { get; set; }
        public int TotalEventRegistrations { get; set; }
    }

    public class OnlineUserInfo
    {
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateTime ConnectedAt { get; set; }
    }

    public class TopBookInfo
    {
        public string Title { get; set; } = string.Empty;
        public int BorrowCount { get; set; }
    }

    public class CategoryStats
    {
        public string CategoryName { get; set; } = string.Empty;
        public int BorrowCount { get; set; }
    }
}