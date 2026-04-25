namespace Repository.Models
{
    public class FileInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string LastModified { get; set; } = string.Empty;
        public long Size { get; set; } = 0;
        public bool Previewable { get; set; } = false;
        public string? Sha256 { get; set; }
        
        public bool CanView { get; set; } = true;
        public bool CanDownload { get; set; } = true;
        public bool CanPreview { get; set; } = true;
    }

    public class DirectoryInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string LastModified { get; set; } = string.Empty;
        public string? Sha256 { get; set; }
        
        public bool CanView { get; set; } = true;
        public bool CanDownload { get; set; } = true;
    }

    public class DirectoryListing
    {
        public List<DirectoryInfo> Directories { get; set; } = new();
        public List<FileInfo> Files { get; set; } = new();
        public string CurrentPath { get; set; } = string.Empty;
        public string? DirectoryHash { get; set; }
    }
}