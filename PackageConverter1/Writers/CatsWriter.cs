using System.Globalization;
using System.Xml.Linq;
using PackageConverter1.Common;
using PackageConverter1.Model;

namespace PackageConverter1.Writers;

/// <summary>Создаёт пакет CATS: один XML-файл в корне и файлы тестов в папке tests.</summary>
public class CatsWriter : IPackageWriter
{
    public string Format => "CATS";

    public void Write(ProblemPackage package, string directory)
    {
        Directory.CreateDirectory(directory);
        var copier = new FileCopier(package);
        var total = package.Tests.Count;

        var seconds = package.TimeLimitMs / 1000.0;
        var megabytes = (package.MemoryLimitBytes + Units.Megabyte - 1) / Units.Megabyte;

        var problem = new XElement("Problem",
            new XAttribute("title", package.Name),
            new XAttribute("tlimit", seconds.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("mlimit", megabytes),
            new XAttribute("inputFile", "*STDIN"),
            new XAttribute("outputFile", "*STDOUT"));

        if (copier.Copy(package.Statement, Path.Combine(directory, "problem.tex")))
        {
            problem.Add(new XElement("Attachment", new XAttribute("name", "statement"),
                new XAttribute("src", "problem.tex")));
            problem.Add(new XElement("ProblemStatement", new XAttribute("attachment", "statement")));
        }
        else
            problem.Add(new XElement("ProblemStatement", new XElement("p", "Условие задачи не перенесено.")));

        AddChecker(package, problem, directory, copier);

        if (package.Interactor != null && !package.Interactor.IsBuiltIn)
        {
            var name = "interactor" + Path.GetExtension(package.Interactor.Name);
            if (copier.Copy(package.Interactor, Path.Combine(directory, name)))
            {
                problem.Add(new XElement("Interactor", new XAttribute("name", "interactor"),
                    new XAttribute("src", name)));
                problem.Add(new XElement("Run", new XAttribute("method", "interactive")));
            }
        }

        for (var i = 0; i < total; i++)
        {
            var test = package.Tests[i];
            var number = PatternFormatter.Number(i + 1, total);
            var input = $"tests/{number}";
            var answer = $"tests/{number}.a";
            copier.Copy(test.Input, Path.Combine(directory, input));
            copier.Copy(test.Output, Path.Combine(directory, answer));

            var element = new XElement("Test", new XAttribute("rank", i + 1));
            if (test.Points > 0) element.Add(new XAttribute("points", test.Points));
            element.Add(new XElement("In", new XAttribute("src", input)));
            element.Add(new XElement("Out", new XAttribute("src", answer)));
            problem.Add(element);
        }

        foreach (var group in package.Groups)
        {
            var numbers = Enumerable.Range(1, total).Where(n => package.Tests[n - 1].GroupId == group.Id);
            var set = new XElement("Testset",
                new XAttribute("name", SetName(group.Id)),
                new XAttribute("tests", Ranges.ToSpec(numbers)),
                new XAttribute("points", group.Points));
            var depends = group.Prerequisites.Where(p => package.Groups.Any(g => g.Id == p)).Select(SetName).ToList();
            if (depends.Count > 0) set.Add(new XAttribute("depends_on", string.Join(",", depends)));
            problem.Add(set);
        }

        copier.Finish();
        var cats = new XElement("CATS", new XAttribute("version", "1.4"), problem);
        XmlFile.Save(new XDocument(cats), Path.Combine(directory, SafeFileName(package.Name) + ".xml"),
            withDeclaration: true);
    }

    private static void AddChecker(ProblemPackage package, XElement problem, string directory, FileCopier copier)
    {
        var checker = package.Checker;
        if (checker == null) return;

        if (checker.IsBuiltIn)
        {
            var guid = checker.Name.StartsWith("$std.") ? checker.Name[1..] : "std.strs";
            if (guid == "std.strs" && checker.Name != "$std.strs")
                package.Warnings.Add($"Встроенный чекер {checker.Name} заменён на std.strs.");
            problem.Add(new XElement("Import", new XAttribute("guid", guid)));
            return;
        }

        var name = "check" + Path.GetExtension(checker.Name);
        if (!copier.Copy(checker, Path.Combine(directory, name))) return;
        problem.Add(new XElement("Checker", new XAttribute("name", "checker"),
            new XAttribute("src", name), new XAttribute("style", "testlib")));
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;
        package.Warnings.Add("CATS ожидает исходный код чекера, а в исходном пакете был только .exe.");
    }

    private static string SetName(string id) => id.All(char.IsDigit) ? "group" + id : id;

    private static string SafeFileName(string name) => XmlFile.SafeName(name);
}