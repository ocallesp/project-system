// Licensed to the .NET Foundation under one or more agreements. The .NET Foundation licenses this file to you under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.ProjectSystem.Properties;
using Microsoft.VisualStudio.ProjectSystem.VS.References.Roslyn;

namespace Microsoft.VisualStudio.ProjectSystem.VS.References
{
    internal partial class ReferenceCleanupService
    {
        private abstract class ReferenceHandler
        {
            private readonly ReferenceType _referenceType;
            private readonly string _schema;
            private readonly string _itemSpecification;

            private IProjectRuleSnapshot? _snapshotReferences;

            protected ReferenceHandler(ReferenceType referenceType, string schema, string itemSpecification)
            {
                _referenceType = referenceType;
                _schema = schema;
                _itemSpecification = itemSpecification;
            }

            public abstract Task<bool> RemoveReferenceAsync(ConfiguredProject configuredProject, ReferenceInfo reference);

            public IProjectRuleSnapshot GetProjectSnapshot(ConfiguredProject selectedConfiguredProject)
            {
                IProjectSubscriptionService? serviceSubscription = selectedConfiguredProject.Services.ProjectSubscription;
                Assumes.Present(serviceSubscription);

                serviceSubscription.ProjectRuleSource.SourceBlock.TryReceive(filter, out IProjectVersionedValue<IProjectSubscriptionUpdate> item);

                _snapshotReferences = item.Value.CurrentState[_schema];

                return _snapshotReferences;
            }

            private static bool filter(IProjectVersionedValue<IProjectSubscriptionUpdate> obj)
            {
                return true;
            }

            public List<ReferenceInfo> GetReferences()
            {
                List<ReferenceInfo> references = new List<ReferenceInfo>();

                foreach (var item in _snapshotReferences.Items)
                {
                    string treatAsUsed = GetAttributeTreatAsUsed(item);
                    string itemSpecification = GetAttributeItemSpecification(item);

                    references.Add(new ReferenceInfo(_referenceType, itemSpecification, treatAsUsed == "True"));
                }

                return references;
            }

            private string GetAttributeTreatAsUsed(KeyValuePair<string, IImmutableDictionary<string, string>> item)
            {
                item.Value.TryGetValue("TreatAsUsed", out string treatAsUsed);
                treatAsUsed = string.IsNullOrEmpty(treatAsUsed) ? "False" : treatAsUsed;
                return treatAsUsed;
            }

            private string GetAttributeItemSpecification(KeyValuePair<string, IImmutableDictionary<string, string>> item)
            {
                item.Value.TryGetValue(_itemSpecification, out string itemSpecification);
                return itemSpecification;
            }

            public async Task<bool> UpdateReferenceAsync(ConfiguredProject activeConfiguredProject, ReferenceUpdate referenceUpdate, CancellationToken cancellationToken)
            {
                bool wasUpdated = false;

                string projectPath = activeConfiguredProject.UnconfiguredProject.FullPath;

                string newValue = referenceUpdate.Action == UpdateAction.TreatAsUsed ? "True" : "False";

                await activeConfiguredProject.Services.ProjectLockService.WriteLockAsync(async access =>
                {
                    var projectOther = await access.GetProjectAsync(activeConfiguredProject, cancellationToken);

                    var item = projectOther.AllEvaluatedItems.Where(i =>
                        string.Compare(i.ItemType, _schema, StringComparison.OrdinalIgnoreCase) == 0 &&
                        i.EvaluatedInclude == referenceUpdate.ReferenceInfo.ItemSpecification).First();

                    if (item != null)
                    {
                        await access.CheckoutAsync(projectPath);

                        item.SetMetadataValue("TreatAsUsed", newValue);

                        wasUpdated = true;
                    }
                }, cancellationToken);

                return wasUpdated;
            }
        }

        private class ProjectReferenceHandler : ReferenceHandler
        {
            internal ProjectReferenceHandler() : base(ReferenceType.Project, ProjectReference.SchemaName, "Identity")
            { }

            public override async Task<bool> RemoveReferenceAsync(ConfiguredProject configuredProject, ReferenceInfo reference)
            {
                Assumes.Present(configuredProject);
                Assumes.Present(configuredProject.Services);
                Assumes.Present(configuredProject.Services.ProjectReferences);

                await configuredProject.Services.ProjectReferences.RemoveAsync(reference.ItemSpecification);

                return true;
            }
        }

        private class PackageReferenceHandler : ReferenceHandler
        {
            internal PackageReferenceHandler() : base(ReferenceType.Package, PackageReference.SchemaName, "Name")
            { }

            public override async Task<bool> RemoveReferenceAsync(ConfiguredProject configuredProject, ReferenceInfo reference)
            {
                Assumes.Present(configuredProject);
                Assumes.Present(configuredProject.Services);
                Assumes.Present(configuredProject.Services.PackageReferences);

                await configuredProject.Services.PackageReferences.RemoveAsync(reference.ItemSpecification);

                return true;
            }
        }

        private class AssemblyReferenceHandler : ReferenceHandler
        {
            internal AssemblyReferenceHandler() : base(ReferenceType.Assembly, AssemblyReference.SchemaName, "HintPath")
            { }

            public override async Task<bool> RemoveReferenceAsync(ConfiguredProject configuredProject, ReferenceInfo reference)
            {
                Assumes.Present(configuredProject);
                Assumes.Present(configuredProject.Services);
                Assumes.Present(configuredProject.Services.AssemblyReferences);

                await configuredProject.Services.AssemblyReferences.RemoveAsync(null, reference.ItemSpecification);

                return true;
            }
        }

        private class SdkReferenceHandler : ReferenceHandler
        {
            internal SdkReferenceHandler() : base(ReferenceType.Unknown, SdkReference.SchemaName, "Name")
            { }

            public override Task<bool> RemoveReferenceAsync(ConfiguredProject configuredProject, ReferenceInfo reference)
            {
                // Do not remove Sdks
                return Task.FromResult(false);
            }
        }
    }
}
