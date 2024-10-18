using ExcelDataReader;
using ExcelToSqlApi.Contracts;
using ExcelToSqlApi.Entities;
using ExcelToSqlApi.Infrastructures.Persistence;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace ExcelToSqlApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UploadController(IBackgroundTaskQueue taskQueue,
                              ILogger<UploadController> logger,
                              IServiceProvider serviceProvider) : ControllerBase
{
    [HttpPost("upload-excel")]
    public async Task<IActionResult> UploadExcel(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("No file uploaded.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not ".xlsx" && extension is not ".xls")
            return BadRequest("Invalid file format. Only Excel files are allowed.");

        // Save the file to a temporary location
        var tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}{extension}");
        try
        {
            using var stream = new FileStream(tempFilePath, FileMode.Create);
            await file.CopyToAsync(stream);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error saving uploaded file.");
            return StatusCode(StatusCodes.Status500InternalServerError, "Error saving file.");
        }

        // Create a new processing status
        var status = new ProcessingStatus
        {
            Id = Guid.NewGuid(),
            FileName = file.FileName,
            Status = "Queued",
            CreatedAt = DateTime.UtcNow
        };

        // Save status to DB
        using (var scope = serviceProvider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.ProcessingStatuses.AddAsync(status);
            await dbContext.SaveChangesAsync();
        }

        // Enqueue the processing task with the file path and status ID
        taskQueue.QueueBackgroundWorkItem(async token =>
        {
            using var scope = serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Update status to "Processing"
            var procStatus = await dbContext.ProcessingStatuses.FindAsync([status.Id], cancellationToken: token);
            ArgumentNullException.ThrowIfNull(procStatus);
            procStatus.Status = "Processing";
            await dbContext.SaveChangesAsync(token);

            try
            {
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
                using var stream = System.IO.File.OpenRead(tempFilePath);
                using var reader = ExcelReaderFactory.CreateReader(stream);

                var result = reader.AsDataSet(new ExcelDataSetConfiguration
                {
                    ConfigureDataTable = (_) => new ExcelDataTableConfiguration
                    {
                        UseHeaderRow = true
                    }
                });

                var dataTable = result.Tables[0]; // Assuming data is in the first sheet

                int batchSize = 1000;
                var personsBatch = new List<Person>(batchSize);

                foreach (DataRow row in dataTable.Rows)
                {
                    var person = new Person
                    {
                        Name = row["Name"]?.ToString() ?? throw new Exception("Name is null"),
                        Email = row["Email"]?.ToString() ?? throw new Exception("Email is null"),
                        DateCreated = DateTime.TryParse(row["DateCreated"].ToString(), out var date) ? date : DateTime.UtcNow
                    };

                    personsBatch.Add(person);

                    if (personsBatch.Count == batchSize)
                    {
                        await dbContext.Persons.AddRangeAsync(personsBatch, token);
                        await dbContext.SaveChangesAsync(token);
                        personsBatch.Clear();
                    }
                }

                // Save remaining records
                if (personsBatch.Any())
                {
                    await dbContext.Persons.AddRangeAsync(personsBatch, token);
                    await dbContext.SaveChangesAsync(token);
                }

                // Update status to "Completed"
                procStatus.Status = "Completed";
                procStatus.CompletedAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(token);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing Excel file.");

                // Update status to "Failed"
                procStatus.Status = "Failed";
                procStatus.ErrorMessage = ex.Message;
                procStatus.CompletedAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(token);
            }
            finally
            {
                // Delete the temp file
                try
                {
                    if (System.IO.File.Exists(tempFilePath))
                        System.IO.File.Delete(tempFilePath);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not delete temp file.");
                }
            }
        });

        return Accepted(new { status.Id, message = "File is being processed." });
    }

    [HttpGet("status/{id}")]
    public async Task<IActionResult> GetStatus(Guid id)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var status = await dbContext.ProcessingStatuses.FindAsync(id);

        if (status == null)
            return NotFound("Status not found.");

        return Ok(new
        {
            status.Id,
            status.FileName,
            status.Status,
            status.CreatedAt,
            status.CompletedAt,
            status.ErrorMessage
        });
    }
}