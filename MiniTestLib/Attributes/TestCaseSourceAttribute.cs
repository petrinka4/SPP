using System;

namespace MiniTestLib.Attributes
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class TestCaseSourceAttribute : Attribute
    {
        public string MethodName { get; }

        public TestCaseSourceAttribute(string methodName)
        {
            MethodName = methodName;
        }
    }
}