using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Minio;
using Minio.DataModel.Args;

namespace Gateway.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class StorageController : ControllerBase
{
    private readonly IMinioClient _minioClient;
    private readonly string _bucketName = "sbd-storage";

    public StorageController(IConfiguration configuration)
    {
        var endpoint = configuration["MinIO:Endpoint"] ?? throw new InvalidOperationException("MinIO Endpoint not configured");
        var accessKey = configuration["MinIO:AccessKey"] ?? throw new InvalidOperationException("MinIO AccessKey not configured");
        var secretKey = configuration["MinIO:SecretKey"] ?? throw new InvalidOperationException("MinIO SecretKey not configured");

        _minioClient = new MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(accessKey, secretKey)
            .Build();
    }

    [HttpPost("upload")]
    public async Task<ActionResult> Upload(IFormFile file, [FromQuery] string? folder = null)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "No file provided" });

        var objectName = string.IsNullOrEmpty(folder)
            ? file.FileName
            : $"{folder}/{file.FileName}";

        try
        {
            // Ensure bucket exists
            var bucketExistsArgs = new BucketExistsArgs().WithBucket(_bucketName);
            bool found = await _minioClient.BucketExistsAsync(bucketExistsArgs);
            if (!found)
            {
                var makeBucketArgs = new MakeBucketArgs().WithBucket(_bucketName);
                await _minioClient.MakeBucketAsync(makeBucketArgs);
            }

            // Upload file
            using var stream = file.OpenReadStream();
            var putObjectArgs = new PutObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(objectName)
                .WithStreamData(stream)
                .WithObjectSize(file.Length)
                .WithContentType(file.ContentType);

            await _minioClient.PutObjectAsync(putObjectArgs);

            return Ok(new { fileName = objectName, size = file.Length });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Error uploading file", error = ex.Message });
        }
    }

    [HttpGet("download/{*fileName}")]
    public async Task<ActionResult> Download(string fileName)
    {
        try
        {
            var memoryStream = new MemoryStream();
            var getObjectArgs = new GetObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(fileName)
                .WithCallbackStream(stream =>
                {
                    stream.CopyTo(memoryStream);
                });

            await _minioClient.GetObjectAsync(getObjectArgs);
            memoryStream.Position = 0;

            return File(memoryStream, "application/octet-stream", Path.GetFileName(fileName));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Error downloading file", error = ex.Message });
        }
    }
}
