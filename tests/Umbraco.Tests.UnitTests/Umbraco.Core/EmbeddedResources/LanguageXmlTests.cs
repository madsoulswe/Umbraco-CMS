using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.FileProviders;
using NUnit.Framework;
using Umbraco.Cms.Core.Composing;

namespace Umbraco.Cms.Tests.UnitTests.Umbraco.Core.EmbeddedResources;

[TestFixture]
public class LanguageXmlTests
{
    private IEnumerable<IFileInfo> _fileInfos;

    [SetUp]
    public void SetUp()
    {
        var languageProvider = new EmbeddedFileProvider(typeof(IAssemblyProvider).Assembly, "Umbraco.Cms.Core.EmbeddedResources.Lang");
        _fileInfos = languageProvider.GetDirectoryContents(string.Empty)
            .Where(x => !x.IsDirectory && x.Name.EndsWith(".xml"));
    }

    [Test]
    public void Can_Load_Language_Xml_Files()
    {
        var readFilesCount = 0;
        var xmlDocument = new XmlDocument();
        foreach (var languageFile in _fileInfos)
        {
            using var stream = new StreamReader(languageFile.CreateReadStream());

            // Load will throw an exception if the XML isn't valid.
            xmlDocument.Load(stream);

            readFilesCount++;
        }

        // Ensure that at least one file was read.
        Assert.AreNotEqual(0, readFilesCount);
    }

    [Test]
    public void Should_Have_Valid_Culture_And_Lcid_Attributes_In_All_Files()
    {

        var errors = new List<string>();
        foreach (var languageFile in _fileInfos)
        {
            using var stream = new StreamReader(languageFile.CreateReadStream());
            XDocument xmlDoc = XDocument.Load(stream);

            var languageElement = xmlDoc.Element("language");

            if (languageElement != null)
            {
                // Check 'culture' attribute validity
                var cultureAttr = languageElement.Attribute("culture")?.Value;
                if (!string.IsNullOrEmpty(cultureAttr))
                {
                    try
                    {
                        // Validate 'culture' attribute by checking if it is a valid .NET culture
                        new CultureInfo(cultureAttr);
                    }
                    catch (CultureNotFoundException)
                    {
                        errors.Add($"Invalid 'culture' value in file {languageFile.Name}: {cultureAttr}");
                    }
                }

                // Check 'lcid' attribute validity
                var lcidAttr = languageElement.Attribute("lcid")?.Value;
                if (!string.IsNullOrEmpty(lcidAttr))
                {
                    try
                    {
                        // Validate 'lcid' is a number and is a valid CultureInfo
                        new CultureInfo(int.Parse(lcidAttr));
                    }
                    catch (CultureNotFoundException)
                    {
                        errors.Add($"Invalid 'lcid' value in file {languageFile.Name}: {lcidAttr}");
                    }
                }
            }
        }

        Assert.IsEmpty(errors, string.Join(Environment.NewLine, errors));
    }

    [Test]
    public void Should_Not_Have_Duplicate_Area_Aliases_In_All_Files()
    {
        foreach (var languageFile in _fileInfos)
        {
            using var stream = new StreamReader(languageFile.CreateReadStream());
            XDocument xmlDoc = XDocument.Load(stream);

            var areaAliases = xmlDoc.Descendants("area")
                .Select(area => area.Attribute("alias")?.Value)
                .ToList();

            var duplicateAreas = areaAliases
                .GroupBy(alias => alias)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToList();

            Assert.IsEmpty(duplicateAreas,
                $"Duplicate area aliases found in file {languageFile.Name}: {string.Join(", ", duplicateAreas)}");
        }
    }

    [Test]
    public void Should_Not_Have_Duplicate_Area_Key_Aliases_In_All_Files()
    {

        var duplicates = new Dictionary<string, List<string>>();

        foreach (var languageFile in _fileInfos)
        {
            using var stream = new StreamReader(languageFile.CreateReadStream());
            XDocument xmlDoc = XDocument.Load(stream);

            var joinedAliases = xmlDoc.Descendants("area")
                .SelectMany(area => area.Descendants("key").Select(key => $"{area.Attribute("alias")?.Value}.{key.Attribute("alias")?.Value}"))
                .ToList();

            var duplicateAliases = joinedAliases
                    .GroupBy(alias => alias)
                    .Where(group => group.Count() > 1)
                    .Select(group => new { Alias = group.Key, Count = group.Count() })
                    .ToList();

            if(duplicateAliases.Count > 0)
            {
                duplicates.Add(languageFile.Name, duplicateAliases.Select(d => d.Alias).ToList());
            }

            
        }

        if(duplicates.Count > 0)
        {
            Assert.Multiple(() =>
            {
                foreach(var duplicate in duplicates)
                {
                    Assert.IsEmpty(duplicate.Value,
                        $"Duplicate area key aliases found in file {duplicate.Key}: {string.Join(", ", duplicate.Value)}");
                }
            });
        }


    }

}
