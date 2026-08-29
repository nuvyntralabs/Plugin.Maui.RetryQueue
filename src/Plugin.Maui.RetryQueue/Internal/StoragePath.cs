namespace Plugin.Maui.RetryQueue;

internal static class StoragePath
{
    public static string Resolve(RetryQueueOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.DatabasePath))
        {
            return options.DatabasePath;
        }

        try
        {
            return Path.Combine(FileSystem.AppDataDirectory, options.DatabaseFileName);
        }
        catch (Exception)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                options.DatabaseFileName);
        }
    }
}
