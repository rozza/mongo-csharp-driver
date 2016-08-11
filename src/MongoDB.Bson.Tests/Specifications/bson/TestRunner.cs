﻿/* Copyright 2016 MongoDB Inc.
*
* Licensed under the Apache License, Version 2.0 (the "License");
* you may not use this file except in compliance with the License.
* You may obtain a copy of the License at
*
* http://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using NUnit.Framework;

namespace MongoDB.Bson.Specifications.bson
{
    [TestFixture]
    public class TestRunner
    {
        [TestCaseSource(typeof(TestCaseFactory), "GetTestCases")]
        public void RunTestDefinition(TestType testType, BsonDocument definition)
        {
            switch (testType)
            {
                case TestType.Valid:
                    RunValid(definition);
                    break;
                case TestType.ParseError:
                    RunParseError(definition);
                    break;
                case TestType.DecodeError:
                    RunDecodeError(definition);
                    break;
            }
        }

        private void RunValid(BsonDocument definition)
        {
            var subjectHex = ((string)definition["subject"]).ToLowerInvariant();
            var subjectBytes = BsonUtils.ParseHexString(subjectHex);

            BsonDocument subject = null;
            using (var stream = new MemoryStream(subjectBytes))
            using (var reader = new BsonBinaryReader(stream))
            {
                var context = BsonDeserializationContext.CreateRoot(reader);
                subject = BsonDocumentSerializer.Instance.Deserialize(context);
            }

            if (!definition.GetValue("decodeOnly", false).ToBoolean())
            {
                using (var stream = new MemoryStream())
                using (var writer = new BsonBinaryWriter(stream))
                {
                    var context = BsonSerializationContext.CreateRoot(writer);
                    BsonDocumentSerializer.Instance.Serialize(context, subject);

                    var actualEncodedHex = BsonUtils.ToHexString(stream.ToArray());
                    actualEncodedHex.Should().Be(subjectHex);
                }
            }

            var extjson = ((string)definition["extjson"]).Replace(" ", "");
            if (definition.GetValue("from_extjson", true).ToBoolean())
            {
                var fromExtjson = BsonDocument.Parse(extjson);
                fromExtjson.Should().Be(subject);
            }
            if (definition.GetValue("to_extjson", true).ToBoolean())
            {
                var toExtjson = subject.ToString().Replace(" ", "");
                toExtjson.Should().Be(extjson);
            }

            if (definition.Contains("string"))
            {
                var value = subject.GetElement(0).Value;
                value.ToString().Should().Be(definition["string"].ToString());
            }
        }

        private void RunParseError(BsonDocument definition)
        {
            var subject = (string)definition["subject"];
            Decimal128 result;
            if (Decimal128.TryParse(subject, out result))
            {
                Assert.Fail($"{subject} should have resulted in a parse failure.");
            }
        }

        private void RunDecodeError(BsonDocument definition)
        {
            var subjectHex = ((string)definition["subject"]).ToLowerInvariant();
            var subjectBytes = BsonUtils.ParseHexString(subjectHex);

            using (var stream = new MemoryStream(subjectBytes))
            using (var reader = new BsonBinaryReader(stream))
            {
                var context = BsonDeserializationContext.CreateRoot(reader);
                Action act = () => BsonDocumentSerializer.Instance.Deserialize(context);
                act.ShouldThrow<Exception>();
            }
        }

        public enum TestType
        {
            Valid,
            ParseError,
            DecodeError
        }

        private static class TestCaseFactory
        {
            public static IEnumerable<ITestCaseData> GetTestCases()
            {
                const string prefix = "MongoDB.Bson.Tests.Specifications.bson.tests.";
                return Assembly
                    .GetExecutingAssembly()
                    .GetManifestResourceNames()
                    .Where(path => path.StartsWith(prefix) && path.EndsWith(".json"))
                    .SelectMany(path =>
                    {
                        var definition = ReadDefinition(path);
                        var fullName = path.Remove(0, prefix.Length);

                        IEnumerable<ITestCaseData> tests = Enumerable.Empty<ITestCaseData>();

                        if (definition.Contains("valid"))
                        {
                            tests = tests.Concat(GetTestCases(
                                TestType.Valid,
                                (string)definition["description"],
                                definition["valid"].AsBsonArray.Cast<BsonDocument>()));
                        }
                        if (definition.Contains("parseErrors"))
                        {
                            tests = tests.Concat(GetTestCases(
                            TestType.ParseError,
                            (string)definition["description"],
                            definition["parseErrors"].AsBsonArray.Cast<BsonDocument>()));
                        }
                        if (definition.Contains("decodeErrors"))
                        {
                            tests = tests.Concat(GetTestCases(
                                TestType.DecodeError,
                                (string)definition["description"],
                                definition["decodeErrors"].AsBsonArray.Cast<BsonDocument>()));
                        }
                        return tests;
                    });
            }

            private static IEnumerable<TestCaseData> GetTestCases(TestType type, string description, IEnumerable<BsonDocument> documents)
            {
                var dataList = new List<ITestCaseData>();
                var nameList = new Dictionary<string, int>();
                foreach (BsonDocument document in documents)
                {
                    var data = new TestCaseData(type, document);
                    data.Categories.Add("Specifications");
                    data.Categories.Add("bson");

                    var name = GetTestName(description, document);
                    int i = 0;
                    if (nameList.TryGetValue(name, out i))
                    {
                        nameList[name] = i + 1;
                        name += " #" + i;
                    }
                    else
                    {
                        nameList[name] = 1;
                    }

                    yield return data.SetName(name);
                }
            }

            private static string GetTestName(string description, BsonDocument definition)
            {
                var name = description;
                if (definition.Contains("description"))
                {
                    name += " - " + (string)definition["description"];
                }

                return name;
            }

            private static BsonDocument ReadDefinition(string path)
            {
                using (var definitionStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(path))
                using (var definitionStringReader = new StreamReader(definitionStream))
                {
                    var definitionString = definitionStringReader.ReadToEnd();
                    return BsonDocument.Parse(definitionString);
                }
            }
        }
    }
}