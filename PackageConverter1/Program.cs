using System.Text;
using PackageConverter1.Common;
using PackageConverter1.Model;
using PackageConverter1.Readers;
using PackageConverter1.Writers;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
Console.OutputEncoding = Encoding.UTF8;

var converter = new FormatConverter(
    readers: [new KrsuReader(), new CatsReader(), new Pcms2Reader(), new EjudgeReader()],
    writers: [new KrsuWriter(), new CatsWriter(), new Pcms2Writer(), new EjudgeWriter()]);

if (args.Length != 4)
{
    Console.WriteLine("Использование: Program.exe [FORMAT_FROM] [FORMAT_TO] [PACKAGE_1] [PACKAGE_2]");
    Console.WriteLine($"  Форматы:   {string.Join(", ", converter.SourceFormats)}");
    Console.WriteLine("  PACKAGE_1: исходный пакет (папка или .zip)");
    Console.WriteLine(
        "  PACKAGE_2: итоговый пакет (папка или .zip; если имя оканчивается на .zip — будет создан архив)");
    Console.WriteLine("Пример:      Program.exe PCMS2 KRSU dungeon.zip dungeon_krsu.zip");
    return 1;
}

try
{
    var package = converter.Convert(args[0], args[1], args[2], args[3]);

    Console.WriteLine($"Готово: {args[0].ToUpperInvariant()} -> {args[1].ToUpperInvariant()}, " +
                      $"задача «{package.Name}»: тестов {package.Tests.Count}, групп {package.Groups.Count}.");
    foreach (var warning in package.Warnings.Distinct())
        Console.WriteLine($"  Предупреждение: {warning}");
    return 0;
}
catch (Exception e) when (e is ArgumentException or IOException or PackageFormatException
                              or System.Xml.XmlException or FormatException or InvalidDataException)
{
    Console.Error.WriteLine($"Ошибка: {e.Message}");
    return 2;
}