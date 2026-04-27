using System.Linq;

namespace Repository.Services
{
    public static class PathSecurity
    {
        public static bool IsSystemPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            
            var normalizedPath = path.Replace('\\', '/').Trim('/');
            var segments = normalizedPath.Split('/');
            
            var systemDirs = new[] { ".keys" };
            
            return segments.Any(segment => 
                systemDirs.Any(sd => string.Equals(segment, sd, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
