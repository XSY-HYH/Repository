using System.ComponentModel.DataAnnotations;

namespace Repository.Models
{
    /// <summary>
    /// 临时链接请求模型
    /// </summary>
    public class TempLinkRequest
    {
        /// <summary>
        /// 文件路径
        /// </summary>
        [Required(ErrorMessage = "文件路径不能为空")]
        public string FilePath { get; set; } = string.Empty;
    }
}