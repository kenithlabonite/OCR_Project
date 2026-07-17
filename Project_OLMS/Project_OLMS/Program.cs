using Microsoft.EntityFrameworkCore;
using Project_OLMS.Data;
// Added the using directive to reference your Services folder
using Project_OLMS.Services;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
// Add services to the container.
builder.Services.AddControllers();

// Register AppDbContext with MySQL using the connection string from appsettings.json
builder.Services.AddDbContext<Project_OLMS.Data.AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// --- Registered the Email Service here ---
builder.Services.AddScoped<IEmailService, EmailService>();
// ------------------------------------------

// --- Registered the Gemini AI Service here ---
builder.Services.AddScoped<IGeminiService, GeminiService>();
// ----------------------------------------------

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();