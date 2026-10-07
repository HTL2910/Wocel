using System.Text.Json;
using Wocel.Core.Contracts;

namespace Wocel.Shell.Services;

public class CloudSyncService
{
    private readonly HttpClient _httpClient = new();
    private readonly string _supabaseUrl;
    private readonly string _supabaseAnonKey;
    private readonly string _storageBucket;

    public CloudSyncService()
    {
        _supabaseUrl = EnvironmentService.SupabaseUrl;
        _supabaseAnonKey = EnvironmentService.SupabaseAnonKey;
        _storageBucket = EnvironmentService.SupabaseStorageBucket;

        _httpClient.DefaultRequestHeaders.Add("apikey", _supabaseAnonKey);
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_supabaseAnonKey}");
    }

    public bool IsConfigured => !string.IsNullOrEmpty(_supabaseUrl) && !string.IsNullOrEmpty(_supabaseAnonKey);

    public async Task<bool> SaveDocumentAsync(IOfficeDocumentSession session, string documentId)
    {
        if (!IsConfigured) return false;

        try
        {
            // Save document to memory stream
            using var memoryStream = new System.IO.MemoryStream();
            var extension = session.ModuleType == OfficeModuleType.Word ? ".docx" : ".xlsx";
            await session.SaveAsync(memoryStream, extension);
            memoryStream.Position = 0;

            // Upload to Supabase Storage
            var fileName = $"{documentId}{extension}";
            var uploadUrl = $"{_supabaseUrl}/storage/v1/object/{_storageBucket}/{fileName}";
            
            using var content = new MultipartFormDataContent();
            content.Add(new StreamContent(memoryStream), "file", fileName);

            var response = await _httpClient.PutAsync(uploadUrl, content);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<Stream?> LoadDocumentAsync(string documentId, OfficeModuleType moduleType)
    {
        if (!IsConfigured) return null;

        try
        {
            var extension = moduleType == OfficeModuleType.Word ? ".docx" : ".xlsx";
            var fileName = $"{documentId}{extension}";
            var downloadUrl = $"{_supabaseUrl}/storage/v1/object/{_storageBucket}/{fileName}";

            var response = await _httpClient.GetAsync(downloadUrl);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStreamAsync();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<CloudDocumentMetadata>> ListDocumentsAsync()
    {
        if (!IsConfigured) return new List<CloudDocumentMetadata>();

        try
        {
            var listUrl = $"{_supabaseUrl}/storage/v1/object/{_storageBucket}?limit=100";
            var response = await _httpClient.GetAsync(listUrl);
            
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var documents = JsonSerializer.Deserialize<List<CloudDocumentMetadata>>(json);
                return documents ?? new List<CloudDocumentMetadata>();
            }
            return new List<CloudDocumentMetadata>();
        }
        catch
        {
            return new List<CloudDocumentMetadata>();
        }
    }

    public async Task<bool> DeleteDocumentAsync(string documentId, OfficeModuleType moduleType)
    {
        if (!IsConfigured) return false;

        try
        {
            var extension = moduleType == OfficeModuleType.Word ? ".docx" : ".xlsx";
            var fileName = $"{documentId}{extension}";
            var deleteUrl = $"{_supabaseUrl}/storage/v1/object/{_storageBucket}/{fileName}";

            var response = await _httpClient.DeleteAsync(deleteUrl);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> SaveDocumentMetadataAsync(CloudDocumentMetadata metadata)
    {
        if (!IsConfigured) return false;

        try
        {
            var tableUrl = $"{_supabaseUrl}/rest/v1/documents";
            var json = JsonSerializer.Serialize(metadata);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(tableUrl, content);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<CloudDocumentMetadata>> GetDocumentMetadataAsync()
    {
        if (!IsConfigured) return new List<CloudDocumentMetadata>();

        try
        {
            var tableUrl = $"{_supabaseUrl}/rest/v1/documents?select=*";
            var response = await _httpClient.GetAsync(tableUrl);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var documents = JsonSerializer.Deserialize<List<CloudDocumentMetadata>>(json);
                return documents ?? new List<CloudDocumentMetadata>();
            }
            return new List<CloudDocumentMetadata>();
        }
        catch
        {
            return new List<CloudDocumentMetadata>();
        }
    }
}

public class CloudDocumentMetadata
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public OfficeModuleType ModuleType { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }
    public long FileSize { get; set; }
    public string? ThumbnailUrl { get; set; }
}
