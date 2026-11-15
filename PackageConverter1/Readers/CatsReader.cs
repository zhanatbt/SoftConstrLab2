using System.Globalization;
using System.Xml.Linq;
using PackageConverter1.Common;
using PackageConverter1.Model;

namespace PackageConverter1.Readers;

/// <summary>Формат CATS: в корне архива один XML-файл с описанием задачи.</summary>
public class CatsReader : IPackageReader
{
    public string Format => "CATS";

    public ProblemPackage Read(string directory)
    {
        var xmlFiles = Directory.GetFiles(directory, "*.xml");
        if (xmlFiles.Length != 1)
            throw new PackageFormatException(
                $"В корне пакета CATS должен быть ровно один XML-файл, найдено: {xmlFiles.Length}.");

        var problem = XDocument.Load(xmlFiles[0]).Root?.Element("Problem")
                      ?? throw new PackageFormatException("В XML-файле CATS нет тега <Problem>.");

        var package = new ProblemPackage { Name = (string?)problem.Attribute("title") ?? "" };

        if ((string?)problem.Attribute("tlimit") is { } tlimit) package.TimeLimitMs = Units.SecondsToMs(tlimit);
        if ((string?)problem.Attribute("mlimit") is { } mlimit)
            package.MemoryLimitBytes = Units.ParseMemory(mlimit, Units.Megabyte);

        ReadTools(directory, problem, package);
        ReadTests(directory, problem, package);
        ReadTestsets(problem, package);

        if (problem.Element("ProblemStatement") != null)
            package.Warnings.Add("Условие задачи CATS записано в формате STML и не переносится.");
        return package;
    }

    private static void ReadTools(string dir, XElement problem, ProblemPackage package)
    {
        if ((string?)problem.Attribute("stdChecker") is { } std)
            package.Checker = new PackageFile("$" + std);
        else if (problem.Elements("Import").Select(i => (string?)i.Attribute("guid"))
                     .FirstOrDefault(g => g != null && g.StartsWith("std.")) is { } guid)
            package.Checker = new PackageFile("$" + guid);
        else
            package.Checker = FileResolver.FindExecutable(dir, (string?)problem.Element("Checker")?.Attribute("src"));

        package.Interactor = FileResolver.FindExecutable(dir, (string?)problem.Element("Interactor")?.Attribute("src"));
    }

    private static void ReadTests(string dir, XElement problem, ProblemPackage package)
    {
        // Описание теста может быть разбито на несколько тегов, поэтому собираем по номеру.
        var byRank = new SortedDictionary<int, (string? In, string? Out, int Points)>();
        var answersFromSolution = false;

        foreach (var element in problem.Elements().Where(e => e.Name == "Test" || e.Name == "TestRange"))
        {
            List<int> ranks;
            if (element.Name == "Test")
                ranks = Ranges.ParseSpec((string?)element.Attribute("rank") ?? "");
            else
                ranks = Ranges.ParseSpec($"{(string?)element.Attribute("from")}-{(string?)element.Attribute("to")}");

            var inSrc = (string?)element.Element("In")?.Attribute("src");
            var outSrc = (string?)element.Element("Out")?.Attribute("src");
            if (element.Element("Out")?.Attribute("use") != null) answersFromSolution = true;
            var points = Units.ParsePoints((string?)element.Attribute("points"));

            foreach (var rank in ranks)
            {
                byRank.TryGetValue(rank, out var old);
                byRank[rank] = (inSrc != null ? Substitute(inSrc, rank) : old.In,
                    outSrc != null ? Substitute(outSrc, rank) : old.Out,
                    points != 0 ? points : old.Points);
            }
        }

        foreach (var (rank, test) in byRank)
        {
            package.Tests.Add(new TestCase
            {
                Input = new PackageFile(test.In ?? "", test.In == null ? null : Path.Combine(dir, test.In)),
                Output = new PackageFile(test.Out ?? "", test.Out == null ? null : Path.Combine(dir, test.Out)),
                Points = test.Points
            });
        }

        if (answersFromSolution)
            package.Warnings.Add("В CATS ответы тестов не хранятся в пакете, их получает эталонное решение. " +
                                 "Выходные файлы тестов в новом пакете не созданы.");
    }

    private static string Substitute(string src, int number) =>
        src.Replace("%0n", number.ToString("D2", CultureInfo.InvariantCulture))
            .Replace("%n", number.ToString(CultureInfo.InvariantCulture));

    private static void ReadTestsets(XElement problem, ProblemPackage package)
    {
        var testsets = problem.Elements("Testset").ToList();
        var names = testsets.Select(t => (string?)t.Attribute("name") ?? "").ToList();

        foreach (var set in testsets)
        {
            var group = new TestGroup
            {
                Id = (string?)set.Attribute("name") ?? "",
                Points = Units.ParsePoints((string?)set.Attribute("points"))
            };
            var depends = (string?)set.Attribute("depends_on") ?? "";
            group.Prerequisites.AddRange(depends
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(names.Contains));
            package.Groups.Add(group);

            foreach (var number in Ranges.ParseSpec((string?)set.Attribute("tests") ?? ""))
                if (number >= 1 && number <= package.Tests.Count)
                    package.Tests[number - 1].GroupId = group.Id;
        }
    }
}