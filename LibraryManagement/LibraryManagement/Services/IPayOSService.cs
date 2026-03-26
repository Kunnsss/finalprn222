namespace LibraryManagement.Services
{
    public interface IPayOSService
    {
        Task<(string checkoutUrl, string paymentLinkId)> CreatePaymentLinkAsync(
            long orderCode, int amount, string description, string returnUrl, string cancelUrl);
    }
}
