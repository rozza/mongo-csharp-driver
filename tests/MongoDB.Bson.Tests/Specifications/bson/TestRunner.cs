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
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using Xunit;

namespace MongoDB.Bson.Specifications.bson
{
    public class TestRunner
    {
        [Theory]
        [ClassData(typeof(TestCaseFactory))]
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
            var style = NumberStyles.Float & ~NumberStyles.AllowTrailingWhite;
            Decimal128 result;
            if (Decimal128.TryParse(subject, style, NumberFormatInfo.CurrentInfo, out result))
            {
                Assert.True(false, $"{subject} should have resulted in a parse failure.");
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

        private class TestCaseFactory : IEnumerable<object[]>
        {
            public  IEnumerator<object[]> GetEnumerator()
            {
#if NETSTANDARD1_6
                const string prefix = "MongoDB.Bson.Tests.Dotnet.Specifications.bson.tests.";
#else
                const string prefix = "MongoDB.Bson.Tests.Specifications.bson.tests.";
#endif
                var executingAssembly = typeof(TestCaseFactory).GetTypeInfo().Assembly;
                var enumerable = executingAssembly
                    .GetManifestResourceNames()
                    .Where(path => path.StartsWith(prefix) && path.EndsWith(".json"))
                    .SelectMany(path =>
                    {
                        var definition = ReadDefinition(path);
                        var fullName = path.Remove(0, prefix.Length);

                        var tests = new List<object[]>();

                        if (definition.Contains("valid"))
                        {
                            tests.AddRange(GetTestCasesHelper(
                                TestType.Valid,
                                (string)definition["description"],
                                definition["valid"].AsBsonArray.Cast<BsonDocument>()));
                        }
                        if (definition.Contains("parseErrors"))
                        {
                            tests.AddRange(GetTestCasesHelper(
                            TestType.ParseError,
                            (string)definition["description"],
                            definition["parseErrors"].AsBsonArray.Cast<BsonDocument>()));
                        }
                        if (definition.Contains("decodeErrors"))
                        {
                            tests.AddRange(GetTestCasesHelper(
                                TestType.DecodeError,
                                (string)definition["description"],
                            
                                definition["decodeErrors"].AsBsonArray.Cast<BsonDocument>()));
                        }

                        return tests;
                    });
                return enumerable.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            private static IEnumerable<object[]> GetTestCasesHelper(TestType type, string description, IEnumerable<BsonDocument> documents)
            {
                var nameList = new Dictionary<string, int>();
                foreach (BsonDocument document in documents)
                {
                    //var data = new TestCaseData(type, document);
                    //data.SetCategory("Specifications");
                    //data.SetCategory("bson");

                    //var name = GetTestName(description, document);
                    //int i = 0;
                    //if (nameList.TryGetValue(name, out i))
                    //{
                    //    nameList[name] = i + 1;
                    //    name += " #" + i;
                    //}
                    //else
                    //{
                    //    nameList[name] = 1;
                    //}

                    //yield return data.SetName(name);

                    var data = new object[] { type, document };
                    yield return data;
                }
            }

            //private static string GetTestName(string description, BsonDocument definition)
            //{
            //    var name = description;
            //    if (definition.Contains("description"))
            //    {
            //        name += " - " + (string)definition["description"];
            //    }

            //    return name;
            //}

            private static BsonDocument ReadDefinition(string path)
            {
                var executingAssembly = typeof(TestCaseFactory).GetTypeInfo().Assembly;
                using (var definitionStream = executingAssembly.GetManifestResourceStream(path))
                using (var definitionStringReader = new StreamReader(definitionStream))
                {
                    var definitionString = definitionStringReader.ReadToEnd();
                    return BsonDocument.Parse(definitionString);
                }
            }
        }
    }
}