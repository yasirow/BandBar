using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BandBar.Tests
{
    [TestClass]
    public class StrictJsonTests
    {
        [DataTestMethod]
        [DataRow("{}")]
        [DataRow(" { } \r\n")]
        [DataRow("{\"a\":1}")]
        [DataRow("{ \"a\": -0.5e+10, \"b\": [true, false, null, \"x\", {}, []], \"c\": { \"d\": \"\\u00e7\\n\\\"\" } }")]
        [DataRow("{ \"zone\": \"Bölge/İç\" }")]
        public void AcceptsValidObjects(string json)
        {
            Assert.IsTrue(StrictJson.IsObject(json));
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("[]")]
        [DataRow("null")]
        [DataRow("\"text\"")]
        [DataRow("{")]
        [DataRow("{ \"a\": 1")]
        [DataRow("{ \"a\": 1, }")]
        [DataRow("{ \"a\" 1 }")]
        [DataRow("{ a: 1 }")]
        [DataRow("{ 'a': 1 }")]
        [DataRow("{ \"a\": 01 }")]
        [DataRow("{ \"a\": 1. }")]
        [DataRow("{ \"a\": .5 }")]
        [DataRow("{ \"a\": tru }")]
        [DataRow("{ \"a\": \"unterminated }")]
        [DataRow("{ \"a\": \"bad \\x escape\" }")]
        [DataRow("{ \"a\": \"\\u12\" }")]
        [DataRow("{ \"a\": [1, 2,] }")]
        [DataRow("{} {}")]
        [DataRow("{ \"a\": 1 } // comment")]
        public void RejectsAnythingElse(string json)
        {
            Assert.IsFalse(StrictJson.IsObject(json));
        }

        [TestMethod]
        public void RejectsDeepNesting()
        {
            string deep = "{\"a\":" + new string('[', 100) + new string(']', 100) + "}";
            Assert.IsFalse(StrictJson.IsObject(deep));
        }

        [TestMethod]
        public void RejectsControlCharactersInStrings()
        {
            Assert.IsFalse(StrictJson.IsObject("{ \"a\": \"line\nbreak\" }"));
        }
    }
}
