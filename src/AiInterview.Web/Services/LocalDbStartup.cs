using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace AiInterview.Web.Services;

/// <summary>Prepares the configured development LocalDB without replacing an instance or database.</summary>
public sealed class LocalDbStartup(ILocalDbStartupRuntime runtime, ILogger<LocalDbStartup> logger)
{
    public async Task EnsureReadyAsync(string connectionString, bool isDevelopment, CancellationToken cancellationToken = default)
    {
        if (!isDevelopment || !runtime.IsWindows) return;
        var configured = new SqlConnectionStringBuilder(connectionString);
        const string prefix = @"(localdb)\";
        if (!configured.IntegratedSecurity || !configured.DataSource.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;
        var instance = configured.DataSource[prefix.Length..];
        // Shared/remote instances and unusual names remain under the administrator's control.
        if (!Regex.IsMatch(instance, @"\A[A-Za-z0-9][A-Za-z0-9_.-]{0,127}\z")) return;

        var probe = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master", AttachDBFilename = "", ConnectTimeout = 3, Pooling = false
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        try
        {
            logger.LogInformation("Đang chuẩn bị SQL LocalDB {Instance}; giữ nguyên cơ sở dữ liệu đã cấu hình.", instance);
            var listing = await runtime.RunAsync(["info"], ct);
            if (listing.ExitCode != 0) throw Failure(instance);
            var exists = listing.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Any(name => string.Equals(name.Trim(), instance, StringComparison.OrdinalIgnoreCase));
            if (!exists)
            {
                // Another app may create the same instance concurrently; starting it below is authoritative.
                await runtime.RunAsync(["create", instance], ct);
            }

            for (var attempt = 0; attempt < 3; attempt++)
            {
                var started = await runtime.RunAsync(["start", instance], ct);
                if (started.ExitCode == 0) break;
                if (await runtime.CanConnectAsync(probe.ConnectionString, ct)) return;
                if (attempt < 2) await runtime.DelayAsync(ct);
            }
            // LocalDB can report started before recovery and the client pipe are ready.
            for (var attempt = 0; attempt < 12; attempt++)
            {
                if (await runtime.CanConnectAsync(probe.ConnectionString, ct)) return;
                if (attempt < 11) await runtime.DelayAsync(ct);
            }
            throw Failure(instance);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Failure(instance);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("Không tìm thấy hoặc không chạy được SQL Server Express LocalDB. Mở Visual Studio Installer → Modify → Individual components, cài SQL Server Express LocalDB, rồi chạy lại InterviewLab. Dữ liệu hiện có không bị thay đổi.");
        }
    }

    private static InvalidOperationException Failure(string instance) => new(
        $"SQL LocalDB '{instance}' chưa sẵn sàng sau khi ứng dụng đã thử khởi động. Dừng Debug, đóng các phiên InterviewLab đang chạy rồi mở lại. Nếu vẫn lỗi, mở Visual Studio Installer → Modify → Individual components và Repair/cài SQL Server Express LocalDB; xem error.log tại %LOCALAPPDATA%\\Microsoft\\Microsoft SQL Server Local DB\\Instances\\{instance}. Không xóa instance hoặc file .mdf/.ldf vì dữ liệu cũ vẫn nằm ở đó.");
}

public sealed record LocalDbCommandResult(int ExitCode, string Output);

public interface ILocalDbStartupRuntime
{
    bool IsWindows { get; }
    Task<LocalDbCommandResult> RunAsync(string[] arguments, CancellationToken cancellationToken);
    Task<bool> CanConnectAsync(string connectionString, CancellationToken cancellationToken);
    Task DelayAsync(CancellationToken cancellationToken);
}

public sealed class LocalDbStartupRuntime : ILocalDbStartupRuntime
{
    public bool IsWindows => OperatingSystem.IsWindows();

    public async Task<LocalDbCommandResult> RunAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(FindExecutable())
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Không khởi động được công cụ SQL LocalDB.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            await error; // Drain stderr but do not expose environment details in application logs.
            return new LocalDbCommandResult(process.ExitCode, await output);
        }
        catch (OperationCanceledException)
        {
            // Only the CLI process started above; never terminate the SQL Server process or its tree.
            if (!process.HasExited) process.Kill(entireProcessTree: false);
            throw;
        }
    }

    public async Task<bool> CanConnectAsync(string connectionString, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("SELECT 1", connection) { CommandTimeout = 3 };
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
        }
        catch (SqlException) { return false; }
    }

    public Task DelayAsync(CancellationToken cancellationToken) => Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

    private static string FindExecutable()
    {
        // Visual Studio installations do not always add SqlLocalDB.exe to PATH.
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var root = Path.Combine(Environment.GetFolderPath(folder), "Microsoft SQL Server");
            if (!Directory.Exists(root)) continue;
            foreach (var version in Directory.EnumerateDirectories(root)
                .Where(path => int.TryParse(Path.GetFileName(path), out _))
                .OrderByDescending(path => int.Parse(Path.GetFileName(path))))
            {
                var candidate = Path.Combine(version, "Tools", "Binn", "SqlLocalDB.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        return "SqlLocalDB.exe";
    }
}
