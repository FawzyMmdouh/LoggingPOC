using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Net.Http.Headers;
using Serilog;
using Serilog.Context;
using Serilog.Events;

namespace LoggingPOC.MiddleWares
{
    public class RequestResponseLoggingMiddleware
    {
        private readonly HashSet<string> HeaderWhitelist = new HashSet<string> { "Content-Type", "Content-Length", "User-Agent" };
        private readonly RequestDelegate _next;
        private readonly IConfiguration _configuration;
        private readonly ILogger Log = Serilog.Log.ForContext<RequestResponseLoggingMiddleware>();
        const string MessageTemplate = "HTTP {RequestMethod} {QueryParameters} {RequestPath} {RequestId} {RequestBody} responded {ResponseBody} {StatusCode} in {Elapsed:0.0000} ms";


        public RequestResponseLoggingMiddleware(RequestDelegate next, IConfiguration configuration)
        {
            _next = next;
            _configuration = configuration;
        }
        public async Task InvokeAsync(HttpContext context)
        {
            if (IsWhiteListedEndPoint(context.Request.Method+context.Request.Path.ToString()))
            {
                var serviceId = context.Request.Path.ToString().Split('/')[1];
                var auth = context.Request.Headers[HeaderNames.Authorization];
                // Push the user name into the log context so that it is included in all log entries
                var queryParameters = context.Request.QueryString;
                //var requestHeaders = context.Request.Headers;
                if (!string.IsNullOrEmpty(serviceId))
                {
                    LogContext.PushProperty("ServiceId", serviceId);

                }
                context.Request.EnableBuffering();
                var request = await FormatRequest(context.Request);
                
                //Copy a pointer to the original response body stream
                var originalBodyStream = context.Response.Body;
                //Create a new memory stream...
                using var responseBody = new MemoryStream();
                //...and use that for the temporary response body
                context.Response.Body = responseBody;
                //Continue down the Middleware pipeline, eventually returning to this class
                var start = Stopwatch.GetTimestamp();
                await _next(context);
                var elapsedMs = GetElapsedMilliseconds(start, Stopwatch.GetTimestamp());
                //Format the response from the server
                var response = await FormatResponse(context.Response);
               
                //Save log to chosen datastore
                //var sep = "\n" + string.Concat(Enumerable.Repeat("*", 100)) + "\n" + string.Concat(Enumerable.Repeat("*", 100));
                var statusCode = context.Response?.StatusCode;
                var level = statusCode > 499 ? LogEventLevel.Error : LogEventLevel.Information;
                var log = level == LogEventLevel.Error ? LogForErrorContext(context) : Log;

                log.Write(level, MessageTemplate, context.Request.Method/*,requestHeaders*/, queryParameters, context.Request.Path.ToString(), Guid.NewGuid(),
                         request, response, statusCode, elapsedMs);
                //Copy the contents of the new memory stream (which contains the response) to the original stream, which is then returned to the client.
                await responseBody.CopyToAsync(originalBodyStream);
            }
            else
            {
                await _next(context);
            }
        }
        private double GetElapsedMilliseconds(long start, long stop)
        {
            return (stop - start) * 1000 / (double)Stopwatch.Frequency;
        }
        private ILogger LogForErrorContext(HttpContext httpContext)
        {
            var request = httpContext.Request;

            var loggedHeaders = request.Headers
                .Where(h => HeaderWhitelist.Contains(h.Key))
                .ToDictionary(h => h.Key, h => h.Value.ToString());

            var result = Log
                .ForContext("RequestHeaders", loggedHeaders, destructureObjects: true)
                .ForContext("RequestHost", request.Host)
                .ForContext("RequestProtocol", request.Protocol);

            return result;
        }
        private async Task<string> FormatRequest(HttpRequest request)
        {
            // Leave the body open so the next middleware can read it.
            using var reader = new StreamReader(
                request.Body,
                encoding: Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            // Do some processing with body…
            var formattedRequest = body;
            // Reset the request body stream position so the next middleware can read it
            request.Body.Position = 0;
            return formattedRequest;
        }
        private async Task<string> FormatResponse(HttpResponse response)
        {
            //We need to read the response stream from the beginning...
            response.Body.Seek(0, SeekOrigin.Begin);
            //...and copy it into a string
            string text = await new StreamReader(response.Body).ReadToEndAsync();
            //We need to reset the reader for the response so that the client can read it.
            response.Body.Seek(0, SeekOrigin.Begin);
            //Return the string for the response, including the status code (e.g. 200, 404, 401, etc.)
            return $"{response.StatusCode}: {text}";
        }

        private bool IsWhiteListedEndPoint(string path)
        {
            var whiteList = _configuration["LogWhiteList"].Split(",");
            return whiteList.Contains(path.ToLower());
        }
    }
}