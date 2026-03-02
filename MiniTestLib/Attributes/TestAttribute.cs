using System;

namespace MiniTestLib.Attributes
{
    [AttributeUsage(AttributeTargets.Method)]
    public class TestAttribute : Attribute
    {
        public string Description { get; }
        public TestAttribute(string description = "") => Description = description;
    }
}
