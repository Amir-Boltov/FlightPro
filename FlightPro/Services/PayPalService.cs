using PayPalCheckoutSdk.Core;
using PayPalCheckoutSdk.Orders;
using System.Collections.Generic;
using System.Threading.Tasks;

public class PayPalService
{
    private readonly PayPalHttpClient _client;


    private readonly IConfiguration _configuration;

    public PayPalService(IConfiguration configuration)
    {
        _configuration = configuration;
        var clientId = _configuration["PayPal:ClientId"];
        var clientSecret = _configuration["PayPal:ClientSecret"];
        PayPalEnvironment environment = new SandboxEnvironment(clientId, clientSecret);
        _client = new PayPalHttpClient(environment);
    }


    // יצירת הזמנה (שליחה לפייפאל)
    public async Task<string> CreateOrder(decimal amount, string returnUrl, string cancelUrl)
    {
        var orderRequest = new OrderRequest()
        {
            CheckoutPaymentIntent = "CAPTURE",
            ApplicationContext = new ApplicationContext
            {
                ReturnUrl = returnUrl,
                CancelUrl = cancelUrl,
                BrandName = "FlightPro Booking", // השם שיופיע ללקוח
                LandingPage = "BILLING",
                UserAction = "PAY_NOW"
            },
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    AmountWithBreakdown = new AmountWithBreakdown
                    {
                        CurrencyCode = "USD",
                        Value = amount.ToString("F2") // שתי ספרות אחרי הנקודה
                    }
                }
            }
        };

        var request = new OrdersCreateRequest();
        request.Prefer("return=representation");
        request.RequestBody(orderRequest);

        var response = await _client.Execute(request);
        var result = response.Result<Order>();

        // מציאת הלינק להפניה
        foreach (var link in result.Links)
        {
            if (link.Rel.ToLower() == "approve") return link.Href;
        }
        return null;
    }

    // חיוב סופי (אחרי שהלקוח אישר)
    public async Task<string> CaptureOrder(string orderId)
    {
        var request = new OrdersCaptureRequest(orderId);
        request.RequestBody(new OrderActionRequest());
        var response = await _client.Execute(request);
        var result = response.Result<Order>();
        return result.Id; // מחזיר את מספר העסקה
    }
}