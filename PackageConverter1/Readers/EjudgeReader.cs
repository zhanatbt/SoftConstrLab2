using System.Text.RegularExpressions;
using PackageConverter1.Common;
using PackageConverter1.Model;

namespace PackageConverter1.Readers;

/// <summary>Формат EJUDGE: problem.cfg (настройки), valuer.cfg (группы), папка tests.</summary>
public class EjudgeReader : IPackageReader
{
    public string Format => "EJUDGE";

    public ProblemPackage Read(string directory)
    {
        var cfgPath = Path.Combine(directory, "problem.cfg");
        if (!File.Exists(cfgPath))
            throw new PackageFormatException("В пакете EJUDGE не найден файл problem.cfg.");

        var cfg = ParseConfig(File.ReadAllText(cfgPath));
        var package = new ProblemPackage
        {
            Name = cfg.GetValueOrDefault("long_name") ?? cfg.GetValueOrDefault("internal_name") ?? ""
        };

        if (cfg.TryGetValue("time_limit_millis", out var millis)) package.TimeLimitMs = int.Parse(millis);
        else if (cfg.TryGetValue("time_limit", out var seconds)) package.TimeLimitMs = Units.SecondsToMs(seconds);

        if (cfg.TryGetValue("max_vm_size", out var vm)) package.MemoryLimitBytes = Units.ParseMemory(vm, 1);

        package.Checker = FileResolver.FindExecutable(directory, cfg.GetValueOrDefault("check_cmd"));
        package.Interactor = FileResolver.FindExecutable(directory, cfg.GetValueOrDefault("interactor_cmd"));
        package.Statement = FileResolver.FindExisting(directory, "problem.tex", "statements/problem.tex");

        var testPoints = new Dictionary<int, int>();
        var testGroup = new Dictionary<int, string>();
        var valuerPath = Path.Combine(directory, "valuer.cfg");
        if (File.Exists(valuerPath))
            ReadValuer(File.ReadAllText(valuerPath), package, testGroup, testPoints);

        var testPattern = cfg.GetValueOrDefault("test_pat") ?? "%03d.dat";
        var answerPattern = cfg.GetValueOrDefault("corr_pat") ?? "%03d.ans";
        var testDir = cfg.GetValueOrDefault("test_dir") ?? "tests";

        var count = testGroup.Count > 0 ? testGroup.Keys.Max() : 0;
        if (testGroup.Count == 0)
            while (File.Exists(Path.Combine(directory, testDir, PatternFormatter.Format(testPattern, count + 1))))
                count++;

        for (var n = 1; n <= count; n++)
        {
            var input = $"{testDir}/{PatternFormatter.Format(testPattern, n)}";
            var answer = $"{testDir}/{PatternFormatter.Format(answerPattern, n)}";
            package.Tests.Add(new TestCase
            {
                Input = new PackageFile(input, Path.Combine(directory, input)),
                Output = new PackageFile(answer, Path.Combine(directory, answer)),
                GroupId = testGroup.GetValueOrDefault(n),
                Points = testPoints.GetValueOrDefault(n)
            });
        }

        return package;
    }

    private static Dictionary<string, string> ParseConfig(string text)
    {
        var result = new Dictionary<string, string>();
        foreach (var rawLine in text.Split('\n'))
        {
            var match = Regex.Match(rawLine.Trim(), @"^(\w+)\s*=\s*(.*)$");
            if (!match.Success) continue;

            var value = match.Groups[2].Value.Trim();
            if (value.StartsWith('"'))
            {
                int end = value.IndexOf('"', 1);
                value = end > 0 ? value[1..end] : value[1..];
            }
            else
                value = Regex.Replace(value, @"\s*(//|#).*$", "").Trim();

            result[match.Groups[1].Value] = value;
        }

        return result;
    }

    private static void ReadValuer(string text, ProblemPackage package,
        Dictionary<int, string> testGroup, Dictionary<int, int> testPoints)
    {
        text = Regex.Replace(text, "#.*", ""); // убираем комментарии

        foreach (Match m in Regex.Matches(text, @"group\s+(\d+)\s*\{([^}]*)\}"))
        {
            var id = m.Groups[1].Value;
            var body = m.Groups[2].Value;
            var group = new TestGroup { Id = id };

            if (Regex.Match(body, @"\bscore\s+(\d+)") is { Success: true } score)
                group.Points = int.Parse(score.Groups[1].Value);
            if (Regex.Match(body, @"requires\s+([\d,\s]+);") is { Success: true } requires)
                group.Prerequisites.AddRange(requires.Groups[1].Value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            var perTest = Regex.Match(body, @"test_score\s+(\d+)") is { Success: true } ts
                ? int.Parse(ts.Groups[1].Value)
                : 0;

            foreach (Match range in Regex.Matches(body, @"\btests\s+(\d+)(?:\s*-\s*(\d+))?"))
            {
                var from = int.Parse(range.Groups[1].Value);
                var to = range.Groups[2].Success ? int.Parse(range.Groups[2].Value) : from;
                for (var n = from; n <= to; n++)
                {
                    testGroup[n] = id;
                    if (perTest > 0) testPoints[n] = perTest;
                }
            }

            package.Groups.Add(group);
        }
    }
}