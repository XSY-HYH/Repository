using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Repository.Services
{
    /// <summary>
    /// 视频流服务，用于处理视频切片
    /// </summary>
    public class VideoStreamingService
    {
        private readonly Logger _logger;
        private readonly ConfigManager _configManager;
        private readonly string _tempDirectory;
        private readonly Dictionary<string, VideoSegmentInfo> _segmentCache;
        private readonly object _lockObject = new object();

        public VideoStreamingService(Logger logger, ConfigManager configManager)
        {
            _logger = logger;
            _configManager = configManager;
            
            // 创建临时目录用于存储切片
            _tempDirectory = Path.Combine(Path.GetTempPath(), "Repository", "VideoSegments");
            if (!Directory.Exists(_tempDirectory))
            {
                Directory.CreateDirectory(_tempDirectory);
            }
            
            _segmentCache = new Dictionary<string, VideoSegmentInfo>();
            
            _logger.LogInfo(I18nService.Instance.T("video.initialized", _tempDirectory));
        }

        /// <summary>
        /// 创建视频的HLS播放列表
        /// </summary>
        public async Task<string> CreateHLSPlaylistAsync(string videoPath, string relativePath)
        {
            try
            {
                var videoId = GenerateVideoId(relativePath);
                var playlistPath = Path.Combine(_tempDirectory, $"{videoId}.m3u8");
                
                // 检查是否已存在播放列表
                if (File.Exists(playlistPath))
                {
                    var lastModified = File.GetLastWriteTime(playlistPath);
                    var videoModified = File.GetLastWriteTime(videoPath);
                    
                    // 如果视频文件比播放列表新，则重新生成
                    if (videoModified <= lastModified)
                    {
                        return playlistPath;
                    }
                }
                
                // 获取视频信息
                var videoInfo = await GetVideoInfoAsync(videoPath);
                if (videoInfo == null)
                {
                    throw new InvalidOperationException(I18nService.Instance.T("video.info_failed", videoPath));
                }
                
                // 创建切片
                var segments = await CreateVideoSegmentsAsync(videoPath, videoId, videoInfo.Duration);
                
                // 生成HLS播放列表
                var playlist = GenerateHLSPlaylist(segments, videoId);
                
                // 保存播放列表
                await File.WriteAllTextAsync(playlistPath, playlist);
                
                _logger.LogInfo(I18nService.Instance.T("video.playlist_created", playlistPath, segments.Count));
                
                return playlistPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("video.playlist_failed", videoPath));
                throw;
            }
        }

        /// <summary>
        /// 创建视频切片
        /// </summary>
        private async Task<List<VideoSegment>> CreateVideoSegmentsAsync(string videoPath, string videoId, TimeSpan duration)
        {
            var segments = new List<VideoSegment>();
            const int segmentDurationSeconds = 10; // 每个切片10秒
            
            // 这里我们模拟切片过程，实际应用中应该使用FFMpeg等工具
            // 由于C#没有内置的视频处理库，我们使用简化的方法
            
            var totalSeconds = (int)duration.TotalSeconds;
            var segmentCount = Math.Ceiling((double)totalSeconds / segmentDurationSeconds);
            
            for (int i = 0; i < segmentCount; i++)
            {
                var segmentPath = Path.Combine(_tempDirectory, $"{videoId}_seg{i}.ts");
                var segment = new VideoSegment
                {
                    Index = i,
                    Path = segmentPath,
                    Duration = Math.Min(segmentDurationSeconds, totalSeconds - i * segmentDurationSeconds),
                    Encrypted = false
                };
                
                // 创建模拟的切片文件（实际应用中应该使用FFMpeg等工具进行切片）
                await CreateSimulatedSegmentAsync(videoPath, segmentPath, i, segment.Duration);
                
                segments.Add(segment);
                
                // 缓存切片信息
                lock (_lockObject)
                {
                    _segmentCache[segmentPath] = new VideoSegmentInfo
                    {
                        VideoId = videoId,
                        SegmentIndex = i,
                        Duration = segment.Duration,
                        IsEncrypted = false
                    };
                }
            }
            
            return segments;
        }

        /// <summary>
        /// 创建模拟的视频切片（实际应用中应使用FFMpeg）
        /// </summary>
        private async Task CreateSimulatedSegmentAsync(string videoPath, string segmentPath, int index, int duration)
        {
            try
            {
                // 这里我们只是复制原视频文件的一部分作为模拟切片
                // 实际应用中应该使用FFMpeg等工具进行精确切片
                
                using (var sourceStream = new FileStream(videoPath, FileMode.Open, FileAccess.Read))
                using (var destStream = new FileStream(segmentPath, FileMode.Create, FileAccess.Write))
                {
                    // 简单地将视频文件分成多个部分
                    var fileSize = sourceStream.Length;
                    var segmentSize = fileSize / 10; // 假设视频分为10个片段
                    
                    var startPosition = index * segmentSize;
                    if (startPosition < fileSize)
                    {
                        sourceStream.Seek(startPosition, SeekOrigin.Begin);
                        
                        var remainingBytes = fileSize - startPosition;
                        var bytesToCopy = Math.Min(segmentSize, remainingBytes);
                        
                        var buffer = new byte[81920];
                        int bytesRead;
                        long totalBytesRead = 0;
                        
                        while (totalBytesRead < bytesToCopy && 
                              (bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            var bytesToWrite = (int)Math.Min(bytesRead, bytesToCopy - totalBytesRead);
                            await destStream.WriteAsync(buffer, 0, bytesToWrite);
                            totalBytesRead += bytesToWrite;
                        }
                    }
                }
                
                _logger.LogInfo(I18nService.Instance.T("video.segment_created", segmentPath));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("video.segment_failed", segmentPath));
                throw;
            }
        }

        /// <summary>
        /// 生成HLS播放列表内容
        /// </summary>
        private string GenerateHLSPlaylist(List<VideoSegment> segments, string videoId)
        {
            var sb = new StringBuilder();
            
            // HLS播放列表头部
            sb.AppendLine("#EXTM3U");
            sb.AppendLine("#EXT-X-VERSION:3");
            sb.AppendLine("#EXT-X-TARGETDURATION:12"); // 片段最大时长
            sb.AppendLine("#EXT-X-MEDIA-SEQUENCE:0");
            
            // 添加每个片段
            foreach (var segment in segments)
            {
                sb.AppendLine($"#EXTINF:{segment.Duration},");
                sb.AppendLine($"api/hls/{videoId}/{segment.Index}");
            }
            
            // 播放列表结束
            sb.AppendLine("#EXT-X-ENDLIST");
            
            return sb.ToString();
        }

        /// <summary>
        /// 检查切片是否存在
        /// </summary>
        public bool SegmentExists(string videoId, int segmentIndex)
        {
            var segmentPath = Path.Combine(_tempDirectory, $"{videoId}_seg{segmentIndex}.ts");
            return File.Exists(segmentPath);
        }

        /// <summary>
        /// 获取切片内容
        /// </summary>
        public async Task<byte[]> GetSegmentAsync(string videoId, int segmentIndex)
        {
            var segmentPath = Path.Combine(_tempDirectory, $"{videoId}_seg{segmentIndex}.ts");
            
            if (!File.Exists(segmentPath))
            {
                throw new FileNotFoundException(I18nService.Instance.T("video.segment_not_found", segmentPath));
            }
            
            return await File.ReadAllBytesAsync(segmentPath);
        }

        /// <summary>
        /// 根据视频ID获取视频信息
        /// </summary>
        public async Task<VideoInfo?> GetVideoInfoByIdAsync(string videoId)
        {
            try
            {
                // 查找对应的播放列表文件
                var playlistPath = Path.Combine(_tempDirectory, $"{videoId}.m3u8");
                
                if (!File.Exists(playlistPath))
                {
                    throw new FileNotFoundException(I18nService.Instance.T("video.playlist_not_found", videoId));
                }
                
                // 读取播放列表文件内容
                var playlistContent = await File.ReadAllTextAsync(playlistPath);
                
                // 从播放列表中提取信息
                var lines = playlistContent.Split('\n');
                var segments = lines.Where(line => line.StartsWith("api/hls/") && !line.StartsWith("#")).ToList();
                
                // 估算视频时长（基于片段数量）
                var estimatedDuration = TimeSpan.FromSeconds(segments.Count * 10); // 假设每个片段10秒
                
                // 尝试从播放列表文件中推断原始视频文件路径
                // 这里我们使用一个简化的方法，实际应用中可能需要更复杂的逻辑
                var videoPath = ""; // 这里应该有逻辑来确定原始视频路径
                
                return new VideoInfo
                {
                    Duration = estimatedDuration,
                    Format = ".mp4", // 默认格式
                    FullPath = videoPath
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("video.info_by_id_failed", videoId));
                return null;
            }
        }

        /// <summary>
        /// 获取HLS视频片段
        /// </summary>
        public async Task<byte[]> GetHLSSegmentAsync(string videoId, int segmentIndex)
        {
            try
            {
                var segmentPath = Path.Combine(_tempDirectory, $"{videoId}_seg{segmentIndex}.ts");
                
                if (!File.Exists(segmentPath))
                {
                    throw new FileNotFoundException(I18nService.Instance.T("video.hls_segment_not_found", videoId, segmentIndex));
                }
                
                return await File.ReadAllBytesAsync(segmentPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("video.hls_segment_failed", videoId, segmentIndex));
                throw;
            }
        }

        /// <summary>
        /// 生成视频ID
        /// </summary>
        public string GenerateVideoId(string relativePath)
        {
            // 使用路径的哈希值作为视频ID
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(relativePath));
                return Convert.ToBase64String(hash).Replace("/", "_").Replace("+", "-").Substring(0, 16);
            }
        }

        /// <summary>
        /// 获取视频信息（简化版，实际应用中应使用FFMpeg等库）
        /// </summary>
        private Task<VideoInfo?> GetVideoInfoAsync(string videoPath)
        {
            try
            {
                var fileInfo = new FileInfo(videoPath);
                
                // 简单的视频信息提取，基于文件扩展名和文件大小
                var extension = Path.GetExtension(videoPath).ToLowerInvariant();
                
                // 根据文件大小估算视频时长（这是一个非常粗略的估算）
                // 假设平均比特率为2Mbps
                double estimatedDurationSeconds = fileInfo.Length / (2.0 * 1024 * 1024 / 8); // 2Mbps
                
                return Task.FromResult<VideoInfo?>(new VideoInfo
                {
                    Duration = TimeSpan.FromSeconds(estimatedDurationSeconds),
                    Format = extension
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("video.info_failed", videoPath));
                return Task.FromResult<VideoInfo?>(null);
            }
        }

        /// <summary>
        /// 清理过期的切片文件
        /// </summary>
        public void CleanupExpiredSegments()
        {
            try
            {
                var expirationTime = TimeSpan.FromHours(24); // 24小时后过期
                var now = DateTime.Now;
                
                foreach (var file in Directory.GetFiles(_tempDirectory, "*.ts"))
                {
                    var fileInfo = new FileInfo(file);
                    if (now - fileInfo.LastAccessTime > expirationTime)
                    {
                        File.Delete(file);
                        _logger.LogInfo(I18nService.Instance.T("video.expired_segment_deleted", file));
                    }
                }
                
                foreach (var file in Directory.GetFiles(_tempDirectory, "*.m3u8"))
                {
                    var fileInfo = new FileInfo(file);
                    if (now - fileInfo.LastAccessTime > expirationTime)
                    {
                        File.Delete(file);
                        _logger.LogInfo(I18nService.Instance.T("video.expired_playlist_deleted", file));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, I18nService.Instance.T("video.cleanup_failed"));
            }
        }
    }

    /// <summary>
    /// 视频信息
    /// </summary>
    public class VideoInfo
    {
        public TimeSpan Duration { get; set; }
        public string Format { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
    }

    /// <summary>
    /// 视频切片
    /// </summary>
    internal class VideoSegment
    {
        public int Index { get; set; }
        public string Path { get; set; } = string.Empty;
        public int Duration { get; set; }
        public bool Encrypted { get; set; }
    }

    /// <summary>
    /// 视频切片信息
    /// </summary>
    public class VideoSegmentInfo
    {
        public string VideoId { get; set; } = string.Empty;
        public int SegmentIndex { get; set; }
        public int Duration { get; set; }
        public bool IsEncrypted { get; set; }
    }
}