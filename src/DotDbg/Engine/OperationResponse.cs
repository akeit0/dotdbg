using System.Text.Json.Nodes;

namespace DotDbg.Engine;

internal static class OperationResponse
{
    internal static JsonObject ResponseOk(string message, JsonObject? data = null)
    {
        var response = new JsonObject { ["success"] = true, ["message"] = message };
        if (data is not null)
            response["data"] = data;
        return response;
    }

    internal static JsonObject ResponseError(string message) =>
        new() { ["success"] = false, ["message"] = message };
}
