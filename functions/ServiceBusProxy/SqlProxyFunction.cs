using System.Data;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ServiceBusProxy;

public class SqlProxyFunction
{
    private readonly ILogger<SqlProxyFunction> _logger;
    private readonly IConfiguration _configuration;

    public SqlProxyFunction(
        ILogger<SqlProxyFunction> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Health check for SQL proxy
    /// </summary>
    [Function("SqlHealthCheck")]
    public HttpResponseData SqlHealthCheck(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "apim/sql/{connectionId}/health")]
        HttpRequestData req,
        string connectionId)
    {
        _logger.LogInformation($"[SQL-PROXY] Health check for connection {connectionId}");
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.WriteString("SQL Proxy OK");
        return response;
    }

    /// <summary>
    /// Execute stored procedure
    /// POST /apim/sql/{connectionId}/v2/datasets/{server},{database}/procedures/{procedureName}
    /// </summary>
    [Function("ExecuteStoredProcedure")]
    public async Task<HttpResponseData> ExecuteStoredProcedure(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "apim/sql/{connectionId}/v2/datasets/{server},{database}/procedures/{procedureName}")]
        HttpRequestData req,
        string connectionId,
        string server,
        string database,
        string procedureName)
    {
        _logger.LogInformation($"========================================");
        _logger.LogInformation($"[SQL-PROXY] Execute Stored Procedure");
        _logger.LogInformation($"[SQL-PROXY] Connection: {connectionId}");
        _logger.LogInformation($"[SQL-PROXY] Server: {server}, Database: {database}");
        _logger.LogInformation($"[SQL-PROXY] Procedure: {procedureName}");
        _logger.LogInformation($"========================================");

        try
        {
            // Get connection string from configuration
            var connectionString = _configuration["SQL_CONNECTION_STRING"];
            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogError("[SQL-PROXY] SQL_CONNECTION_STRING not found in configuration");
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new { error = "SQL_CONNECTION_STRING not configured" }));
                return errorResponse;
            }

            // Read request body (parameters)
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation($"[SQL-PROXY] Request Body: {requestBody}");

            JsonDocument? requestJson = null;
            if (!string.IsNullOrWhiteSpace(requestBody))
            {
                requestJson = JsonDocument.Parse(requestBody);
            }

            // Decode procedure name (comes URL encoded like %255Bdbo%255D.%255Bsp_GetPendingOutboxMessages%255D)
            var decodedProcedure = Uri.UnescapeDataString(Uri.UnescapeDataString(procedureName));
            _logger.LogInformation($"[SQL-PROXY] Decoded Procedure: {decodedProcedure}");

            // Execute stored procedure
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            using var command = new SqlCommand(decodedProcedure, connection);
            command.CommandType = CommandType.StoredProcedure;

            // Add parameters if present
            if (requestJson != null && requestJson.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in requestJson.RootElement.EnumerateObject())
                {
                    object? paramValue = property.Value.ValueKind switch
                    {
                        JsonValueKind.Number => property.Value.GetInt32(),
                        JsonValueKind.String => property.Value.GetString(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Null => DBNull.Value,
                        _ => property.Value.GetRawText()
                    };
                    
                    command.Parameters.AddWithValue($"@{property.Name}", paramValue ?? DBNull.Value);
                    _logger.LogInformation($"[SQL-PROXY] Param: @{property.Name} = {paramValue}");
                }
            }

            // Execute and get results
            using var reader = await command.ExecuteReaderAsync();
            
            var results = new List<Dictionary<string, object?>>();
            
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var value = reader.GetValue(i);
                    row[reader.GetName(i)] = value == DBNull.Value ? null : value;
                }
                results.Add(row);
            }

            _logger.LogInformation($"[SQL-PROXY] Returned {results.Count} rows");

            // Format response to match Logic Apps SQL connector format
            var responseData = new
            {
                resultSets = new
                {
                    Table1 = results
                },
                outputParameters = new { },
                returnCode = 0
            };

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await response.WriteStringAsync(JsonSerializer.Serialize(responseData));

            _logger.LogInformation($"[SQL-PROXY] ✓ Stored procedure executed successfully");

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError($"========================================");
            _logger.LogError($"[SQL-PROXY] ❌ ERROR executing stored procedure");
            _logger.LogError($"[SQL-PROXY] Exception: {ex.Message}");
            _logger.LogError($"[SQL-PROXY] Stack Trace: {ex.StackTrace}");
            _logger.LogError($"========================================");

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new
            {
                error = ex.Message,
                exceptionType = ex.GetType().Name
            }));
            return errorResponse;
        }
    }

    /// <summary>
    /// Get items from table (SELECT)
    /// GET /apim/sql/{connectionId}/v2/datasets/{server},{database}/tables/{tableName}/items
    /// </summary>
    [Function("GetTableItems")]
    public async Task<HttpResponseData> GetTableItems(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "apim/sql/{connectionId}/v2/datasets/{server},{database}/tables/{tableName}/items")]
        HttpRequestData req,
        string connectionId,
        string server,
        string database,
        string tableName)
    {
        _logger.LogInformation($"[SQL-PROXY] GET Table Items: {tableName}");

        try
        {
            var connectionString = _configuration["SQL_CONNECTION_STRING"];
            if (string.IsNullOrEmpty(connectionString))
            {
                var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
                await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new { error = "SQL_CONNECTION_STRING not configured" }));
                return errorResponse;
            }

            // Decode table name
            var decodedTable = Uri.UnescapeDataString(Uri.UnescapeDataString(tableName));
            _logger.LogInformation($"[SQL-PROXY] Decoded Table: {decodedTable}");

            // Parse query parameters ($filter, $top, etc.)
            var query = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
            var filter = query["$filter"];
            var top = query["$top"];

            _logger.LogInformation($"[SQL-PROXY] Filter: {filter}, Top: {top}");

            // Build SQL query
            var sql = $"SELECT {(string.IsNullOrEmpty(top) ? "" : $"TOP {top} ")}* FROM {decodedTable}";
            
            if (!string.IsNullOrEmpty(filter))
            {
                // Simple filter parsing (e.g., "MessageId eq 'abc123'")
                sql += $" WHERE {ConvertODataFilterToSql(filter)}";
            }

            _logger.LogInformation($"[SQL-PROXY] SQL: {sql}");

            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            using var command = new SqlCommand(sql, connection);
            using var reader = await command.ExecuteReaderAsync();

            var results = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var value = reader.GetValue(i);
                    row[reader.GetName(i)] = value == DBNull.Value ? null : value;
                }
                results.Add(row);
            }

            _logger.LogInformation($"[SQL-PROXY] ✓ Returned {results.Count} rows");

            var responseData = new { value = results };
            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await response.WriteStringAsync(JsonSerializer.Serialize(responseData));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError($"[SQL-PROXY] ❌ ERROR: {ex.Message}");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new { error = ex.Message }));
            return errorResponse;
        }
    }

    /// <summary>
    /// Insert item into table
    /// POST /apim/sql/{connectionId}/v2/datasets/{server},{database}/tables/{tableName}/items
    /// </summary>
    [Function("InsertTableItem")]
    public async Task<HttpResponseData> InsertTableItem(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "apim/sql/{connectionId}/v2/datasets/{server},{database}/tables/{tableName}/items")]
        HttpRequestData req,
        string connectionId,
        string server,
        string database,
        string tableName)
    {
        _logger.LogInformation($"[SQL-PROXY] INSERT Table Item: {tableName}");

        try
        {
            var connectionString = _configuration["SQL_CONNECTION_STRING"];
            var decodedTable = Uri.UnescapeDataString(Uri.UnescapeDataString(tableName));

            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            _logger.LogInformation($"[SQL-PROXY] Insert Body: {requestBody}");

            var item = JsonDocument.Parse(requestBody);

            // Build INSERT statement
            var columns = new List<string>();
            var values = new List<string>();
            var parameters = new List<SqlParameter>();

            int paramIndex = 0;
            foreach (var property in item.RootElement.EnumerateObject())
            {
                columns.Add($"[{property.Name}]");
                values.Add($"@p{paramIndex}");
                
                object? value = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.GetDecimal(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => DBNull.Value,
                    _ => property.Value.GetRawText()
                };
                
                parameters.Add(new SqlParameter($"@p{paramIndex}", value ?? DBNull.Value));
                paramIndex++;
            }

            var sql = $"INSERT INTO {decodedTable} ({string.Join(", ", columns)}) VALUES ({string.Join(", ", values)})";
            _logger.LogInformation($"[SQL-PROXY] SQL: {sql}");

            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            using var command = new SqlCommand(sql, connection);
            command.Parameters.AddRange(parameters.ToArray());

            await command.ExecuteNonQueryAsync();

            _logger.LogInformation($"[SQL-PROXY] ✓ Row inserted successfully");

            var response = req.CreateResponse(HttpStatusCode.OK);
            await response.WriteStringAsync(requestBody); // Return inserted item
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError($"[SQL-PROXY] ❌ ERROR: {ex.Message}");
            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteStringAsync(JsonSerializer.Serialize(new { error = ex.Message }));
            return errorResponse;
        }
    }

    private string ConvertODataFilterToSql(string oDataFilter)
    {
        // Simple conversion: "MessageId eq 'abc'" -> "MessageId = 'abc'"
        return oDataFilter
            .Replace(" eq ", " = ")
            .Replace(" ne ", " <> ")
            .Replace(" gt ", " > ")
            .Replace(" lt ", " < ")
            .Replace(" ge ", " >= ")
            .Replace(" le ", " <= ");
    }
}
