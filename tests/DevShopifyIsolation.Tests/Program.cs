using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Site_2024.Web.Api.Models;
using Site_2024.Web.Api.Services;

var forbiddenShops = new[]
{
    "wrong-shop.myshopify.com",
    "https://another-shop.myshopify.com",
    "gr-sons-shipping-dev.myshopify.com.evil.com",
    ""
};

foreach (string shop in forbiddenShops)
{
    var network = new RecordingHandler();
    var token = new ShopifyTokenService(
        new HttpClient(network),
        Options.Create(new ShopifySettings { ShopDomain = shop }),
        NullLogger<ShopifyTokenService>.Instance);

    bool tokenBlocked = false;
    try
    {
        await token.GetAccessTokenAsync();
    }
    catch (InvalidOperationException e)
        when (e.Message.Contains("DEV Shopify isolation blocked"))
    {
        tokenBlocked = true;
    }
    if (!tokenBlocked || network.Calls != 0)
        throw new Exception($"Unsafe Shopify token request was not blocked for '{shop}'.");

    var checkout = new ShopifyCheckoutService(
        null!,
        Options.Create(new ShopifySettings { ShopDomain = shop }),
        NullLogger<ShopifyCheckoutService>.Instance);
    bool checkoutBlocked = false;
    try
    {
        checkout.CreateCheckoutUrl(new ShopifyCheckoutRequest
        {
            Items = new List<ShopifyCheckoutLineItemRequest>
            {
                new() { PartId = 1, Quantity = 1 }
            }
        });
    }
    catch (InvalidOperationException e)
        when (e.Message.Contains("DEV Shopify checkout is disabled") ||
              e.Message.Contains("Shopify shop domain is missing"))
    {
        checkoutBlocked = true;
    }
    if (!checkoutBlocked)
        throw new Exception($"Unsafe Shopify checkout URL was not blocked for '{shop}'.");
}

// Prove that the *correct* isolated shop passes the target check, with all
// outgoing HTTP intercepted locally; no live Shopify request is ever sent.
var approvedNetwork = new RecordingHandler();
var approvedToken = new ShopifyTokenService(
    new HttpClient(approvedNetwork),
    Options.Create(new ShopifySettings
    {
        ShopDomain = "gr-sons-shipping-dev.myshopify.com",
        ClientId = "local-test-only",
        ClientSecret = "local-test-only"
    }),
    NullLogger<ShopifyTokenService>.Instance);
try
{
    await approvedToken.GetAccessTokenAsync();
}
catch (ApplicationException)
{
    // The fake HTTP handler intentionally returns 400, never contacting Shopify.
}
if (approvedNetwork.Calls != 1 ||
    approvedNetwork.LastHost != "gr-sons-shipping-dev.myshopify.com")
    throw new Exception("Isolated DEV shop was not routed to the expected host.");

Console.WriteLine("DEV Shopify isolation checks passed; no external network requests made.");

sealed class RecordingHandler : HttpMessageHandler
{
    public int Calls { get; private set; }
    public string? LastHost { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        LastHost = request.RequestUri?.Host;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{}")
        });
    }
}
