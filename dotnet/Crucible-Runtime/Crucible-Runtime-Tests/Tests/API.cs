using System.Reflection;
using Crucible.Core.Math;
using Crucible.Tests.Utilities;

namespace Crucible.Tests;

public static class API
{
    private static bool outputTestData(MethodInfo methodInfo)
    {
        long startMilliseconds = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
        try
        {
            var success = methodInfo.Invoke(null, null);
            long endMilliseconds = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            if (success is bool successStatus)
            {
                if (successStatus)
                {
                    Console.WriteLine($"<OK> API {methodInfo.DeclaringType.FullName}.{methodInfo.Name} ({endMilliseconds-startMilliseconds} ms)");
                    return true;
                }
                else
                {
                    Console.WriteLine($"<FAILED> API {methodInfo.DeclaringType.FullName}.{methodInfo.Name} ({endMilliseconds-startMilliseconds} ms)");
                    // [  FAILED  ] TestCaseName.TestName (time_in_ms ms)
                    // path/to/source_file.cpp:lineNumber: Failure
                    // Expected: condition_expected_to_be_true
                    // Actual: condition_found_to_be_false
                    return false;
                }
            }
            else
            {
                Console.WriteLine($"<FAILED> API {methodInfo.DeclaringType.FullName}.{methodInfo.Name} ({endMilliseconds-startMilliseconds} ms)");
                // [  FAILED  ] TestCaseName.TestName (time_in_ms ms)
                // path/to/source_file.cpp:lineNumber: Failure
                // Expected: condition_expected_to_be_true
                // Actual: condition_found_to_be_false
                return false;
            }
        }
        catch (Exception e)
        {
            long endMilliseconds = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            Console.WriteLine($"<FAILED> API {methodInfo.DeclaringType.FullName}.{methodInfo.Name} ({endMilliseconds-startMilliseconds} ms)");
            Console.WriteLine(e.Message);
            Console.WriteLine(e.StackTrace);
            // [  FAILED  ] TestCaseName.TestName (time_in_ms ms)
            // path/to/source_file.cpp:lineNumber: Failure
            // Expected: condition_expected_to_be_true
            // Actual: condition_found_to_be_false
            return false;
        }
        
    }
    public static Int32 RunAllTests()
    {
        Assembly assembly = Assembly.GetCallingAssembly();
        
        IEnumerable<Type> classesWithTest = assembly.GetTypes()
            .Where(type => type.GetCustomAttribute<TestAttribute>() != null);
        bool allTestsPassed = true;
        foreach (Type testType in classesWithTest)
        {
            var testMethods = testType.GetMethods(BindingFlags.Static | BindingFlags.Public);
            foreach (MethodInfo methodInfo in testMethods)
            {
                if (methodInfo.ReturnType == typeof(bool) && methodInfo.GetParameters().Length == 0)
                {
                    if (!outputTestData(methodInfo))
                    {
                        allTestsPassed = false;
                    }
                }
            }

            if (testMethods.Count() > 0)
            {
                Console.WriteLine();
            }
        }

        if (allTestsPassed)
        {
            return 1;
        }

        return 0;
    }
    
}