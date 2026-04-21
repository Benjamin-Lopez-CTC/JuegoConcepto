using Microsoft.EntityFrameworkCore;
using JuegoConcepto.Models;
using System.Text.RegularExpressions;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Render inyecta el puerto vía variable de entorno PORT
string port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Configurar Base de Datos (PostgreSQL en Render vs SQLite Local)
string databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
if (!string.IsNullOrEmpty(databaseUrl))
{
    // Render provee "postgres://user:password@host/dbname"
    // Npgsql necesita "Host=host;Database=dbname;Username=user;Password=password"
    var databaseUri = new Uri(databaseUrl);
    var userInfo = databaseUri.UserInfo.Split(':');
    var builderConn = new Npgsql.NpgsqlConnectionStringBuilder
    {
        Host = databaseUri.Host,
        Port = databaseUri.Port > 0 ? databaseUri.Port : 5432,
        Username = userInfo[0],
        Password = userInfo[1],
        Database = databaseUri.LocalPath.TrimStart('/')
    };
    builder.Services.AddDbContext<GameDbContext>(options =>
        options.UseNpgsql(builderConn.ToString()));
}
else
{
    // Desarrollo Local Windows
    builder.Services.AddDbContext<GameDbContext>(options =>
        options.UseSqlite("Data Source=game_local.db"));
}

builder.Services.AddControllersWithViews();

// Sesión (sigue siendo útil para mensajes temporales tipo TempData, aunque el estado vaya a BD)
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(2);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

// No usar HTTPS redirect: Render maneja TLS en su capa de proxy
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Auto-Migración
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<GameDbContext>();
    // Si la DB (Postgres/SQLite) no existe, la crea. Si le faltan tablas, las crea.
    dbContext.Database.Migrate();
}

app.Run();
