using System.Reflection;

namespace Crucible.Tests;

public static class API
{
    private static void outputTestData(MethodInfo methodInfo)
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
                    Console.WriteLine($"[ OK ] API.{methodInfo.Name} ({endMilliseconds-startMilliseconds} ms)");
                }
                else
                {
                    Console.WriteLine($"[ FAILED ] API.{methodInfo.Name} ({endMilliseconds-startMilliseconds} ms)");
                    // [  FAILED  ] TestCaseName.TestName (time_in_ms ms)
                    // path/to/source_file.cpp:lineNumber: Failure
                    // Expected: condition_expected_to_be_true
                    // Actual: condition_found_to_be_false
                }
            }
            else
            {
                Console.WriteLine($"[ FAILED ] API.{methodInfo.Name} ({endMilliseconds-startMilliseconds} ms)");
                // [  FAILED  ] TestCaseName.TestName (time_in_ms ms)
                // path/to/source_file.cpp:lineNumber: Failure
                // Expected: condition_expected_to_be_true
                // Actual: condition_found_to_be_false
            }
        }
        catch (Exception e)
        {
            long endMilliseconds = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
            Console.WriteLine($"[ FAILED ] API.{methodInfo.Name} ({endMilliseconds-startMilliseconds} ms)");
            // [  FAILED  ] TestCaseName.TestName (time_in_ms ms)
            // path/to/source_file.cpp:lineNumber: Failure
            // Expected: condition_expected_to_be_true
            // Actual: condition_found_to_be_false
            Console.WriteLine(e);
            throw;
        }
        
    }
    public static void RunAllTests()
    {
        Vector3Tests.DotTest();
    }
    
}