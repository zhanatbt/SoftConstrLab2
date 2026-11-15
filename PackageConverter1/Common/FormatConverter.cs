using PackageConverter1.Model;
using PackageConverter1.Readers;
using PackageConverter1.Writers;

namespace PackageConverter1.Common;

/// <summary>
/// Преобразует пакет одного формата в другой: читатель -> общая модель -> писатель.
/// Класс ничего не знает про конкретные форматы, ему передают готовые списки читателей и писателей.
/// </summary>
public class FormatConverter
{
    private readonly Dictionary<string, IPackageReader> _readers;
    private readonly Dictionary<string, IPackageWriter> _writers;

    public FormatConverter(IEnumerable<IPackageReader> readers, IEnumerable<IPackageWriter> writers)
    {
        _readers = readers.ToDictionary(r => r.Format, StringComparer.OrdinalIgnoreCase);
        _writers = writers.ToDictionary(w => w.Format, StringComparer.OrdinalIgnoreCase);
    }

    public IEnumerable<string> SourceFormats => _readers.Keys;
    public IEnumerable<string> TargetFormats => _writers.Keys;

    public ProblemPackage Convert(string formatFrom, string formatTo, string sourcePath, string targetPath)
    {
        if (!_readers.TryGetValue(formatFrom, out var reader))
            throw new ArgumentException(
                $"Неизвестный исходный формат «{formatFrom}». Доступно: {string.Join(", ", SourceFormats)}.");
        if (!_writers.TryGetValue(formatTo, out var writer))
            throw new ArgumentException(
                $"Неизвестный итоговый формат «{formatTo}». Доступно: {string.Join(", ", TargetFormats)}.");

        var targetIsZip = targetPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        if (targetIsZip
                ? File.Exists(targetPath)
                : Directory.Exists(targetPath) && Directory.EnumerateFileSystemEntries(targetPath).Any())
            throw new IOException($"«{targetPath}» уже существует. Укажите другое имя или удалите его.");

        var tempDirs = new List<string>();
        try
        {
            var sourceDir = OpenSource(sourcePath, tempDirs);
            var package = reader.Read(sourceDir);
            if (string.IsNullOrWhiteSpace(package.Name))
                package.Name = Path.GetFileNameWithoutExtension(Path.GetFullPath(sourcePath).TrimEnd('\\', '/'));

            if (targetIsZip)
            {
                var work = PackageArchive.CreateTempDir();
                tempDirs.Add(work);
                writer.Write(package, work);
                PackageArchive.Pack(work, targetPath);
            }
            else
                writer.Write(package, targetPath);

            return package;
        }
        finally
        {
            foreach (var dir in tempDirs)
                try
                {
                    Directory.Delete(dir, recursive: true);
                }
                catch
                {
                    /* временная папка */
                }
        }
    }

    private static string OpenSource(string path, List<string> tempDirs)
    {
        if (Directory.Exists(path)) return path;
        if (!File.Exists(path)) throw new FileNotFoundException($"Пакет «{path}» не найден.");

        var temp = PackageArchive.CreateTempDir();
        tempDirs.Add(temp);
        return PackageArchive.Extract(path, temp);
    }
}