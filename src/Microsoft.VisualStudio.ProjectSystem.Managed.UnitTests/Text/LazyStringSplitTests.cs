﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

using Xunit;

namespace Microsoft.VisualStudio.Text
{
    public sealed class LazyStringSplitTests
    {
        [Theory]
        [InlineData("a;b;c",       ';', new[] { "a", "b", "c" })]
        [InlineData("a_b_c",       '_', new[] { "a", "b", "c" })]
        [InlineData("aa;bb;cc",    ';', new[] { "aa", "bb", "cc" })]
        [InlineData("aaa;bbb;ccc", ';', new[] { "aaa", "bbb", "ccc" })]
        [InlineData(";a;b;c",      ';', new[] { "a", "b", "c" })]
        [InlineData("a;b;c;",      ';', new[] { "a", "b", "c" })]
        [InlineData(";a;b;c;",     ';', new[] { "a", "b", "c" })]
        [InlineData(";;a;;b;;c;;", ';', new[] { "a", "b", "c" })]
        [InlineData("",            ';', new string[0])]
        [InlineData(";",           ';', new string[0])]
        [InlineData(";;",          ';', new string[0])]
        [InlineData(";;;",         ';', new string[0])]
        [InlineData(";;;a",        ';', new[] { "a" })]
        [InlineData("a;;;",        ';', new[] { "a" })]
        [InlineData(";a;;",        ';', new[] { "a" })]
        [InlineData(";;a;",        ';', new[] { "a" })]
        [InlineData("a",           ';', new[] { "a" })]
        [InlineData("aa",          ';', new[] { "aa" })]
        public void ProducesCorrectEnumeration(string input, char delimiter, string[] expected)
        {
            // This boxes
            IEnumerable<string> actual = new LazyStringSplit(input, delimiter);

            Assert.Equal(expected, actual);

            // Non boxing foreach
            var list = new List<string>();

            foreach (var s in new LazyStringSplit(input, delimiter))
            {
                list.Add(s);
            }

            Assert.Equal(expected, list);

            // Equivalence with string.Split
            Assert.Equal(expected, input.Split(new[] { delimiter }, StringSplitOptions.RemoveEmptyEntries));
        }

        [Fact]
        public void Constructor_WithNullInput_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new LazyStringSplit(null!, ' '));
        }
    }
}
