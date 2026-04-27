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
        private readonly ClientIPService _clientIPService;

        public HLSController(ConfigManager configManager, Logger logger, VideoStreamingService videoStreamingService, ClientIPService clientIPService)
        {
            _configManager = configManager;
            _logger = logger;
            _videoStreamingService = videoStreamingService;
            _clientIPService = clientIPService;
        }

        /// <summary>
        /// 获取HLS播放列表
        /// </summary>
        [HttpGet("api/hls/{videoId}/playlist.m3u8")]
        public async Task<IActionResult> GetHLSPlaylist(string videoId)
        {
            var clientIP = _clientIPService.GetClientIP(HttpContext);
            
            // 记录HLS播放列表请求开始
            _logger.LogInfo(I18nService.Instance.T("hls.playlist_request", clientIP, videoId));
            
            try
            {
                var config = _configManager.GetConfig();
                if (!config.PreviewEnabled)
                {
                    _logger.LogWarning(I18nService.Instance.T("hls.preview_disabled", clientIP, videoId));
                    return StatusCode(403, "预览功能已禁用");
                }
                
                var videoInfo = await _videoStreamingService.GetVideoInfoByIdAsync(videoId);
                if (videoInfo == null)
                {
                    _logger.LogWarning(I18nService.Instance.T("hls.video_expired", clientIP, videoId));
                    return NotFound("视频不存在或已过期");
                }
                
                if (!System.IO.File.Exists(videoInfo.FullPath))
                {
                    _logger.LogWarning(I18nService.Instance.T("hls.video_not_exist", clientIP, videoInfo.FullPath));
                    return NotFound("视频文件不存在");
                }
                
                var playlistPath = Path.Combine(Path.GetTempPath(), "Repository", "VideoSegments", $"{videoId}.m3u8");
                if (!System.IO.File.Exists(playlistPath))
                {
                    _logger.LogWarning(I18nService.Instance.T("hls.playlist_not_exist", clientIP, videoId));
                    return NotFound("HLS播放列表不存在");
                }
                
                var playlistContent = await System.IO.File.ReadAllTextAsync(playlistPath);
                if (string.IsNullOrEmpty(playlistContent))
                {
                    _logger.LogWarning(I18nService.Instance.T("hls.playlist_failed", clientIP, videoId));
                    return StatusCode(500, "无法获取HLS播放列表");
                }
                
                _logger.LogInfo(I18nService.Instance.T("hls.playlist_success", clientIP, videoId));
                
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
            var clientIP = _clientIPService.GetClientIP(HttpContext);
            
            // 记录HLS片段请求开始
            _logger.LogInfo(I18nService.Instance.T("hls.segment_request", clientIP, videoId, segmentIndex));
            
            try
            {
                var config = _configManager.GetConfig();
                if (!config.PreviewEnabled)
                {
                    _logger.LogWarning(I18nService.Instance.T("hls.preview_disabled", clientIP, videoId));
                    return StatusCode(403, "预览功能已禁用");
                }
                
                var videoInfo = await _videoStreamingService.GetVideoInfoByIdAsync(videoId);
                if (videoInfo == null)
                {
                    _logger.LogWarning(I18nService.Instance.T("hls.video_expired", clientIP, videoId));
                    return NotFound("视频不存在或已过期");
                }
                
                if (!System.IO.File.Exists(videoInfo.FullPath))
                {
                    _logger.LogWarning(I18nService.Instance.T("hls.video_not_exist", clientIP, videoInfo.FullPath));
                    return NotFound("视频文件不存在");
                }
                
                var segmentData = await _videoStreamingService.GetHLSSegmentAsync(videoId, segmentIndex);
                if (segmentData == null || segmentData.Length == 0)
                {
                    _logger.LogWarning(I18nService.Instance.T("hls.segment_failed", clientIP, videoId, segmentIndex));
                    return NotFound("HLS片段不存在");
                }
                
                _logger.LogInfo(I18nService.Instance.T("hls.segment_success", clientIP, videoId, segmentIndex, FormatFileSize(segmentData.Length)));
                
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