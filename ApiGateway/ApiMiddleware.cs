﻿using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace Fast.ApiGateway
{
    public class ApiMiddleware
    {
        public ApiMiddleware(RequestDelegate request) { }

        public Task InvokeAsync(HttpContext context, IResponse response)
        {
            response.Content(context);
            return Task.CompletedTask;
        }
    }
}
 