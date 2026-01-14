using PayPalCheckoutSdk.Core;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddDistributedMemoryCache();

builder.Services.AddHttpContextAccessor();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromSeconds(3600);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddScoped<PayPalService>();
builder.Services.AddScoped<StripeService>();
builder.Services.AddScoped<WaitlistService>();
builder.Services.AddScoped<BookingRuleService>();
builder.Services.AddTransient<EmailService>();

Stripe.StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];


builder.Services.AddSingleton(x =>
    new PayPalHttpClient(new SandboxEnvironment(
        builder.Configuration["PayPal:ClientId"],
        builder.Configuration["PayPal:ClientSecret"]
    ))
);
var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}
app.UseStaticFiles();
app.UseSession();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
