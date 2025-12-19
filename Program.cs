using Microsoft.Extensions.FileProviders;
using System.IO;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;
using Kursovoi.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// register a simple DbHelper service that uses configuration connection string
builder.Services.AddSingleton<DbHelperClient>();

// Add cookie authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.Cookie.Name = "KursovoiAuth";

        // For AJAX requests we should return 401/403 instead of redirecting to login page
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = ctx =>
            {
                if (ctx.Request.Headers["X-Requested-With"] == "XMLHttpRequest" || ctx.Request.Headers["Accept"].ToString().Contains("application/json"))
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return System.Threading.Tasks.Task.CompletedTask;
                }
                ctx.Response.Redirect(ctx.RedirectUri);
                return System.Threading.Tasks.Task.CompletedTask;
            },
            OnRedirectToAccessDenied = ctx =>
            {
                if (ctx.Request.Headers["X-Requested-With"] == "XMLHttpRequest" || ctx.Request.Headers["Accept"].ToString().Contains("application/json"))
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return System.Threading.Tasks.Task.CompletedTask;
                }
                ctx.Response.Redirect(ctx.RedirectUri);
                return System.Threading.Tasks.Task.CompletedTask;
            }
        };
    });

// Add session for cart
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromDays(7);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

// Serve Images folder from project root at request path /Images
var imagesPath = Path.Combine(builder.Environment.ContentRootPath, "Images");
if (Directory.Exists(imagesPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(imagesPath),
        RequestPath = "/Images"
    });
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// IMPORTANT: enable session before authentication if authentication handlers rely on session
app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
