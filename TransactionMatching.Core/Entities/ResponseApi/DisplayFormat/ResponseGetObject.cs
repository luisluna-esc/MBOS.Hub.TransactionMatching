using System.Net;
using System.Text.Json.Serialization;
using TransactionMatching.Core.Entities.ResponseApi.Details;

namespace TransactionMatching.Core.Entities.ResponseApi.DisplayFormat;

public class ResponseGetObject
{
    public object Data { get; set; } = new();
    public Message[] Messages { get; set; } = [];
    [JsonIgnore]
    public HttpStatusCode StatusCode { get; set; }
}