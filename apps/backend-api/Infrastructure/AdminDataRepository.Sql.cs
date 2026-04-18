using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace VinhKhanh.BackendApi.Infrastructure;

public sealed partial class AdminDataRepository
{
    private SqlConnection OpenConnection()
    {
        try
        {
            return OpenConnectionCore(_connectionString);
        }
        catch (SqlException exception) when (_allowCreateDatabase && _allowSeedDatabase && ShouldAttemptDatabaseBootstrap(exception))
        {
            EnsureDatabaseAccessible();
            return OpenConnectionCore(_connectionString);
        }
    }

    private static SqlConnection OpenConnectionCore(string connectionString)
    {
        var connection = new SqlConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static SqlCommand CreateCommand(
        SqlConnection connection,
        SqlTransaction? transaction,
        string sql,
        params object?[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = ReplacePositionalParameters(sql, parameters.Length);

        for (var index = 0; index < parameters.Length; index++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = $"@p{index}";
            parameter.Value = ToDbValue(parameters[index]);
            command.Parameters.Add(parameter);
        }

        return command;
    }

    private static int ExecuteNonQuery(
        SqlConnection connection,
        SqlTransaction? transaction,
        string sql,
        params object?[] parameters)
    {
        using var command = CreateCommand(connection, transaction, sql, parameters);
        return command.ExecuteNonQuery();
    }

    private static int ExecuteScalarInt(
        SqlConnection connection,
        SqlTransaction? transaction,
        string sql,
        params object?[] parameters)
    {
        using var command = CreateCommand(connection, transaction, sql, parameters);
        var value = command.ExecuteScalar();
        return value is null or DBNull ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static bool TableExists(SqlConnection connection, SqlTransaction? transaction, string tableName)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = ? AND TABLE_NAME = ?;
            """;

        return ExecuteScalarInt(connection, transaction, sql, "dbo", tableName) > 0;
    }

    private static bool HasAdminAuditLogTable(SqlConnection connection, SqlTransaction? transaction = null)
        => TableExists(connection, transaction, "AdminAuditLogs");

    private static bool HasLegacyAuditLogTable(SqlConnection connection, SqlTransaction? transaction = null)
        => TableExists(connection, transaction, "AuditLogs");

    private static bool HasUserActivityLogTable(SqlConnection connection, SqlTransaction? transaction = null)
        => TableExists(connection, transaction, "UserActivityLogs");

    private static string ReadString(SqlDataReader reader, string columnName)
    {
        var value = reader[columnName];
        return value is DBNull ? string.Empty : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string? ReadNullableString(SqlDataReader reader, string columnName)
    {
        var value = reader[columnName];
        return value is DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static bool ReadBool(SqlDataReader reader, string columnName)
    {
        return Convert.ToBoolean(reader[columnName], CultureInfo.InvariantCulture);
    }

    private static int ReadInt(SqlDataReader reader, string columnName)
    {
        return Convert.ToInt32(reader[columnName], CultureInfo.InvariantCulture);
    }

    private static int? ReadNullableInt(SqlDataReader reader, string columnName)
    {
        var value = reader[columnName];
        return value is DBNull ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static bool? ReadNullableBool(SqlDataReader reader, string columnName)
    {
        var value = reader[columnName];
        return value is DBNull ? null : Convert.ToBoolean(value, CultureInfo.InvariantCulture);
    }

    private static long? ReadNullableLong(SqlDataReader reader, string columnName)
    {
        var value = reader[columnName];
        return value is DBNull ? null : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static double ReadDouble(SqlDataReader reader, string columnName)
    {
        return Convert.ToDouble(reader[columnName], CultureInfo.InvariantCulture);
    }

    private static double? ReadNullableDouble(SqlDataReader reader, string columnName)
    {
        var value = reader[columnName];
        return value is DBNull ? null : Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ReadDateTimeOffset(SqlDataReader reader, string columnName)
    {
        var value = reader[columnName];
        return value switch
        {
            DateTimeOffset dateTimeOffset => dateTimeOffset,
            DateTime dateTime => new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)),
            string text => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            _ => DateTimeOffset.Parse(
                Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind)
        };
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(SqlDataReader reader, string columnName)
    {
        return reader[columnName] is DBNull ? null : ReadDateTimeOffset(reader, columnName);
    }

    private static IEnumerable<string> NormalizeList(IEnumerable<string>? values, bool distinct = true)
    {
        if (values is null)
        {
            return [];
        }

        var cleaned = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim());

        return distinct
            ? cleaned.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : cleaned.ToList();
    }

    private static object ToDbValue(object? value)
    {
        return value switch
        {
            null => DBNull.Value,
            DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
            DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
            _ => value
        };
    }

    private static string ReplacePositionalParameters(string sql, int parameterCount)
    {
        var builder = new System.Text.StringBuilder(sql.Length + (parameterCount * 2));
        var parameterIndex = 0;

        foreach (var character in sql)
        {
            if (character == '?' && parameterIndex < parameterCount)
            {
                builder.Append("@p");
                builder.Append(parameterIndex);
                parameterIndex++;
                continue;
            }

            builder.Append(character);
        }

        if (parameterIndex != parameterCount)
        {
            throw new InvalidOperationException("So luong placeholder SQL khong khop voi so parameter duoc truyen vao.");
        }

        return builder.ToString();
    }

    private static string CreateId(string prefix)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return $"{prefix}-{suffix}";
    }

    private static string QuoteSqlIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Ten dinh danh SQL khong hop le.");
        }

        return $"[{value.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    private static string ResolveConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("AdminSqlServer");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return NormalizeConnectionString(connectionString);
        }

        connectionString = configuration["SqlServer:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return NormalizeConnectionString(connectionString);
        }

        throw new InvalidOperationException(
            "Chua cau hinh connection string SQL Server. Hay set ConnectionStrings:AdminSqlServer trong appsettings hoac user secrets.");
    }

    private static bool ResolveDatabaseInitializationFlag(IConfiguration configuration, string key)
        => configuration.GetValue<bool?>($"DatabaseInitialization:{key}") ?? false;

    private static string ResolveSeedSqlPath(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configuredPath = configuration["SqlServer:SeedPath"];
        var relativePath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine("..", "admin-web", "src", "data", "sql", "admin-seed-sqlserver.sql")
            : configuredPath;

        if (Path.IsPathRooted(relativePath))
        {
            return relativePath;
        }

        foreach (var basePath in EnumerateSeedBasePaths(environment.ContentRootPath))
        {
            var candidate = Path.GetFullPath(relativePath, basePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.GetFullPath(relativePath, environment.ContentRootPath);
    }

    private void EnsureDatabaseAccessible()
    {
        var databaseName = GetConfiguredDatabaseName();
        if (string.IsNullOrWhiteSpace(databaseName) ||
            string.Equals(databaseName, "master", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        using var masterConnection = OpenMasterConnection();

        if (!DatabaseExists(masterConnection, databaseName))
        {
            ExecuteSeedScript(masterConnection);
            return;
        }

        EnsureCurrentLoginCanAccessDatabase(masterConnection, databaseName);
    }

    private void EnsureDatabaseSeeded()
    {
        using var masterConnection = OpenMasterConnection();
        ExecuteSeedScript(masterConnection);
    }

    private SqlConnection OpenMasterConnection()
    {
        var builder = TryCreateConnectionStringBuilder()
            ?? throw new InvalidOperationException("Không thể phân tích connection string SQL Server hiện tại.");

        builder.InitialCatalog = "master";
        builder["Database"] = "master";

        if (!string.IsNullOrWhiteSpace(builder.AttachDBFilename))
        {
            builder.AttachDBFilename = string.Empty;
        }

        return OpenConnectionCore(builder.ConnectionString);
    }

    private string GetConfiguredDatabaseName()
    {
        var builder = TryCreateConnectionStringBuilder();
        return builder?.InitialCatalog ?? string.Empty;
    }

    private static bool DatabaseExists(SqlConnection connection, string databaseName)
    {
        using var command = CreateCommand(connection, null, "SELECT DB_ID(?);", databaseName);
        return command.ExecuteScalar() is not null and not DBNull;
    }

    private static void EnsureCurrentLoginCanAccessDatabase(SqlConnection masterConnection, string databaseName)
    {
        var quotedDatabaseName = QuoteIdentifier(databaseName);
        using var command = masterConnection.CreateCommand();
        command.CommandText =
            $"""
            DECLARE @login sysname = SUSER_SNAME();
            IF @login IS NOT NULL
            BEGIN
                DECLARE @sql nvarchar(max) = N'USE {quotedDatabaseName};
                IF SUSER_ID(@login) IS NOT NULL AND DATABASE_PRINCIPAL_ID(@login) IS NULL
                    EXEC(N''CREATE USER '' + QUOTENAME(@login) + N'' FOR LOGIN '' + QUOTENAME(@login));
                IF DATABASE_PRINCIPAL_ID(@login) IS NOT NULL AND IS_ROLEMEMBER(N''db_owner'', @login) <> 1
                    EXEC(N''ALTER ROLE [db_owner] ADD MEMBER '' + QUOTENAME(@login));';

                EXEC sp_executesql @sql, N'@login sysname', @login = @login;
            END;
            """;
        command.ExecuteNonQuery();
    }

    private void ExecuteSeedScript(SqlConnection masterConnection)
    {
        if (!File.Exists(_seedSqlServerPath))
        {
            throw new InvalidOperationException(
                $"Không tìm thấy file seed SQL Server để khởi tạo database: {_seedSqlServerPath}");
        }

        var scriptContent = File.ReadAllText(_seedSqlServerPath);
        foreach (var batch in SplitSqlBatches(scriptContent))
        {
            if (string.IsNullOrWhiteSpace(batch))
            {
                continue;
            }

            using var command = masterConnection.CreateCommand();
            command.CommandText = batch;
            command.CommandTimeout = 180;
            command.ExecuteNonQuery();
        }
    }

    private static IEnumerable<string> SplitSqlBatches(string scriptContent)
    {
        return Regex
            .Split(scriptContent, @"^\s*GO\s*(?:--.*)?$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Where(batch => !string.IsNullOrWhiteSpace(batch))
            .Select(batch => batch.Trim());
    }

    private static bool ShouldAttemptDatabaseBootstrap(SqlException exception)
    {
        return exception.Number == 4060 ||
            CollapseWhitespace(exception.Message).Contains("Cannot open database", StringComparison.OrdinalIgnoreCase);
    }

    private string BuildSqlConnectionFailureMessage(SqlException exception)
    {
        var builder = TryCreateConnectionStringBuilder();
        var server = string.IsNullOrWhiteSpace(builder?.DataSource) ? "(khong ro)" : builder.DataSource;
        var authMode = builder?.IntegratedSecurity == true ? "Windows Authentication" : "SQL Login";
        var databaseName = string.IsNullOrWhiteSpace(builder?.InitialCatalog) ? "(khong ro)" : builder.InitialCatalog;
        var isNamedPipe = server.StartsWith("np:", StringComparison.OrdinalIgnoreCase);
        var detail = CollapseWhitespace(exception.Message);

        if (detail.Contains("Cannot generate SSPI context", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("target principal name is incorrect", StringComparison.OrdinalIgnoreCase))
        {
            var namedPipeHint = isNamedPipe
                ? " Chuoi ket noi hien tai dang dung named pipe."
                : string.Empty;

            return $"Không thể kết nối SQL Server tại Server='{server}'. Lỗi hiện tại là Windows Authentication/SSPI ({authMode}): {detail}.{namedPipeHint} Nếu bạn đang dùng Trusted_Connection=True, hãy thử SQL login (User ID/Password) hoặc sửa SPN/Windows auth của instance SQL Server.";
        }

        if (detail.Contains("error: 40", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("network-related", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("refused the network connection", StringComparison.OrdinalIgnoreCase))
        {
            return $"Không thể kết nối SQL Server tại Server='{server}'. Backend đang gặp lỗi mạng/transport: {detail}. Hãy kiểm tra SQL Server service, SQL Browser, TCP/IP và firewall của instance này.";
        }

        if (detail.Contains("Cannot open database", StringComparison.OrdinalIgnoreCase))
        {
            return $"Không thể mở database '{databaseName}' trên Server='{server}' ({authMode}). Backend đã thử khởi tạo database hoặc gán quyền cho login hiện tại nếu có thể, nhưng vẫn thất bại. Chi tiết: {detail}";
        }

        return $"Không thể kết nối SQL Server tại Server='{server}' ({authMode}). Chi tiết: {detail}";
    }

    private SqlConnectionStringBuilder? TryCreateConnectionStringBuilder()
    {
        try
        {
            return new SqlConnectionStringBuilder(NormalizeConnectionString(_connectionString));
        }
        catch
        {
            return null;
        }
    }

    private static string CollapseWhitespace(string value)
    {
        return string.Join(" ", value.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private static IEnumerable<string> EnumerateSeedBasePaths(string contentRootPath)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = contentRootPath;

        while (!string.IsNullOrWhiteSpace(current) && visited.Add(current))
        {
            yield return current;

            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                yield break;
            }

            current = parent.FullName;
        }
    }

    private static string NormalizeConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        var normalized = Regex.Replace(
            connectionString,
            @"(^|;)\s*username\s*=",
            "$1User ID=",
            RegexOptions.IgnoreCase);

        normalized = Regex.Replace(
            normalized,
            @"(^|;)\s*userid\s*=",
            "$1User ID=",
            RegexOptions.IgnoreCase);

        return normalized;
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }
}
