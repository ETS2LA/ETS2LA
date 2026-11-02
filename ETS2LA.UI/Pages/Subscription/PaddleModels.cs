using System.Text.Json.Serialization;

namespace ETS2LA.UI.Pages.Subscription;

// These models mirror Paddle.js PricePreview responses.
// https://developer.paddle.com/paddlejs/methods/price-preview

public class PaddlePricePreviewResponse
{
    [JsonPropertyName("data")]
    public PaddlePricePreviewData? Data { get; set; }
}

public class PaddlePricePreviewData
{
    [JsonPropertyName("currencyCode")]
    public string CurrencyCode { get; set; } = "USD";

    [JsonPropertyName("details")]
    public PaddlePricePreviewDetails? Details { get; set; }
}

public class PaddlePricePreviewDetails
{
    [JsonPropertyName("lineItems")]
    public List<PaddleLineItem> LineItems { get; set; } = new();
}

public class PaddleLineItem
{
    [JsonPropertyName("price")]
    public PaddlePrice? Price { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("totals")]
    public PaddleTotals? Totals { get; set; }

    [JsonPropertyName("formattedTotals")]
    public PaddleTotals? FormattedTotals { get; set; }

    [JsonPropertyName("product")]
    public PaddleProduct? Product { get; set; }
}

public class PaddlePrice
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("billingCycle")]
    public PaddleTimePeriod? BillingCycle { get; set; }

    [JsonPropertyName("trialPeriod")]
    public PaddleTimePeriod? TrialPeriod { get; set; }

    [JsonPropertyName("unitPrice")]
    public PaddleUnitPrice? UnitPrice { get; set; }
}

public class PaddleTimePeriod
{
    [JsonPropertyName("interval")]
    public string Interval { get; set; } = string.Empty;

    [JsonPropertyName("frequency")]
    public int Frequency { get; set; }
}

public class PaddleUnitPrice
{
    [JsonPropertyName("amount")]
    public string Amount { get; set; } = "0";

    [JsonPropertyName("currencyCode")]
    public string CurrencyCode { get; set; } = "USD";
}

public class PaddleTotals
{
    [JsonPropertyName("subtotal")]
    public string Subtotal { get; set; } = "0";

    [JsonPropertyName("discount")]
    public string Discount { get; set; } = "0";

    [JsonPropertyName("tax")]
    public string Tax { get; set; } = "0";

    [JsonPropertyName("total")]
    public string Total { get; set; } = "0";
}

public class PaddleProduct
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
