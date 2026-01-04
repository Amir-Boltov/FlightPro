using Microsoft.Extensions.Configuration; // Needed for IConfiguration
using Stripe;
using Stripe.Checkout;
using System.Collections.Generic;

public class StripeService
{
    private readonly IConfiguration _configuration;

    // 1. ADD THIS CONSTRUCTOR
    public StripeService(IConfiguration configuration)
    {
        _configuration = configuration;
        StripeConfiguration.ApiKey = _configuration["Stripe:SecretKey"];
    }

    public string CreateCheckoutSession(decimal amount, string returnUrl, string cancelUrl)
    {
        var options = new SessionCreateOptions
        {
            PaymentMethodTypes = new List<string> { "card" },
            LineItems = new List<SessionLineItemOptions>
            {
                new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        UnitAmount = (long)(amount * 100),
                        Currency = "usd",
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = "Flight & Hotel Package",
                        },
                    },
                    Quantity = 1,
                },
            },
            Mode = "payment",
            SuccessUrl = returnUrl + "?session_id={CHECKOUT_SESSION_ID}",
            CancelUrl = cancelUrl,
        };

        var service = new SessionService();
        Session session = service.Create(options);
        return session.Url;
    }
}