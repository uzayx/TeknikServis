using System.Reflection;
using System.Text.Json.Serialization;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.EntityFrameworkCore;
using TeknikServis.Api.Middleware;
using TeknikServis.Application;
using TeknikServis.Application.Common.Options;
using TeknikServis.Infrastructure;
using TeknikServis.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssembly(typeof(TeknikServis.Application.DependencyInjection).Assembly);

builder.Services.Configure<SlaOptions>(builder.Configuration.GetSection(SlaOptions.SectionName));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Teknik Servis Yonetim API",
        Version = "v1",
        Description = "Teknik servis ariza kaydi yonetim sistemi REST API.\n\n" +
                      "Durum akisi: New -> Assigned -> InProgress -> Completed -> Approved -> Closed\n" +
                      "Geriye donus ve adim atlama engellidir. Approved/Closed kayitlar duzenlenemez."
    });

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.IsDevelopment());

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

// Container ortaminda ilk acilista semayi kurar (compose: Database__ApplyMigrationsOnStartup=true).
// Lokal gelistirmede kapali; sema "dotnet ef database update" ile yonetilir.
// Uretim notu: migration startup'ta degil deployment pipeline'inda uygulanmalidir --
// ayni anda acilan birden cok instance migrate yarisina girer.
if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Teknik Servis API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseCors("AllowAll");
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.MapGet("/health/db", async (AppDbContext db) =>
{
    var canConnect = await db.Database.CanConnectAsync();
    return canConnect
        ? Results.Ok(new { status = "Healthy", database = "Connected" })
        : Results.Problem("Veritabanina baglanilamiyor.");
});

app.Run();

