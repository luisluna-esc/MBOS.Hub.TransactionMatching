using System.Net;
using TransactionMatching.Core.Entities.ResponseApi.Details;
using TransactionMatching.Core.Entities.ResponseApi.DisplayFormat;

namespace TransactionMatching.Application.Services;

/// <summary>Atajos para armar ResponseGetObject y ResponsePost en los servicios de asistencia.</summary>
internal static class Responses
{
    public static ResponseGetObject Ok(object data) => new()
    {
        Data = data,
        Messages = [],
        StatusCode = HttpStatusCode.OK
    };

    public static ResponseGetObject GetError(HttpStatusCode statusCode, string description) => new()
    {
        Data = new(),
        Messages = [Error(description)],
        StatusCode = statusCode
    };

    public static ResponsePost Saved(long id, string description, HttpStatusCode statusCode = HttpStatusCode.OK) => new()
    {
        Id = id,
        Messages = [new Message { Type = MessageType.Success, Description = description }],
        StatusCode = statusCode
    };

    public static ResponsePost PostError(HttpStatusCode statusCode, params string[] descriptions) => new()
    {
        Id = 0,
        Messages = descriptions.Select(Error).ToArray(),
        StatusCode = statusCode
    };

    private static Message Error(string description) => new() { Type = MessageType.Error, Description = description };
}
