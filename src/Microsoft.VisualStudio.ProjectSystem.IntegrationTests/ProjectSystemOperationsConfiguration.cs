﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Reflection;

using Microsoft.Test.Apex;
using Microsoft.Test.Apex.Providers;
using Microsoft.Test.Apex.Services.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.VisualStudio
{
    // Heavily based on MsTestOperationsConfiguration, which is only needed so that we can control the CompositionAssemblies
    // to avoid MEF composition errors being output into the test output and making it harder to understand the build log.
    internal class ProjectSystemOperationsConfiguration : OperationsConfiguration
    {
        internal ProjectSystemOperationsConfiguration(TestContext testContext)
        {
            TestContext = testContext;
        }

        public override IEnumerable<string> CompositionAssemblies => ProjectSystemHostConfiguration.CompositionAssemblyPaths;

        public TestContext TestContext { get; }

        protected override Type Verifier => typeof(IAssertionVerifier);

        protected override Type Logger => typeof(TestContextLogger);

        protected override void OnOperationsCreated(Operations operations)
        {
            base.OnOperationsCreated(operations);

            IAssertionVerifier verifier = operations.Get<IAssertionVerifier>();
            verifier.AssertionDelegate = Assert.Fail;
            verifier.FinalFailure += WriteVerificationFailureTree;

            var logger = operations.Get<TestContextLogger>();

            var property = typeof(TestContextLogger).GetProperty("TestContext", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (property == null)
                throw new InvalidOperationException("Unable to find TestContextLogger.TestContext. Has it been renamed?");

            property.SetValue(logger, TestContext);
        }

        protected override void OnProbingDirectoriesProviderCreated(IProbingDirectoriesProvider provider)
        {
        }

        private static void WriteVerificationFailureTree(object sender, FailureEventArgs e)
        {
            e.Logger.WriteEntry(SinkEntryType.Message, "Full verification failure tree:" + Environment.NewLine + Environment.NewLine + ResultMessageTreeController.Instance.FormatTreeAsText());
        }
    }
}
