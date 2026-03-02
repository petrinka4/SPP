using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Globalization;
using MiniTestLib.Attributes;
// dotnet run --project .\TestExecutor -- .\SampleApp.Tests\bin\Debug\net9.0\SampleApp.Tests.dll

namespace TestExecutor
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            Console.WriteLine("Mini test runner start");

            if (args.Length == 0)
            {
                Console.WriteLine("Usage: dotnet run --project TestExecutor -- <path-to-tests-dll>");
                return 1;
            }

            var dllPath = args[0];
            if (!File.Exists(dllPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"File not found: {dllPath}");
                Console.ResetColor();
                return 2;
            }

            var asm = Assembly.LoadFrom(dllPath);
            var types = asm.GetTypes().Where(t => t.GetCustomAttribute(typeof(TestSuiteAttribute)) != null);

            int total = 0, passed = 0, failed = 0;

            foreach (var t in types)
            {
                Console.WriteLine($"\nSuite: {t.FullName}");
                var before = t.GetMethods().FirstOrDefault(m => m.GetCustomAttribute(typeof(BeforeAttribute)) != null);
                var after = t.GetMethods().FirstOrDefault(m => m.GetCustomAttribute(typeof(AfterAttribute)) != null);
                var tests = t.GetMethods().Where(m => m.GetCustomAttribute(typeof(TestAttribute)) != null);

                foreach (var test in tests)
                {
                    var dataAttrs = test.GetCustomAttributes().Where(a => a.GetType() == typeof(DataAttribute)).Cast<DataAttribute>().ToArray();

                    if (dataAttrs.Length == 0)
                    {
                        total++;
                        var instance = Activator.CreateInstance(t);
                        try
                        {
                            before?.Invoke(instance, null);

                            var parameters = test.GetParameters();
                            object[] argsToPass = ConvertArguments(parameters, null);
                            var result = test.Invoke(instance, argsToPass);
                            if (result is Task task) await task;

                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"[PASS] {test.Name}");
                            passed++;
                        }
                        catch (TargetInvocationException tie) when (tie.InnerException != null)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"[FAIL] {test.Name} -> {tie.InnerException.GetType().Name}: {tie.InnerException.Message}");
                            failed++;
                        }
                        catch (Exception ex)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"[FAIL] {test.Name} -> {ex.GetType().Name}: {ex.Message}");
                            failed++;
                        }
                        finally
                        {
                            Console.ResetColor();
                            try { after?.Invoke(instance, null); } catch { }
                        }
                    }
                    else
                    {
                        foreach (var da in dataAttrs)
                        {
                            var rawValues = da.Values ?? Array.Empty<object>();
                            total++;
                            var instance = Activator.CreateInstance(t);
                            try
                            {
                                before?.Invoke(instance, null);

                                var parameters = test.GetParameters();
                                object[] argsToPass = ConvertArguments(parameters, rawValues);
                                var result = test.Invoke(instance, argsToPass);
                                if (result is Task task) await task;

                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine($"[PASS] {test.Name} (data: {string.Join(", ", rawValues)})");
                                passed++;
                            }
                            catch (TargetInvocationException tie) when (tie.InnerException != null)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"[FAIL] {test.Name} (data: {string.Join(", ", rawValues)}) -> {tie.InnerException.GetType().Name}: {tie.InnerException.Message}");
                                failed++;
                            }
                            catch (Exception ex)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.WriteLine($"[FAIL] {test.Name} (data: {string.Join(", ", rawValues)}) -> {ex.GetType().Name}: {ex.Message}");
                                failed++;
                            }
                            finally
                            {
                                Console.ResetColor();
                                try { after?.Invoke(instance, null); } catch { }
                            }
                        }
                    }
                }
            }

            Console.WriteLine("\nSummary:");
            Console.WriteLine($"Total: {total}, Passed: {passed}, Failed: {failed}");
            return failed == 0 ? 0 : 3;
        }

        static object[] ConvertArguments(ParameterInfo[] paramInfos, object[] provided)
        {
            if (paramInfos == null || paramInfos.Length == 0) return Array.Empty<object>();
            var result = new object[paramInfos.Length];

            for (int i = 0; i < paramInfos.Length; i++)
            {
                var targetType = paramInfos[i].ParameterType;
                if (provided == null || i >= provided.Length)
                {
                    result[i] = GetDefaultForParameter(targetType);
                    continue;
                }

                var val = provided[i];
                if (val == null)
                {
                    result[i] = null;
                    continue;
                }

                var valType = val.GetType();
                if (targetType.IsAssignableFrom(valType))
                {
                    result[i] = val;
                    continue;
                }

                try
                {
                    var nonNullableTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
                    if (val is string s)
                    {
                        result[i] = Convert.ChangeType(s, nonNullableTarget, CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        result[i] = Convert.ChangeType(val, nonNullableTarget, CultureInfo.InvariantCulture);
                    }
                }
                catch
                {
                    try
                    {
                        var nonNullableTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
                        var s = val.ToString();
                        result[i] = Convert.ChangeType(s, nonNullableTarget, CultureInfo.InvariantCulture);
                    }
                    catch
                    {
                        result[i] = val;
                    }
                }
            }

            return result;
        }

        static object GetDefaultForParameter(Type t)
        {
            if (!t.IsValueType) return null;
            return Activator.CreateInstance(t);
        }
    }
}
