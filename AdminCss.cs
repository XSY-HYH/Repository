using System;
using System.IO;
using System.Reflection;
using System.Text;

public static class AdminCss
{
    public static string GetCss()
    {
        try
        {
            string resourceName = "Repository.Admin.Css";
                
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
        
        return string.Empty;
    }
}
