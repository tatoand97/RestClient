using System;
using System.Net;

namespace NameProject.RestClient.Exceptions;

public class RestRequestFailedException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public RestRequestFailedException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public RestRequestFailedException() { }
    public RestRequestFailedException(string message) : base(message) { }
    public RestRequestFailedException(string message, Exception inner) : base(message, inner) { }
}
