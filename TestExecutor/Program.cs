using System.Reflection;
using System.Diagnostics;
using MiniTestLib.Attributes;
using MiniTestLib.Exceptions;
using System.Threading;


namespace TestExecutor
{
    internal class Program
    {
        private class TestCase
        {
            public Type SuiteType { get; set; } = null!;
            public MethodInfo TestMethod { get; set; } = null!;
            public MethodInfo? BeforeMethod { get; set; }
            public MethodInfo? AfterMethod { get; set; }
            public object[]? RawValues { get; set; }
        }

        private class TestRunResult
        {
            public string SuiteName { get; set; } = string.Empty;
            public string TestName { get; set; } = string.Empty;
            public string DataInfo { get; set; } = string.Empty;
            public bool Passed { get; set; }
            public string? ErrorType { get; set; }
            public string? ErrorMessage { get; set; }
        }
        public delegate bool TestFilter(MethodInfo testMethod);
        private static Task<TestRunResult> EnqueueTest(
    DynamicThreadPool pool,
    TestCase tc)
        {
            var tcs = new TaskCompletionSource<TestRunResult>();

            pool.Enqueue(async () =>
            {
                try
                {
                    var r = await ExecuteTestCaseAsync(tc);
                    tcs.SetResult(r);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            return tcs.Task;
        }

        private static async Task Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: TestExecutor <tests-assembly-path> [maxDegree]");
                return;
            }

            var assemblyPath = args[0];
            int maxDegree = 1;
            if (args.Length > 1 && int.TryParse(args[1], out var parsed) && parsed > 0)
                maxDegree = parsed;

            Console.WriteLine("Mini test runner start");
            Console.WriteLine();

            var asm = Assembly.LoadFrom(assemblyPath);
            var allCases = DiscoverTestCases(asm);

            // фильтр: только Slow-тесты
            TestFilter slowTestsFilter = m =>
            {
                var meta = m.GetCustomAttribute<TestMetaAttribute>();
                return meta != null && meta.Category == "Slow";
            };

            // фильтр: высокий приоритет
            TestFilter highPriorityFilter = m =>
            {
                var meta = m.GetCustomAttribute<TestMetaAttribute>();
                return meta != null && meta.Priority >= 2;
            };

            var slowCases = DiscoverTestCases(asm, slowTestsFilter);
            var highPriorityCases = DiscoverTestCases(asm, highPriorityFilter);

            Console.WriteLine($"Total discovered test cases: {allCases.Count}");
            Console.WriteLine($"Slow cases: {slowCases.Count}");
            Console.WriteLine($"High priority cases: {highPriorityCases.Count}");
            Console.WriteLine();

            Console.WriteLine($"Total discovered test cases: {allCases.Count}");
            Console.WriteLine();

            // 1) последовательный запуск
            var sw = Stopwatch.StartNew();
            var sequentialResults = new List<TestRunResult>();
            foreach (var tc in allCases)
            {
                sequentialResults.Add(await ExecuteTestCaseAsync(tc));
            }
            sw.Stop();
            var sequentialMs = sw.ElapsedMilliseconds;
            PrintResults("Sequential run", sequentialResults);

            // 2) параллельный запуск через собственный пул потоков + сценарии нагрузки

            var config = new ThreadPoolConfig
            {
                MinThreads = 2,
                MaxThreads = maxDegree, // можно привязать к аргументу
                IdleWorkerLifetime = TimeSpan.FromSeconds(5),
                MaxQueueWait = TimeSpan.FromMilliseconds(500)
            };

            var allRuns = new List<TestRunResult>();

            Console.WriteLine();
            Console.WriteLine("=== Dynamic thread pool load simulation ===");

            // 2.1. одиночные подачи
            Console.WriteLine("Phase 1: single submissions");
            foreach (var tc in allCases)
            {
                var results = await RunWithDynamicPoolAsync(
                    new List<TestCase> { tc }, config);
                allRuns.AddRange(results);
                await Task.Delay(50);
            }

            // 2.2. период пиковой нагрузки – несколько полных пачек
            Console.WriteLine("Phase 2: peak load (full batches)");
            for (int i = 0; i < 5; i++)
            {
                var results = await RunWithDynamicPoolAsync(allCases, config);
                allRuns.AddRange(results);
            }

            // 2.3. интервал простоя
            Console.WriteLine("Phase 3: idle interval");
            await Task.Delay(5000);

            // 2.4. несколько маленьких пачек
            Console.WriteLine("Phase 4: small batches");
            for (int i = 0; i < 10; i++)
            {
                var subset = allCases.Take(3).ToList();
                var results = await RunWithDynamicPoolAsync(subset, config);
                allRuns.AddRange(results);
                await Task.Delay(200);
            }

            Console.WriteLine();
            PrintResults("Dynamic thread pool aggregated results", allRuns);
        }

        private static List<TestCase> DiscoverTestCases(Assembly asm, TestFilter? filter = null)
        {
            var result = new List<TestCase>();

            var types = asm
                .GetTypes()
                .Where(t => t.GetCustomAttribute<TestSuiteAttribute>() != null)
                .ToArray();

            foreach (var t in types)
            {
                var before = t.GetMethods()
                    .FirstOrDefault(m => m.GetCustomAttribute<BeforeAttribute>() != null);

                var after = t.GetMethods()
                    .FirstOrDefault(m => m.GetCustomAttribute<AfterAttribute>() != null);

                var testsQuery = t.GetMethods()
                    .Where(m => m.GetCustomAttribute<TestAttribute>() != null);

                if (filter != null)
                    testsQuery = testsQuery.Where(m => filter(m));

                var tests = testsQuery.ToArray();

                foreach (var test in tests)
                {
                    var dataAttrs = test.GetCustomAttributes<DataAttribute>().ToArray();
                    var sourceAttrs = test.GetCustomAttributes<TestCaseSourceAttribute>().ToArray();

                    // Если нет ни [Data], ни [TestCaseSource] — один обычный тест-кейс
                    if (dataAttrs.Length == 0 && sourceAttrs.Length == 0)
                    {
                        result.Add(new TestCase
                        {
                            SuiteType = t,
                            TestMethod = test,
                            BeforeMethod = before,
                            AfterMethod = after,
                            RawValues = null
                        });

                        continue;
                    }

                    // Кейсы из [Data(...)]
                    foreach (var da in dataAttrs)
                    {
                        result.Add(new TestCase
                        {
                            SuiteType = t,
                            TestMethod = test,
                            BeforeMethod = before,
                            AfterMethod = after,
                            RawValues = da.Values ?? Array.Empty<object>()
                        });
                    }

                    // Кейсы из [TestCaseSource(...)]
                    foreach (var sa in sourceAttrs)
                    {
                        var sourceMethod = t.GetMethod(
                            sa.MethodName,
                            BindingFlags.Public |
                            BindingFlags.NonPublic |
                            BindingFlags.Static |
                            BindingFlags.Instance);

                        if (sourceMethod == null)
                        {
                            throw new InvalidOperationException(
                                $"Test case source method '{sa.MethodName}' not found in type '{t.FullName}'.");
                        }

                        object? sourceInstance = null;
                        if (!sourceMethod.IsStatic)
                            sourceInstance = Activator.CreateInstance(t);

                        var sourceResult = sourceMethod.Invoke(sourceInstance, null);

                        if (sourceResult is not IEnumerable<object[]> enumerable)
                        {
                            throw new InvalidOperationException(
                                $"Method '{sa.MethodName}' in type '{t.FullName}' must return IEnumerable<object[]>.");
                        }

                        foreach (var values in enumerable)
                        {
                            result.Add(new TestCase
                            {
                                SuiteType = t,
                                TestMethod = test,
                                BeforeMethod = before,
                                AfterMethod = after,
                                RawValues = values
                            });
                        }
                    }
                }
            }

            return result;
        }

        private static async Task<TestRunResult> ExecuteTestCaseAsync(TestCase testCase)
        {
            var suiteName = testCase.SuiteType.FullName ?? testCase.SuiteType.Name;
            var testAttr = testCase.TestMethod.GetCustomAttribute<TestAttribute>()!;
            var testName = testAttr.Description ?? testCase.TestMethod.Name;

            string dataInfo = string.Empty;
            var args = Array.Empty<object>();
            if (testCase.RawValues != null)
            {
                args = ConvertArguments(testCase.TestMethod, testCase.RawValues);
                dataInfo = string.Join(", ", testCase.RawValues.Select(v => v?.ToString() ?? "null"));
            }

            var result = new TestRunResult
            {
                SuiteName = suiteName,
                TestName = testName,
                DataInfo = dataInfo
            };

            object? instance = null;
            try
            {
                instance = Activator.CreateInstance(testCase.SuiteType);

                if (testCase.BeforeMethod != null)
                    testCase.BeforeMethod.Invoke(instance, null);

                await InvokeWithTimeoutAsync(instance, testCase.TestMethod, args);

                if (testCase.AfterMethod != null)
                    testCase.AfterMethod.Invoke(instance, null);

                result.Passed = true;
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException ?? ex;

                if (inner is AssertFailedException)
                {
                    result.Passed = false;
                    result.ErrorType = "AssertFailed";
                    result.ErrorMessage = inner.Message;
                }
                else if (inner is TestTimeoutException)
                {
                    result.Passed = false;
                    result.ErrorType = "Timeout";
                    result.ErrorMessage = inner.Message;
                }
                else
                {
                    result.Passed = false;
                    result.ErrorType = "Exception";
                    result.ErrorMessage = inner.Message;
                }
            }
            catch (TestTimeoutException ex)
            {
                result.Passed = false;
                result.ErrorType = "Timeout";
                result.ErrorMessage = ex.Message;
            }
            catch (Exception ex)
            {
                result.Passed = false;
                result.ErrorType = "Exception";
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private static async Task InvokeWithTimeoutAsync(object? instance, MethodInfo method, object[] args)
        {
            var timeoutAttr = method.GetCustomAttribute<TimeoutAttribute>();
            var result = method.Invoke(instance, args);

            // async-тест
            if (result is Task task)
            {
                if (timeoutAttr == null)
                {
                    await task;
                    return;
                }

                var delayTask = Task.Delay(timeoutAttr.Milliseconds);
                var completed = await Task.WhenAny(task, delayTask);
                if (completed != task)
                    throw new TestTimeoutException(
                        $"Test exceeded timeout of {timeoutAttr.Milliseconds} ms");

                await task; // дождаться, чтобы исключения дошли
            }
            else
            {
                // sync-тест
                if (timeoutAttr == null)
                    return;

                var syncTask = Task.Run(() => method.Invoke(instance, args));
                var completed = await Task.WhenAny(syncTask, Task.Delay(timeoutAttr.Milliseconds));
                if (completed != syncTask)
                    throw new TestTimeoutException(
                        $"Test exceeded timeout of {timeoutAttr.Milliseconds} ms");

                await syncTask;
            }
        }

        private static async Task<List<TestRunResult>> RunInParallelAsync(
            List<TestCase> allCases,
            int maxDegree)
        {
            var semaphore = new SemaphoreSlim(maxDegree);
            var tasks = allCases.Select(async tc =>
            {
                await semaphore.WaitAsync();
                try
                {
                    return await ExecuteTestCaseAsync(tc);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            var results = await Task.WhenAll(tasks);
            return results.ToList();
        }
        private static async Task<List<TestRunResult>> RunWithDynamicPoolAsync(
    List<TestCase> allCases,
    ThreadPoolConfig config)
        {
            using var pool = new DynamicThreadPool(config);

            pool.WorkerStateChanged += (s, e) =>
            {
                Console.WriteLine($"[POOL] Worker {e.WorkerId} {e.State} " +
                                  $"{(e.Exception != null ? e.Exception.Message : "")}");
            };

            pool.QueueChanged += (s, e) =>
            {
                Console.WriteLine($"[POOL] Queue length = {e.QueueLength}");
            };

            var tasks = allCases.Select(tc => EnqueueTest(pool, tc)).ToList();

            var results = await Task.WhenAll(tasks);
            return results.ToList();
        }

        private static void PrintResults(string title, List<TestRunResult> results)
        {
            Console.WriteLine(title);
            Console.WriteLine();

            if (results.Count == 0)
            {
                Console.WriteLine("No tests executed.");
                return;
            }

            var suiteGroups = results.GroupBy(r => r.SuiteName);

            foreach (var suite in suiteGroups)
            {
                Console.WriteLine($"Suite: {suite.Key}");
                foreach (var r in suite)
                {
                    var prefix = r.Passed ? "[PASS]" : "[FAIL]";
                    var dataPart = string.IsNullOrEmpty(r.DataInfo)
                        ? ""
                        : $" (data: {r.DataInfo})";

                    Console.WriteLine($"{prefix} {r.TestName}{dataPart}");

                    if (!r.Passed && !string.IsNullOrEmpty(r.ErrorType))
                    {
                        Console.WriteLine($"       {r.ErrorType}: {r.ErrorMessage}");
                    }
                }

                Console.WriteLine();
            }

            var total = results.Count;
            var passed = results.Count(r => r.Passed);
            var failed = total - passed;

            Console.WriteLine("Summary:");
            Console.WriteLine($"Total: {total}, Passed: {passed}, Failed: {failed}");
        }

        private static object[] ConvertArguments(MethodInfo method, object[] rawValues)
        {
            var parameters = method.GetParameters();
            if (parameters.Length == 0 || rawValues.Length == 0)
                return Array.Empty<object>();

            var converted = new object[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                if (i >= rawValues.Length || rawValues[i] == null)
                {
                    converted[i] = rawValues.Length > i ? rawValues[i]! : GetDefault(parameters[i].ParameterType);
                    continue;
                }

                var targetType = parameters[i].ParameterType;
                var value = rawValues[i];

                if (targetType.IsInstanceOfType(value))
                {
                    converted[i] = value;
                }
                else
                {
                    converted[i] = Convert.ChangeType(value, targetType);
                }
            }

            return converted;
        }

        private static object? GetDefault(Type t)
        {
            return t.IsValueType ? Activator.CreateInstance(t) : null;
        }
    }
}