using System;
using System.IO;
using NUnit.Framework;
using MutSea.Framework.Diagnostics;
using MutSea.Tests.Common;

namespace MutSea.Tests
{
    [TestFixture]
    public class FunctionTracerTests : MutSeaTestCase
    {
        [Test]
        public void TraceLogsEnterAndExit()
        {
            string original = Environment.GetEnvironmentVariable("MUTSEA_TRACE");
            try
            {
                Environment.SetEnvironmentVariable("MUTSEA_TRACE", "1");
                TestLogging.LogToConsole();

                using StringWriter sw = new StringWriter();
                TextWriter oldOut = Console.Out;
                Console.SetOut(sw);

                using (FunctionTracer.Trace("dummy"))
                {
                    // do nothing
                }

                Console.Out.Flush();
                Console.SetOut(oldOut);

                string output = sw.ToString();
                Assert.IsTrue(output.Contains("TRACE ENTER"), "Enter line missing");
                Assert.IsTrue(output.Contains("TRACE EXIT"), "Exit line missing");
            }
            finally
            {
                Environment.SetEnvironmentVariable("MUTSEA_TRACE", original);
            }
        }
    }
}
