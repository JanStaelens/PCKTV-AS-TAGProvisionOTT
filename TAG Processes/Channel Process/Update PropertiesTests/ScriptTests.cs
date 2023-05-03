using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Script;
using Skyline.DataMiner.Automation;
using Skyline.DataMiner.DataMinerSolutions.ProcessAutomation.Manager;
using Skyline.DataMiner.ExceptionHelper;
using Skyline.DataMiner.Core.DataMinerSystem.Common;
using Skyline.DataMiner.Net.Apps.DataMinerObjectModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Script.Tests
{
    [TestClass()]
    public class ScriptTests
    {
        [TestMethod()]
        public void CheckAndUpdateLayoutTest()
        {
            var fakeEngine = new Mock<Engine>();

            var domHelper = new DomHelper(fakeEngine.Object.SendSLNetMessages, "process_automation");
            var exceptionHelper = new ExceptionHelper(fakeEngine.Object, domHelper);

            var tagInfo = new Mock<TagChannelInfo>();
            tagInfo.Object.ChannelMatch = "Channel Match Test";

            string layout = "Layout Test";
            Script script = new Script();

            tagInfo.Setup(tag => tag.GetLayoutsFromTable(layout)).Returns(new List<object[]> { new object[] { "1/1" }, new object[] { "1/2" } });

            var indexToUpdate = script.CheckLayoutIndexes(fakeEngine.Object, "Update Properties Test", exceptionHelper, tagInfo.Object, layout);

            Assert.IsTrue(indexToUpdate == "1/1");
        }
    }
}