using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using PackageConverter1.Model;

namespace PackageConverter1.Common;

/// <summary>Разбор единиц измерения: время, память, баллы.</summary>
public static class Units
{
    public static int SecondsToMs(string text)
    {
        text = text.Trim().TrimEnd('s', 'S');
        return (int)Math.Round(double.Parse(text, CultureInfo.InvariantCulture) * 1000);
    }

    public static long ParseMemory(string text, long defaultUnit)
    {
        text = text.Trim();
        var unit = defaultUnit;
        if (!char.IsLetter(text[^1])) return (long)(double.Parse(text, CultureInfo.InvariantCulture) * unit);
        unit = char.ToUpperInvariant(text[^1]) switch
        {
            'B' => 1,
            'K' => 1024L,
            'M' => 1024L * 1024,
            'G' => 1024L * 1024 * 1024,
            _ => throw new FormatException($"Неизвестная единица памяти: {text}")
        };
        text = text[..^1].Trim();

        return (long)(double.Parse(text, CultureInfo.InvariantCulture) * unit);
    }

    public static int ParsePoints(string? text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : (int)Math.Round(double.Parse(text, CultureInfo.InvariantCulture));

    public const long Megabyte = 1024L * 1024;
}

/// <summary>Подстановка номера теста в шаблон имени файла.</summary>
public static class PatternFormatter
{
    public static string Format(string pattern, int number)
    {
        if (pattern.Contains('#'))
            return Regex.Replace(pattern, "#+", m => number.ToString().PadLeft(m.Length, '0'));

        return Regex.Replace(pattern, @"%(0?)(\d*)d", m =>
        {
            var width = m.Groups[2].Value == "" ? 0 : int.Parse(m.Groups[2].Value);
            var fill = m.Groups[1].Value == "0" ? '0' : ' ';
            return number.ToString().PadLeft(width, fill);
        });
    }

    public static int Width(int totalTests) => Math.Max(2, totalTests.ToString().Length);

    public static string Number(int n, int totalTests) => n.ToString().PadLeft(Width(totalTests), '0');
}

/// <summary>Работа со списками номеров тестов: "1-3,5,7-9".</summary>
public static class Ranges
{
    public static List<int> ParseSpec(string spec)
    {
        var result = new List<int>();
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bounds = part.Split('-', StringSplitOptions.TrimEntries);
            if (!int.TryParse(bounds[0], out int from)) continue;
            var to = bounds.Length > 1 && int.TryParse(bounds[1], out var t) ? t : from;
            for (var i = from; i <= to; i++) result.Add(i);
        }

        return result;
    }

    public static List<(int From, int To)> Compress(IEnumerable<int> numbers)
    {
        var result = new List<(int, int)>();
        foreach (var n in numbers.Distinct().OrderBy(x => x))
        {
            if (result.Count > 0 && result[^1].Item2 == n - 1)
                result[^1] = (result[^1].Item1, n);
            else
                result.Add((n, n));
        }

        return result;
    }

    public static string ToSpec(IEnumerable<int> numbers) =>
        string.Join(",", Compress(numbers).Select(r => r.From == r.To ? $"{r.From}" : $"{r.From}-{r.To}"));
}

/// <summary>Поиск файлов внутри распакованного пакета.</summary>
public static class FileResolver
{
    private static readonly string[] SourceExtensions = [".cpp", ".dpr", ".pas", ".java", ".py"];

    public static PackageFile? FindExecutable(string dir, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        relativePath = relativePath.Replace('\\', '/');
        var withoutExt = Path.ChangeExtension(relativePath, null);

        foreach (var ext in new[] { ".exe" }.Concat(SourceExtensions))
        {
            var candidate = withoutExt + ext;
            var full = Path.Combine(dir, candidate);
            if (File.Exists(full)) return new PackageFile(candidate, full);
        }

        return new PackageFile(relativePath, Path.Combine(dir, relativePath));
    }

    public static PackageFile? FindExisting(string dir, params string?[] relativePaths)
    {
        foreach (var rel in relativePaths)
        {
            if (string.IsNullOrWhiteSpace(rel)) continue;
            var file = new PackageFile(rel.Replace('\\', '/'), Path.Combine(dir, rel));
            if (file.Exists) return file;
        }

        return null;
    }
}

/// <summary>Нумерация групп для форматов, где нужны числа (KRSU, EJUDGE, PCMS2).</summary>
public static class GroupIds
{
    public static Dictionary<string, int> Numbers(IReadOnlyList<TestGroup> groups)
    {
        var parsed = groups.Select(g => int.TryParse(g.Id, out int n) ? n : (int?)null).ToList();
        var allNumeric = parsed.All(x => x.HasValue) && parsed.Distinct().Count() == parsed.Count;

        var result = new Dictionary<string, int>();
        for (var i = 0; i < groups.Count; i++)
            result[groups[i].Id] = allNumeric ? parsed[i]!.Value : i + 1;
        return result;
    }
}

/// <summary>Копирование файлов в новый пакет; считает файлы, которых не оказалось.</summary>
public class FileCopier(ProblemPackage package)
{
    private int _missing;
    private string? _firstMissing;

    public bool Copy(PackageFile? file, string destinationPath)
    {
        if (file == null || file.Name == "") return false;
        if (file.IsBuiltIn)
        {
            package.Warnings.Add($"Встроенный чекер {file.Name} не имеет файла, поэтому не перенесён.");
            return false;
        }

        if (!file.Exists)
        {
            _missing++;
            _firstMissing ??= file.Name;
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(file.SourcePath!, destinationPath, overwrite: true);
        return true;
    }

    public void Finish()
    {
        if (_missing > 0)
            package.Warnings.Add(
                $"В исходном пакете не найдено файлов: {_missing} (например, {_firstMissing}). " +
                "Возможно, они создаются генератором при сборке исходного пакета.");
    }
}

/// <summary>Запись XML в UTF-8 без BOM.</summary>
public static class XmlFile
{
    public static void Save(XDocument document, string path, bool withDeclaration)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = !withDeclaration
        };
        using var writer = XmlWriter.Create(path, settings);
        document.Save(writer);
    }

    public static string SafeName(string name)
    {
        var safe = Regex.Replace(name, @"[^A-Za-z0-9_\-]", "_").Trim('_');
        return safe == "" ? "problem" : safe;
    }
}

/// <summary>ZIP-архивы: распаковка во временную папку и упаковка результата.</summary>
public static class PackageArchive
{
    public static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pkgconv_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string Extract(string zipPath, string tempDir)
    {
        ZipFile.ExtractToDirectory(zipPath, tempDir);
        var dir = tempDir;
        while (Directory.GetFiles(dir).Length == 0 && Directory.GetDirectories(dir).Length == 1)
            dir = Directory.GetDirectories(dir)[0];
        return dir;
    }

    public static void Pack(string dir, string zipPath) =>
        ZipFile.CreateFromDirectory(dir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
}