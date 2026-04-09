// Program.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using LibraryManagement.Data;
using LibraryManagement.Models;
using LibraryManagement.Services;
using LibraryManagement.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllersWithViews();

// Database
builder.Services.AddDbContext<LibraryDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    });

// Password Hasher
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

// Application Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IRentalService, RentalServiceEnhanced>();
builder.Services.AddScoped<IOnlineRentalService, OnlineRentalService>();
builder.Services.AddScoped<INotificationService, NotificationService>();

// PayOS
builder.Services.AddScoped<IPayOSService, PayOSService>();

// Book Services
builder.Services.AddScoped<IBookReservationService, BookReservationService>();
builder.Services.AddScoped<IBookRecommendationService, BookRecommendationService>();

// Reading History Service
builder.Services.AddScoped<IReadingHistoryService, ReadingHistoryService>();

// SignalR
builder.Services.AddSignalR();

// Session
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Background Services
builder.Services.AddHostedService<OverdueCheckService>();
builder.Services.AddHostedService<ExpiredOnlineRentalUpdateService>(); // THÊM SERVICE MỚI

var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseSession();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// SignalR Hubs
app.MapHub<NotificationHub>("/notificationHub");
app.MapHub<LibraryHub>("/libraryHub");

app.Run();