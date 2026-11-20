using HospitalManagementSystem.Web.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

var hospitalDatabaseConnection = builder.Configuration.GetConnectionString("HospitalDatabase");

if (string.IsNullOrWhiteSpace(hospitalDatabaseConnection))
{
    throw new InvalidOperationException(
        "The 'HospitalDatabase' connection string is missing. Configure ConnectionStrings:HospitalDatabase using user secrets for local development.");
}

builder.Services.AddDbContext<HospitalDbContext>(options =>
    options.UseNpgsql(hospitalDatabaseConnection));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, such as production deployment.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
