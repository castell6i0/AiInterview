using AiInterview.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AiInterview.Web;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var root = Directory.GetCurrentDirectory();
        if (!File.Exists(Path.Combine(root, "appsettings.json")))
            root = Path.Combine(root, "src", "AiInterview.Web");
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
        var configuration = new ConfigurationBuilder().SetBasePath(root)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true);
        if (environment == "Development") configuration.AddUserSecrets<Program>(optional: true);
        configuration.AddEnvironmentVariables();
        var connection = configuration.Build().GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connection))
            throw new InvalidOperationException("Thiếu ConnectionStrings:DefaultConnection. Cấu hình user-secrets (Development) hoặc biến môi trường trước khi chạy migration.");
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).Options);
    }
}
