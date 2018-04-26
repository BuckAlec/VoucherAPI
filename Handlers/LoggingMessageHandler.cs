using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Web;
using FortressCodesApi.Repsoitory;

namespace FortressCodesApi.Handlers
{
    public class LoggingMessageHandler: DelegatingHandler
    {
        protected async override System.Threading.Tasks.Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
        {
            Debug.WriteLine("Process request");
            var response = await base.SendAsync(request, cancellationToken);
            Debug.WriteLine("Process response");
 	        return response;
        }
    }
}