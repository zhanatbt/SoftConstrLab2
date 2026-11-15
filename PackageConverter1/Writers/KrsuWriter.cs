using System.Xml.Linq;
using PackageConverter1.Common;
using PackageConverter1.Model;

namespace PackageConverter1.Writers;

/// <summary>Создаёт пакет KRSU: testinfo.xml + inputNN.txt / outputNN.txt.</summary>
public class KrsuWriter : IPackageWriter
{
    public string Format => "KRSU";

    public void Write(ProblemPackage package, string directory)
    {
        Directory.CreateDirectory(directory);
        var copier = new FileCopier(package);
        var groupNumbers = GroupIds.Numbers(package.Groups);

        var testinfo = new XElement("testinfo");

        testinfo.Add(new XElement("checker",
            CopyTool(package, package.Checker, "checker", directory, copier) ?? "$fc"));
        if (CopyTool(package, package.Interactor, "interactor", directory, copier) is { } interactor)
            testinfo.Add(new XElement("interactor", interactor));
        if (copier.Copy(package.Statement, Path.Combine(directory, "problem.tex")))
            testinfo.Add(new XElement("problem", "problem.tex"));

        testinfo.Add(new XElement("memorylimit", package.MemoryLimitBytes));
        testinfo.Add(new XElement("timelimit", package.TimeLimitMs));
        testinfo.Add(new XElement("testversion", 1));
        testinfo.Add(new XElement("runtype", package.Groups.Count > 0 ? 2 : 0));

        foreach (var group in package.Groups)
        {
            var prereq = group.Prerequisites.Where(groupNumbers.ContainsKey).Select(p => groupNumbers[p]);
            testinfo.Add(new XElement("group",
                new XAttribute("id", groupNumbers[group.Id]),
                new XAttribute("points", group.Points),
                new XAttribute("prereq", string.Join(",", prereq))));
        }

        var total = package.Tests.Count;
        for (var i = 0; i < total; i++)
        {
            var test = package.Tests[i];
            var number = PatternFormatter.Number(i + 1, total);
            var input = $"input{number}.txt";
            var output = $"output{number}.txt";
            copier.Copy(test.Input, Path.Combine(directory, input));
            copier.Copy(test.Output, Path.Combine(directory, output));

            var element = new XElement("test",
                new XAttribute("input", input),
                new XAttribute("output", output));
            if (test.GroupId != null && groupNumbers.TryGetValue(test.GroupId, out int groupId))
                element.Add(new XAttribute("groupid", groupId));
            element.Add(new XAttribute("points", test.Points));
            testinfo.Add(element);
        }

        copier.Finish();
        XmlFile.Save(new XDocument(testinfo), Path.Combine(directory, "testinfo.xml"), withDeclaration: false);
    }

    private static string? CopyTool(ProblemPackage package, PackageFile? file, string baseName,
        string directory, FileCopier copier)
    {
        if (file == null) return null;
        if (file.IsBuiltIn) return file.Name;

        var extension = Path.GetExtension(file.Name);
        var name = baseName + extension;
        if (!copier.Copy(file, Path.Combine(directory, name))) return null;

        if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
            package.Warnings.Add(
                $"{baseName} перенесён как исходник ({name}), его нужно скомпилировать в {baseName}.exe.");
        return name;
    }
}