// Licensed to the .NET Foundation under one or more agreements. The .NET Foundation licenses this file to you under the MIT license. See the LICENSE.md file in the project root for more information.

using EnvDTE;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Exceptions;
using Microsoft.VisualStudio.ProjectSystem.VS.References.Roslyn;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Microsoft.VisualStudio.ProjectSystem.VS.References
{
    [Export(typeof(IReferenceCleanupService))]
    internal partial class ReferenceCleanupService : IReferenceCleanupService
    {
        ReferenceHandler _projectReferenceHandler = new ProjectReferenceHandler();
        ReferenceHandler _packageReferenceHandler = new PackageReferenceHandler();
        ReferenceHandler _assemblyReferenceHandler = new AssemblyReferenceHandler();
        ReferenceHandler _sdkReferenceHandler = new SdkReferenceHandler();

        private readonly ConfiguredProject _configuredProject;
        private readonly IVsUIService<DTE> _dte;
        private readonly IVsUIService<SVsSolution, IVsSolution> _solution;

        private Dictionary<ReferenceType, string> _referenceTypes = new Dictionary<ReferenceType, string>();

        [ImportingConstructor]
        public ReferenceCleanupService(ConfiguredProject configuredProject, IVsUIService<SDTE, DTE> dte, IVsUIService<SVsSolution, IVsSolution> solution)
        {
            _configuredProject = configuredProject;
            _dte = dte;
            _solution = solution;
        }

        public string GetTargetFrameworkMoniker(CodeAnalysis.ProjectId projectId)
        {
            throw new NotImplementedException();
        }

        public Task<string> GetProjectAssetsFilePathAsync(string projectPath, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<string> GetProjectAssetsFilePathAsync(string projectPath, string targetFramework, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public async Task<ImmutableArray<ReferenceInfo>> GetProjectReferencesAsync(string projectPath, string targetFramework, CancellationToken cancellationToken)
        {
            List<ReferenceInfo> references;

            try
            {
                ConfiguredProject activeConfiguredProject = await GetActiveConfiguredProjectByPathAsync(projectPath);

                references = await GetAllReferencesInConfiguredProjectAsync(activeConfiguredProject);
            }
            catch
            {
                throw new InvalidProjectFileException();
            }

            return references.ToImmutableArray();
        }

        private async Task<ConfiguredProject> GetActiveConfiguredProjectByPathAsync(string projectPath)
        {
            var unconfiguredProjectPath = FindUnconfiguredProjectByPath(projectPath);

            var activeConfiguredProject = await unconfiguredProjectPath.GetSuggestedConfiguredProjectAsync();
            if (activeConfiguredProject is null)
            {
                throw new InvalidProjectFileException();
            }

            return activeConfiguredProject;
        }

        private UnconfiguredProject FindUnconfiguredProjectByPath(string projectPath)
        {
            var unconfigProjects = _configuredProject.Services.ProjectService.LoadedUnconfiguredProjects;

            UnconfiguredProject unconfiguredProject = unconfiguredProject = unconfigProjects.First(project =>
                StringComparers.Paths.Equals((string)project.FullPath, projectPath));

            return unconfiguredProject;
        }

        private async Task<List<ReferenceInfo>> GetAllReferencesInConfiguredProjectAsync(ConfiguredProject selectedConfiguredProject)
        {
            GetProjectSnapshotAsync(selectedConfiguredProject);

            var references = GetReferences();

            return references;
        }

        private void GetProjectSnapshotAsync(ConfiguredProject selectedConfiguredProject)
        {
            _projectReferenceHandler.GetProjectSnapshot(selectedConfiguredProject);
            _packageReferenceHandler.GetProjectSnapshot(selectedConfiguredProject);
            _assemblyReferenceHandler.GetProjectSnapshot(selectedConfiguredProject);
            _sdkReferenceHandler.GetProjectSnapshot(selectedConfiguredProject);
        }

        private List<ReferenceInfo> GetReferences()
        {
            List<ReferenceInfo> references = new List<ReferenceInfo>();

            references.AddRange(_projectReferenceHandler.GetReferences());
            references.AddRange(_packageReferenceHandler.GetReferences());
            references.AddRange(_assemblyReferenceHandler.GetReferences());
            references.AddRange(_sdkReferenceHandler.GetReferences());

            return references;
        }

        public async Task<bool> TryUpdateReferenceAsync(string projectPath, string targetFrameworkMoniker, ReferenceUpdate referenceUpdate, CancellationToken cancellationToken)
        {
            bool wasUpdated = false;

            if (referenceUpdate.Action == UpdateAction.None)
            {
                return wasUpdated;
            }

            ConfiguredProject activeConfiguredProject = await GetActiveConfiguredProjectByPathAsync(projectPath);

            ReferenceHandler referenceHandler = FindReferenceHandler(referenceUpdate);

            if (referenceUpdate.Action == UpdateAction.TreatAsUsed || referenceUpdate.Action == UpdateAction.TreatAsUnused)
            {
                wasUpdated = await referenceHandler.UpdateReferenceAsync(activeConfiguredProject, referenceUpdate, cancellationToken);
            }
            else
            {
                wasUpdated = await referenceHandler.RemoveReferenceAsync(activeConfiguredProject, referenceUpdate.ReferenceInfo);
            }
            
            return wasUpdated;
        }

        private ReferenceHandler FindReferenceHandler(ReferenceUpdate referenceUpdate)
        {
            ReferenceHandler referenceHandler;

            if (referenceUpdate.ReferenceInfo.ReferenceType == ReferenceType.Project)
            {
                referenceHandler = _projectReferenceHandler;
            }
            else if (referenceUpdate.ReferenceInfo.ReferenceType == ReferenceType.Package)
            {
                referenceHandler = _packageReferenceHandler;
            }
            else if (referenceUpdate.ReferenceInfo.ReferenceType == ReferenceType.Assembly)
            {
                referenceHandler = _assemblyReferenceHandler;
            }
            else
            {
                referenceHandler = _sdkReferenceHandler;
            }

            return referenceHandler;
        }

        public bool IsProjectCpsBased(string projectPath)
        {
            IVsHierarchy? projectHierarchy = GetProjectHierarchy(projectPath);
            var isCps = projectHierarchy.IsCapabilityMatch("CPS");

            return isCps;
        }

        private IVsHierarchy? GetProjectHierarchy(string projectPath)
        {
            Project project = TryGetProjectFromPath(projectPath);
            if (project == null)
            {
                return null;
            }

            return TryGetIVsHierarchy(project);
        }

        private IVsHierarchy? TryGetIVsHierarchy(Project project)
        {
            if (_solution.Value.GetProjectOfUniqueName(project.UniqueName, out IVsHierarchy projectHierarchy) == HResult.OK)
            {
                return projectHierarchy;
            }

            return null;
        }

        private Project? TryGetProjectFromPath(string projectPath)
        {
            foreach (Project project in _dte.Value.Solution.Projects)
            {
                string? fullName;
                try
                {
                    fullName = project.FullName;
                }
                catch (Exception)
                {
                    // DTE COM calls can fail for any number of valid reasons.
                    continue;
                }

                if (StringComparers.Paths.Equals(fullName, projectPath))
                {
                    return project;
                }
            }

            return null;
        }
    }
}
