using System;

namespace MiniTestLib.Exceptions
{
    public class AssertFailedException : Exception
    {
        public AssertFailedException(string message) : base(message) { }
    }
}
