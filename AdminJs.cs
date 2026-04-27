using System;
using System.IO;
using System.Reflection;
using System.Text;

public static class AdminJs
{
    public static string GetJs()
    {
        try
        {
            string resourceName = "Repository.Admin.JS";
                
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
            Console.WriteLine($"Error reading embedded JavaScript resource: {ex.Message}");
        }
        
        return string.Empty;
    }
}
