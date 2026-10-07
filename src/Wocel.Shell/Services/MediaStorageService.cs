using System.Text.Json;

namespace Wocel.Shell.Services;

public class MediaStorageService
{
    private readonly HttpClient _httpClient = new();
    private readonly string _cloudName;
    private readonly string _apiKey;
    private readonly string _apiSecret;
    private readonly string _uploadPreset;

    public MediaStorageService()
    {
        _cloudName = EnvironmentService.CloudinaryCloudName;
        _apiKey = EnvironmentService.CloudinaryApiKey;
        _apiSecret = EnvironmentService.CloudinaryApiSecret;
        _uploadPreset = EnvironmentService.CloudinaryUploadPreset;
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_cloudName) && !string.IsNullOrEmpty(_apiKey);

    public async Task<string?> UploadImageAsync(Stream imageStream, string fileName)
    {
        if (!IsConfigured) return null;

        try
        {
            var uploadUrl = $"https://api.cloudinary.com/v1_1/{_cloudName}/image/upload";
            
            using var content = new MultipartFormDataContent();
            content.Add(new StreamContent(imageStream), "file", fileName);
            
            if (!string.IsNullOrEmpty(_uploadPreset))
            {
                content.Add(new StringContent(_uploadPreset), "upload_preset");
            }

            var response = await _httpClient.PostAsync(uploadUrl, content);
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<CloudinaryUploadResult>(json);
                return result?.SecureUrl;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> UploadDocumentThumbnailAsync(Stream thumbnailStream, string fileName)
    {
        // Reuse the image upload method for thumbnails
        return await UploadImageAsync(thumbnailStream, $"thumb_{fileName}");
    }

    public async Task<bool> DeleteImageAsync(string publicId)
    {
        if (!IsConfigured) return false;

        try
        {
            var deleteUrl = $"https://api.cloudinary.com/v1_1/{_cloudName}/image/destroy";
            
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(_apiKey), "api_key");
            content.Add(new StringContent(_apiSecret), "api_secret");
            content.Add(new StringContent(publicId), "public_id");

            var response = await _httpClient.PostAsync(deleteUrl, content);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public string GetImageUrl(string publicId, string transformation = "")
    {
        if (string.IsNullOrEmpty(_cloudName)) return string.Empty;

        var baseUrl = $"https://res.cloudinary.com/{_cloudName}/image/upload";
        return string.IsNullOrEmpty(transformation) 
            ? $"{baseUrl}/{publicId}" 
            : $"{baseUrl}/{transformation}/{publicId}";
    }

    public string GetThumbnailUrl(string publicId, int width = 200, int height = 200)
    {
        var transformation = $"c_fill,h_{height},w_{width}";
        return GetImageUrl(publicId, transformation);
    }
}

public class CloudinaryUploadResult
{
    public string? PublicId { get; set; }
    public string? SecureUrl { get; set; }
    public string? Url { get; set; }
    public long Bytes { get; set; }
    public string? Format { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}
