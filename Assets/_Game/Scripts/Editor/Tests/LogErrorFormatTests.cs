using System;
using System.Text.RegularExpressions;
using CluckWars.Logging;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CluckWars.Tests
{
    /// <summary>
    /// A23 (architecture audit 2026-11-12): an <see cref="ILogService.Error"/> with an exception used to go to
    /// <c>Debug.LogException(new Exception(line, cause))</c>. Unity walks to the INNERMOST exception for the
    /// console headline, so what the Console showed was the cause's type and message and the
    /// <c>[Source]</c> tag — the one thing that says which system failed — was only visible inside the trace.
    /// The line is the headline now and the cause is under it.
    /// </summary>
    public sealed class LogErrorFormatTests
    {
        [Test]
        public void AnErrorWithACause_LeadsWithTheSourceLine_AndStillCarriesTheCause()
        {
            // One entry: our line first (so the Console headline names the source), the cause under it.
            LogAssert.Expect(LogType.Error, new Regex(
                @"^\[[^\]]+\]\[[^\]]+\]\[Error\]\[Disk\] could not save[\s\S]*InvalidOperationException[\s\S]*the cause"));

            new UnityLogService(LogLevel.Verbose)
                .Error("Disk", "could not save", new InvalidOperationException("the cause"));

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void AnErrorWithoutACause_IsTheBareLine()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"\[Error\]\[Disk\] could not save$"));
            new UnityLogService(LogLevel.Verbose).Error("Disk", "could not save");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void TheMinLevelStillGates_AnErrorWithACause()
        {
            // Off must stay silent on this path too: it is the one branch that used a different Debug call.
            new UnityLogService(LogLevel.Off).Error("Disk", "could not save", new InvalidOperationException("the cause"));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
