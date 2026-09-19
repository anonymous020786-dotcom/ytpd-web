using System.IO.Compression;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using YtpdWeb.Api.Data;
using YtpdWeb.Api.Models;
using YtpdWeb.Api.Services;

namespace YtpdWeb.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/downloads")]
public class DownloadsController(
    AppDbContext db,
    DownloadQueue queue,
    IOptions<StorageOptions> storageOptions
) : ControllerBase
{
    private readonly StorageOptions _storage = storageOptions.Value;

    [HttpPost]
    public async Task<ActionResult<JobStatusDto>> Create(CreateJobRequest request, CancellationToken ct)
    {
        if (request.Items is null || request.Items.Count == 0)
            return BadRequest(new { message = "Select at least one video." });

        if (!Enum.TryParse<DownloadFormat>(request.Format, true, out var format))
            return BadRequest(new { message = $"Unknown format '{request.Format}'." });

        var job = new DownloadJob
        {
            SourceUrl = request.SourceUrl,
            Format = format,
            Quality = string.IsNullOrWhiteSpace(request.Quality) ? "best" : request.Quality,
            EmbedMetadata = request.EmbedMetadata,
        };

        job.Items = request.Items.Select(i => new DownloadJobItem
        {
            JobId = job.Id,
            Job = job,
            VideoId = i.VideoId,
            Title = string.IsNullOrWhiteSpace(i.Title) ? i.VideoId : i.Title,
            Author = i.Author ?? "",
        }).ToList();

        db.Jobs.Add(job);
        await db.SaveChangesAsync(ct);

        foreach (var item in job.Items)
            await queue.EnqueueAsync(item.Id, ct);

        return Ok(ToDto(job));
    }

    [HttpGet]
    public async Task<ActionResult<List<JobStatusDto>>> List(CancellationToken ct)
    {
        var jobs = await db.Jobs.Include(j => j.Items)
            .OrderByDescending(j => j.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        return Ok(jobs.Select(ToDto).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<JobStatusDto>> Get(Guid id, CancellationToken ct)
    {
        var job = await db.Jobs.Include(j => j.Items).FirstOrDefaultAsync(j => j.Id == id, ct);
        return job is null ? NotFound() : Ok(ToDto(job));
    }

    [HttpGet("{id:guid}/items/{itemId:guid}/file")]
    public async Task<IActionResult> GetFile(Guid id, Guid itemId, CancellationToken ct)
    {
        var item = await db.JobItems.FirstOrDefaultAsync(i => i.Id == itemId && i.JobId == id, ct);
        if (item is null || item.Status != JobItemStatus.Completed || item.OutputFileName is null)
            return NotFound();

        var path = Path.Combine(_storage.DownloadsPath, id.ToString(), item.OutputFileName);
        if (!System.IO.File.Exists(path))
            return NotFound();

        var stream = System.IO.File.OpenRead(path);
        return File(stream, "application/octet-stream", item.OutputFileName);
    }

    [HttpGet("{id:guid}/zip")]
    public async Task<IActionResult> GetZip(Guid id, CancellationToken ct)
    {
        var job = await db.Jobs.Include(j => j.Items).FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null) return NotFound();

        var completed = job.Items.Where(i => i.Status == JobItemStatus.Completed && i.OutputFileName is not null).ToList();
        if (completed.Count == 0) return NotFound();

        var jobDir = Path.Combine(_storage.DownloadsPath, id.ToString());
        var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            foreach (var item in completed)
            {
                var path = Path.Combine(jobDir, item.OutputFileName!);
                if (!System.IO.File.Exists(path)) continue;
                archive.CreateEntryFromFile(path, item.OutputFileName!);
            }
        }
        memory.Position = 0;
        return File(memory, "application/zip", $"download-{id}.zip");
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null) return NotFound();

        db.Jobs.Remove(job); // cascades to items
        await db.SaveChangesAsync(ct);

        try { Directory.Delete(Path.Combine(_storage.DownloadsPath, id.ToString()), true); } catch { /* best effort */ }

        return NoContent();
    }

    private static JobStatusDto ToDto(DownloadJob job) => new(
        job.Id, job.CreatedAt, job.SourceUrl, job.Format.ToString(), job.Quality,
        job.Items.Select(i => new JobItemStatusDto(
            i.Id, i.VideoId, i.Title, i.Author, i.Status.ToString(), i.Progress, i.ErrorMessage, i.OutputFileName
        )).ToList()
    );
}
