using NoteManagement.Application;
using NoteManagement.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Consistent error contract (SDS §39, ASP.NET Core Problem Details) for every controller,
// not just this ticket's health check.
builder.Services.AddProblemDetails();

// No wildcard CORS (SDS §65) — restricted to the configured frontend origin.
const string frontendCorsPolicy = "Frontend";
builder.Services.AddCors(options =>
{
    options.AddPolicy(frontendCorsPolicy, policy => policy
        .WithOrigins(builder.Configuration.GetValue<string>("Cors:FrontendOrigin") ?? "http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

// Each layer self-registers via its own DependencyInjection.cs; only Program.cs composes them.
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseCors(frontendCorsPolicy);
app.UseAuthorization();
app.MapControllers();

app.Run();

// Required so NoteManagement.Tests.Integration can target this host via WebApplicationFactory<Program>.
public partial class Program
{
}
