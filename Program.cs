using Amazon.Lambda;
using Amazon.Lambda.APIGatewayEvents;
using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace LambdaAPIGatewayConsole;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.WebHost.ConfigureKestrel(o =>
        {
            o.ListenAnyIP(80);
        });

        builder.Services.AddScoped<APIGatewayHandler>();

        var app = builder.Build();        

        app.Map("", c =>
        {
            //var handler = c.RequestServices.GetService<APIGatewayHandler>()!;
            var handler = new APIGatewayHandler();
            handler.ServiceURL = "http://lambda-0001.10.29.211.2.nip.io:32474/";
            handler.FunctionName = "lambda-0001";
            c.Use(handler.Handle);
        });

        await app.RunAsync();
    }
}

internal class APIGatewayHandler
{
    public String ServiceURL { get; set; } = null!;
    public String FunctionName { get; set; } = null!;
    public async Task Handle(HttpContext context, RequestDelegate next)
    {
        var handled = false;

        AmazonLambdaConfig config = new AmazonLambdaConfig()
        {
            ServiceURL = this.ServiceURL
        };

        AmazonLambdaClient client = new AmazonLambdaClient("AK", "SK", config);

        var request = new APIGatewayProxyRequest()
        {
            Path = context.Request.Path,             
            HttpMethod = context.Request.Method
        };
        
        if (context.Request.QueryString.HasValue)
        {
            request.QueryStringParameters = context.Request.Query
                .Where(kv => kv.Value.Count == 1)
                .ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
            request.MultiValueQueryStringParameters = context.Request.Query
                .Where(kv => kv.Value.Count > 1)
                .ToDictionary(kv => kv.Key, kv => (IList<String>)kv.Value.ToList());
        }

        //request.Headers = new Dictionary<String, String>();
        //request.Headers.Add("Host", context.Request.Host.ToString());
        request.Headers = context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());

        switch (context.Request.Method)
        {
            case "POST":
            case "PUT":
                //request.Headers.Add("Content-Type", context.Request.ContentType);

                if ((context.Request.ContentLength ?? 0) > 0)
                {
                    //request.Headers.Add("Content-Length", (context.Request.ContentLength ?? 0).ToString());
                    var bodyBuffer = new Byte[context.Request.ContentLength ?? 0];
                    await context.Request.Body.ReadAsync(bodyBuffer, 0, bodyBuffer.Length);
                    request.Body = System.Text.Encoding.UTF8.GetString(bodyBuffer);
                    request.IsBase64Encoded = false;
                }
                else
                {
                    request.Body = "";
                    request.IsBase64Encoded = false;
                }
                break;
            default:
                request.Body = "";
                request.IsBase64Encoded = false;
                break;
        }

        var lambdaResponse = await client.InvokeAsync(new Amazon.Lambda.Model.InvokeRequest()
        {
            FunctionName = this.FunctionName,
            InvocationType = "RequestResponse",
            Payload = JsonConvert.SerializeObject(request)
        });

        var responseBuffer = new Byte[lambdaResponse.Payload.Length];
        var count = await lambdaResponse.Payload.ReadAsync(responseBuffer, 0, responseBuffer.Length);
        var responseString = System.Text.Encoding.UTF8.GetString(responseBuffer);
        var response = JsonConvert.DeserializeObject<APIGatewayProxyResponse>(responseString);

        if (response != null)
        {
            context.Response.StatusCode = response.StatusCode;
            context.Response.ContentType = response.Headers.ContainsKey("Content-Type") ? response.Headers["Content-Type"] : response.MultiValueHeaders["Content-Type"].FirstOrDefault()!;
            context.Response.Headers.Connection = "Close";
            if (response.IsBase64Encoded)
            {
                await context.Response.Body.WriteAsync(Convert.FromBase64String(response.Body));
            }
            else
            {
                var outputBuffer = System.Text.Encoding.UTF8.GetBytes(response.Body);
                await context.Response.Body.WriteAsync(outputBuffer);                
            }

            await context.Response.Body.FlushAsync();
            context.Response.Body.Close();            

            handled = true;
        }

        if (!handled)
        {
            await next.Invoke(context);
        }
    }
}
