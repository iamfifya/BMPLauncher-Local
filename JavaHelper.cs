using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace BMPLauncher.Core
{
    public static class JavaHelper
    {
        public static JavaInfo FindJava()
        {
            // Проверяем JAVA_HOME
            string javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                string javaExe = Path.Combine(javaHome, "bin", "java.exe");
                if (File.Exists(javaExe))
                {
                    return new JavaInfo(javaExe, GetJavaVersion(javaExe));
                }
            }

            // Проверяем в реестре
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\JavaSoft\Java Runtime Environment"))
            {
                if (key != null)
                {
                    string currentVersion = key.GetValue("CurrentVersion")?.ToString();
                    if (!string.IsNullOrEmpty(currentVersion))
                    {
                        using (var subKey = key.OpenSubKey(currentVersion))
                        {
                            string javaHomeReg = subKey?.GetValue("JavaHome")?.ToString();
                            if (!string.IsNullOrEmpty(javaHomeReg))
                            {
                                string javaExe = Path.Combine(javaHomeReg, "bin", "java.exe");
                                if (File.Exists(javaExe))
                                {
                                    return new JavaInfo(javaExe, GetJavaVersion(javaExe));
                                }
                            }
                        }
                    }
                }
            }

            // Ищем в Program Files
            string[] searchPaths = {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Java")
            };

            foreach (var path in searchPaths)
            {
                if (Directory.Exists(path))
                {
                    foreach (var dir in Directory.GetDirectories(path).Where(d => d.Contains("jdk") || d.Contains("jre")))
                    {
                        string javaExe = Path.Combine(dir, "bin", "java.exe");
                        if (File.Exists(javaExe))
                        {
                            return new JavaInfo(javaExe, GetJavaVersion(javaExe));
                        }
                    }
                }
            }

            return null;
        }

        public static string GetJavaVersion(string javaPath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = javaPath,
                    Arguments = "-version",
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    string output = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    int start = output.IndexOf('"') + 1;
                    int end = output.IndexOf('"', start);
                    if (start > 0 && end > start)
                    {
                        return output.Substring(start, end - start);
                    }
                }
            }
            catch { }
            return "Unknown";
        }
    }
}