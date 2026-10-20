using System.Diagnostics;
using System.Reflection;

internal static class ShutdownChecks
{
    public static void Run(string modelFile)
    {
        var start=new ProcessStartInfo("dotnet") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location); start.ArgumentList.Add("--shutdown-child"); start.ArgumentList.Add(modelFile);
        using var child=Process.Start(start)!;
        var output=child.StandardOutput.ReadToEndAsync(); var error=child.StandardError.ReadToEndAsync();
        if(!child.WaitForExit(20000)) { child.Kill(true); throw new Exception("Closing the main window left renderer/hidden editor threads running"); }
        if(child.ExitCode!=0) throw new Exception(output.Result+error.Result);
        Console.WriteLine("SHUTDOWN PASS: child process opened a renderer and hidden embedded mesh window, imported GLB, closed the main window, and fully exited without lingering threads");
    }
}
