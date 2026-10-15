using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
 
builder.Services.AddControllers();
var connectionString = builder.Configuration.GetConnectionString("DatabaseConnection")
    ?? throw new InvalidOperationException("Configure ConnectionStrings:DatabaseConnection for the document database.");
builder.Services.AddDbContext<LinuxMobileApp.Services.DocumentsDbContext>(options =>
    options.UseSqlServer(connectionString, sql => sql.CommandTimeout(60)));
builder.Services.AddHttpClient<LinuxMobileApp.Services.DocumentQaService>(client =>
    client.Timeout = TimeSpan.FromMinutes(2));
builder.Services.AddCors(options => options.AddPolicy("FlutterDevelopment", policy =>
    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<LinuxMobileApp.Services.DocumentsDbContext>();
    await database.Database.EnsureCreatedAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// The Flutter development client targets the local HTTP profile. Keep that.
// connection on HTTP in Development; production should use HTTPS.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
if (app.Environment.IsDevelopment())
{
    app.UseCors("FlutterDevelopment");
}

app.UseAuthorization();

app.MapControllers();

app.MapGet("/api/hello", () =>
{
    return Results.Ok(new
    {
        message = "Hello from C# ASP.NET Core!"
    });
});

app.Run();
