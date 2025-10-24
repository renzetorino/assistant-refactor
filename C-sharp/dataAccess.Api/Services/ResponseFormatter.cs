using System.Text.Json;

namespace dataAccess.Api.Services;

/// <summary>
/// Formats the final response for the frontend.
/// Combines SQL, validation status, results, and summary into a clean JSON structure.
/// </summary>
public class ResponseFormatter
{
    /// <summary>
    /// Formats a successful query response.
    /// </summary>
    public object FormatSuccess(
        string question,
        string sql,
        int rowCount,
        string summary,
        JsonElement? results = null)
    {
        var response = new Dictionary<string, object>
        {
            ["success"] = true,
            ["question"] = question,
            ["sql"] = sql,
            ["rowCount"] = rowCount,
            ["summary"] = summary
        };

        // Include full results if provided (optional for detail view)
        if (results.HasValue)
        {
            response["results"] = results.Value;
        }

        return response;
    }

    /// <summary>
    /// Formats an error response.
    /// </summary>
    public object FormatError(string question, string error, string? sql = null)
    {
        var response = new Dictionary<string, object>
        {
            ["success"] = false,
            ["question"] = question,
            ["error"] = error
        };

        if (!string.IsNullOrWhiteSpace(sql))
        {
            response["sql"] = sql;
        }

        return response;
    }

    /// <summary>
    /// Formats a validation failure response.
    /// </summary>
    public object FormatValidationError(
        string question,
        string sql,
        string validationError)
    {
        return new Dictionary<string, object>
        {
            ["success"] = false,
            ["question"] = question,
            ["sql"] = sql,
            ["validated"] = false,
            ["error"] = validationError
        };
    }
}
