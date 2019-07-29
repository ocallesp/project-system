﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Microsoft.VisualStudio.Utilities;

using Xunit;
using Xunit.Abstractions;

namespace Microsoft.VisualStudio.Setup
{
    public sealed class SwrTests
    {
        private static readonly Regex _swrFolderPattern = new Regex(@"^\s*folder\s+""(?<path>[^""]+)""\s*$", RegexOptions.Compiled);
        private static readonly Regex _swrFilePattern = new Regex(@"^\s*file\s+source=""(?<path>[^""]+)""\s*$", RegexOptions.Compiled);
        private static readonly Regex _xlfFilePattern = new Regex(@"^(?<filename>.+\.xaml)\.(?<culture>[^.]+)\.xlf$", RegexOptions.Compiled);

        private readonly ITestOutputHelper _output;

        public SwrTests(ITestOutputHelper output) => _output = output;

        [Fact]
        public void CommonFiles_ContainsAllXamlFiles()
        {
            var rootPath = RepoUtil.FindRepoRootPath();

            var swrPath = Path.Combine(
                rootPath,
                "src",
                "setup",
                "Microsoft.VisualStudio.ProjectSystem.Managed.CommonFiles",
                "CommonFiles.swr");

            var setupFilesByCulture = ReadSwrFiles(swrPath)
                .Select(ParseSwrFile)
                .Where(pair => pair.File.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                .ToLookup(pair => pair.Culture, pair => pair.File);

            var rulesPath = Path.Combine(
                rootPath,
                "src",
                "Microsoft.VisualStudio.ProjectSystem.Managed",
                "ProjectSystem",
                "Rules");

            var ruleFilesByCulture = Directory.EnumerateFiles(rulesPath, "*", SearchOption.AllDirectories)
                .Select(ParseRepoFile)
                .Where(pair => pair.Culture != null)
                .ToLookup(pair => pair.Culture, pair => pair.File);

            var setupCultures = setupFilesByCulture.Select(p => p.Key);
            var ruleCultures = ruleFilesByCulture.Select(p => p.Key);

            Assert.True(
                setupCultures.ToHashSet().SetEquals(ruleCultures!),
                "Set of cultures must match.");

            var guilty = false;

            foreach (var culture in setupCultures)
            {
                var setupFiles = setupFilesByCulture[culture];
                var ruleFiles = ruleFilesByCulture[culture];

                foreach (var missing in ruleFiles.Except(setupFiles, StringComparer.OrdinalIgnoreCase))
                {
                    guilty = true;
                    _output.WriteLine($"- Missing file {missing} in culture {culture}");
                }

                foreach (var extra in setupFiles.Except(ruleFiles, StringComparer.OrdinalIgnoreCase))
                {
                    guilty = true;
                    _output.WriteLine($"- Extra file {extra} in culture {culture}");
                }
            }

            Assert.False(guilty, "See test output for details");

            return;

            static (string Culture, string File) ParseSwrFile((string Folder, string File) item)
            {
                const string folderPrefix = @"InstallDir:MSBuild\Microsoft\VisualStudio\Managed";
                const string filePrefix = @"$(VisualStudioXamlRulesDir)";

                Assert.StartsWith(folderPrefix, item.Folder);
                Assert.StartsWith(filePrefix, item.File);

                var culture = item.Folder.Substring(folderPrefix.Length).TrimStart('\\');
                var fileName = culture.Length == 0
                    ? item.File.Substring(filePrefix.Length)
                    : item.File.Substring(filePrefix.Length + culture.Length + 1);

                return (culture, fileName);
            }

            static (string? Culture, string? File) ParseRepoFile(string path)
            {
                var fileName = Path.GetFileName(path);

                if (fileName.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                {
                    return ("", fileName);
                }

                var match = _xlfFilePattern.Match(fileName);

                if (match.Success)
                {
                    return (match.Groups["culture"].Value, match.Groups["filename"].Value);
                }

                return (null, null);
            }
        }        

        private static IEnumerable<(string Folder, string File)> ReadSwrFiles(string path)
        {
            // Parse data with the following structure repeated.
            // There may be other lines in the file which are ignored for our purposes.
            //
            // folder "folder\path"
            //   file source = "file\path1"
            //   file source = "file\path2"

            string? folder = null;

            foreach (var line in File.ReadLines(path))
            {
                var folderMatch = _swrFolderPattern.Match(line);
                if (folderMatch.Success)
                {
                    folder = folderMatch.Groups["path"].Value;
                    continue;
                }

                var fileMatch = _swrFilePattern.Match(line);
                if (fileMatch.Success)
                {
                    if (folder == null)
                        throw new FileFormatException("'file' entry appears before a 'folder' entry.");
                    var file = fileMatch.Groups["path"].Value;
                    yield return (folder, file);
                }
            }
        }
    }
}
