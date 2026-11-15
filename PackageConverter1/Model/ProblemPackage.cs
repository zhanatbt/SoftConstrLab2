namespace PackageConverter1.Model;

// Общая модель задачи.
// Все читатели превращают свой формат в эту модель, а все писатели создают свой формат из этой модели.

/// <summary>Файл пакета: как он называется и где лежит в исходном пакете.</summary>
public class PackageFile(string name, string? sourcePath = null)
{
    public string Name { get; } = name;
    public string? SourcePath { get; } = sourcePath != null && File.Exists(sourcePath) ? sourcePath : null;

    public bool IsBuiltIn => Name.StartsWith('$');

    public bool Exists => SourcePath != null;
}

/// <summary>Группа тестов: баллы и от каких групп зависит.</summary>
public class TestGroup
{
    public string Id { get; set; } = "";
    public int Points { get; set; }
    public List<string> Prerequisites { get; } = [];
}

/// <summary>Один тест: входной и выходной (ответ) файл.</summary>
public class TestCase
{
    public PackageFile Input { get; set; } = new("");
    public PackageFile Output { get; set; } = new("");
    public string? GroupId { get; set; }
    public int Points { get; set; }
}

public class ProblemPackage
{
    public string Name { get; set; } = "";
    public int TimeLimitMs { get; set; } = 1000;
    public long MemoryLimitBytes { get; set; } = 256L * 1024 * 1024;

    public PackageFile? Checker { get; set; }
    public PackageFile? Interactor { get; set; }
    public PackageFile? Statement { get; set; }

    public List<TestGroup> Groups { get; } = [];
    public List<TestCase> Tests { get; } = [];

    public List<string> Warnings { get; } = [];
}

public class PackageFormatException(string message) : Exception(message);
