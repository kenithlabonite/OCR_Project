using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace OCR_Frontend.Helpers
{
    public static class ApiErrorHelper
    {
        public static async Task<string> ExtractErrorMessageAsync(HttpResponseMessage response, string fallbackMessage = "An error occurred.")
        {
            if (response == null) return fallbackMessage;

            try
            {
                string content = await response.Content.ReadAsStringAsync();
                return ParseErrorMessage(content, fallbackMessage);
            }
            catch
            {
                return fallbackMessage;
            }
        }

        public static string ParseErrorMessage(string? jsonString, string fallbackMessage = "An error occurred.")
        {
            if (string.IsNullOrWhiteSpace(jsonString)) return fallbackMessage;

            string trimmed = jsonString.Trim();

            // If it's plain text (does not start with JSON object/array)
            if (!trimmed.StartsWith("{") && !trimmed.StartsWith("["))
            {
                return trimmed.Trim('"');
            }

            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;

                // 1. Check for ASP.NET Core ValidationProblemDetails "errors" dictionary
                // e.g. { "errors": { "Password": ["Password must contain at least one capital letter."] } }
                if (root.TryGetProperty("errors", out var errorsProp) && errorsProp.ValueKind == JsonValueKind.Object)
                {
                    var messages = new List<string>();
                    foreach (var prop in errorsProp.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in prop.Value.EnumerateArray())
                            {
                                string? msg = item.GetString();
                                if (!string.IsNullOrWhiteSpace(msg))
                                {
                                    messages.Add(msg);
                                }
                            }
                        }
                        else if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            string? msg = prop.Value.GetString();
                            if (!string.IsNullOrWhiteSpace(msg))
                            {
                                messages.Add(msg);
                            }
                        }
                    }

                    if (messages.Count > 0)
                    {
                        return string.Join(" ", messages);
                    }
                }

                // 2. Check for standard JSON error properties: "message", "detail", "error", "title"
                if (root.TryGetProperty("message", out var messageProp) && messageProp.ValueKind == JsonValueKind.String)
                {
                    string? msg = messageProp.GetString();
                    if (!string.IsNullOrWhiteSpace(msg)) return msg;
                }

                if (root.TryGetProperty("detail", out var detailProp) && detailProp.ValueKind == JsonValueKind.String)
                {
                    string? msg = detailProp.GetString();
                    if (!string.IsNullOrWhiteSpace(msg)) return msg;
                }

                if (root.TryGetProperty("error", out var errorProp) && errorProp.ValueKind == JsonValueKind.String)
                {
                    string? msg = errorProp.GetString();
                    if (!string.IsNullOrWhiteSpace(msg)) return msg;
                }

                if (root.TryGetProperty("title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String)
                {
                    string? msg = titleProp.GetString();
                    if (!string.IsNullOrWhiteSpace(msg) && msg != "One or more validation errors occurred.")
                    {
                        return msg;
                    }
                }
            }
            catch
            {
                // Fallback if JSON parsing fails
            }

            return trimmed.Trim('"');
        }
    }
}
