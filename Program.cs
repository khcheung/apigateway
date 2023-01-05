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
            var handler = c.ApplicationServices.GetService<APIGatewayHandler>()!;
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

        request.Headers = new Dictionary<String, String>();
        request.Headers.Add("Host", context.Request.Host.ToString());

        switch (context.Request.Method)
        {
            case "POST":
            case "PUT":
                request.Headers.Add("Content-Type", context.Request.ContentType);

                if ((context.Request.ContentLength ?? 0) > 0)
                {
                    request.Headers.Add("Content-Length", (context.Request.ContentLength ?? 0).ToString());
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

            var outputBuffer = System.Text.Encoding.UTF8.GetBytes(response.Body);
            await context.Response.Body.WriteAsync(outputBuffer);

            handled = true;
        }

        if (!handled)
        {
            await next.Invoke(context);
        }
    }
}
