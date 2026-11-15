using System.Xml.Linq;
using PackageConverter1.Common;
using PackageConverter1.Model;

namespace PackageConverter1.Readers;

/// <summary>
/// Формат PCMS2: problem.xml. Бывает в двух видах:
///  1) Polygon (как в Codeforces) — judging/testset с time-limit, tests, groups;
///  2) olymp.ru — judging/script/testset с test-group.
/// </summary>
public class Pcms2Reader : IPackageReader
{
    public string Format => "PCMS2";

    public ProblemPackage Read(string directory)
    {
        var path = Path.Combine(directory, "problem.xml");
        if (!File.Exists(path))
            throw new PackageFormatException("В пакете PCMS2 не найден файл problem.xml.");

        var root = XDocument.Load(path).Root!;
        var judging = root.Element("judging")
                      ?? throw new PackageFormatException("В problem.xml нет раздела <judging>.");

        var package = new ProblemPackage();
        if (judging.Element("script") is { } script)
            ReadOlympVariant(directory, root, script, package);
        else
            ReadPolygonVariant(directory, root, judging, package);
        return package;
    }

    private static void ReadPolygonVariant(string dir, XElement root, XElement judging, ProblemPackage package)
    {
        package.Name = root.Element("names")?.Element("name")?.Attribute("value")?.Value
                       ?? (string?)root.Attribute("short-name") ?? "";

        var testset = judging.Elements("testset").FirstOrDefault(t => (string?)t.Attribute("name") == "tests")
                      ?? judging.Elements("testset").FirstOrDefault()
                      ?? throw new PackageFormatException("В problem.xml нет раздела <testset>.");

        if (testset.Element("time-limit") is { } time) package.TimeLimitMs = int.Parse(time.Value);
        if (testset.Element("memory-limit") is { } memory) package.MemoryLimitBytes = long.Parse(memory.Value);

        var inputPattern = testset.Element("input-path-pattern")?.Value ?? "tests/%02d";
        var answerPattern = testset.Element("answer-path-pattern")?.Value ?? "tests/%02d.a";
        var tests = testset.Element("tests")?.Elements("test").ToList() ?? [];

        foreach (var g in testset.Element("groups")?.Elements("group") ?? [])
        {
            var group = new TestGroup
            {
                Id = (string?)g.Attribute("name") ?? "",
                Points = Units.ParsePoints((string?)g.Attribute("points"))
            };
            foreach (var d in g.Element("dependencies")?.Elements("dependency") ?? [])
                group.Prerequisites.Add((string?)d.Attribute("group") ?? "");
            package.Groups.Add(group);
        }

        if (package.Groups.Count == 0)
            foreach (var id in tests.Select(t => (string?)t.Attribute("group")).Where(id => id != null).Distinct())
                package.Groups.Add(new TestGroup { Id = id! });

        for (var i = 0; i < tests.Count; i++)
        {
            var input = PatternFormatter.Format(inputPattern, i + 1);
            var answer = PatternFormatter.Format(answerPattern, i + 1);
            package.Tests.Add(new TestCase
            {
                Input = new PackageFile(input, Path.Combine(dir, input)),
                Output = new PackageFile(answer, Path.Combine(dir, answer)),
                GroupId = (string?)tests[i].Attribute("group"),
                Points = Units.ParsePoints((string?)tests[i].Attribute("points"))
            });
        }

        var assets = root.Element("assets");
        package.Checker = FindTool(dir, assets?.Element("checker"));
        package.Interactor = FindTool(dir, assets?.Element("interactor"));

        var texStatement = root.Element("statements")?.Elements("statement")
            .FirstOrDefault(s => (string?)s.Attribute("type") == "application/x-tex");
        package.Statement = FileResolver.FindExisting(dir, (string?)texStatement?.Attribute("path"),
            "statements/russian/problem.tex", "problem.tex");
    }

    private static PackageFile? FindTool(string dir, XElement? tool)
    {
        if (tool == null) return null;
        var path = (string?)tool.Element("binary")?.Attribute("path")
                   ?? (string?)tool.Element("source")?.Attribute("path");
        return FileResolver.FindExecutable(dir, path);
    }

    private static void ReadOlympVariant(string dir, XElement root, XElement script, ProblemPackage package)
    {
        package.Name = (string?)root.Attribute("id") ?? "";

        var testset = script.Elements("testset").OrderByDescending(t => t.Descendants("test").Count()).FirstOrDefault()
                      ?? throw new PackageFormatException("В problem.xml нет раздела <testset>.");

        if ((string?)testset.Attribute("time-limit") is { } time) package.TimeLimitMs = Units.SecondsToMs(time);
        if ((string?)testset.Attribute("memory-limit") is { } memory)
            package.MemoryLimitBytes = Units.ParseMemory(memory, 1);

        var inputPattern = (string?)testset.Attribute("input-href") ?? "tests/##";
        var answerPattern = (string?)testset.Attribute("answer-href") ?? "tests/##.a";

        var number = 0;

        void AddTest(string? groupId, XElement test)
        {
            number++;
            var input = PatternFormatter.Format(inputPattern, number);
            var answer = PatternFormatter.Format(answerPattern, number);
            package.Tests.Add(new TestCase
            {
                Input = new PackageFile(input, Path.Combine(dir, input)),
                Output = new PackageFile(answer, Path.Combine(dir, answer)),
                GroupId = groupId,
                Points = Units.ParsePoints((string?)test.Attribute("points"))
            });
        }

        var groupNumber = 0;
        foreach (var element in testset.Elements())
        {
            if (element.Name == "test")
                AddTest(null, element);
            else if (element.Name == "test-group")
            {
                groupNumber++;
                var group = new TestGroup
                {
                    Id = groupNumber.ToString(),
                    Points = Units.ParsePoints((string?)element.Attribute("group-bonus"))
                };
                var require = (string?)element.Attribute("require-groups") ?? "";
                group.Prerequisites.AddRange(
                    require.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries));
                package.Groups.Add(group);
                foreach (var test in element.Elements("test")) AddTest(group.Id, test);
            }
        }

        package.Checker = FileResolver.FindExecutable(dir,
            (string?)script.Element("verifier")?.Element("binary")?.Attribute("file"));
        package.Interactor = FileResolver.FindExecutable(dir,
            (string?)script.Element("interactor")?.Element("binary")?.Attribute("file"));
        package.Statement = FileResolver.FindExisting(dir, "statements/russian/problem.tex", "problem.tex");
    }
}