using System.Net;
using System.Text.Json.Serialization;
using TransactionMatching.Core.Entities.ResponseApi.Details;

namespace TransactionMatching.Core.Entities.ResponseApi.DisplayFormat;

public class ResponsePost
{
    public Message[] Messages { get; set; } = [];
    public long Id { get; set; }
    [JsonIgnore]
    public HttpStatusCode StatusCode { get; set; }
}