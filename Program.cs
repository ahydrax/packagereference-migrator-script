using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Wmhelp.XPath2;

namespace PackageReferenceMigrator
{
    class Program
    {
        static IEnumerable<string> FindProjects(string dir)
        {
            foreach (var sdir in Directory.GetDirectories(dir))
                foreach (var sres in FindProjects(sdir))
                    yield return sres;
            foreach (var f in Directory.GetFiles(dir, "*.csproj"))
                yield return f;
        }


        class PackageReference
        {
            public string Id { get; set; }
            public string Version { get; set; }
        }

        static XName GetName(string name) => XName.Get(name, "http://schemas.microsoft.com/developer/msbuild/2003");

        static void Migrate(string file)
        {
            var dir = Path.GetDirectoryName(file);
            var pkgconfig = Path.Combine(dir, "packages.config");
            if (!File.Exists(pkgconfig))
                return;
            var packages = XDocument.Load(pkgconfig).Root.Elements("package").Select(p => new PackageReference
            {
                Id = p.Attribute("id").Value,
                Version = p.Attribute("version").Value
            }).ToList();

            var project = XDocument.Load(file, LoadOptions.PreserveWhitespace);
            project.Root.RemoveAttributes();
            project.Root.SetAttributeValue("Sdk", "Microsoft.NET.Sdk");
            project.Root.SetAttributeValue("ToolsVersion", "15.0");
            foreach (var reference in project.Root.Descendants(GetName("Reference")).ToList())
            {
                var hintPath = reference.Elements(GetName("HintPath")).FirstOrDefault()?.Value;
                if (hintPath != null && hintPath.Contains("\\packages\\"))
                {
                    var ws = reference.PreviousNode as XText;
                    reference.Remove();
                    ws?.Remove();
                }
            }


            var memew = new XElement("ItemGroup");
            var memeTg = project.Root;
            memeTg.Add(memew);

            var sourceFiles = project.Root.Descendants(GetName("Compile")).ToList();
            foreach (var sourceFile in sourceFiles)
            {
                if (sourceFile.Descendants(GetName("AutoGen")).Any())
                {
                    sourceFile.Remove();
                    continue;
                }
                
                if (sourceFile.Descendants().Any())
                {
                    sourceFile.Remove();
                    memew.Add(sourceFile);
                    continue;
                }
                if (sourceFile.Attribute("Include")?.Value.EndsWithOneOf(".cs", ".resx") == true)
                {
                    sourceFile.Remove();
                }
            }

            var excludeProjects = new string[]
            {
                "Microsoft.Common.props",
                "Microsoft.CSharp.targets",
                "NuGet.targets",
                "WebApplication.targets",
                "Microsoft.TestTools.targets"
            };
            foreach (var projectImport in project.Root.Descendants(GetName("Import")).ToList())
            {
                if (projectImport.Attribute("Project")?.Value.EndsWithOneOf(excludeProjects) == true)
                {
                    projectImport.Remove();
                }
            }

            foreach (var projectReference in project.Root.Descendants(GetName("ProjectReference")).ToList())
            {
                foreach (var desc in projectReference.Descendants().ToList())
                {
                    desc.Remove();
                }
            }

            foreach (var targetsA in project.Root.Descendants(GetName("Target")).ToList())
            {
                if (targetsA.Attribute("Name")?.Value == "EnsureNuGetPackageBuildImports")
                {
                    targetsA.Remove();
                }
            }


            foreach (var errorTexts in project.Root.Descendants(GetName("ErrorText")).ToList())
            {
                errorTexts.Remove();
            }

            // add package reference
            var grp = new XElement(GetName("ItemGroup"));
            foreach (var pkg in packages)
            {
                grp.Add("\r\n    ");
                var reference = new XElement(GetName("PackageReference"));
                reference.SetAttributeValue("Include", pkg.Id);
                reference.Add("\r\n      ");
                reference.Add(new XElement(GetName("Version"), pkg.Version));
                reference.Add("\r\n    ");
                grp.Add(reference);
            }
            grp.Add("\r\n");

            var itemGroupUnited = new XElement(GetName("ItemGroup"));
            foreach (var itemGroup in project.Root.Descendants(GetName("ItemGroup")).ToList())
            {
                foreach (var childProp in itemGroup.Descendants(GetName("Reference")).ToList().Concat(itemGroup.Descendants(GetName("ProjectReference")).ToList()))
                {
                    itemGroupUnited.Add(childProp);
                    itemGroupUnited.Add("\r\n");
                }

                itemGroup.Remove();
            }
            project.Root.Add(itemGroupUnited);
            var propGroupUnited = new XElement(GetName("PropertyGroup"));
            propGroupUnited.Add(new XElement("TargetFramework") { Value = "net47" });
            foreach (var propGroup in project.Root.Descendants(GetName("PropertyGroup")).ToList())
            {
                foreach (var childProp in propGroup.Descendants().ToList())
                {
                    propGroupUnited.Add(childProp);
                    propGroupUnited.Add("\r\n");
                }

                propGroup.Remove();
            }
            project.Root.Add(propGroupUnited);
            project.Root.Add("\r\n");
            project.Root.Add(grp);
            project.Root.Add("\r\n");

            project.Save(file, SaveOptions.OmitDuplicateNamespaces);

            var alltext = File.ReadAllText(file);
            alltext = alltext.Replace("xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\"", "")
                .Replace("xmlns=\"\"", "");

            var xmldoc = new XmlDocument();
            xmldoc.LoadXml(alltext);

            alltext = xmldoc.Beautify()
                .Replace("></ProjectReference>", "/>")
                .Replace(">\r\n</ProjectReference>", "/>")
                .Replace("utf-16", "utf-8");

            File.WriteAllText(file, alltext, Encoding.UTF8);


            File.Delete(pkgconfig);
        }

        static void Main(string[] args)
        {
            foreach (var project in FindProjects(Directory.GetCurrentDirectory()))
            {
                Migrate(project);
            }

        }
    }

    internal static class Utils
    {
        public static bool EndsWithOneOf(this string self, params string[] values)
        {
            foreach (var val in values)
            {
                if (self.EndsWith(val))
                    return true;
            }

            return false;
        }

        public static string Beautify(this XmlDocument doc)
        {
            StringBuilder sb = new StringBuilder();
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\r\n",
                NewLineHandling = NewLineHandling.Replace,
                Encoding = Encoding.UTF8
            };
            using (XmlWriter writer = XmlWriter.Create(sb, settings))
            {
                doc.Save(writer);
            }
            return sb.ToString();
        }
    }
}
