using System.Xml.Linq;
using PackageConverter1.Common;
using PackageConverter1.Model;

namespace PackageConverter1.Writers;

/// <summary>Создаёт пакет PCMS2 в виде Polygon: problem.xml, tests/NN, tests/NN.a.</summary>
public class Pcms2Writer : IPackageWriter
{
    public string Format => "PCMS2";

    public void Write(ProblemPackage package, string directory)
    {
        Directory.CreateDirectory(directory);
        var copier = new FileCopier(package);
        var groupNumbers = GroupIds.Numbers(package.Groups);
        var total = package.Tests.Count;
        var width = PatternFormatter.Width(total);

        var tests = new XElement("tests");
        for (var i = 0; i < total; i++)
        {
            var test = package.Tests[i];
            var number = PatternFormatter.Number(i + 1, total);
            copier.Copy(test.Input, Path.Combine(directory, "tests", number));
            copier.Copy(test.Output, Path.Combine(directory, "tests", number + ".a"));

            var element = new XElement("test", new XAttribute("method", "manual"));
            if (test.GroupId != null && groupNumbers.TryGetValue(test.GroupId, out int group))
                element.Add(new XAttribute("group", group));
            if (test.Points > 0)
                element.Add(new XAttribute("points", test.Points));
            tests.Add(element);
        }

        var testset = new XElement("testset", new XAttribute("name", "tests"),
            new XElement("time-limit", package.TimeLimitMs),
            new XElement("memory-limit", package.MemoryLimitBytes),
            new XElement("test-count", total),
            new XElement("input-path-pattern", $"tests/%0{width}d"),
            new XElement("answer-path-pattern", $"tests/%0{width}d.a"),
            tests);

        if (package.Groups.Count > 0)
        {
            var groups = new XElement("groups");
            foreach (var g in package.Groups)
            {
                var element = new XElement("group",
                    new XAttribute("name", groupNumbers[g.Id]),
                    new XAttribute("points", g.Points),
                    new XAttribute("points-policy", "complete-group"));
                var dependencies = g.Prerequisites.Where(groupNumbers.ContainsKey).ToList();
                if (dependencies.Count > 0)
                    element.Add(new XElement("dependencies",
                        dependencies.Select(d =>
                            new XElement("dependency", new XAttribute("group", groupNumbers[d])))));
                groups.Add(element);
            }

            testset.Add(groups);
        }

        var problem = new XElement("problem",
            new XAttribute("revision", 1),
            new XAttribute("short-name", XmlFile.SafeName(package.Name)),
            new XElement("names", new XElement("name",
                new XAttribute("language", "russian"), new XAttribute("value", package.Name))));

        if (copier.Copy(package.Statement, Path.Combine(directory, "statements", "russian", "problem.tex")))
            problem.Add(new XElement("statements", new XElement("statement",
                new XAttribute("charset", "UTF-8"), new XAttribute("language", "russian"),
                new XAttribute("path", "statements/russian/problem.tex"),
                new XAttribute("type", "application/x-tex"))));

        problem.Add(new XElement("judging",
            new XAttribute("input-file", ""), new XAttribute("output-file", ""), testset));

        var assets = new XElement("assets");
        if (CopyTool(package, package.Checker, "check", directory, copier) is { } checker)
            assets.Add(new XElement("checker", new XAttribute("type", "testlib"), checker));
        if (CopyTool(package, package.Interactor, "interact", directory, copier) is { } interactor)
            assets.Add(new XElement("interactor", interactor));
        if (assets.HasElements) problem.Add(assets);

        copier.Finish();
        XmlFile.Save(new XDocument(problem), Path.Combine(directory, "problem.xml"), withDeclaration: true);
    }

    private static XElement? CopyTool(ProblemPackage package, PackageFile? file, string baseName,
        string directory, FileCopier copier)
    {
        if (file == null) return null;
        if (file.IsBuiltIn)
        {
            package.Warnings.Add($"Встроенный чекер {file.Name} не имеет файла в PCMS2, не перенесён.");
            return null;
        }

        var name = baseName + Path.GetExtension(file.Name);
        if (!copier.Copy(file, Path.Combine(directory, name))) return null;

        var isBinary = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        return new XElement(isBinary ? "binary" : "source", new XAttribute("path", name));
    }
}