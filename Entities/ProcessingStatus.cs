namespace ExcelToSqlApi.Entities;

public class ProcessingStatus
{
    public Guid Id { get; set; }
    public required string FileName { get; set; }
    public required string Status { get; set; } // "Queued", "Processing", "Completed", "Failed"
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}