
namespace ETS2LA.Networking.News;

public enum Importance
{
    Informational,
    Warning,
    Critical
}

public class Article
{
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string ContentSummary { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;

    public Importance Importance { get; set; } = Importance.Informational;
    public bool Visible { get; set; } = true;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; } = null;
}