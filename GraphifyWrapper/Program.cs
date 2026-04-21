using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Python.Runtime;

namespace GraphifyWrapper
{
    class Program
    {
        static void Main(string[] args)
        {
            // Determine the Python shared library dynamically based on OS.
            // On Linux (the sandbox environment), we look for a python3 shared library.
            // In a production app, we would make this more robust or configurable.
            string pythonDll = "";

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Typical Windows path might be "python312.dll" or whatever version is installed.
                // Assuming it's in the PATH
                pythonDll = "python312.dll"; // fallback default
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                pythonDll = "libpython3.12.dylib";
            }
            else
            {
                // On Linux, rely on standard library paths or python3-config.
                // In production, this should ideally be configurable via environment variable or config file.
                pythonDll = Environment.GetEnvironmentVariable("PYTHON_DLL") ?? "libpython3.12.so";
            }

            Runtime.PythonDLL = pythonDll;

            // Initialize the Python engine
            PythonEngine.Initialize();

            try
            {
                using (Py.GIL())
                {
                    // Import the sys module to manipulate the path and argv
                    dynamic sys = Py.Import("sys");

                    // Get the path to the original python code directory (repository root)
                    // We assume this C# app is running from within the repo, either in the root or a subfolder.
                    string appDirectory = AppDomain.CurrentDomain.BaseDirectory;

                    // We traverse up to find the root where 'graphify' package lives.
                    string? repoRoot = FindRepoRoot(appDirectory ?? string.Empty);

                    if (string.IsNullOrEmpty(repoRoot))
                    {
                        Console.WriteLine("Error: Could not find the 'graphify' python module directory.");
                        return;
                    }

                    // Append the repo root to Python's sys.path so it can find 'graphify'
                    sys.path.append(repoRoot);

                    // Set up sys.argv
                    // The first argument in Python's sys.argv is usually the script name
                    PyList pyArgv = new PyList();
                    pyArgv.Append(new PyString("graphify")); // Fake script name

                    // Append the arguments passed to the C# application
                    foreach (var arg in args)
                    {
                        pyArgv.Append(new PyString(arg));
                    }

                    sys.argv = pyArgv;

                    try
                    {
                        // Some python output gets eaten because the console stream isn't flushed
                        // or because python exits directly.
                        // Let's explicitly flush sys.stdout.

                        // Import the __main__ module of graphify
                        dynamic graphifyMain = Py.Import("graphify.__main__");

                        try
                        {
                            // Call the main function
                            graphifyMain.main();
                        }
                        catch (PythonException ex) when (ex.Type.Handle == sys.GetAttr("SystemExit").Handle || ex.Message.Contains("SystemExit"))
                        {
                            // Ignore SystemExit, it just means python is done
                        }

                        sys.stdout.flush();
                        sys.stderr.flush();
                    }
                    catch (PythonException ex)
                    {
                        // Print python exceptions clearly
                        Console.WriteLine("Python Error executing graphify:");
                        Console.WriteLine(ex.Message);
                        // Exit with an error code
                        Environment.ExitCode = 1;
                    }
                }
            }
            finally
            {
                // Shutdown the Python engine
                PythonEngine.Shutdown();
            }
        }

        // Helper to find the repository root by looking for the 'graphify' directory or 'pyproject.toml'
        static string? FindRepoRoot(string startPath)
        {
            DirectoryInfo? dir = new DirectoryInfo(startPath);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "graphify")) &&
                    File.Exists(Path.Combine(dir.FullName, "pyproject.toml")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }

            // Fallback: try current directory
            string currentDir = Directory.GetCurrentDirectory();
            if (Directory.Exists(Path.Combine(currentDir, "graphify")) &&
                File.Exists(Path.Combine(currentDir, "pyproject.toml")))
            {
                return currentDir;
            }

            return null;
        }
    }
}
