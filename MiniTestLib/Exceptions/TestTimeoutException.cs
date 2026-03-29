using System;

namespace MiniTestLib.Exceptions
{
    public class TestTimeoutException : Exception
    {
        public TestTimeoutException(string message) : base(message) { }
    }
}