using PackageConverter1.Model;

namespace PackageConverter1.Writers;

/// <summary>Умеет создать пакет одного формата из общей модели.</summary>
public interface IPackageWriter
{
    /// <summary>Название формата: KRSU, CATS, PCMS2, EJUDGE.</summary>
    string Format { get; }

    void Write(ProblemPackage package, string directory);
}
