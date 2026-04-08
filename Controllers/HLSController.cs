using Microsoft.AspNetCore.Mvc;
using Repository.Models;
using Repository.Services;
using System.IO;

namespace Repository.Controllers
{
    /// <summary>
    /// 处理HLS视频流相关操作的控制器
    /// </summary>
    public class HLSController : Controller
    {
        private readonly ConfigManager _configManager;
        private readonly Logger _logger;
        private readonly VideoStreamingService _videoStreamingService;

        public HLSController(ConfigManager configManager, Logger logger, VideoStreamingService videoStreamingService)
        {
            _configManager = configManager;
            _logger = logger;
            _videoStreamingService = videoStreamingService;
        }

        /// <summary>
        /// 获取HLS播放列表
        /// </summary>
        [HttpGet("api/hls/{videoId}/playlist.m3u8")]
        public async Task<IActionResult> GetHLSPlaylist(string videoId)
        {
            // 获取客户端IP地址
            var clientIP = GetClientIP();
            
            // 记录HLS播放列表请求开始
            _logger.LogInfo($"开始处理HLS播放列表请求，客户端IP: {clientIP}，视频ID: {videoId}");
            
            try
            {
                // 检查预览功能是否启用
                var config = _configManager.GetConfig();
                if (!config.PreviewEnabled)
                {
                    _logger.LogWarning($"文件预览功能已禁用，客户端IP: {clientIP}，视频ID: {videoId}");
                    return StatusCode(403, "预览功能已禁用");
                }
                
                // 获取视频信息
                var videoInfo = await _videoStreamingService.GetVideoInfoByIdAsync(videoId);
                if (videoInfo == null)
                {
                    _logger.LogWarning($"视频不存在或已过期，客户端IP: {clientIP}，视频ID: {videoId}");
                    return NotFound("视频不存在或已过期");
                }
                
                // 检查文件是否存在
                if (!System.IO.File.Exists(videoInfo.FullPath))
                {
                    _logger.LogWarning($"视频文件不存在，客户端IP: {clientIP}，视频路径: {videoInfo.FullPath}");
                    return NotFound("视频文件不存在");
                }
                
                // 获取HLS播放列表
                var playlistPath = Path.Combine(Path.GetTempPath(), "Repository", "VideoSegments", $"{videoId}.m3u8");
                if (!System.IO.File.Exists(playlistPath))
                {
                    _logger.LogWarning($"HLS播放列表不存在，客户端IP: {clientIP}，视频ID: {videoId}");
                    return NotFound("HLS播放列表不存在");
                }
                
                var playlistContent = await System.IO.File.ReadAllTextAsync(playlistPath);
                if (string.IsNullOrEmpty(playlistContent))
                {
                    _logger.LogWarning($"无法获取HLS播放列表，客户端IP: {clientIP}，视频ID: {videoId}");
                    return StatusCode(500, "无法获取HLS播放列表");
                }
                
                _logger.LogInfo($"HLS播放列表获取成功，客户端IP: {clientIP}，视频ID: {videoId}");
                
                // 返回HLS播放列表
                return Content(playlistContent, "application/vnd.apple.mpegurl");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"获取HLS播放列表时发生错误，客户端IP: {clientIP}，视频ID: {videoId}");
                return StatusCode(500, "服务器内部错误");
            }
        }

        /// <summary>
        /// 获取HLS视频片段
        /// </summary>
        [HttpGet("api/hls/{videoId}/{segmentIndex}")]
        public async Task<IActionResult> GetHLSSegment(string videoId, int segmentIndex)
        {
            // 获取客户端IP地址
            var clientIP = GetClientIP();
            
            // 记录HLS片段请求开始
            _logger.LogInfo($"开始处理HLS片段请求，客户端IP: {clientIP}，视频ID: {videoId}，片段索引: {segmentIndex}");
            
            try
            {
                // 检查预览功能是否启用
                var config = _configManager.GetConfig();
                if (!config.PreviewEnabled)
                {
                    _logger.LogWarning($"文件预览功能已禁用，客户端IP: {clientIP}，视频ID: {videoId}，片段索引: {segmentIndex}");
                    return StatusCode(403, "预览功能已禁用");
                }
                
                // 获取视频信息
                var videoInfo = await _videoStreamingService.GetVideoInfoByIdAsync(videoId);
                if (videoInfo == null)
                {
                    _logger.LogWarning($"视频不存在或已过期，客户端IP: {clientIP}，视频ID: {videoId}，片段索引: {segmentIndex}");
                    return NotFound("视频不存在或已过期");
                }
                
                // 检查文件是否存在
                if (!System.IO.File.Exists(videoInfo.FullPath))
                {
                    _logger.LogWarning($"视频文件不存在，客户端IP: {clientIP}，视频路径: {videoInfo.FullPath}，片段索引: {segmentIndex}");
                    return NotFound("视频文件不存在");
                }
                
                // 获取HLS片段
                var segmentData = await _videoStreamingService.GetHLSSegmentAsync(videoId, segmentIndex);
                if (segmentData == null || segmentData.Length == 0)
                {
                    _logger.LogWarning($"无法获取HLS片段，客户端IP: {clientIP}，视频ID: {videoId}，片段索引: {segmentIndex}");
                    return NotFound("HLS片段不存在");
                }
                
                _logger.LogInfo($"HLS片段获取成功，客户端IP: {clientIP}，视频ID: {videoId}，片段索引: {segmentIndex}，大小: {FormatFileSize(segmentData.Length)}");
                
                // 返回HLS片段
                return File(segmentData, "video/MP2T");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"获取HLS片段时发生错误，客户端IP: {clientIP}，视频ID: {videoId}，片段索引: {segmentIndex}");
                return StatusCode(500, "服务器内部错误");
            }
        }

        /// <summary>
        /// 获取客户端IP地址
        /// </summary>
        private string GetClientIP()
        {
            try
            {
                var httpContext = HttpContext;
                
                // 首先检查X-Forwarded-For头部（代理服务器）
                var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
                if (!string.IsNullOrEmpty(forwardedFor))
                {
                    // X-Forwarded-For可能包含多个IP，取第一个
                    var ips = forwardedFor.Split(',').Select(ip => ip.Trim());
                    return ips.FirstOrDefault() ?? "Unknown";
                }
                
                // 检查X-Real-IP头部
                var realIP = httpContext.Request.Headers["X-Real-IP"].FirstOrDefault();
                if (!string.IsNullOrEmpty(realIP))
                {
                    return realIP;
                }
                
                // 最后使用RemoteIpAddress
                return httpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取客户端IP地址时发生错误");
                return "Unknown";
            }
        }

        /// <summary>
        /// 格式化文件大小
        /// </summary>
        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}