using System.ComponentModel.DataAnnotations.Schema;

namespace LibraryManagement.Models
{
    // ViewModel để hiển thị tất cả giao dịch (cả offline và online)
    public class AllRentalsViewModel
    {
        public IEnumerable<RentalTransaction> OfflineRentals { get; set; }
        public IEnumerable<OnlineRentalTransaction> OnlineRentals { get; set; }
    }

    // Extension của OnlineRentalTransaction để thêm property tương tự RentalTransaction
    public static class OnlineRentalTransactionExtensions
    {
        public static int GetRentalDays(this OnlineRentalTransaction transaction)
        {
            return (DateTime.Now - transaction.PurchaseDate).Days;
        }

        public static bool IsExpired(this OnlineRentalTransaction transaction)
        {
            return transaction.ExpiryDate < DateTime.Now && transaction.Status != "Expired";
        }
    }
}