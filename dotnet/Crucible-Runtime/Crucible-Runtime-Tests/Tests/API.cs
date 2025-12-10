using System.Reflection;
using Crucible.Core.Math;
using Crucible.Tests.Utilities;

namespace Crucible.Tests;

public static class API
{
    private static bool outputTestData(MethodInfo methodInfo)
    {
        var originalConsole =  Console.Out;
        StringWriter outWriter = new StringWriter();
        Console.SetOut(outWriter);
        long startMilliseconds = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
        try
        {
            var success = methodInfo.Invoke(null, null);
            long endMilliseconds = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;

            var consoleText = outWriter.ToString();
            Console.SetOut(originalConsole);

            if (success is TestResult successStatus)
            {
                if (successStatus.Result == TestResult.ResultCode.Pass)
                {
                    Console.WriteLine(
                        $"<OK> API {methodInfo.DeclaringType?.FullName}.{methodInfo.Name} ({endMilliseconds - startMilliseconds} ms)");
                    if (!string.IsNullOrEmpty(consoleText))
                    {
                        Console.WriteLine(consoleText);
                    }
                    return true;
                }
                else
                {
                    Console.WriteLine(
                        $"<FAILED> API {methodInfo.DeclaringType?.FullName}.{methodInfo.Name} ({endMilliseconds - startMilliseconds} ms)");
                    
                    if (!string.IsNullOrEmpty(consoleText))
                    {
                        Console.WriteLine(consoleText);
                    }
                    Console.WriteLine($"[[{successStatus.Message}: On Line ({successStatus.Line}) {successStatus.File}]]");
                    return false;
                }
            }
            else
            {
                Console.WriteLine(
                    $"<FAILED> API {methodInfo.DeclaringType?.FullName}.{methodInfo.Name} ({endMilliseconds - startMilliseconds} ms)");
                return false;
            }
        }
        catch (Exception e)
        {
            long endMilliseconds = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            if (e.InnerException != null)
            {
                Console.WriteLine(e.InnerException.Message);
                Console.WriteLine(e.InnerException.StackTrace);
            }
            Console.WriteLine(e.Message);
            Console.WriteLine(e.StackTrace);
            var consoleText = outWriter.ToString();
            Console.SetOut(originalConsole);
            Console.WriteLine(
                $"<FAILED> API {methodInfo.DeclaringType?.FullName}.{methodInfo.Name} ({endMilliseconds - startMilliseconds} ms)");
            if (!string.IsNullOrEmpty(consoleText))
            {
                Console.WriteLine(consoleText);
            }
            return false;
        }
        finally
        {
            Console.SetOut(originalConsole);
        }
        
    }
    public static Int32 RunAllTests()
    {
        Assembly assembly = Assembly.GetCallingAssembly();
        
        IEnumerable<Type> classesWithTest = assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<TestAttribute>() != null);
        bool allTestsPassed = true;
        int totalTests = 0;
        int passingTests = 0;
        int failingTests = 0;
        foreach (Type testType in classesWithTest)
        {
            var testMethods = testType.GetMethods(BindingFlags.Static | BindingFlags.Public);
            foreach (MethodInfo methodInfo in testMethods)
            {
                if (methodInfo.ReturnType == typeof(TestResult) && methodInfo.GetParameters().Length == 0)
                {
                    if (!outputTestData(methodInfo))
                    {
                        allTestsPassed = false;
                        failingTests++;
                    }
                    else
                    {
                        passingTests++;
                    }
                    totalTests++;
                }
            }
            if (testMethods.Count() > 0)
            {
                Console.WriteLine();
            }
        }
        Console.WriteLine($"Tests Passed: {passingTests}, Tests: {totalTests}, Failing: {failingTests}");
        if (allTestsPassed)
        {
            return 1;
        }

        return 0;
    }
    
}