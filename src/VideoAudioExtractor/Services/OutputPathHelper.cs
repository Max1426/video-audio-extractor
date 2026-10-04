namespace VideoAudioExtractor.Services;

public static class OutputPathHelper
{
    public static string GetUniquePath(string outputDirectory, string inputPath, ISet<string> reservedPaths)
    {
        var baseName = Path.GetFileNameWithoutExtension(inputPath);
        var candidate = Path.Combine(outputDirectory, baseName + ".mp3");
        var index = 1;

        while (File.Exists(candidate) || reservedPaths.Contains(candidate))
        {
            candidate = Path.Combine(outputDirectory, $"{baseName} ({index++}).mp3");
        }

        reservedPaths.Add(candidate);
        return candidate;
    }
}

