namespace Crucible;
using System.Runtime.CompilerServices;
public class TestResult
{
    public enum ResultCode
    {
        Pass,
        Fail
    }

    public string  Message { get; private set; }
    public string File {get; private set;}
    public int Line {get; private set;}
    public string Caller { get; private set; }
    public ResultCode Result { get; private set; }

    private TestResult(string msg, string file, int line, string caller, ResultCode result)
    {
        Message = msg;
        File = file;
        Line = line;
        Caller = caller;
        Result = result;
    }

    public static TestResult Pass([CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0,
        [CallerMemberName] string memberName = "")
    {
        return new TestResult("",sourceFilePath,sourceLineNumber,memberName,ResultCode.Pass);
    }

    public static TestResult Fail(string message, [CallerFilePath] string sourceFilePath = "",
        [CallerLineNumber] int sourceLineNumber = 0,
        [CallerMemberName] string memberName = "")
    {
        return new TestResult(message,sourceFilePath,sourceLineNumber,memberName,ResultCode.Fail);
    }
}