﻿using FluentAssertions;
using NUnit.Framework;
using Tailviewer.BusinessLogic.Analysis;
using Tailviewer.BusinessLogic.Plugins;

namespace Tailviewer.Test.BusinessLogic.Plugins
{
	[TestFixture]
	public sealed class LogAnalyserPluginTest
	{
		[Test]
		public void TestGetLogAnalyserPluginVersion()
		{
			PluginInterfaceVersionAttribute.GetInterfaceVersion(typeof(ILogAnalyserPlugin)).Should().Be(PluginInterfaceVersion.First);
		}
	}
}
