namespace backend.Data.Entities;

/// <summary>
/// Banner o promoción administrado desde el panel.
/// </summary>
public class SiteContent : IHasTimestamps
{
    public Guid Id { get; set; }

    /// <summary>Uno de los valores de <see cref="SiteContentTypes"/>.</summary>
    public string ContentType { get; set; } = SiteContentTypes.Banner;

    public string Title { get; set; } = string.Empty;

    public string? Body { get; set; }

    public string? ImagePath { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? StartAt { get; set; }

    public DateTime? EndAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public static class SiteContentTypes
{
    public const string Banner = "banner";
    public const string Promotion = "promotion";
}
