using System;
using System.IO;
using System.Reflection;
using System.Text;

public static class DirectoryListingCss
{
    public static string GetCss()
    {
        try
        {
            string resourceName = "Repository.DirectoryListing.Css";
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream != null)
                {
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading embedded CSS resource: {ex.Message}");
        }
        
        // 如果资源不存在或读取失败，返回空字符串
        return string.Empty;
    }
}