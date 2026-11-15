using PackageConverter1.Model;

namespace PackageConverter1.Readers;

/// <summary>Умеет прочитать пакет одного формата (из распакованной папки) в общую модель.</summary>
public interface IPackageReader
{
    string Format { get; }

    ProblemPackage Read(string directory);
}
