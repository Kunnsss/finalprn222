using PayOS;
using PayOS.Models.V2.PaymentRequests;

namespace LibraryManagement.Services
{
    public class PayOSService : IPayOSService
    {
        private readonly PayOSClient _client;

        public PayOSService(IConfiguration config)
        {
            _client = new PayOSClient(new PayOSOptions
            {
                ClientId = config["PayOS:ClientId"]!,
                ApiKey = config["PayOS:ApiKey"]!,
                ChecksumKey = config["PayOS:ChecksumKey"]!
            });
        }

        public async Task<(string checkoutUrl, string paymentLinkId)> CreatePaymentLinkAsync(
            long orderCode, int amount, string description, string returnUrl, string cancelUrl)
        {
            // PayOS description max 25 chars
            if (description.Length > 25)
                description = description[..25];

            var request = new CreatePaymentLinkRequest
            {
                OrderCode = orderCode,
                Amount = amount,
                Description = description,
                ReturnUrl = returnUrl,
                CancelUrl = cancelUrl
            };

            var response = await _client.PaymentRequests.CreateAsync(request);
            return (response.CheckoutUrl, response.PaymentLinkId);
        }
    }
}
