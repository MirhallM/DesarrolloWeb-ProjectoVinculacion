namespace backend.Data.Entities;

/// <summary>
/// Metadatos de la imagen de un producto (una por producto).
/// El archivo vive en el volumen; aquí solo se guarda su ruta.
/// </summary>
public class ProductImage : IHasTimestamps
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public string ImagePath { get; set; } = string.Empty;

    public string? AltText { get; set; }

    public string MimeType { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    public Guid? UploadedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
