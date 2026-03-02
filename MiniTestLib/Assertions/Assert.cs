using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MiniTestLib.Exceptions;

namespace MiniTestLib.Assertions
{
    public static class Assert
    {
        public static void AreEqual<T>(T expected, T actual, string message = "")
        {
            if (!Equals(expected, actual))
                throw new AssertFailedException($"AreEqual failed. Expected: <{expected}> Actual: <{actual}>. {message}");
        }

        public static void AreNotEqual<T>(T notExpected, T actual, string message = "")
        {
            if (Equals(notExpected, actual))
                throw new AssertFailedException($"AreNotEqual failed. NotExpected: <{notExpected}> Actual: <{actual}>. {message}");
        }

        public static void IsTrue(bool cond, string message = "")
        {
            if (!cond) throw new AssertFailedException($"IsTrue failed. {message}");
        }

        public static void IsFalse(bool cond, string message = "")
        {
            if (cond) throw new AssertFailedException($"IsFalse failed. {message}");
        }

        public static void IsNull(object obj, string message = "")
        {
            if (obj != null) throw new AssertFailedException($"IsNull failed. {message}");
        }

        public static void IsNotNull(object obj, string message = "")
        {
            if (obj == null) throw new AssertFailedException($"IsNotNull failed. {message}");
        }

        public static void IsInstanceOf<T>(object obj, string message = "")
        {
            if (obj is not T) throw new AssertFailedException($"IsInstanceOf failed. Expected: {typeof(T).Name}. {message}");
        }

        public static void IsEmpty<T>(IEnumerable<T> collection, string message = "")
        {
            if (collection.Any()) throw new AssertFailedException($"IsEmpty failed. {message}");
        }

        public static void Contains<T>(IEnumerable<T> collection, T item, string message = "")
        {
            if (!collection.Contains(item)) throw new AssertFailedException($"Contains failed. Item not found. {message}");
        }

        public static void Single<T>(ICollection<T> collection, string message = "")
        {
            if (collection.Count != 1) throw new AssertFailedException($"Single failed. Count = {collection.Count}. {message}");
        }

        public static TException Throws<TException>(Action action, string message = "") where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException ex) { return ex; }
            catch (Exception ex) { throw new AssertFailedException($"Throws failed. Expected {typeof(TException).Name} but got {ex.GetType().Name}. {message}"); }
            throw new AssertFailedException($"Throws failed. Expected {typeof(TException).Name} but no exception thrown. {message}");
        }

        public static async Task<TException> ThrowsAsync<TException>(Func<Task> action, string message = "") where TException : Exception
        {
            try
            {
                await action();
            }
            catch (TException ex) { return ex; }
            catch (Exception ex) { throw new AssertFailedException($"ThrowsAsync failed. Expected {typeof(TException).Name} but got {ex.GetType().Name}. {message}"); }
            throw new AssertFailedException($"ThrowsAsync failed. Expected {typeof(TException).Name} but no exception thrown. {message}");
        }
    }
}
