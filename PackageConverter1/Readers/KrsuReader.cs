using System.Xml.Linq;
using PackageConverter1.Common;
using PackageConverter1.Model;

namespace PackageConverter1.Readers;

/// <summary>Формат KRSU: testinfo.xml + файлы inputNN.txt / outputNN.txt.</summary>
public class KrsuReader : IPackageReader
{
    public string Format => "KRSU";

    public ProblemPackage Read(string directory)
    {
        var path = Path.Combine(directory, "testinfo.xml");
        if (!File.Exists(path))
            throw new PackageFormatException("В пакете KRSU не найден файл testinfo.xml.");

        var root = XDocument.Load(path).Root!;
        var package = new ProblemPackage();

        if (root.Element("timelimit") is { } time)
            package.TimeLimitMs = int.Parse(time.Value);
        if (root.Element("memorylimit") is { } memory)
            package.MemoryLimitBytes = long.Parse(memory.Value);

        package.Checker = ReadTool(directory, root.Element("checker")?.Value);
        package.Interactor = ReadTool(directory, root.Element("interactor")?.Value);
        package.Statement = FileResolver.FindExisting(directory, root.Element("problem")?.Value);

        foreach (var g in root.Elements("group"))
        {
            var group = new TestGroup
            {
                Id = (string?)g.Attribute("id") ?? "",
                Points = Units.ParsePoints((string?)g.Attribute("points"))
            };
            var prereq = (string?)g.Attribute("prereq") ?? "";
            group.Prerequisites.AddRange(prereq.Split(',',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            package.Groups.Add(group);
        }

        foreach (var t in root.Elements("test"))
        {
            var input = (string?)t.Attribute("input") ?? "";
            var output = (string?)t.Attribute("output") ?? "";
            package.Tests.Add(new TestCase
            {
                Input = new PackageFile(input, Path.Combine(directory, input)),
                Output = new PackageFile(output, Path.Combine(directory, output)),
                GroupId = (string?)t.Attribute("groupid"),
                Points = Units.ParsePoints((string?)t.Attribute("points"))
            });
        }

        return package;
    }

    private static PackageFile? ReadTool(string directory, string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim();
        return name.StartsWith('$') ? new PackageFile(name) : new PackageFile(name, Path.Combine(directory, name));
    }
}