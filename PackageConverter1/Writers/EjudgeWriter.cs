using System.Text;
using PackageConverter1.Common;
using PackageConverter1.Model;

namespace PackageConverter1.Writers;

/// <summary>Создаёт пакет EJUDGE: problem.cfg, valuer.cfg, tests/NN, tests/NN.a.</summary>
public class EjudgeWriter : IPackageWriter
{
    public string Format => "EJUDGE";

    public void Write(ProblemPackage package, string directory)
    {
        Directory.CreateDirectory(directory);
        var copier = new FileCopier(package);
        var total = package.Tests.Count;
        var width = PatternFormatter.Width(total);

        for (var i = 0; i < total; i++)
        {
            string number = PatternFormatter.Number(i + 1, total);
            copier.Copy(package.Tests[i].Input, Path.Combine(directory, "tests", number));
            copier.Copy(package.Tests[i].Output, Path.Combine(directory, "tests", number + ".a"));
        }

        var checker = CopyTool(package, package.Checker, "checker", directory, copier);
        var interactor = CopyTool(package, package.Interactor, "interactor", directory, copier);
        copier.Copy(package.Statement, Path.Combine(directory, "problem.tex"));

        File.WriteAllText(Path.Combine(directory, "problem.cfg"),
            BuildProblemCfg(package, width, checker, interactor), new UTF8Encoding(false));

        var valuer = BuildValuerCfg(package);
        if (valuer != null)
            File.WriteAllText(Path.Combine(directory, "valuer.cfg"), valuer, new UTF8Encoding(false));

        copier.Finish();
    }

    private static string BuildProblemCfg(ProblemPackage package, int width, string? checker, string? interactor)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[problem]");
        sb.AppendLine("use_stdin = 1");
        sb.AppendLine("use_stdout = 1");
        sb.AppendLine("use_corr = 1");
        if (checker != null) sb.AppendLine("enable_testlib_mode = 1");

        if (package.TimeLimitMs % 1000 == 0) sb.AppendLine($"time_limit = {package.TimeLimitMs / 1000}");
        else sb.AppendLine($"time_limit_millis = {package.TimeLimitMs}");

        sb.AppendLine(package.MemoryLimitBytes % Units.Megabyte == 0
            ? $"max_vm_size = {package.MemoryLimitBytes / Units.Megabyte}M"
            : $"max_vm_size = {package.MemoryLimitBytes}");

        sb.AppendLine($"long_name = \"{package.Name.Replace("\"", "'")}\"");
        sb.AppendLine($"internal_name = \"{XmlFile.SafeName(package.Name)}\"");
        sb.AppendLine($"test_pat = \"%0{width}d\"");
        sb.AppendLine($"corr_pat = \"%0{width}d.a\"");
        if (checker != null) sb.AppendLine($"check_cmd = \"{checker}\"");
        if (interactor != null) sb.AppendLine($"interactor_cmd = \"{interactor}\"");
        return sb.ToString();
    }

    private static string? BuildValuerCfg(ProblemPackage package)
    {
        var sb = new StringBuilder();

        if (package.Groups.Count > 0)
        {
            var numbers = GroupIds.Numbers(package.Groups);
            sb.AppendLine("global {\n    stat_to_judges 1;\n    stat_to_users 1;\n}\n");

            foreach (var group in package.Groups)
            {
                var testNumbers = Enumerable.Range(1, package.Tests.Count)
                    .Where(n => package.Tests[n - 1].GroupId == group.Id).ToList();
                var ranges = Ranges.Compress(testNumbers);

                sb.AppendLine($"group {numbers[group.Id]} {{");
                foreach (var (from, to) in ranges)
                    sb.AppendLine($"    tests {from}-{to};");
                sb.AppendLine($"    score {group.Points};");
                var requires = group.Prerequisites.Where(numbers.ContainsKey).Select(p => numbers[p]).ToList();
                if (requires.Count > 0)
                    sb.AppendLine($"    requires {string.Join(", ", requires)};");
                sb.AppendLine("}\n");

                if (ranges.Count > 1)
                    package.Warnings.Add(
                        $"Тесты группы {group.Id} идут не подряд — в valuer.cfg записано несколько строк tests.");
            }

            if (package.Tests.Any(t => t.Points > 0))
                package.Warnings.Add("Баллы за отдельные тесты внутри групп в EJUDGE не переносятся.");
            return sb.ToString();
        }

        if (package.Tests.Any(t => t.Points > 0))
        {
            for (var n = 1; n <= package.Tests.Count; n++)
                sb.AppendLine($"group {n} {{\n    tests {n}-{n};\n    score {package.Tests[n - 1].Points};\n}}\n");
            return sb.ToString();
        }

        return null;
    }

    private static string? CopyTool(ProblemPackage package, PackageFile? file, string baseName,
        string directory, FileCopier copier)
    {
        if (file == null) return null;
        if (file.IsBuiltIn)
        {
            package.Warnings.Add($"Встроенный чекер {file.Name} не имеет файла в EJUDGE, не перенесён.");
            return null;
        }

        var name = baseName + Path.GetExtension(file.Name);
        return copier.Copy(file, Path.Combine(directory, name)) ? name : null;
    }
}